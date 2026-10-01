using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using FerrarisPOS.Data;
using FerrarisPOS.Models;
using Microsoft.Data.Sqlite;
using QRCoder;

namespace FerrarisPOS.Services;

/// <summary>
/// Panel web de FerrariPOS. Solo lectura sobre SQLite y accesible por la red local.
/// La capa de publicación a Internet se podrá agregar posteriormente sin modificar el panel.
/// </summary>
public sealed class WebDashboardServer : IDisposable
{
    private const int PreferredPort = 8787;
    private const int MaxPortAttempts = 20;
    private int _port = PreferredPort;
    public int Port => _port;
    private const string CloudflaredVersion = "Stable · Quick Tunnel automático · HTTP/2 + IPv4 + recuperación de red";
    private const string CloudflaredDownloadUrl = "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe";
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private Process? _cloudflared;
    private readonly object _urlLock = new();
    private static readonly ConcurrentDictionary<string,int> MobileUserByToken = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<int,string> WindowsExternalTableSnapshot = new();
    private static readonly AsyncLocal<int> CurrentMobileUser = new();

    private static string DocumentsLogFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Ferrari'sPOS");

    private static string CloudflaredLogPath => Path.Combine(DocumentsLogFolder, "cloudflared_ferrari.log");
    private static string? ActiveDiagnosticLogPath;

    public static WebDashboardServer? Current { get; private set; }
    public string? AccessUrl { get; private set; }
    public event EventHandler? AccessUrlChanged;

    public void Start()
    {
        if (_cts != null) return;
        Current = this;
        _cts = new CancellationTokenSource();

        // El puerto 8787 es el preferido, pero puede estar ocupado por otra
        // instancia de FerrariPOS, otro servicio o un proceso que quedó vivo.
        // En ese caso elegimos automáticamente el siguiente puerto libre en
        // lugar de abortar el arranque completo del POS. Cloudflare utiliza el
        // puerto seleccionado mediante la variable Port, por lo que no se toca
        // ninguna configuración del túnel.
        TcpListener? listener = null;
        for (var offset = 0; offset < MaxPortAttempts; offset++)
        {
            var candidate = PreferredPort + offset;
            var candidateListener = new TcpListener(IPAddress.Any, candidate);
            try
            {
                candidateListener.Start();
                listener = candidateListener;
                _port = candidate;
                break;
            }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied)
            {
                try { candidateListener.Stop(); } catch { }
            }
        }

        if (listener == null)
        {
            _cts.Dispose();
            _cts = null;
            Current = null;
            throw new SocketException((int)SocketError.AddressAlreadyInUse);
        }

        _listener = listener;
        _loopTask = Task.Run(() => ListenLoopAsync(_cts.Token));

        SetLanAccessUrl();
        _ = Task.Run(() => StartCloudflareAsync(_cts.Token));
    }

    private void SetAccessUrl(string url)
    {
        // NUNCA aceptar endpoints internos de la API de TryCloudflare como si fueran
        // el hostname público del túnel. Por ejemplo, cloudflared puede escribir
        // https://api.trycloudflare.com/tunnel en sus logs durante el alta del túnel.
        // Ese endpoint NO es navegable ni debe guardarse para Android/Panel Web.
        if (url.Contains("trycloudflare.com", StringComparison.OrdinalIgnoreCase) &&
            !IsValidPublicTunnelUrl(url))
        {
            WriteTunnelLog("URL_CLOUDFLARE_IGNORADA: no es un hostname público de Quick Tunnel -> " + url);
            return;
        }

        lock (_urlLock) AccessUrl = url;
        SaveAccessFile(url);
        try { AccessUrlChanged?.Invoke(this, EventArgs.Empty); } catch { }
    }

    private void SetLanAccessUrl()
    {
        var lan = GetLanIPv4() ?? "localhost";
        SetAccessUrl($"http://{lan}:{Port}/");
    }

    private async Task StartCloudflareAsync(CancellationToken token)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FerrarisPOS");
        Directory.CreateDirectory(dir);
        var exe = Path.Combine(dir, "cloudflared.exe");
        // Preferir el ejecutable incluido por el instalador. Si no existe,
        // se utilizará la copia de AppData como respaldo.
        var bundledExe = Path.Combine(AppContext.BaseDirectory, "cloudflared.exe");
        if (File.Exists(bundledExe))
        {
            try
            {
                Directory.CreateDirectory(dir);
                if (!File.Exists(exe))
                    File.Copy(bundledExe, exe, false);
                else
                {
                    // Si existe una copia local válida, conservarla.
                    using var probe = File.OpenRead(exe);
                }
            }
            catch { }
        }
        var origin = $"http://127.0.0.1:{Port}";
        var lanIp = GetLanIPv4() ?? "N/D";
        WriteTunnelLog($"RED_LOCAL: IPv4 LAN={lanIp}; ORIGEN_CLOUDFLARE={origin}; EDGE_IP_VERSION=4; PROTOCOLO=auto");
        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                        WriteTunnelLog($"INTERFAZ_IPV4: {ni.Name} -> {ua.Address}");
            }
        }
        catch (Exception ex) { WriteTunnelLog("ERROR_ENUMERANDO_IPS: " + ex.Message); }

        try
        {
            Directory.CreateDirectory(DocumentsLogFolder);
            ActiveDiagnosticLogPath = Path.Combine(DocumentsLogFolder, $"FerrariPOS_Cloudflare_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.log");
            WriteTunnelLog($"NUEVA_SESION_DIAGNOSTICO: {ActiveDiagnosticLogPath}");
            WriteTunnelLog($"INICIO Cloudflare: origin={origin}, puerto={Port}");
            WriteCloudflareStatus("INICIANDO", $"Origen local: {origin}");
            WriteTunnelLog($"CARPETA_DOCUMENTOS: {DocumentsLogFolder}");
            WriteTunnelLog($"EJECUTABLE_CLOUDFLARED: {exe}");

            await NetworkRecovery.TryRecoverAsync(token);
            await EnsureCloudflaredAsync(exe, dir, token);
            WriteTunnelLog("CLOUDFLARED_DISPONIBLE: verificación/descarga completada.");

            while (!token.IsCancellationRequested && !await IsOriginHealthyAsync(origin, token))
            {
                WriteTunnelLog($"ORIGEN_NO_DISPONIBLE: {origin}");
                WriteCloudflareStatus("ESPERANDO_ORIGEN", $"FerrariPOS todavía no responde en {origin}");
                try { await Task.Delay(1000, token); }
                catch (OperationCanceledException) { return; }
            }

            WriteTunnelLog($"ORIGEN_OK: {origin}");
            WriteCloudflareStatus("ORIGEN_OK", $"FerrariPOS responde correctamente en {origin}");

            for (var attempt = 1; !token.IsCancellationRequested; attempt++)
            {
                Process? process = null;
                var keepProcessAlive = false;
                var publicUrlReady = false;

                try
                {
                    var lan = GetLanIPv4() ?? "localhost";
                    lock (_urlLock)
                    {
                        if (string.IsNullOrWhiteSpace(AccessUrl) ||
                            (AccessUrl.Contains("trycloudflare.com", StringComparison.OrdinalIgnoreCase) && !IsValidPublicTunnelUrl(AccessUrl)))
                            AccessUrl = $"http://{lan}:{Port}/";
                    }
                    SaveAccessFile(AccessUrl ?? $"http://{lan}:{Port}/");

                    // Quick Tunnel oficial: no necesita token, cuenta, DNS ni config.yaml.
                    // Se utiliza selección automática de protocolo + IPv4 para que Cloudflare
                    // pueda usar QUIC/UDP o HTTP/2/TCP según lo que permita la red,
                    // sin requerir una configuración manual del cliente.
                    var logFile = CloudflaredLogPath;
                    Directory.CreateDirectory(DocumentsLogFolder);
                    WriteTunnelLog($"LOG_DIAGNOSTICO: {logFile}");
                    WriteTunnelLog($"ORIGEN_EXACTO: {origin}");
                    WriteTunnelLog($"IP_LAN_DETECTADA: {lan}");
                    WriteTunnelLog($"COMANDO_CLOUDFLARED: tunnel --no-autoupdate --protocol auto --edge-ip-version 4 --loglevel info --url {origin}");

                    // No usamos --logfile de cloudflared para detectar la URL: en
                    // algunas versiones/entornos puede retrasar el contenido o dejar
                    // un archivo anterior. Capturamos stdout/stderr directamente y
                    // nosotros mismos guardamos cada línea en el log de Documentos.
                    var psi = new ProcessStartInfo
                    {
                        FileName = exe,
                        Arguments = $"tunnel --no-autoupdate --protocol auto --edge-ip-version 4 --loglevel info --url {origin}",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WorkingDirectory = dir
                    };

                    process = new Process { StartInfo = psi, EnableRaisingEvents = true };
                    process.OutputDataReceived += (_, e) => CaptureCloudflaredLine("OUT", e.Data);
                    process.ErrorDataReceived += (_, e) => CaptureCloudflaredLine("ERR", e.Data);
                    process.Exited += (_, _) =>
                    {
                        try
                        {
                            WriteTunnelLog($"PROCESO_CLOUDFLARED_FINALIZADO: PID={process?.Id}, ExitCode={process?.ExitCode}");
                        }
                        catch { }
                    };

                    WriteTunnelLog($"INTENTO #{attempt}: iniciando Quick Tunnel -> {origin}");
                    WriteCloudflareStatus("INICIANDO_TUNNEL", $"Intento #{attempt}; origen {origin}");

                    if (!process.Start())
                        throw new InvalidOperationException("No se pudo iniciar cloudflared.exe.");

                    _cloudflared = process;
                    WriteTunnelLog($"CLOUDFLARED_INICIADO: PID={process.Id}");
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    var deadline = DateTime.UtcNow.AddSeconds(120);
                    var lastLogLength = 0L;
                    while (!token.IsCancellationRequested && !process.HasExited && DateTime.UtcNow < deadline)
                    {
                        string? current;
                        lock (_urlLock) current = AccessUrl;

                        // La URL se captura directamente desde stdout/stderr mediante
                        // CaptureCloudflaredLine. No dependemos de un archivo de log de
                        // cloudflared para saber si el túnel llegó a estar listo.
                        await Task.Delay(250, token);

                        lock (_urlLock) current = AccessUrl;
                        if (!string.IsNullOrWhiteSpace(current) && current.Contains("trycloudflare.com", StringComparison.OrdinalIgnoreCase))
                        {
                            publicUrlReady = true;
                            keepProcessAlive = true;
                            WriteTunnelLog("PUBLIC_URL_READY: " + current);
                            WriteCloudflareStatus("CONECTADO", "URL pública: " + current);

                            // NO matar cloudflared al detectar la URL. El proceso debe
                            // permanecer vivo para que el enlace público siga funcionando.
                            var lastHealthCheck = DateTime.UtcNow;
                            var failedHealthChecks = 0;
                            var firstFailureAt = DateTime.MinValue;
                            while (!token.IsCancellationRequested && !process.HasExited)
                            {
                                await Task.Delay(1000, token);
                                if (DateTime.UtcNow - lastHealthCheck >= TimeSpan.FromSeconds(3))
                                {
                                    lastHealthCheck = DateTime.UtcNow;
                                    var healthy = await IsPublicTunnelHealthyAsync(current!, token);
                                    if (healthy)
                                    {
                                        failedHealthChecks = 0;
                                        firstFailureAt = DateTime.MinValue;
                                        WriteCloudflareStatus("CONECTADO", $"URL pública activa; cloudflared PID={process.Id}");
                                    }
                                    else
                                    {
                                        failedHealthChecks++;
                                        if (firstFailureAt == DateTime.MinValue) firstFailureAt = DateTime.UtcNow;
                                        var failureSeconds = (int)(DateTime.UtcNow - firstFailureAt).TotalSeconds;
                                        WriteTunnelLog($"SALUD_TUNNEL_FALLA: intento={failedHealthChecks}; segundos={failureSeconds}; URL={current}");

                                        // IMPORTANTE: un Quick Tunnel puede devolver 530 durante
                                        // una caída temporal aunque cloudflared siga vivo. NO matamos
                                        // el proceso por el resultado de este health-check porque eso
                                        // cambia el *.trycloudflare.com y deja al Android apuntando a
                                        // una URL vieja. cloudflared es quien debe recuperar su propia
                                        // conexión. Solo un proceso realmente terminado provoca un
                                        // nuevo intento y, por tanto, una nueva URL.
                                        WriteCloudflareStatus("RECONECTANDO", $"Cloudflare no respondió temporalmente ({failureSeconds}s); se conserva la misma URL y cloudflared sigue vivo.");

                                        // Si la URL lleva 90 segundos sin responder de forma continua,
                                        // forzamos una recuperación controlada. Esto evita quedar eternamente
                                        // atrapados con un proceso cloudflared vivo pero un túnel inutilizable.
                                        if (failureSeconds >= 90)
                                        {
                                            WriteTunnelLog("RECUPERACION_FORZADA: el Quick Tunnel lleva 90s sin responder; se reiniciará cloudflared.");
                                            WriteCloudflareStatus("RECUPERANDO", "El túnel público no respondió durante 90 segundos; generando una nueva conexión.");
                                            try { if (!process.HasExited) process.Kill(true); } catch { }
                                            break;
                                        }
                                    }
                                }
                            }
                            break;
                        }

                        await Task.Delay(500, token);
                    }

                    if (token.IsCancellationRequested)
                        return;

                    if (process.HasExited)
                    {
                        WriteTunnelLog($"CLOUDFLARED_EXIT: código={process.ExitCode}; URL_READY={publicUrlReady}");
                        SetLanAccessUrl();
                        WriteCloudflareStatus("DESCONECTADO", $"cloudflared terminó con código {process.ExitCode}; se limpió el enlace público anterior.");
                    }
                    else if (!publicUrlReady)
                    {
                        WriteTunnelLog("CLOUDFLARED_TIMEOUT: no entregó URL pública dentro de 120 segundos.");
                        WriteTunnelLog("DIAGNOSTICO_TIMEOUT: revisar las líneas OUT:/ERR: inmediatamente anteriores para detectar DNS, TLS, QUIC/HTTP2, firewall o rechazo del origen.");
                        WriteCloudflareStatus("TIMEOUT", "No se obtuvo URL pública dentro de 120 segundos.");
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    WriteTunnelLog("ERROR CLOUDFLARE: " + ex);
                    WriteCloudflareStatus("ERROR", ex.Message);
                }
                finally
                {
                    // Corrección crítica: si el túnel llegó a estar conectado, NO se
                    // mata cloudflared. La versión anterior lo finalizaba desde finally.
                    if (!keepProcessAlive)
                    {
                        try
                        {
                            if (process != null && !process.HasExited)
                            {
                                WriteTunnelLog($"DETENIENDO_INTENTO: PID={process.Id}");
                                process.Kill(true);
                            }
                        }
                        catch (Exception ex) { WriteTunnelLog("Aviso al detener cloudflared: " + ex.Message); }
                        try { process?.Dispose(); } catch { }
                        if (ReferenceEquals(_cloudflared, process)) _cloudflared = null;
                    }
                    else if (process != null && process.HasExited)
                    {
                        try { process.Dispose(); } catch { }
                        if (ReferenceEquals(_cloudflared, process)) _cloudflared = null;
                    }
                }

                if (!token.IsCancellationRequested)
                {
                    // Si cloudflared terminó, el origen no puede seguir publicándose con la URL anterior.
                    // Reiniciamos rápidamente el conector para recuperar el servicio; mientras tanto
                    // Android conserva la ruta LAN como respaldo y no borra su configuración.
                    // Si el proceso sigue vivo pero hubo 530, NO lo reiniciamos: el conector puede recuperarse
                    // sin cambiar la URL pública.
                    // Reconexión con backoff para evitar ciclos agresivos.
                    // Esto permite recuperar un corte de Internet sin castigar CPU/red.
                    var waitSeconds = process != null && process.HasExited ? 1 : 2;
                    WriteTunnelLog($"REINTENTO_CLOUDFLARE: recuperación rápida; próximo intento en {waitSeconds} segundo(s).");
                    WriteCloudflareStatus("REINTENTANDO", $"Próximo intento en {waitSeconds} segundos.");
                    try { await Task.Delay(TimeSpan.FromSeconds(waitSeconds), token); }
                    catch (OperationCanceledException) { return; }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            WriteTunnelLog("ERROR GENERAL Cloudflare: " + ex);
            WriteCloudflareStatus("ERROR_GENERAL", ex.Message);
        }
    }

    private static async Task<bool> IsPublicTunnelHealthyAsync(string url, CancellationToken token)
    {
        try
        {
            // El endpoint móvil exige el mismo token que usa Android. La versión
            // anterior hacía el health-check sin autenticación y recibía 401,
            // marcando como caída una conexión que en realidad estaba viva.
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            using var request = new HttpRequestMessage(HttpMethod.Get, url.TrimEnd('/') + "/api/mobile/ping");
            request.Headers.TryAddWithoutValidation("X-FerrariPOS-Token", GetMobileToken());
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public static string GetMobileTokenForDiagnostics() => GetMobileToken();

    public async Task<bool> RepairCloudflareAsync(CancellationToken token = default)
    {
        try
        {
            if (_cloudflared != null && !_cloudflared.HasExited)
            {
                var current = AccessUrl;
                if (!string.IsNullOrWhiteSpace(current) && await IsPublicTunnelHealthyAsync(current, token))
                    return true;

                WriteTunnelLog("REPARACION_MANUAL: se solicita reinicio controlado de cloudflared desde F7.");
                WriteCloudflareStatus("RECUPERANDO", "Reparación solicitada desde F7 → Configuración.");
                try { _cloudflared.Kill(true); } catch { }
            }
            else
            {
                WriteTunnelLog("REPARACION_MANUAL: cloudflared no estaba activo; el ciclo automático lo iniciará nuevamente.");
            }
            return true;
        }
        catch (Exception ex)
        {
            WriteTunnelLog("REPARACION_MANUAL_ERROR: " + ex);
            WriteCloudflareStatus("ERROR_REPARACION", ex.Message);
            return false;
        }
    }

    public static string GetMobilePairingQrText()
    {
        // El QR lleva la configuración completa. Así el teléfono no depende de
        // hacer una segunda petición al Quick Tunnel justo durante la vinculación.
        // También incluye lanUrl para que Android pueda cambiar automáticamente
        // a la red local si Cloudflare devuelve 530 y ambos equipos están en la LAN.
        return BuildMobilePairingJson();
    }

    public static void RegenerateMobileQr()
    {
        try
        {
            // Renovar el código NO debe reiniciar cloudflared. Reiniciar un Quick Tunnel
            // cambia el *.trycloudflare.com y desconecta inmediatamente a los teléfonos
            // que ya estaban trabajando con la URL anterior.
            var newCode = RegenerateMobilePairingCode();
            WriteTunnelLog("QR_REGENERADO_MANUALMENTE: código renovado sin reiniciar el túnel; URL pública conservada.");
            WriteCloudflareStatus("CONECTADO", "Código QR renovado sin cortar la conexión pública.");
            WriteTunnelLog("NUEVO_CODIGO_QR: " + newCode);
        }
        catch (Exception ex)
        {
            WriteTunnelLog("QR_REGENERADO_ERROR: " + ex.Message);
        }
    }

    private static async Task EnsureCloudflaredAsync(string exe, string dir, CancellationToken token)
    {
        // IMPORTANTE: no se exige una versión concreta. La versión instalada
        // por el usuario debe poder utilizarse tal cual. Esto evita que FerrariPOS
        // reemplace una instalación funcional (por ejemplo 2026.8.2) y se quede
        // eternamente en "INICIANDO" durante una descarga.
        const string latestUrl = CloudflaredDownloadUrl;

        if (File.Exists(exe))
        {
            try
            {
                using var check = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = exe,
                        Arguments = "--version",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WorkingDirectory = dir
                    }
                };
                check.Start();
                var stdoutTask = check.StandardOutput.ReadToEndAsync(token);
                var stderrTask = check.StandardError.ReadToEndAsync(token);
                await Task.WhenAll(stdoutTask, stderrTask);
                await check.WaitForExitAsync(token);

                var stdout = stdoutTask.Result.Trim();
                var stderr = stderrTask.Result.Trim();
                var versionText = string.Join(" | ", new[] { stdout, stderr }.Where(x => !string.IsNullOrWhiteSpace(x)));
                WriteTunnelLog($"CLOUDFLARED_VERSION_CHECK: exit={check.ExitCode}; {versionText}");

                // Si el ejecutable responde a --version, se considera válido sin
                // importar si es 2025.x, 2026.7.x, 2026.8.2 u otra versión oficial.
                if (check.ExitCode == 0 && versionText.Contains("cloudflared", StringComparison.OrdinalIgnoreCase))
                {
                    WriteTunnelLog("CLOUDFLARED_EXISTENTE_ACEPTADO: se utilizará el ejecutable instalado; no se descarga/reemplaza.");
                    return;
                }

                // Aun si la salida de --version fuera inusual, no destruimos un
                // ejecutable existente. El siguiente paso intentará ejecutarlo y
                // registrará el error real de cloudflared.
                WriteTunnelLog("CLOUDFLARED_VERSION_CHECK_NO_CONCLUYENTE: se conservará el ejecutable existente y se intentará iniciar el túnel.");
                return;
            }
            catch (Exception ex)
            {
                // No reemplazar automáticamente una instalación existente: queremos
                // conservar la versión que el usuario confirmó que funciona en CMD.
                WriteTunnelLog("CLOUDFLARED_VERSION_CHECK_ERROR: " + ex.Message);
                WriteTunnelLog("CLOUDFLARED_EXISTENTE_CONSERVADO: se intentará iniciar directamente.");
                return;
            }
        }

        // Solo descargamos si FerrariPOS realmente no tiene un ejecutable local.
        // No se toca %USERPROFILE%\.cloudflared ni ninguna configuración externa.
        var temp = Path.Combine(dir, "cloudflared.download.tmp.exe");
        WriteTunnelLog($"CLOUDFLARED_FALTANTE: descargando únicamente porque no existe {exe}");
        using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) })
        {
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FerrariPOS/73.1.30");
            using var response = await http.GetAsync(latestUrl, HttpCompletionOption.ResponseHeadersRead, token);
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync(token);
            await using var output = File.Create(temp);
            await input.CopyToAsync(output, token);
        }

        var info = new FileInfo(temp);
        if (info.Length < 5_000_000)
        {
            try { File.Delete(temp); } catch { }
            throw new InvalidOperationException("La descarga de cloudflared parece incompleta.");
        }

        try
        {
            File.Move(temp, exe, true);
        }
        catch
        {
            try { File.Copy(temp, exe, true); File.Delete(temp); } catch { }
        }
        WriteTunnelLog("CLOUDFLARED_DESCARGADO_Y_LISTO: " + exe);
    }

    private static void CaptureCloudflaredLine(string stream, string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        WriteTunnelLog(stream + ": " + line);
        TryCaptureTunnelUrlStatic(line);
    }

    private static async Task<bool> IsOriginHealthyAsync(string origin, CancellationToken token)
    {
        try
        {
            using var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(3)
            };
            using var response = await http.GetAsync(origin + "/", HttpCompletionOption.ResponseHeadersRead, token);
            return (int)response.StatusCode >= 200 && (int)response.StatusCode < 500;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteTunnelLog(string text)
    {
        try
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}{Environment.NewLine}";
            var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FerrarisPOS");
            Directory.CreateDirectory(local);
            File.AppendAllText(Path.Combine(local, "cloudflared_ferrari.log"), line);

            var docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Ferrari'sPOS");
            Directory.CreateDirectory(docs);
            File.AppendAllText(Path.Combine(docs, "cloudflared_ferrari.log"), line);
            File.AppendAllText(Path.Combine(docs, "FerrariPOS_Cloudflare_Diagnostico.log"), line);
            var session = ActiveDiagnosticLogPath;
            if (!string.IsNullOrWhiteSpace(session)) File.AppendAllText(session, line);
        }
        catch { }
    }

    private static void WriteCloudflareStatus(string status, string detail)
    {
        try
        {
            var docs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Ferrari'sPOS");
            Directory.CreateDirectory(docs);
            var path = Path.Combine(docs, "FerrariPOS_Cloudflare_Estado.txt");
            File.WriteAllText(path,
                "FERRARI'SPOS · ESTADO CLOUDFLARE\r\n" +
                "===============================\r\n" +
                $"Fecha: {DateTime.Now:dd/MM/yyyy HH:mm:ss}\r\n" +
                $"Estado: {status}\r\n" +
                $"Detalle: {detail}\r\n");
        }
        catch { }
    }

    private static bool IsValidPublicTunnelUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        if (!uri.Host.EndsWith(".trycloudflare.com", StringComparison.OrdinalIgnoreCase)) return false;

        // api.trycloudflare.com es el endpoint de la API que cloudflared utiliza
        // para crear el túnel. No es el hostname público del sitio.
        if (string.Equals(uri.Host, "api.trycloudflare.com", StringComparison.OrdinalIgnoreCase)) return false;

        var labels = uri.Host.Split('.');
        if (labels.Length < 3 || string.IsNullOrWhiteSpace(labels[0]) ||
            string.Equals(labels[0], "api", StringComparison.OrdinalIgnoreCase)) return false;

        // El Quick Tunnel público debe ser la raíz del hostname. Si el log trae
        // /tunnel u otra ruta interna de la API, jamás la usamos como AccessUrl.
        if (!string.IsNullOrEmpty(uri.AbsolutePath) && uri.AbsolutePath != "/") return false;
        return true;
    }

    private void TryCaptureTunnelUrl(string line) => TryCaptureTunnelUrlStatic(line);

    private static void TryCaptureTunnelUrlStatic(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        // Capturamos solamente el hostname de Quick Tunnel. No incluimos rutas
        // para evitar confundir endpoints como api.trycloudflare.com/tunnel.
        var matches = Regex.Matches(line,
            @"https://[A-Za-z0-9][A-Za-z0-9-]*\.trycloudflare\.com",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        foreach (Match match in matches)
        {
            var candidate = match.Value.TrimEnd('.', ',', ';', ')', ']', '|', '\\', '"', '\'');
            if (!IsValidPublicTunnelUrl(candidate))
            {
                WriteTunnelLog("URL_CLOUDFLARE_IGNORADA: " + candidate);
                continue;
            }

            var instance = Current;
            if (instance == null) return;
            var url = candidate + "/";
            WriteTunnelLog("URL PÚBLICA DETECTADA: " + url);
            instance.SetAccessUrl(url);
            return;
        }
    }

    /// <summary>
    /// Espera hasta que el Quick Tunnel público de Cloudflare esté disponible.
    /// No devuelve el enlace LAN: solo devuelve un URL trycloudflare.com válido
    /// para enviar a un administrador que esté fuera de la red local.
    /// </summary>
    public async Task<string?> WaitForPublicUrlAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            string? url;
            lock (_urlLock) url = AccessUrl;
            if (!string.IsNullOrWhiteSpace(url) && IsValidPublicTunnelUrl(url))
                return url;

            try { await Task.Delay(500, cancellationToken); }
            catch (OperationCanceledException) { break; }
        }
        return null;
    }

    private void SaveAccessFile(string url)
    {
        try
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var folder = Path.Combine(documents, "Ferrari'sPOS");
            Directory.CreateDirectory(folder);
            var txtPath = Path.Combine(folder, "FerrariPOS_web.txt");
            File.WriteAllText(txtPath,
                "FERRARI'SPOS · PANEL WEB\r\n" +
                "========================\r\n\r\n" +
                $"Dirección para abrir en el navegador:\r\n{url}\r\n\r\n" +
                $"Puerto: {Port}\r\n" +
                "Modo: solo lectura\r\n" +
                "Acceso público: FerrariPOS inicia el enlace automáticamente; el cliente no necesita instalar Cloudflare.\r\n\r\n" +
                "El panel muestra ventas, caja, stock, productos y clientes/deudas.\r\n" +
                $"Archivo: {txtPath}\r\n");
        }
        catch { }
    }

    private static string? GetLanIPv4()
    {
        try
        {
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ua.Address))
                        return ua.Address.ToString();
            }
        }
        catch { }
        return null;
    }

    private async Task ListenLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var listener = _listener;
                if (listener == null) break;
                var client = await listener.AcceptTcpClientAsync(token);
                _ = Task.Run(() => HandleClientAsync(client, token), token);
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                if (!token.IsCancellationRequested)
                    await Task.Delay(250, token).ContinueWith(_ => { });
            }
        }
    }

    // ==================== FERRARIPOS MANAGER · API MÓVIL ====================
    // El panel web histórico sigue funcionando sin token. La API móvil usa un
    // token aleatorio de alta entropía guardado en settings y enviado por
    // X-FerrariPOS-Token. El QR solo se genera desde Configuración como ADMIN.
    private static string GetMobileToken()
    {
        var token = Database.GetSetting("mobile_api_token", "");
        if (!string.IsNullOrWhiteSpace(token)) return token.Trim();
        token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace("+", "-").Replace("/", "_").TrimEnd('=');
        Database.SetSetting("mobile_api_token", token);
        return token;
    }

    private static string GetMobileBaseUrl()
    {
        var server = Current;
        if (server == null || server.Port <= 0)
            throw new InvalidOperationException("El servidor de FerrariPOS todavía no está iniciado.");
        var publicUrl = server.AccessUrl?.Trim().TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(publicUrl) &&
            publicUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
            (!publicUrl.Contains("trycloudflare.com", StringComparison.OrdinalIgnoreCase) || IsValidPublicTunnelUrl(publicUrl)))
            return publicUrl;
        var ip = GetLanIPv4() ?? "127.0.0.1";
        return $"http://{ip}:{server.Port}";
    }

    private static string BuildMobilePairingJson()
    {
        var token = GetMobileToken();
        var lanUrl = Current == null ? "" : $"http://{GetLanIPv4() ?? "127.0.0.1"}:{Current.Port}";
        var publicUrl = GetMobileBaseUrl();
        return JsonSerializer.Serialize(new
        {
            app = "FerrariPOS Manager",
            version = 3,
            baseUrl = publicUrl,
            lanUrl,
            token,
            store = Database.GetSetting("business_name", "FerrariPOS"),
            mode = publicUrl.Contains("trycloudflare.com", StringComparison.OrdinalIgnoreCase) ? "PUBLICO+LAN" : "LAN"
        });
    }

    public static string GetMobilePairingPayload() => BuildMobilePairingJson();

    public static void UpdateWindowsExternalTableSnapshot(IReadOnlyDictionary<int,string> snapshot)
    {
        WindowsExternalTableSnapshot.Clear();
        foreach (var pair in snapshot)
            if (!string.IsNullOrWhiteSpace(pair.Value)) WindowsExternalTableSnapshot[pair.Key] = pair.Value;
    }

    public static string GetMobilePairingCode()
    {
        var code = Database.GetSetting("mobile_pair_code", "");
        if (string.IsNullOrWhiteSpace(code))
        {
            code = Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
            Database.SetSetting("mobile_pair_code", code);
        }
        return "FPM3." + code;
    }

    public static string RegenerateMobilePairingCode()
    {
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(4));
        Database.SetSetting("mobile_pair_code", code);
        return "FPM3." + code;
    }

    public static bool TryGetMobilePairingByShortCode(string code, out string json)
    {
        json = "";
        code = (code ?? "").Trim();
        if (!code.StartsWith("FPM3.", StringComparison.OrdinalIgnoreCase)) return false;
        var expected = Database.GetSetting("mobile_pair_code", "");
        if (string.IsNullOrWhiteSpace(expected) || !string.Equals(code[5..], expected, StringComparison.OrdinalIgnoreCase)) return false;
        json = BuildMobilePairingJson();
        return true;
    }

    public static bool TryGetMobilePairingFromCode(string code, out string json)
    {
        json = "";
        try
        {
            code = (code ?? "").Trim();
            if (!code.StartsWith("FPM2.", StringComparison.OrdinalIgnoreCase)) return false;
            var raw = code[5..].Replace("-", "+").Replace("_", "/");
            raw = raw.PadRight(raw.Length + ((4 - raw.Length % 4) % 4), '=');
            json = Encoding.UTF8.GetString(Convert.FromBase64String(raw));
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("baseUrl", out var b) && b.GetString() is { Length: > 0 } &&
                   doc.RootElement.TryGetProperty("token", out var t) && t.GetString() is { Length: > 0 };
        }
        catch { json = ""; return false; }
    }

    private static bool IsMobileAuthorized(string request)
    {
        var match = Regex.Match(request, @"(?im)^X-FerrariPOS-Token:\s*(.+?)\s*$");
        if (!match.Success) return false;
        var supplied = match.Groups[1].Value.Trim();
        var expected = GetMobileToken();
        return supplied.Length == expected.Length &&
               CryptographicOperations.FixedTimeEquals(
                   Encoding.UTF8.GetBytes(supplied),
                   Encoding.UTF8.GetBytes(expected));
    }

    private static List<object> GetMobileProducts(string q)
    {
        q = (q ?? "").Trim();
        using var cn = OpenReadOnly();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = @"
SELECT p.id,p.barcode,p.description,p.sale_price,p.wholesale_price,p.cost_price,
       p.stock,p.min_stock,p.category,p.unit,p.active,p.is_bulk,COALESCE(p.uses_inventory,1),
       COALESCE(p.adds_iva_21,0),COALESCE(p.round_sale_to_5,0),
       COALESCE((SELECT sp.supplier_id FROM supplier_products sp WHERE sp.product_id=p.id ORDER BY sp.unit_cost,sp.supplier_id LIMIT 1),0),
       COALESCE((SELECT s.name FROM supplier_products sp JOIN suppliers s ON s.id=sp.supplier_id WHERE sp.product_id=p.id ORDER BY sp.unit_cost,sp.supplier_id LIMIT 1),'')
FROM products p
WHERE p.active=1 AND p.barcode <> '__COMUN__'
  AND ($q='' OR p.description LIKE $like OR p.barcode LIKE $like OR p.category LIKE $like)
ORDER BY p.description LIMIT 500;";
        cmd.Parameters.AddWithValue("$q", q);
        cmd.Parameters.AddWithValue("$like", "%" + q + "%");
        using var r=cmd.ExecuteReader();
        var list=new List<object>();
        while(r.Read())
            list.Add(new { id=r.GetInt32(0), barcode=Text(r,1), description=Text(r,2), salePrice=Dec(r,3), wholesalePrice=Dec(r,4), costPrice=Dec(r,5), stock=Dec(r,6), minStock=Dec(r,7), category=Text(r,8), unit=Text(r,9), active=r.GetInt32(10)!=0, bulk=r.GetInt32(11)!=0, usesInventory=r.GetInt32(12)!=0, iva21=r.GetInt32(13)!=0, roundTo5=r.GetInt32(14)!=0, supplierId=r.GetInt32(15), supplierName=Text(r,16) });
        return list;
    }

    private static List<MobileSaleItemDto> NormalizeMobileItems(IEnumerable<MobileSaleItemDto>? source)
    {
        var items=(source??Enumerable.Empty<MobileSaleItemDto>()).ToList();
        if(items.Any(x=>x.IsCommon && x.ProductId<=0))
        {
            using var cn=Database.Open(); using var cmd=cn.CreateCommand();
            cmd.CommandText="SELECT id FROM products WHERE barcode='__COMUN__' LIMIT 1";
            var commonId=Convert.ToInt32(cmd.ExecuteScalar()??0);
            if(commonId<=0) throw new InvalidOperationException("No está configurado el PRODUCTO EN COMÚN en Windows.");
            foreach(var item in items) if(item.IsCommon && item.ProductId<=0) item.ProductId=commonId;
        }
        return items;
    }

    private static void EnsureMobileOpenTickets(){using var cn=Database.Open();using var cmd=cn.CreateCommand();cmd.CommandText=@"CREATE TABLE IF NOT EXISTS mobile_open_tickets(id INTEGER PRIMARY KEY AUTOINCREMENT,table_id INTEGER NOT NULL UNIQUE,table_name TEXT NOT NULL DEFAULT '',customer_id INTEGER NOT NULL DEFAULT 1,customer_name TEXT NOT NULL DEFAULT '',items_json TEXT NOT NULL,notes TEXT NOT NULL DEFAULT '',created_by INTEGER,updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP)";cmd.ExecuteNonQuery();}
    public static void EnsureMobileOpenTicketsForMainForm(){EnsureMobileOpenTickets();}
    private static void EnsureMobilePendingTickets(){using var cn=Database.Open();using var cmd=cn.CreateCommand();cmd.CommandText=@"CREATE TABLE IF NOT EXISTS mobile_pending_tickets(id INTEGER PRIMARY KEY AUTOINCREMENT,customer_id INTEGER NOT NULL DEFAULT 1,customer_name TEXT NOT NULL DEFAULT '',items_json TEXT NOT NULL,notes TEXT NOT NULL DEFAULT '',created_by INTEGER,updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP)";cmd.ExecuteNonQuery();}
    public static void RemoveMobilePendingTicket(long id){try{EnsureMobilePendingTickets();using var cn=Database.Open();using var cmd=cn.CreateCommand();cmd.CommandText="DELETE FROM mobile_pending_tickets WHERE id=$id";cmd.Parameters.AddWithValue("$id",id);cmd.ExecuteNonQuery();}catch{}}
    public static void UpdateMobilePendingTicketItems(long id, IReadOnlyList<FerrarisPOS.Models.CartItem> items)
    {
        if (id <= 0 || items == null || items.Count == 0) return;
        EnsureMobilePendingTickets();
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "UPDATE mobile_pending_tickets SET items_json=$items,updated_at=CURRENT_TIMESTAMP WHERE id=$id";
        var dto = items.Select(x => new MobileSaleItemDto
        {
            ProductId = x.ProductId,
            Quantity = x.Quantity,
            UnitPrice = x.UnitPrice,
            Discount = x.DiscountAmount,
            Description = x.Description,
            IsCommon = x.IsCommon
        }).ToList();
        cmd.Parameters.AddWithValue("$items", JsonSerializer.Serialize(dto));
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }
    public static void UpdateMobileOpenTicketItems(int tableId, IReadOnlyList<FerrarisPOS.Models.CartItem> items)
    {
        if (tableId <= 0 || items == null || items.Count == 0) return;
        EnsureMobileOpenTickets();
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "UPDATE mobile_open_tickets SET items_json=$items,updated_at=CURRENT_TIMESTAMP WHERE table_id=$tableId";
        var dto = items.Select(x => new MobileSaleItemDto
        {
            ProductId = x.ProductId,
            Quantity = x.Quantity,
            UnitPrice = x.UnitPrice,
            Discount = x.DiscountAmount,
            Description = x.Description,
            IsCommon = x.IsCommon
        }).ToList();
        cmd.Parameters.AddWithValue("$items", JsonSerializer.Serialize(dto));
        cmd.Parameters.AddWithValue("$tableId", tableId);
        cmd.ExecuteNonQuery();
    }
    public static void EnsureMobilePendingTicketsForMainForm(){EnsureMobilePendingTickets();}
    private static List<object> GetMobilePromotions(){using var cn=Database.Open();var ids=new List<(long id,string name,string description,double price,bool active,string start,string end)>();using(var cmd=cn.CreateCommand()){cmd.CommandText="SELECT id,name,COALESCE(description,''),COALESCE(promotion_price,amount,0),active,COALESCE(start_at,''),COALESCE(end_at,'') FROM promotions ORDER BY active DESC,name";using var r=cmd.ExecuteReader();while(r.Read())ids.Add((r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetDouble(3),r.GetInt32(4)!=0,r.GetString(5),r.GetString(6)));}return ids.Select(x=>new{id=x.id,name=x.name,description=x.description,price=x.price,active=x.active,startAt=x.start,endAt=x.end,items=GetPromotionItems(x.id)}).Cast<object>().ToList();}
    private static List<object> GetPromotionItems(long id){using var cn=Database.Open();using var c=cn.CreateCommand();c.CommandText="SELECT pi.product_id,p.description,pi.quantity FROM promotion_items pi JOIN products p ON p.id=pi.product_id WHERE pi.promotion_id=$id ORDER BY p.description";c.Parameters.AddWithValue("$id",id);using var r=c.ExecuteReader();var a=new List<object>();while(r.Read())a.Add(new{productId=r.GetInt32(0),description=r.GetString(1),quantity=r.GetDouble(2)});return a;}
    public static void RemoveMobileOpenTicket(int tableId)
    {
        try
        {
            EnsureMobileOpenTickets();
            using var cn=Database.Open(); using var cmd=cn.CreateCommand();
            cmd.CommandText="DELETE FROM mobile_open_tickets WHERE table_id=$t";
            cmd.Parameters.AddWithValue("$t",tableId);
            cmd.ExecuteNonQuery();
        }
        catch { }
    }

    private static List<object> GetMobileTables(int? salonId=null){
        TableService.EnsureTables(); EnsureMobileOpenTickets(); var open=new HashSet<int>();
        using(var cn0=Database.Open()){using var oc=cn0.CreateCommand();oc.CommandText="SELECT table_id FROM mobile_open_tickets";using var or=oc.ExecuteReader();while(or.Read())open.Add(or.GetInt32(0));}
        if(FerrarisPOS.Forms.MainForm.CurrentInstance is { } main){using var allCn=Database.Open();using var allCmd=allCn.CreateCommand();allCmd.CommandText="SELECT id FROM restaurant_tables WHERE enabled=1";using var ar=allCmd.ExecuteReader();while(ar.Read()){var id=ar.GetInt32(0);if(main.GetExternalTableTicketJson(id)!=null)open.Add(id);}}
        var sid=salonId.GetValueOrDefault(TableService.ActiveSalonId);if(sid<=0)sid=TableService.ActiveSalonId;
        using var cn=Database.Open();using var cmd=cn.CreateCommand();cmd.CommandText="SELECT id,name,x,y,width,height,capacity,shape,salon_id,rotation,color FROM restaurant_tables WHERE enabled=1 AND salon_id=$s ORDER BY id";cmd.Parameters.AddWithValue("$s",sid);using var r=cmd.ExecuteReader();var list=new List<object>();while(r.Read())list.Add(new{id=r.GetInt32(0),name=r.GetString(1),x=r.GetInt32(2),y=r.GetInt32(3),width=r.GetInt32(4),height=r.GetInt32(5),capacity=r.GetInt32(6),shape=r.GetString(7),salonId=r.GetInt32(8),rotation=r.GetDouble(9),color=r.GetString(10),occupied=open.Contains(r.GetInt32(0))});return list;
    }
    private static List<object> GetMobileSalons(){return TableService.GetSalons(true).Select(x=>new{id=x.Id,name=x.Name,description=x.Description,active=x.Active}).ToList<object>();}
    private static List<object> GetMobileOpenTickets(){EnsureMobileOpenTickets();using var cn=Database.Open();using var cmd=cn.CreateCommand();cmd.CommandText="SELECT table_id,table_name,customer_id,customer_name,items_json,notes,updated_at FROM mobile_open_tickets ORDER BY table_id";using var r=cmd.ExecuteReader();var list=new List<object>();var seen=new HashSet<int>();while(r.Read()){var tableId=r.GetInt32(0);var json=r.GetString(4);seen.Add(tableId);list.Add(new{tableId,tableName=r.GetString(1),customerId=r.GetInt32(2),customerName=r.GetString(3),items=JsonSerializer.Deserialize<List<MobileSaleItemDto>>(json)??new List<MobileSaleItemDto>(),notes=r.GetString(5),updatedAt=r.GetString(6)});}if(FerrarisPOS.Forms.MainForm.CurrentInstance is { } main){foreach(var t in TableService.GetTables().Where(x=>x.Enabled)){if(seen.Contains(t.Id))continue;var json=main.GetExternalTableTicketJson(t.Id);if(string.IsNullOrWhiteSpace(json))continue;try{var dto=JsonSerializer.Deserialize<MobileOpenTicketDto>(json);if(dto?.Items!=null&&dto.Items.Count>0)list.Add(new{tableId=dto.TableId,tableName=dto.TableName,customerId=dto.CustomerId,customerName=dto.CustomerName,items=dto.Items,notes=dto.Notes??"",updatedAt=dto.UpdatedAt??""});}catch{}}}return list;}
    private static List<object> GetMobileSuppliers(){using var cn=Database.Open();using var cmd=cn.CreateCommand();cmd.CommandText="SELECT id,name,document,phone,email,address FROM suppliers WHERE active=1 ORDER BY name";using var r=cmd.ExecuteReader();var list=new List<object>();while(r.Read())list.Add(new{id=r.GetInt32(0),name=r.GetString(1),document=r.GetString(2),phone=r.GetString(3),email=r.GetString(4),address=r.GetString(5)});return list;}
    private static List<object> GetMobilePurchases(){using var cn=Database.Open();using var cmd=cn.CreateCommand();cmd.CommandText="SELECT po.id,po.order_no,s.name,po.status,po.total,po.received_total,datetime(po.order_date,'localtime'),COALESCE(po.expected_date,'') FROM purchase_orders po JOIN suppliers s ON s.id=po.supplier_id ORDER BY po.id DESC LIMIT 100";using var r=cmd.ExecuteReader();var list=new List<object>();while(r.Read())list.Add(new{id=r.GetInt64(0),orderNo=r.GetString(1),supplier=r.GetString(2),status=r.GetString(3),total=r.GetDouble(4),receivedTotal=r.GetDouble(5),date=r.GetString(6),expectedDate=r.GetString(7)});return list;}
    private static object? GetMobileProduct(int id) => GetMobileProducts("").FirstOrDefault(x => Convert.ToInt32(x.GetType().GetProperty("id")?.GetValue(x) ?? 0) == id);

    private sealed class MobileLicenseTokenDto
    {
        public string Token { get; set; } = "";
    }

    private static int MobileUserId()
    {
        var selected = CurrentMobileUser.Value;
        if (selected > 0) return selected;
        using var cn=Database.Open(); using var cmd=cn.CreateCommand();
        cmd.CommandText="SELECT id FROM users WHERE active=1 ORDER BY CASE WHEN UPPER(username)='ADMIN' THEN 0 ELSE 1 END,id LIMIT 1";
        var v=cmd.ExecuteScalar(); return v==null?0:Convert.ToInt32(v);
    }

    private static string MobileTokenFromRequest(string request)
    {
        var m=Regex.Match(request, @"(?im)^X-FerrariPOS-Token:\s*(.+?)\s*$");
        return m.Success ? m.Groups[1].Value.Trim() : "";
    }

    private static bool VerifyMobilePassword(string password, string storedHash)
    {
        var sha=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)));
        if(sha.Equals(storedHash,StringComparison.OrdinalIgnoreCase)) return true;
        var md5=Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(password)));
        return md5.Equals(storedHash,StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> ReadRequestBodyAsync(NetworkStream stream, string request, CancellationToken token)
    {
        var headerEnd=request.IndexOf("\r\n\r\n",StringComparison.Ordinal); if(headerEnd<0) return "";
        var body=request[(headerEnd+4)..]; var match=Regex.Match(request, @"(?im)^Content-Length:\s*(\d+)\s*$"); if(!match.Success) return body;
        var length=int.Parse(match.Groups[1].Value,CultureInfo.InvariantCulture); var bodyBytes=Encoding.UTF8.GetBytes(body);
        while(bodyBytes.Length<length){var buffer=new byte[Math.Min(8192,length-bodyBytes.Length)];var read=await stream.ReadAsync(buffer,token);if(read<=0)break;var merged=new byte[bodyBytes.Length+read];Buffer.BlockCopy(bodyBytes,0,merged,0,bodyBytes.Length);Buffer.BlockCopy(buffer,0,merged,bodyBytes.Length,read);bodyBytes=merged;}
        return Encoding.UTF8.GetString(bodyBytes,0,Math.Min(length,bodyBytes.Length));
    }

    private static async Task HandleMobileAsync(TcpClient client, NetworkStream stream, string request, string target, CancellationToken token)
    {
        var rawTarget = target;
        target = target.Split('?', 2)[0];
        var method = request.Split(" ",StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "GET";
        if(target == "/api/mobile/vinculacion" && method == "GET")
        {
            var qs = rawTarget.Contains('?') ? rawTarget.Split('?',2)[1] : "";
            var code = Regex.Match(qs, @"(?:^|&)codigo=([^&]*)", RegexOptions.IgnoreCase).Groups[1].Value;
            code = Uri.UnescapeDataString(code.Replace("+", " "));
            if(!TryGetMobilePairingFromCode(code, out var pairing)) { await JsonAsync(stream,new {error="Código de vinculación inválido o vencido."},token,401); return; }
            await JsonAsync(stream,JsonSerializer.Deserialize<JsonElement>(pairing),token); return;
        }
            if(target.StartsWith("/api/mobile/vincular/", StringComparison.OrdinalIgnoreCase) && method=="GET")
            {
                var pairingCode = target.Substring("/api/mobile/vincular/".Length);
                if(!TryGetMobilePairingByShortCode(Uri.UnescapeDataString(pairingCode), out var pairingJson))
                { await JsonAsync(stream,new {error="Código de vinculación inválido o vencido."},token,401); return; }
                await JsonAsync(stream,JsonSerializer.Deserialize<object>(pairingJson)!,token); return;
            }
        if(!IsMobileAuthorized(request)){await JsonAsync(stream,new {error="NO AUTORIZADO",message="Token FerrariPOS Manager inválido o ausente."},token,401);return;}
        var mobileToken=MobileTokenFromRequest(request); CurrentMobileUser.Value = MobileUserByToken.TryGetValue(mobileToken,out var mappedUser) ? mappedUser : 0;
        try
        {
            if(target=="/api/mobile/license/activate" && method=="POST")
            {
                var body=await ReadRequestBodyAsync(stream,request,token);
                var dto=JsonSerializer.Deserialize<MobileLicenseTokenDto>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true}) ?? new MobileLicenseTokenDto();
                if(string.IsNullOrWhiteSpace(dto.Token)) throw new InvalidOperationException("Pegá el token de licencia.");
                var ok=LicenseService.ApplyLicenseToken(dto.Token.Trim(),out var message);
                if(!ok){await JsonAsync(stream,new {ok=false,error=message},token,400);return;}
                AuditService.Log(MobileUserId(),"MOBILE_LICENSE_ACTIVATE","LICENCIAS",$"Licencia aplicada desde Android · {LicenseService.LicenseId}");
                await JsonAsync(stream,new {ok=true,message,status=LicenseService.GetStatusText(),licenseId=LicenseService.LicenseId,expiresAtUtc=LicenseService.ExpiresAtUtc},token);return;
            }
            if(target=="/api/mobile/ping"){await JsonAsync(stream,new {ok=true,app="FerrariPOS",version="73.2.0",store=Database.GetSetting("business_name","FerrariPOS"),mobileApi=2},token);return;}
            if(target=="/api/mobile/usuarios" && method=="GET")
            {
                using var cn=Database.Open(); using var q=cn.CreateCommand();
                q.CommandText="SELECT id,username,COALESCE(full_name,''),COALESCE(role,'') FROM users WHERE active=1 ORDER BY CASE WHEN UPPER(username)='ADMIN' THEN 0 ELSE 1 END,full_name COLLATE NOCASE,username COLLATE NOCASE";
                using var r=q.ExecuteReader(); var users=new List<object>();
                while(r.Read()) users.Add(new{id=r.GetInt32(0),username=r.GetString(1),fullName=r.GetString(2),role=r.GetString(3)});
                await JsonAsync(stream,users,token); return;
            }
            if(target=="/api/mobile/usuario/actual" && method=="GET")
            {
                var id = MobileUserId();
                using var cn=Database.Open(); using var q=cn.CreateCommand();
                q.CommandText="SELECT id,username,COALESCE(full_name,''),COALESCE(role,'') FROM users WHERE id=$id AND active=1 LIMIT 1";
                q.Parameters.AddWithValue("$id",id);
                using var r=q.ExecuteReader();
                if(!r.Read()){ await JsonAsync(stream,new {error="Usuario móvil no encontrado."},token,404); return; }
                await JsonAsync(stream,new {id=r.GetInt32(0),username=r.GetString(1),fullName=r.GetString(2),role=r.GetString(3)},token); return;
            }
            if(target=="/api/mobile/usuario/cambiar" && method=="POST")
            {
                if(CashService.IsOpen()) throw new InvalidOperationException("La caja debe estar cerrada para cambiar de usuario.");
                var body=await ReadRequestBodyAsync(stream,request,token); var dto=JsonSerializer.Deserialize<MobileUserSwitchDto>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException("Usuario inválido.");
                if(dto.UserId<=0 || string.IsNullOrWhiteSpace(dto.Password)) throw new InvalidOperationException("Seleccioná un usuario e ingresá la contraseña.");
                const string master="39242155";
                using var cn=Database.Open(); using var q=cn.CreateCommand(); q.CommandText="SELECT id,username,password_hash,active FROM users WHERE id=$id LIMIT 1"; q.Parameters.AddWithValue("$id",dto.UserId);
                using var r=q.ExecuteReader(); if(!r.Read()) throw new InvalidOperationException("Usuario no encontrado.");
                var id=r.GetInt32(0); var username=r.GetString(1); var hash=r.IsDBNull(2)?"":r.GetString(2); var active=r.GetInt32(3)!=0;
                if(!active || (!VerifyMobilePassword(dto.Password,hash) && !string.Equals(dto.Password,master,StringComparison.Ordinal))) throw new InvalidOperationException("Usuario o contraseña incorrectos.");
                if(string.IsNullOrWhiteSpace(mobileToken)) throw new InvalidOperationException("Token FerrariPOS Manager ausente.");
                MobileUserByToken[mobileToken]=id; CurrentMobileUser.Value=id; AuditService.Log(id,"MOBILE_USER_SWITCH","USUARIOS",$"Usuario seleccionado desde Manager Android: {username}");
                await JsonAsync(stream,new{ok=true,id,username},token); return;
            }
            if(target=="/api/mobile/resumen"){await JsonAsync(stream,GetSummary(),token);return;}
            if(target=="/api/mobile/medios-pago" && method=="GET")
            {
                var methods = new List<string>{ "EFECTIVO", "TRANSFERENCIA", "CRÉDITO", "DÓLARES", "TARJETA" };
                if(CashService.IsOpen() && CashService.IsMercadoPagoEnabled()) methods.Insert(1,"MERCADO PAGO");
                methods.Add("MIXTO");
                await JsonAsync(stream, methods, token); return;
            }
            if(target=="/api/mobile/categorias" && method=="GET")
            {
                await JsonAsync(stream,CategoryService.GetAll(),token); return;
            }
            if(target=="/api/mobile/categorias" && method=="POST")
            {
                var body=await ReadRequestBodyAsync(stream,request,token);
                var dto=JsonSerializer.Deserialize<MobileCategoryDto>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true}) ?? throw new InvalidOperationException("Categoría inválida.");
                var name=CategoryService.Ensure(dto.Name);
                if(string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("El nombre de la categoría es obligatorio.");
                AuditService.Log(MobileUserId(),"MOBILE_CATEGORY_CREATE","INVENTARIO",$"Categoría: {name}");
                await JsonAsync(stream,new {ok=true,name},token); return;
            }
            if(target=="/api/mobile/productos" && method=="GET")
            {
                var qs=rawTarget.Contains('?')?rawTarget.Split('?',2)[1]:""; var q=Regex.Match(qs,@"(?:^|&)q=([^&]*)",RegexOptions.IgnoreCase).Groups[1].Value; if(!string.IsNullOrWhiteSpace(q))q=Uri.UnescapeDataString(q.Replace("+"," ")); await JsonAsync(stream,GetMobileProducts(q),token);return;
            }
            if(target=="/api/mobile/productos" && method=="POST")
            {
                var body=await ReadRequestBodyAsync(stream,request,token);
                var dto=JsonSerializer.Deserialize<MobileProductDto>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true}) ?? throw new InvalidOperationException("Producto inválido.");
                if(string.IsNullOrWhiteSpace(dto.Description)) throw new InvalidOperationException("La descripción es obligatoria.");
                if(string.IsNullOrWhiteSpace(dto.Barcode)) throw new InvalidOperationException("El código de barras es obligatorio.");
                if(dto.SalePrice<0||dto.CostPrice<0||dto.WholesalePrice<0||dto.Stock<0||dto.MinStock<0) throw new InvalidOperationException("Los importes y stock no pueden ser negativos.");
                using var cn=Database.Open(); using var tx=cn.BeginTransaction();
                using(var chk=cn.CreateCommand())
                {
                    chk.Transaction=tx; chk.CommandText="SELECT id FROM products WHERE barcode=$b LIMIT 1"; chk.Parameters.AddWithValue("$b",dto.Barcode.Trim());
                    if(chk.ExecuteScalar()!=null) throw new InvalidOperationException("El código de barras ya existe.");
                }
                int id;
                using(var cmd=cn.CreateCommand())
                {
                    cmd.Transaction=tx; cmd.CommandText="INSERT INTO products(barcode,description,sale_price,wholesale_price,cost_price,stock,min_stock,category,unit,is_bulk,active,uses_inventory,adds_iva_21,updated_at) VALUES($b,$d,$s,$w,$c,$st,$m,$cat,$u,$bulk,1,$inv,$iva,CURRENT_TIMESTAMP);SELECT last_insert_rowid();";
                    cmd.Parameters.AddWithValue("$b",dto.Barcode.Trim()); cmd.Parameters.AddWithValue("$d",dto.Description.Trim()); cmd.Parameters.AddWithValue("$s",dto.SalePrice); cmd.Parameters.AddWithValue("$w",dto.WholesalePrice); cmd.Parameters.AddWithValue("$c",dto.CostPrice); cmd.Parameters.AddWithValue("$st",dto.UsesInventory?dto.Stock:0d); cmd.Parameters.AddWithValue("$m",dto.UsesInventory?dto.MinStock:0d); cmd.Parameters.AddWithValue("$cat",dto.Category?.Trim()??""); cmd.Parameters.AddWithValue("$u",string.IsNullOrWhiteSpace(dto.Unit)?"UN":dto.Unit.Trim()); cmd.Parameters.AddWithValue("$bulk",dto.Bulk?1:0); cmd.Parameters.AddWithValue("$inv",dto.UsesInventory?1:0); cmd.Parameters.AddWithValue("$iva",dto.Iva21?1:0);
                    id=Convert.ToInt32(cmd.ExecuteScalar());
                }
                if(dto.UsesInventory && dto.Stock>0)
                {
                    using var mv=cn.CreateCommand(); mv.Transaction=tx; mv.CommandText="INSERT INTO stock_movements(product_id,movement_type,quantity,reference,user_id) VALUES($id,'ENTRY',$q,'ALTA DESDE FERRARIPOS MANAGER',$u)"; mv.Parameters.AddWithValue("$id",id); mv.Parameters.AddWithValue("$q",dto.Stock); mv.Parameters.AddWithValue("$u",MobileUserId()); mv.ExecuteNonQuery();
                }
                tx.Commit(); AuditService.Log(MobileUserId(),"MOBILE_PRODUCT_CREATE","PRODUCTOS",$"Producto ID {id} creado desde Manager"); try{MobileInventoryExcelSync.Export();}catch{}
                await JsonAsync(stream,new {ok=true,id,product=GetMobileProduct(id)},token); return;
            }
            var productMatch=Regex.Match(target,@"^/api/mobile/productos/(\d+)$");
            if(productMatch.Success)
            {
                var id=int.Parse(productMatch.Groups[1].Value,CultureInfo.InvariantCulture);
                if(method=="GET"){var p=GetMobileProduct(id);if(p==null){await JsonAsync(stream,new{error="Producto no encontrado"},token,404);return;}await JsonAsync(stream,p,token);return;}
                if(method=="POST" || method=="PUT")
                {
                    var body=await ReadRequestBodyAsync(stream,request,token);var dto=JsonSerializer.Deserialize<MobileProductDto>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true}) ?? throw new InvalidOperationException("Producto inválido.");
                    if(string.IsNullOrWhiteSpace(dto.Description))throw new InvalidOperationException("La descripción es obligatoria.");
                    if(string.IsNullOrWhiteSpace(dto.Barcode))throw new InvalidOperationException("El código de barras es obligatorio.");
                    if(dto.SalePrice<0||dto.CostPrice<0||dto.WholesalePrice<0||dto.Stock<0||dto.MinStock<0)throw new InvalidOperationException("Los importes y stock no pueden ser negativos.");
                    using var cn=Database.Open(); using var tx=cn.BeginTransaction();
                    if(method=="POST")
                    {
                        using var chk=cn.CreateCommand();chk.Transaction=tx;chk.CommandText="SELECT id FROM products WHERE barcode=$b LIMIT 1";chk.Parameters.AddWithValue("$b",dto.Barcode.Trim());if(chk.ExecuteScalar()!=null)throw new InvalidOperationException("El código de barras ya existe.");
                        using var cmd=cn.CreateCommand();cmd.Transaction=tx;cmd.CommandText="INSERT INTO products(barcode,description,sale_price,wholesale_price,cost_price,stock,min_stock,category,unit,is_bulk,active,uses_inventory,adds_iva_21,updated_at) VALUES($b,$d,$s,$w,$c,$st,$m,$cat,$u,$bulk,1,$inv,$iva,CURRENT_TIMESTAMP);SELECT last_insert_rowid();";cmd.Parameters.AddWithValue("$b",dto.Barcode.Trim());cmd.Parameters.AddWithValue("$d",dto.Description.Trim());cmd.Parameters.AddWithValue("$s",dto.SalePrice);cmd.Parameters.AddWithValue("$w",dto.WholesalePrice);cmd.Parameters.AddWithValue("$c",dto.CostPrice);cmd.Parameters.AddWithValue("$st",dto.UsesInventory?dto.Stock:0d);cmd.Parameters.AddWithValue("$m",dto.UsesInventory?dto.MinStock:0d);cmd.Parameters.AddWithValue("$cat",dto.Category?.Trim()??"");cmd.Parameters.AddWithValue("$u",string.IsNullOrWhiteSpace(dto.Unit)?"UN":dto.Unit.Trim());cmd.Parameters.AddWithValue("$bulk",dto.Bulk?1:0);cmd.Parameters.AddWithValue("$inv",dto.UsesInventory?1:0);cmd.Parameters.AddWithValue("$iva",dto.Iva21?1:0);id=Convert.ToInt32(cmd.ExecuteScalar());
                        if(dto.UsesInventory && dto.Stock>0){using var mv=cn.CreateCommand();mv.Transaction=tx;mv.CommandText="INSERT INTO stock_movements(product_id,movement_type,quantity,reference,user_id) VALUES($id,'ENTRY',$q,'ALTA DESDE FERRARIPOS MANAGER',$u)";mv.Parameters.AddWithValue("$id",id);mv.Parameters.AddWithValue("$q",dto.Stock);mv.Parameters.AddWithValue("$u",MobileUserId());mv.ExecuteNonQuery();}
                    }
                    else
                    {
                        double previousStock; double previousSalePrice;
                        using(var current=cn.CreateCommand()){current.Transaction=tx;current.CommandText="SELECT stock,sale_price FROM products WHERE id=$id AND active=1 LIMIT 1";current.Parameters.AddWithValue("$id",id);using var rr=current.ExecuteReader();if(!rr.Read())throw new InvalidOperationException("Producto no encontrado o inactivo.");previousStock=rr.GetDouble(0);previousSalePrice=rr.GetDouble(1);}
                        var reductionReason=(dto.StockReductionReason??"").Trim();
                        if(dto.UsesInventory && dto.Stock < previousStock - 0.0000001 && string.IsNullOrWhiteSpace(reductionReason)) throw new InvalidOperationException("La reducción de stock requiere un motivo.");
                        using var cmd=cn.CreateCommand();cmd.Transaction=tx;cmd.CommandText="UPDATE products SET barcode=$b,description=$d,sale_price=$s,wholesale_price=$w,cost_price=$c,stock=$st,min_stock=$m,category=$cat,unit=$u,is_bulk=$bulk,uses_inventory=$inv,adds_iva_21=$iva,updated_at=CURRENT_TIMESTAMP WHERE id=$id AND active=1";cmd.Parameters.AddWithValue("$id",id);cmd.Parameters.AddWithValue("$b",dto.Barcode.Trim());cmd.Parameters.AddWithValue("$d",dto.Description.Trim());cmd.Parameters.AddWithValue("$s",dto.SalePrice);cmd.Parameters.AddWithValue("$w",dto.WholesalePrice);cmd.Parameters.AddWithValue("$c",dto.CostPrice);cmd.Parameters.AddWithValue("$st",dto.UsesInventory?dto.Stock:0d);cmd.Parameters.AddWithValue("$m",dto.UsesInventory?dto.MinStock:0d);cmd.Parameters.AddWithValue("$cat",dto.Category?.Trim()??"");cmd.Parameters.AddWithValue("$u",string.IsNullOrWhiteSpace(dto.Unit)?"UN":dto.Unit.Trim());cmd.Parameters.AddWithValue("$bulk",dto.Bulk?1:0);cmd.Parameters.AddWithValue("$inv",dto.UsesInventory?1:0);cmd.Parameters.AddWithValue("$iva",dto.Iva21?1:0);if(cmd.ExecuteNonQuery()!=1)throw new InvalidOperationException("Producto no encontrado o inactivo");
                        PriceGuardService.RecordProductPriceChange(cn,tx,id,previousSalePrice,dto.SalePrice);
                        if(dto.UsesInventory && dto.Stock < previousStock - 0.0000001){using var mv=cn.CreateCommand();mv.Transaction=tx;mv.CommandText="INSERT INTO stock_movements(product_id,movement_type,quantity,reference,user_id) VALUES($id,'ADJUSTMENT_OUT',$q,$r,$u)";mv.Parameters.AddWithValue("$id",id);mv.Parameters.AddWithValue("$q",previousStock-dto.Stock);mv.Parameters.AddWithValue("$r",reductionReason);mv.Parameters.AddWithValue("$u",MobileUserId());mv.ExecuteNonQuery();}
                    }
                    tx.Commit(); AuditService.Log(MobileUserId(),method=="POST"?"MOBILE_PRODUCT_CREATE":"MOBILE_PRODUCT_UPDATE","PRODUCTOS",$"Producto ID {id} actualizado desde Manager");
                    try{MobileInventoryExcelSync.Export();}catch{}
                    await JsonAsync(stream,new {ok=true,id,product=GetMobileProduct(id)},token);return;
                }
            }
            if(productMatch.Success && method=="DELETE")
            {
                var id=int.Parse(productMatch.Groups[1].Value,CultureInfo.InvariantCulture);
                var body=await ReadRequestBodyAsync(stream,request,token);
                var reason=JsonSerializer.Deserialize<Dictionary<string,string>>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})?.GetValueOrDefault("reason")?.Trim()??"";
                if(string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("Indicá el motivo de eliminación.");
                ProductService.Delete(id,reason,MobileUserId());
                try{MobileInventoryExcelSync.Export();}catch{}
                await JsonAsync(stream,new{ok=true,id},token);return;
            }
            var productSupplierMatch=Regex.Match(target,@"^/api/mobile/productos/(\d+)/proveedor$");
            if(productSupplierMatch.Success && method=="PUT")
            {
                var pid=int.Parse(productSupplierMatch.Groups[1].Value,CultureInfo.InvariantCulture);
                var body=await ReadRequestBodyAsync(stream,request,token);
                var dto=JsonSerializer.Deserialize<MobileSupplierAssignDto>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??new MobileSupplierAssignDto();
                using var cn=Database.Open(); using var tx=cn.BeginTransaction();
                using(var chk=cn.CreateCommand()){chk.Transaction=tx;chk.CommandText="SELECT id FROM products WHERE id=$id AND active=1";chk.Parameters.AddWithValue("$id",pid);if(chk.ExecuteScalar()==null)throw new InvalidOperationException("Producto no encontrado o inactivo.");}
                using(var del=cn.CreateCommand()){del.Transaction=tx;del.CommandText="DELETE FROM supplier_products WHERE product_id=$pid";del.Parameters.AddWithValue("$pid",pid);del.ExecuteNonQuery();}
                if(dto.SupplierId>0)
                {
                    using var chk=cn.CreateCommand();chk.Transaction=tx;chk.CommandText="SELECT id FROM suppliers WHERE id=$sid AND active=1";chk.Parameters.AddWithValue("$sid",dto.SupplierId);if(chk.ExecuteScalar()==null)throw new InvalidOperationException("Proveedor no encontrado o inactivo.");
                    using var ins=cn.CreateCommand();ins.Transaction=tx;ins.CommandText="INSERT INTO supplier_products(supplier_id,product_id,unit_cost,created_at,updated_at) VALUES($sid,$pid,$cost,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP) ON CONFLICT(supplier_id,product_id) DO UPDATE SET unit_cost=excluded.unit_cost,updated_at=CURRENT_TIMESTAMP";ins.Parameters.AddWithValue("$sid",dto.SupplierId);ins.Parameters.AddWithValue("$pid",pid);ins.Parameters.AddWithValue("$cost",dto.UnitCost);ins.ExecuteNonQuery();
                }
                tx.Commit(); AuditService.Log(MobileUserId(),"MOBILE_PRODUCT_SUPPLIER","PRODUCTOS",$"Producto ID {pid} · Proveedor ID {dto.SupplierId}"); try{MobileInventoryExcelSync.Export();}catch{}
                await JsonAsync(stream,new{ok=true,id=pid,product=GetMobileProduct(pid)},token); return;
            }
            var priceMatch=Regex.Match(target,@"^/api/mobile/productos/(\d+)/precio$");
            if(priceMatch.Success && method=="PUT")
            {
                var body=await ReadRequestBodyAsync(stream,request,token);
                var dto=JsonSerializer.Deserialize<MobilePriceUpdate>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true});
                if(dto==null||dto.SalePrice<0)throw new InvalidOperationException("Precio inválido.");
                int id=int.Parse(priceMatch.Groups[1].Value,CultureInfo.InvariantCulture);
                using var cn=Database.Open(); using var tx=cn.BeginTransaction();
                double oldPrice; string name;
                using(var current=cn.CreateCommand()){current.Transaction=tx;current.CommandText="SELECT sale_price,description FROM products WHERE id=$id AND active=1";current.Parameters.AddWithValue("$id",id);using var rr=current.ExecuteReader();if(!rr.Read())throw new InvalidOperationException("Producto no encontrado o inactivo.");oldPrice=rr.GetDouble(0);name=rr.GetString(1);}
                using(var cmd=cn.CreateCommand()){cmd.Transaction=tx;cmd.CommandText="UPDATE products SET sale_price=$p,updated_at=CURRENT_TIMESTAMP WHERE id=$id AND active=1";cmd.Parameters.AddWithValue("$p",dto.SalePrice);cmd.Parameters.AddWithValue("$id",id);cmd.ExecuteNonQuery();}
                PriceGuardService.RecordProductPriceChange(cn,tx,id,oldPrice,dto.SalePrice);
                tx.Commit();
                try{MobileInventoryExcelSync.Export();}catch{}
                await JsonAsync(stream,new {ok=true,id,description=name,oldPrice,price=dto.SalePrice},token);return;
            }

            var stockMatch=Regex.Match(target,@"^/api/mobile/productos/(\d+)/stock$");
            if(stockMatch.Success && method=="POST"){var body=await ReadRequestBodyAsync(stream,request,token);var dto=JsonSerializer.Deserialize<MobileStockDto>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException("Movimiento inválido.");if(dto.Quantity<=0)throw new InvalidOperationException("La cantidad debe ser mayor a cero.");var delta=dto.Type.Equals("SALIDA",StringComparison.OrdinalIgnoreCase)?-dto.Quantity:dto.Quantity;using var cn=Database.Open();using var tx=cn.BeginTransaction();using(var up=cn.CreateCommand()){up.Transaction=tx;up.CommandText="UPDATE products SET stock=stock+$d,updated_at=CURRENT_TIMESTAMP WHERE id=$id AND active=1";up.Parameters.AddWithValue("$d",delta);up.Parameters.AddWithValue("$id",int.Parse(stockMatch.Groups[1].Value,CultureInfo.InvariantCulture));if(up.ExecuteNonQuery()!=1)throw new InvalidOperationException("Producto no encontrado.");}using(var mv=cn.CreateCommand()){mv.Transaction=tx;mv.CommandText="INSERT INTO stock_movements(product_id,movement_type,quantity,reference,user_id) VALUES($id,$t,$q,$r,$u)";mv.Parameters.AddWithValue("$id",int.Parse(stockMatch.Groups[1].Value,CultureInfo.InvariantCulture));mv.Parameters.AddWithValue("$t",dto.Type.Equals("SALIDA",StringComparison.OrdinalIgnoreCase)?"ADJUSTMENT_OUT":"ADJUSTMENT_IN");mv.Parameters.AddWithValue("$q",dto.Quantity);mv.Parameters.AddWithValue("$r",dto.Reference??"AJUSTE DESDE MANAGER");mv.Parameters.AddWithValue("$u",MobileUserId());mv.ExecuteNonQuery();}tx.Commit();AuditService.Log(MobileUserId(),"MOBILE_STOCK_ADJUSTMENT","INVENTARIO",$"Ajuste de stock {dto.Type} · Producto ID {int.Parse(stockMatch.Groups[1].Value,CultureInfo.InvariantCulture)} · Cantidad {dto.Quantity:N3} · Motivo: {dto.Reference}");try{MobileInventoryExcelSync.Export();}catch{}await JsonAsync(stream,new {ok=true,id=int.Parse(stockMatch.Groups[1].Value,CultureInfo.InvariantCulture),stock=GetMobileProduct(int.Parse(stockMatch.Groups[1].Value,CultureInfo.InvariantCulture))},token);return;}

            if(target=="/api/mobile/clientes" && method=="GET"){await JsonAsync(stream,GetMobileCustomers(),token);return;}
            var customerAccount=Regex.Match(target,@"^/api/mobile/clientes/(\d+)/cuenta$");
            if(customerAccount.Success && method=="GET")
            {
                var customerId=int.Parse(customerAccount.Groups[1].Value,CultureInfo.InvariantCulture);
                var customer=GetMobileCustomers().FirstOrDefault(x=>Convert.ToInt32(x.GetType().GetProperty("id")?.GetValue(x)??0)==customerId);
                if(customer==null) throw new InvalidOperationException("Cliente no encontrado.");
                var details=CustomerService.DebtDetails(customerId).Select(x=>new {id=x.Id,dateTime=x.DateTimeText,entryType=x.EntryType,amount=x.Amount,concept=x.Concept,paymentMethod=x.PaymentMethod,saleId=x.SaleId,ticketNumber=x.TicketNumber,products=x.Products}).ToList();
                var balance=CustomerService.Balance(customerId);
                var name=Convert.ToString(customer.GetType().GetProperty("name")?.GetValue(customer))??"Cliente";
                await JsonAsync(stream,new {customerId,customerName=name,balance,details},token); return;
            }
            if(target=="/api/mobile/clientes" && method=="POST"){var body=await ReadRequestBodyAsync(stream,request,token);var dto=JsonSerializer.Deserialize<MobileCustomerDto>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException("Cliente inválido.");if(string.IsNullOrWhiteSpace(dto.Name))throw new InvalidOperationException("El nombre es obligatorio.");using var cn=Database.Open();using var cmd=cn.CreateCommand();cmd.CommandText="INSERT INTO customers(name,document,phone,email,address,credit_limit,active) VALUES($n,$d,$p,$e,$a,$l,1);SELECT last_insert_rowid();";cmd.Parameters.AddWithValue("$n",dto.Name.Trim());cmd.Parameters.AddWithValue("$d",dto.Document??"");cmd.Parameters.AddWithValue("$p",dto.Phone??"");cmd.Parameters.AddWithValue("$e",dto.Email??"");cmd.Parameters.AddWithValue("$a",dto.Address??"");cmd.Parameters.AddWithValue("$l",dto.CreditLimit);var id=Convert.ToInt32(cmd.ExecuteScalar());await JsonAsync(stream,new {ok=true,id},token);return;}
            var customerClearHistory=Regex.Match(target,@"^/api/mobile/clientes/(\d+)/cuenta/limpiar$");
            if(customerClearHistory.Success && method=="POST")
            {
                var customerId=int.Parse(customerClearHistory.Groups[1].Value,CultureInfo.InvariantCulture);
                CustomerService.ClearDebtHistory(customerId);
                AuditService.Log(MobileUserId(),"MOBILE_CUSTOMER_ACCOUNT_CLEAR","CLIENTES",$"Cliente ID {customerId} · historial de cuenta corriente limpiado al quedar la deuda en cero");
                await JsonAsync(stream,new {ok=true,id=customerId},token); return;
            }
            var customerEdit=Regex.Match(target,@"^/api/mobile/clientes/(\d+)$");
            if(customerEdit.Success && method=="PUT")
            {
                var id=int.Parse(customerEdit.Groups[1].Value,CultureInfo.InvariantCulture);
                var body=await ReadRequestBodyAsync(stream,request,token);
                var dto=JsonSerializer.Deserialize<MobileCustomerDto>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException("Cliente inválido.");
                if(id==1) throw new InvalidOperationException("Público General no puede modificarse desde Manager.");
                if(string.IsNullOrWhiteSpace(dto.Name)) throw new InvalidOperationException("El nombre es obligatorio.");
                using var cn=Database.Open(); using var cmd=cn.CreateCommand();
                cmd.CommandText="UPDATE customers SET name=$n,document=$d,phone=$p,email=$e,address=$a,credit_limit=$l WHERE id=$id AND active=1";
                cmd.Parameters.AddWithValue("$id",id);cmd.Parameters.AddWithValue("$n",dto.Name.Trim());cmd.Parameters.AddWithValue("$d",dto.Document??"");cmd.Parameters.AddWithValue("$p",dto.Phone??"");cmd.Parameters.AddWithValue("$e",dto.Email??"");cmd.Parameters.AddWithValue("$a",dto.Address??"");cmd.Parameters.AddWithValue("$l",dto.CreditLimit);
                if(cmd.ExecuteNonQuery()!=1) throw new InvalidOperationException("Cliente no encontrado o inactivo.");
                AuditService.Log(MobileUserId(),"MOBILE_CUSTOMER_UPDATE","CLIENTES",$"Cliente ID {id} modificado desde Manager"); await JsonAsync(stream,new{ok=true,id},token); return;
            }
            if(customerEdit.Success && method=="DELETE")
            {
                var id=int.Parse(customerEdit.Groups[1].Value,CultureInfo.InvariantCulture); if(id==1) throw new InvalidOperationException("Público General no puede eliminarse.");
                var body=await ReadRequestBodyAsync(stream,request,token); var reason="Eliminado desde FerrariPOS Manager";
                try{var raw=JsonSerializer.Deserialize<Dictionary<string,string>>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true}); if(raw!=null && raw.TryGetValue("reason",out var rr)&&!string.IsNullOrWhiteSpace(rr))reason=rr.Trim();}catch{}
                using var cn=Database.Open(); using var tx=cn.BeginTransaction();
                string name="";string doc=""; using(var q=cn.CreateCommand()){q.Transaction=tx;q.CommandText="SELECT name,document FROM customers WHERE id=$id AND active=1";q.Parameters.AddWithValue("$id",id);using var r=q.ExecuteReader();if(!r.Read())throw new InvalidOperationException("Cliente no encontrado o inactivo.");name=r.GetString(0);doc=r.GetString(1);}
                using(var log=cn.CreateCommand()){log.Transaction=tx;log.CommandText="INSERT INTO customer_deletion_log(customer_id,customer_name,customer_document,reason,user_id) VALUES($id,$n,$d,$r,$u)";log.Parameters.AddWithValue("$id",id);log.Parameters.AddWithValue("$n",name);log.Parameters.AddWithValue("$d",doc);log.Parameters.AddWithValue("$r",reason);log.Parameters.AddWithValue("$u",MobileUserId());log.ExecuteNonQuery();}
                using(var del=cn.CreateCommand()){del.Transaction=tx;del.CommandText="UPDATE customers SET active=0 WHERE id=$id";del.Parameters.AddWithValue("$id",id);del.ExecuteNonQuery();} tx.Commit(); AuditService.Log(MobileUserId(),"MOBILE_CUSTOMER_DELETE","CLIENTES",$"Cliente ID {id} eliminado · Motivo: {reason}"); await JsonAsync(stream,new{ok=true,id},token); return;
            }
            var customerPayment=Regex.Match(target,@"^/api/mobile/clientes/(\d+)/abono$");
            if(customerPayment.Success&&method=="POST")
            {
                var body=await ReadRequestBodyAsync(stream,request,token);
                var dto=JsonSerializer.Deserialize<MobileCustomerPaymentDto>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException("Abono inválido.");
                var payments=dto.Payments??new List<MobilePaymentDto>();
                if(payments.Count==0) payments.Add(new MobilePaymentDto{Method=string.IsNullOrWhiteSpace(dto.PaymentMethod)?"EFECTIVO":dto.PaymentMethod,Amount=dto.Amount,Reference=dto.Reference});
                var lines=payments.Where(x=>x.Amount>0).Select(x=>new FerrarisPOS.Models.PaymentLine(SaleService.NormalizePaymentMethod(x.Method),x.Amount,x.Reference??"")).ToList();
                if(lines.Count==0) throw new InvalidOperationException("Ingresá un importe válido.");
                var customerId=int.Parse(customerPayment.Groups[1].Value,CultureInfo.InvariantCulture);
                CustomerService.RegisterPayment(customerId,lines,MobileUserId(),string.IsNullOrWhiteSpace(dto.Concept)?"Abono desde FerrariPOS Manager":dto.Concept);
                AuditService.Log(MobileUserId(),"MOBILE_CUSTOMER_PAYMENT","CLIENTES",$"Cliente ID {customerId} · Abono ${lines.Sum(x=>x.Amount):N2}");
                await JsonAsync(stream,new {ok=true,total=lines.Sum(x=>x.Amount)},token); return;
            }

            if(target=="/api/mobile/ventas"&&method=="GET"){await JsonAsync(stream,GetTodaySales(),token);return;}
            if(target=="/api/mobile/tickets-windows"&&method=="GET")
            {
                var main=FerrarisPOS.Forms.MainForm.CurrentInstance;
                if(main==null) { await JsonAsync(stream,new List<object>(),token); return; }
                await JsonAsync(stream,main.GetWindowsOpenTicketsSnapshot(),token); return;
            }
            var windowsTicketAppend=Regex.Match(target,@"^/api/mobile/tickets-windows/([^/]+)/agregar$");
            if(windowsTicketAppend.Success&&method=="POST")
            {
                if(Database.GetSetting("allow_android_charge","0")!="1") throw new InvalidOperationException("Windows no habilitó el cobro/edición de tickets desde Android.");
                var body=await ReadRequestBodyAsync(stream,request,token);
                var dto=JsonSerializer.Deserialize<MobileWindowsTicketAppendDto>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException("Productos inválidos.");
                if(dto.Items==null||dto.Items.Count==0) throw new InvalidOperationException("Agregá al menos un producto.");
                dto.Items=NormalizeMobileItems(dto.Items);
                var main=FerrarisPOS.Forms.MainForm.CurrentInstance ?? throw new InvalidOperationException("FerrariPOS Windows no está disponible.");
                var items=new List<FerrarisPOS.Models.CartItem>();
                foreach(var x in dto.Items)
                {
                    if(x.IsCommon)
                    {
                        using var ccn=Database.Open(); using var cc=ccn.CreateCommand(); cc.CommandText="SELECT id,description FROM products WHERE barcode='__COMUN__' LIMIT 1"; using var cr=cc.ExecuteReader();
                        if(!cr.Read()) throw new InvalidOperationException("No está configurado el PRODUCTO EN COMÚN.");
                        items.Add(new FerrarisPOS.Models.CartItem{ProductId=cr.GetInt32(0),Barcode="COMÚN",Description=string.IsNullOrWhiteSpace(x.Description)?cr.GetString(1):x.Description??"",UnitPrice=x.UnitPrice, RetailPrice=x.UnitPrice, WholesalePrice=x.UnitPrice, CostPrice=0,Stock=0,IsBulk=false,UsesInventory=false,Quantity=x.Quantity,DiscountAmount=x.Discount,IsCommon=true});
                    }
                    else
                    {
                        using var pcn=Database.Open(); using var pc=pcn.CreateCommand(); pc.CommandText="SELECT barcode,description,sale_price,wholesale_price,cost_price,stock,is_bulk,COALESCE(uses_inventory,1) FROM products WHERE id=$id AND active=1 LIMIT 1"; pc.Parameters.AddWithValue("$id",x.ProductId); using var pr=pc.ExecuteReader();
                        if(!pr.Read()) throw new InvalidOperationException($"El producto {x.ProductId} no existe o está inactivo.");
                        items.Add(new FerrarisPOS.Models.CartItem{ProductId=x.ProductId,Barcode=pr.GetString(0),Description=string.IsNullOrWhiteSpace(x.Description)?pr.GetString(1):x.Description??"",UnitPrice=x.UnitPrice>0?x.UnitPrice:pr.GetDouble(2),RetailPrice=pr.GetDouble(2),WholesalePrice=pr.GetDouble(3),CostPrice=pr.GetDouble(4),Stock=pr.GetDouble(5),IsBulk=pr.GetInt32(6)!=0,UsesInventory=pr.GetInt32(7)!=0,Quantity=x.Quantity,DiscountAmount=x.Discount,IsCommon=false});
                    }
                }
                if(!main.AppendProductsToWindowsTicket(windowsTicketAppend.Groups[1].Value,items)) throw new InvalidOperationException("El ticket de Windows ya no está disponible.");
                AuditService.Log(MobileUserId(),"MOBILE_WINDOWS_TICKET_APPEND","VENTAS",$"Ticket Windows {windowsTicketAppend.Groups[1].Value} · {items.Count} producto(s) agregados desde Android");
                await JsonAsync(stream,new{ok=true},token); return;
            }
            var windowsTicketCharge=Regex.Match(target,@"^/api/mobile/tickets-windows/([^/]+)/cobrar$");
            if(windowsTicketCharge.Success&&method=="POST")
            {
                if(Database.GetSetting("allow_android_charge","0")!="1") throw new InvalidOperationException("Windows no habilitó el cobro de tickets desde Android.");
                var body=await ReadRequestBodyAsync(stream,request,token);
                var dto=JsonSerializer.Deserialize<MobileTableChargeDto>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException("Cobro inválido.");
                var main=FerrarisPOS.Forms.MainForm.CurrentInstance ?? throw new InvalidOperationException("FerrariPOS Windows no está disponible.");
                var ticketId=windowsTicketCharge.Groups[1].Value;
                var snap=main.GetWindowsOpenTicketsSnapshot().FirstOrDefault(x=>string.Equals(Convert.ToString(x.GetType().GetProperty("id")?.GetValue(x)),ticketId,StringComparison.OrdinalIgnoreCase));
                if(snap==null) throw new InvalidOperationException("El ticket de Windows ya no está disponible.");
                var json=JsonSerializer.Serialize(snap);
                var ticket=JsonSerializer.Deserialize<MobileWindowsTicketDto>(json,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException("Ticket inválido.");
                if(ticket.Items==null||ticket.Items.Count==0) throw new InvalidOperationException("El ticket no tiene productos.");
                ticket.Items=NormalizeMobileItems(ticket.Items);
                var items=ticket.Items.Select(x=>new FerrarisPOS.Models.CartItem{ProductId=x.ProductId,Quantity=x.Quantity,UnitPrice=x.UnitPrice,DiscountAmount=x.Discount,Description=x.Description??"",IsCommon=x.IsCommon}).ToList();
                var payments=(dto.Payments??new List<MobilePaymentDto>()).Where(x=>x.Amount>0).Select(x=>new FerrarisPOS.Models.PaymentLine(SaleService.NormalizePaymentMethod(x.Method),x.Amount,x.Reference??"")).ToList();
                if(payments.Count==0) payments.Add(new FerrarisPOS.Models.PaymentLine("EFECTIVO",items.Sum(x=>x.Total)));
                foreach(var pay in payments) if(string.Equals(pay.Method,"MERCADO PAGO",StringComparison.OrdinalIgnoreCase) && !CashService.IsMercadoPagoEnabled()) throw new InvalidOperationException("Mercado Pago no está habilitado en la caja actual.");
                var total=items.Sum(x=>x.Total); var paid=payments.Sum(x=>x.Amount); if(paid+0.01<total) throw new InvalidOperationException($"Falta cobrar {total-paid:N2}."); var change=Math.Max(0,dto.Received-paid);
                var saleId=SaleService.Complete(MobileUserId(),1,items,payments,dto.Received,change,ticket.TableId>0?ticket.TableId:null,"ANDROID_WINDOWS_TICKET","Cobrado desde Android · ticket Windows", "", "N/A", "");
                main.CompleteWindowsTicketFromAndroid(ticketId, SaleService.TicketNumber(saleId), total);
                try{MobileInventoryExcelSync.Export();}catch{}
                AuditService.Log(MobileUserId(),"MOBILE_WINDOWS_TICKET_CHARGE","VENTAS",$"Ticket Windows {ticketId} · Ticket #{SaleService.TicketNumber(saleId)} · Total ${total:N2}");
                await JsonAsync(stream,new{ok=true,id=saleId,ticket=SaleService.TicketNumber(saleId),total},token); return;
            }
            if(target=="/api/mobile/ventas-pendientes"&&method=="POST")
            {
                var body=await ReadRequestBodyAsync(stream,request,token);
                var dto=JsonSerializer.Deserialize<MobilePendingSaleDto>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException("Venta pendiente inválida.");
                if(dto.Items==null||dto.Items.Count==0)throw new InvalidOperationException("La venta no tiene productos.");
                dto.Items=NormalizeMobileItems(dto.Items);
                EnsureMobilePendingTickets();
                using var cn=Database.Open();
                using var cmd=cn.CreateCommand();
                cmd.CommandText="INSERT INTO mobile_pending_tickets(customer_id,customer_name,items_json,notes,created_by,updated_at) VALUES($c,$cn,$i,$n,$u,CURRENT_TIMESTAMP);SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("$c",dto.CustomerId<=0?1:dto.CustomerId);
                cmd.Parameters.AddWithValue("$cn",dto.CustomerName??"");
                cmd.Parameters.AddWithValue("$i",JsonSerializer.Serialize(dto.Items));
                cmd.Parameters.AddWithValue("$n",dto.Notes??"");
                cmd.Parameters.AddWithValue("$u",MobileUserId());
                var id=Convert.ToInt64(cmd.ExecuteScalar());
                AuditService.Log(MobileUserId(),"MOBILE_PENDING_SALE","VENTAS",$"Venta móvil pendiente #{id} · Cliente {dto.CustomerName} · Líneas {dto.Items.Count}");
                await JsonAsync(stream,new{ok=true,id},token);return;
            }
            if(target=="/api/mobile/producto-comun"&&method=="GET")
            {
                using var cn=Database.Open(); using var cmd=cn.CreateCommand();
                cmd.CommandText="SELECT id,barcode,description,active FROM products WHERE barcode='__COMUN__' LIMIT 1";
                using var r=cmd.ExecuteReader();
                if(!r.Read()) throw new InvalidOperationException("No está configurado el PRODUCTO EN COMÚN.");
                await JsonAsync(stream,new{id=r.GetInt32(0),barcode=r.GetString(1),description=r.GetString(2),active=r.GetInt32(3)!=0},token);
                return;
            }
            if(target=="/api/mobile/config"&&method=="GET"){await JsonAsync(stream,new {allowAndroidCharge=Database.GetSetting("allow_android_charge","0")=="1"},token);return;}
            if(target=="/api/mobile/ventas"&&method=="POST")
            {
                var body=await ReadRequestBodyAsync(stream,request,token);
                var dto=JsonSerializer.Deserialize<MobileSaleDto>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException("Venta inválida.");
                if(dto.Items==null||dto.Items.Count==0)throw new InvalidOperationException("La venta no tiene productos.");
                dto.Items=NormalizeMobileItems(dto.Items);
                // Una venta 100% a crédito no mueve efectivo y no debe quedar bloqueada
                // por el estado de la caja. SaleService valida la apertura solamente
                // cuando realmente hay un importe en EFECTIVO.
                var items=dto.Items.Select(x=>new FerrarisPOS.Models.CartItem{ProductId=x.ProductId,Quantity=x.Quantity,UnitPrice=x.UnitPrice,DiscountAmount=x.Discount,Description=x.Description??"",IsCommon=x.IsCommon}).ToList();
                var payments=(dto.Payments??new List<MobilePaymentDto>()).Select(x=>new FerrarisPOS.Models.PaymentLine(SaleService.NormalizePaymentMethod(x.Method),x.Amount,x.Reference??"")).ToList();
                if(payments.Count==0)payments.Add(new FerrarisPOS.Models.PaymentLine("EFECTIVO",items.Sum(x=>x.Total)));
                foreach(var pay in payments)
                    if(string.Equals(pay.Method,"MERCADO PAGO",StringComparison.OrdinalIgnoreCase) && !CashService.IsMercadoPagoEnabled())
                        throw new InvalidOperationException("Mercado Pago no está habilitado en la caja actual. Abrí la caja con Mercado Pago habilitado para poder cobrar por ese medio.");
                var total=items.Sum(x=>x.Total);var paid=payments.Sum(x=>x.Amount);var change=Math.Max(0,dto.Received-paid);
                var saleId=SaleService.Complete(MobileUserId(),dto.CustomerId,items,payments,dto.Received,change,null,"ANDROID",dto.Notes??"",dto.DeliveryAddress??"",dto.DeliveryStatus??"N/A",dto.DiscountReason??"");
                try{MobileInventoryExcelSync.Export();}catch{}
                AuditService.Log(MobileUserId(),"MOBILE_SALE","VENTAS",$"Ticket #{SaleService.TicketNumber(saleId)} · Total ${total:N2}");
                await JsonAsync(stream,new {ok=true,id=saleId,ticket=SaleService.TicketNumber(saleId),total},token);return;
            }
            var returnMatch=Regex.Match(target,@"^/api/mobile/devoluciones/(\d+)$");
            if(returnMatch.Success&&method=="POST"){var body=await ReadRequestBodyAsync(stream,request,token);var dto=JsonSerializer.Deserialize<MobileReturnDto>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException("Devolución inválida.");SaleReturnService.ReturnItem(long.Parse(returnMatch.Groups[1].Value,CultureInfo.InvariantCulture),dto.Quantity,MobileUserId(),string.IsNullOrWhiteSpace(dto.Reason)?"DEVOLUCIÓN DESDE MANAGER":dto.Reason);try{MobileInventoryExcelSync.Export();}catch{}await JsonAsync(stream,new {ok=true},token);return;}
            var cancelMatch=Regex.Match(target,@"^/api/mobile/ventas/(\d+)/cancelar$");
            if(cancelMatch.Success&&method=="POST"){var body=await ReadRequestBodyAsync(stream,request,token);var dto=JsonSerializer.Deserialize<MobileCancelSaleDto>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException("Cancelación inválida.");SaleReturnService.CancelSale(long.Parse(cancelMatch.Groups[1].Value,CultureInfo.InvariantCulture),MobileUserId(),dto.Reason??"");try{MobileInventoryExcelSync.Export();}catch{}await JsonAsync(stream,new {ok=true},token);return;}

            if(target=="/api/mobile/caja"&&method=="GET"){await JsonAsync(stream,GetCashStatus(),token);return;}
            if(target=="/api/mobile/caja/apertura"&&method=="POST")
            {
                var body=await ReadRequestBodyAsync(stream,request,token);var dto=JsonSerializer.Deserialize<MobileCashOpenDto>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException("Apertura inválida.");
                CashService.Open(dto.Amount,MobileUserId(),dto.MercadoPagoEnabled,dto.MercadoPagoOpening,dto.MercadoPagoRetentionPercent);
                if(EmailReportService.IsConfigured) _ = Task.Run(()=>{try{EmailReportService.Send("FerrariPOS · Apertura de caja",$"Apertura de caja realizada desde FerrariPOS Manager.\nFecha: {DateTime.Now:dd/MM/yyyy HH:mm:ss}\nFondo inicial: ${dto.Amount:N2}",(string?)null);}catch{}});
                await JsonAsync(stream,new {ok=true},token);return;
            }
            if(target=="/api/mobile/caja/movimiento"&&method=="POST"){var body=await ReadRequestBodyAsync(stream,request,token);var dto=JsonSerializer.Deserialize<MobileCashMovementDto>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException("Movimiento inválido.");CashService.Movement(dto.Type??"INCOME",dto.Concept??"MOVIMIENTO MANAGER",dto.Amount,MobileUserId(),dto.PaymentMethod??"EFECTIVO");AuditService.Log(MobileUserId(),"MOBILE_CASH_MOVEMENT","CAJA",$"{dto.Type} · ${dto.Amount:N2} · {dto.PaymentMethod}");await JsonAsync(stream,new {ok=true},token);return;}
            if(target=="/api/mobile/caja/cierre"&&method=="POST")
            {
                var body=await ReadRequestBodyAsync(stream,request,token);var dto=JsonSerializer.Deserialize<MobileCashCloseDto>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException("Cierre inválido.");
                var id=CashService.Close(dto.Counted,MobileUserId(),dto.DifferenceReason??"",dto.CountedMercadoPago);
                if(!EmailReportService.IsConfigured) throw new InvalidOperationException("La caja se cerró, pero el correo automático no está configurado en Windows. Revisá F7 → Configuración → correo.");
                var pdf=CashClosingPdfService.Generate(id);
                EmailReportService.Send("FerrariPOS · Cierre de caja",CashClosingPdfService.BuildEmailSummary(id),pdf);
                await JsonAsync(stream,new {ok=true,id,emailSent=true},token);return;
            }

            if(target=="/api/mobile/movimientos-caja"){await JsonAsync(stream,GetCashMovements(),token);return;}
            if(target=="/api/mobile/movimientos-stock"){await JsonAsync(stream,GetStockMovements(),token);return;}
            var voidCash=Regex.Match(target,@"^/api/mobile/movimientos-caja/(\d+)/anular$");
            if(voidCash.Success && method=="POST")
            {
                var id=long.Parse(voidCash.Groups[1].Value,CultureInfo.InvariantCulture);var body=await ReadRequestBodyAsync(stream,request,token);var reason=JsonSerializer.Deserialize<Dictionary<string,string>>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})?.GetValueOrDefault("reason")?.Trim()??"";if(string.IsNullOrWhiteSpace(reason))throw new InvalidOperationException("Indicá el motivo de anulación.");CashService.VoidManualMovement(id,MobileUserId(),reason);AuditService.Log(MobileUserId(),"MOBILE_CASH_VOID","CAJA",$"Movimiento {id} · Motivo: {reason}");await JsonAsync(stream,new{ok=true,id},token);return;
            }
            if(target=="/api/mobile/auditoria" && method=="GET")
            {
                using var cn=OpenReadOnly();using var cmd=cn.CreateCommand();cmd.CommandText="SELECT a.created_at,a.action,a.module,a.details,COALESCE(u.full_name,u.username,'') FROM audit_log a LEFT JOIN users u ON u.id=a.user_id ORDER BY a.id DESC LIMIT 300";using var r=cmd.ExecuteReader();var rows=new List<object>();while(r.Read())rows.Add(new{dateTime=Text(r,0),action=Text(r,1),module=Text(r,2),details=Text(r,3),user=Text(r,4)});await JsonAsync(stream,rows,token);return;
            }
            if(target=="/api/mobile/reportes-detallados" && method=="GET")
            {
                using var cn=OpenReadOnly();var hourly=new List<object>();var cats=new List<object>();var pays=new List<object>();double sales=0;long tickets=0;
                using(var q=cn.CreateCommand()){q.CommandText="SELECT COALESCE(SUM(total),0),COUNT(*) FROM sales WHERE status='COMPLETED' AND date(created_at,'localtime')=date('now','localtime')";using var r=q.ExecuteReader();if(r.Read()){sales=r.GetDouble(0);tickets=r.GetInt64(1);}}
                using(var q=cn.CreateCommand()){q.CommandText="SELECT strftime('%H:00',created_at,'localtime'),COALESCE(SUM(total),0),COUNT(*) FROM sales WHERE status='COMPLETED' AND date(created_at,'localtime')=date('now','localtime') GROUP BY strftime('%H',created_at,'localtime') ORDER BY 1";using var r=q.ExecuteReader();while(r.Read())hourly.Add(new{label=r.GetString(0),value=r.GetDouble(1),count=r.GetInt64(2)});}
                using(var q=cn.CreateCommand()){q.CommandText="SELECT COALESCE(NULLIF(p.category,''),'SIN CATEGORÍA'),COALESCE(SUM(si.total),0),COUNT(DISTINCT s.id) FROM sale_items si JOIN sales s ON s.id=si.sale_id JOIN products p ON p.id=si.product_id WHERE s.status='COMPLETED' AND date(s.created_at,'localtime')=date('now','localtime') GROUP BY COALESCE(NULLIF(p.category,''),'SIN CATEGORÍA') ORDER BY 2 DESC";using var r=q.ExecuteReader();while(r.Read())cats.Add(new{label=r.GetString(0),value=r.GetDouble(1),count=r.GetInt64(2)});}
                using(var q=cn.CreateCommand()){q.CommandText="SELECT method,COALESCE(SUM(amount),0),COUNT(*) FROM payments p JOIN sales s ON s.id=p.sale_id WHERE p.status='APPROVED' AND s.status='COMPLETED' AND date(s.created_at,'localtime')=date('now','localtime') GROUP BY method ORDER BY 2 DESC";using var r=q.ExecuteReader();while(r.Read())pays.Add(new{label=r.GetString(0),value=r.GetDouble(1),count=r.GetInt64(2)});}
                await JsonAsync(stream,new{sales,tickets,averageTicket=tickets>0?sales/tickets:0.0,hourly,categories=cats,payments=pays},token);return;
            }
            if(target=="/api/mobile/promociones" && method=="GET") { await JsonAsync(stream,GetMobilePromotions(),token); return; }
            var promoDelete=Regex.Match(target,@"^/api/mobile/promociones/(\d+)$");
            if(promoDelete.Success&&method=="DELETE")
            {
                var body=await ReadRequestBodyAsync(stream,request,token); var reason=JsonSerializer.Deserialize<Dictionary<string,string>>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})?.GetValueOrDefault("reason")?.Trim()??"";
                if(string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("Indicá el motivo de eliminación de la promoción.");
                var id=long.Parse(promoDelete.Groups[1].Value,CultureInfo.InvariantCulture); using var cn=Database.Open(); using var tx=cn.BeginTransaction(); string name="";
                using(var n=cn.CreateCommand()){n.Transaction=tx;n.CommandText="SELECT name FROM promotions WHERE id=$id LIMIT 1";n.Parameters.AddWithValue("$id",id);name=Convert.ToString(n.ExecuteScalar())??"";}
                if(string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Promoción no encontrada.");
                using(var i=cn.CreateCommand()){i.Transaction=tx;i.CommandText="DELETE FROM promotion_items WHERE promotion_id=$id";i.Parameters.AddWithValue("$id",id);i.ExecuteNonQuery();}
                using(var q=cn.CreateCommand()){q.Transaction=tx;q.CommandText="DELETE FROM promotions WHERE id=$id";q.Parameters.AddWithValue("$id",id);if(q.ExecuteNonQuery()==0)throw new InvalidOperationException("Promoción no encontrada.");}
                tx.Commit(); AuditService.Log(MobileUserId(),"MOBILE_PROMOTION_DELETE","PROMOCIONES",$"Promoción '{name}' (ID {id}) eliminada desde Manager Android · Motivo: {reason}"); await JsonAsync(stream,new{ok=true,id},token); return;
            }
            if(target=="/api/mobile/promociones" && (method=="POST"||method=="PUT"))
            {
                var body=await ReadRequestBodyAsync(stream,request,token); var dto=JsonSerializer.Deserialize<MobilePromotionDto>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException("Promoción inválida.");
                if(string.IsNullOrWhiteSpace(dto.Name)||dto.Price<=0||dto.Items==null||dto.Items.Count==0) throw new InvalidOperationException("La promoción requiere nombre, precio y productos.");
                using var cn=Database.Open(); using var tx=cn.BeginTransaction(); long id=dto.Id;
                if(method=="POST"){using var c=cn.CreateCommand();c.Transaction=tx;c.CommandText="INSERT INTO promotions(name,description,type,amount,promotion_price,active,start_at,end_at) VALUES($n,$d,'PACK',$p,$p,1,$s,$e);SELECT last_insert_rowid();";c.Parameters.AddWithValue("$n",dto.Name.Trim());c.Parameters.AddWithValue("$d",dto.Description??"");c.Parameters.AddWithValue("$p",dto.Price);c.Parameters.AddWithValue("$s",(object?)dto.StartAt??DBNull.Value);c.Parameters.AddWithValue("$e",(object?)dto.EndAt??DBNull.Value);id=Convert.ToInt64(c.ExecuteScalar());}
                else {using var c=cn.CreateCommand();c.Transaction=tx;c.CommandText="UPDATE promotions SET name=$n,description=$d,amount=$p,promotion_price=$p,active=$a,start_at=$s,end_at=$e WHERE id=$id";c.Parameters.AddWithValue("$id",id);c.Parameters.AddWithValue("$n",dto.Name.Trim());c.Parameters.AddWithValue("$d",dto.Description??"");c.Parameters.AddWithValue("$p",dto.Price);c.Parameters.AddWithValue("$a",dto.Active?1:0);c.Parameters.AddWithValue("$s",(object?)dto.StartAt??DBNull.Value);c.Parameters.AddWithValue("$e",(object?)dto.EndAt??DBNull.Value);c.ExecuteNonQuery();}
                using(var d=cn.CreateCommand()){d.Transaction=tx;d.CommandText="DELETE FROM promotion_items WHERE promotion_id=$id";d.Parameters.AddWithValue("$id",id);d.ExecuteNonQuery();}
                foreach(var item in dto.Items){using var c=cn.CreateCommand();c.Transaction=tx;c.CommandText="INSERT INTO promotion_items(promotion_id,product_id,quantity) VALUES($id,$p,$q)";c.Parameters.AddWithValue("$id",id);c.Parameters.AddWithValue("$p",item.ProductId);c.Parameters.AddWithValue("$q",item.Quantity);c.ExecuteNonQuery();}
                tx.Commit(); await JsonAsync(stream,new{ok=true,id},token); return;
            }
            if(target=="/api/mobile/corte-z" && method=="GET")
            {
                var date=DateTime.Today; var result=GeneralCashClosingService.Generate(date);
                await JsonAsync(stream,new{ok=true,date=date.ToString("yyyy-MM-dd"),title=$"CORTE Z GENERAL · {date:dd/MM/yyyy}",text=result.text,tickets=result.tickets,totalSales=result.totalSales,sessions=result.sessions,cashiers=result.cashiers,totalExpectedCash=result.totalExpectedCash},token);return;
            }
            if(target=="/api/mobile/analitica" && method=="GET")
            {
                using var cn=Database.Open();var top=new List<object>();var low=new List<object>();var cust=new List<object>();var sup=new List<object>();var mix=new List<object>();
                using(var q=cn.CreateCommand()){q.CommandText="SELECT si.description,SUM(si.quantity) FROM sale_items si JOIN sales s ON s.id=si.sale_id WHERE s.status='COMPLETED' AND date(s.created_at,'localtime')>=date('now','-30 day','localtime') GROUP BY si.product_id,si.description ORDER BY SUM(si.quantity) DESC LIMIT 8";using var r=q.ExecuteReader();while(r.Read())top.Add(new{label=r.GetString(0),value=r.GetDouble(1)});}
                using(var q=cn.CreateCommand()){q.CommandText="SELECT p.description,MAX(0,p.min_stock-p.stock) FROM products p WHERE p.active=1 AND p.uses_inventory=1 AND p.stock<=p.min_stock ORDER BY (p.stock-p.min_stock) ASC,p.description LIMIT 8";using var r=q.ExecuteReader();while(r.Read())low.Add(new{label=r.GetString(0),value=r.GetDouble(1)});}
                using(var q=cn.CreateCommand()){q.CommandText="SELECT c.name,ROUND(SUM(CASE WHEN ca.entry_type='SALE' THEN ca.amount WHEN ca.entry_type='PAYMENT' THEN -ca.amount ELSE 0 END),2) FROM customer_accounts ca JOIN customers c ON c.id=ca.customer_id GROUP BY ca.customer_id,c.name HAVING SUM(CASE WHEN ca.entry_type='SALE' THEN ca.amount WHEN ca.entry_type='PAYMENT' THEN -ca.amount ELSE 0 END)>0 ORDER BY 2 DESC LIMIT 8";using var r=q.ExecuteReader();while(r.Read())cust.Add(new{label=r.GetString(0),value=r.GetDouble(1)});}
                using(var q=cn.CreateCommand()){q.CommandText="SELECT s.name,ROUND(SUM(CASE WHEN p.stock<=p.min_stock THEN MAX(0,p.min_stock-p.stock) ELSE 0 END),2) FROM supplier_products sp JOIN suppliers s ON s.id=sp.supplier_id JOIN products p ON p.id=sp.product_id WHERE p.active=1 AND p.uses_inventory=1 GROUP BY s.id,s.name HAVING SUM(CASE WHEN p.stock<=p.min_stock THEN MAX(0,p.min_stock-p.stock) ELSE 0 END)>0 ORDER BY 2 DESC LIMIT 8";using var r=q.ExecuteReader();while(r.Read())sup.Add(new{label=r.GetString(0),value=r.GetDouble(1)});}
                using(var q=cn.CreateCommand()){q.CommandText="SELECT method,SUM(amount) FROM payments WHERE status='APPROVED' AND amount>0 AND date((SELECT created_at FROM sales WHERE id=payments.sale_id),'localtime')>=date('now','-30 day','localtime') GROUP BY method ORDER BY 2 DESC";using var r=q.ExecuteReader();while(r.Read())mix.Add(new{label=r.GetString(0),value=r.GetDouble(1)});}
                await JsonAsync(stream,new{topProducts=top,lowStock=low,customers=cust,suppliers=sup,paymentMix=mix},token);return;
            }
            if(target=="/api/mobile/salones" && method=="GET") { await JsonAsync(stream,GetMobileSalons(),token); return; }
            if(target.StartsWith("/api/mobile/mesas") && method=="GET") { int? sid=null;var qm=Regex.Match(target,@"[?&]salonId=(\d+)",RegexOptions.IgnoreCase);if(qm.Success&&int.TryParse(qm.Groups[1].Value,out var x))sid=x;await JsonAsync(stream,GetMobileTables(sid),token);return; }
            if(target=="/api/mobile/tickets-abiertos" && method=="GET") { await JsonAsync(stream,GetMobileOpenTickets(),token); return; }
            if(target=="/api/mobile/tickets-abiertos" && method=="POST")
            {
                var body=await ReadRequestBodyAsync(stream,request,token);var dto=JsonSerializer.Deserialize<MobileOpenTicketDto>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException("Ticket abierto inválido.");
                if(dto.Items==null||dto.Items.Count==0)throw new InvalidOperationException("El ticket abierto debe tener productos.");
                dto.Items=NormalizeMobileItems(dto.Items);
                EnsureMobileOpenTickets(); using var cn=Database.Open();using var cmd=cn.CreateCommand();cmd.CommandText="INSERT INTO mobile_open_tickets(table_id,table_name,customer_id,customer_name,items_json,notes,created_by,updated_at) VALUES($t,$tn,$c,$cn,$i,$n,$u,CURRENT_TIMESTAMP) ON CONFLICT(table_id) DO UPDATE SET customer_id=excluded.customer_id,customer_name=excluded.customer_name,items_json=excluded.items_json,notes=excluded.notes,updated_at=CURRENT_TIMESTAMP";cmd.Parameters.AddWithValue("$t",dto.TableId);cmd.Parameters.AddWithValue("$tn",dto.TableName??$"Mesa {dto.TableId}");cmd.Parameters.AddWithValue("$c",dto.CustomerId);cmd.Parameters.AddWithValue("$cn",dto.CustomerName??"");cmd.Parameters.AddWithValue("$i",JsonSerializer.Serialize(dto.Items));cmd.Parameters.AddWithValue("$n",dto.Notes??"");cmd.Parameters.AddWithValue("$u",MobileUserId());cmd.ExecuteNonQuery();AuditService.Log(MobileUserId(),"MOBILE_TABLE_SAVE","MESAS",$"Mesa {dto.TableId} · Ticket actualizado");await JsonAsync(stream,new{ok=true},token);return;
            }
            var appendTicket=Regex.Match(target,@"^/api/mobile/tickets-abiertos/(\d+)/agregar$");
            if(appendTicket.Success&&method=="POST")
            {
                var body=await ReadRequestBodyAsync(stream,request,token);var dto=JsonSerializer.Deserialize<MobileOpenTicketDto>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException("Productos para mesa inválidos.");
                if(dto.Items==null||dto.Items.Count==0)throw new InvalidOperationException("No hay productos para agregar a la mesa.");
                dto.Items=NormalizeMobileItems(dto.Items);
                var tableId=int.Parse(appendTicket.Groups[1].Value,CultureInfo.InvariantCulture);EnsureMobileOpenTickets();
                var existing=new List<MobileSaleItemDto>();int existingCustomer=1;string existingName="";string existingTableName=$"Mesa {tableId}";string existingNotes="";
                using(var cn=Database.Open()){using var q=cn.CreateCommand();q.CommandText="SELECT table_name,customer_id,customer_name,items_json,notes FROM mobile_open_tickets WHERE table_id=$t";q.Parameters.AddWithValue("$t",tableId);using var r=q.ExecuteReader();if(r.Read()){existingTableName=r.GetString(0);existingCustomer=r.GetInt32(1);existingName=r.GetString(2);existing=JsonSerializer.Deserialize<List<MobileSaleItemDto>>(r.GetString(3))??new List<MobileSaleItemDto>();existingNotes=r.GetString(4);}}
                if(existing.Count==0 && WindowsExternalTableSnapshot.TryGetValue(tableId,out var localJson)){try{var old=JsonSerializer.Deserialize<MobileOpenTicketDto>(localJson,new JsonSerializerOptions{PropertyNameCaseInsensitive=true});if(old?.Items!=null){existing=old.Items;existingTableName=old.TableName??existingTableName;existingCustomer=old.CustomerId;existingName=old.CustomerName??existingName;existingNotes=old.Notes??existingNotes;}}catch{}}
                var combined=existing.Concat(dto.Items).ToList();using(var cn=Database.Open()){using var q=cn.CreateCommand();q.CommandText="INSERT INTO mobile_open_tickets(table_id,table_name,customer_id,customer_name,items_json,notes,created_by,updated_at) VALUES($t,$tn,$c,$cn,$i,$n,$u,CURRENT_TIMESTAMP) ON CONFLICT(table_id) DO UPDATE SET table_name=excluded.table_name,customer_id=excluded.customer_id,customer_name=excluded.customer_name,items_json=excluded.items_json,notes=excluded.notes,updated_at=CURRENT_TIMESTAMP";q.Parameters.AddWithValue("$t",tableId);q.Parameters.AddWithValue("$tn",dto.TableName??existingTableName);q.Parameters.AddWithValue("$c",dto.CustomerId>1?dto.CustomerId:existingCustomer);q.Parameters.AddWithValue("$cn",string.IsNullOrWhiteSpace(dto.CustomerName)?existingName:dto.CustomerName);q.Parameters.AddWithValue("$i",JsonSerializer.Serialize(combined));q.Parameters.AddWithValue("$n",string.IsNullOrWhiteSpace(dto.Notes)?existingNotes:dto.Notes);q.Parameters.AddWithValue("$u",MobileUserId());q.ExecuteNonQuery();}
                AuditService.Log(MobileUserId(),"MOBILE_TABLE_APPEND","MESAS",$"Mesa {tableId} · Se agregaron {dto.Items.Count} línea(s) · Total líneas {combined.Count}");await JsonAsync(stream,new{ok=true,count=combined.Count},token);return;
            }
            var chargeTicket=Regex.Match(target,@"^/api/mobile/mesas/(\d+)/cobrar$");
            if(chargeTicket.Success&&method=="POST")
            {
                var body=await ReadRequestBodyAsync(stream,request,token);var payDto=JsonSerializer.Deserialize<MobileTableChargeDto>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException("Cobro de mesa inválido.");
                var tableId=int.Parse(chargeTicket.Groups[1].Value,CultureInfo.InvariantCulture);EnsureMobileOpenTickets();MobileOpenTicketDto? ticket=null;
                using(var cn=Database.Open()){using var q=cn.CreateCommand();q.CommandText="SELECT table_name,customer_id,customer_name,items_json,notes FROM mobile_open_tickets WHERE table_id=$t";q.Parameters.AddWithValue("$t",tableId);using var r=q.ExecuteReader();if(r.Read()) ticket=new MobileOpenTicketDto{TableId=tableId,TableName=r.GetString(0),CustomerId=r.GetInt32(1),CustomerName=r.GetString(2),Items=JsonSerializer.Deserialize<List<MobileSaleItemDto>>(r.GetString(3)),Notes=r.GetString(4)};}
                if(ticket==null && FerrarisPOS.Forms.MainForm.CurrentInstance is { } main){var json=main.GetExternalTableTicketJson(tableId);if(!string.IsNullOrWhiteSpace(json))ticket=JsonSerializer.Deserialize<MobileOpenTicketDto>(json,new JsonSerializerOptions{PropertyNameCaseInsensitive=true});}
                if(ticket?.Items==null||ticket.Items.Count==0)throw new InvalidOperationException("La mesa no tiene un ticket abierto con productos para cobrar.");
                // V45.1: tickets de mesa creados antes de la corrección V43 podían guardar
                // PRODUCTO COMÚN con ProductId=0. Al cobrar, sale_items exige una FK válida.
                // Normalizamos nuevamente el ticket justo antes de convertirlo en venta para
                // que esos tickets antiguos también puedan cobrarse sin perder sus productos.
                ticket.Items=NormalizeMobileItems(ticket.Items);
                if(!CashService.IsOpen())throw new InvalidOperationException("No hay una caja abierta. Abrí la caja antes de cobrar la mesa.");
                var items=ticket.Items.Select(x=>new FerrarisPOS.Models.CartItem{ProductId=x.ProductId,Quantity=x.Quantity,UnitPrice=x.UnitPrice,DiscountAmount=x.Discount,Description=x.Description??"",IsCommon=x.IsCommon}).ToList();
                var payments=(payDto.Payments??new List<MobilePaymentDto>()).Where(x=>x.Amount>0).Select(x=>new FerrarisPOS.Models.PaymentLine(SaleService.NormalizePaymentMethod(x.Method),x.Amount,x.Reference??"")).ToList();if(payments.Count==0)payments.Add(new FerrarisPOS.Models.PaymentLine("EFECTIVO",items.Sum(x=>x.Total)));var total=items.Sum(x=>x.Total);var paid=payments.Sum(x=>x.Amount);if(paid+0.01<total)throw new InvalidOperationException($"Falta cobrar {total-paid:N2}.");var change=Math.Max(0,payDto.Received-paid);
                var saleId=SaleService.Complete(MobileUserId(),ticket.CustomerId,items,payments,payDto.Received,change,tableId,"ANDROID_MESA",ticket.Notes??"", "", "N/A", "", true);try{MobileInventoryExcelSync.Export();}catch{}RemoveMobileOpenTicket(tableId);FerrarisPOS.Forms.MainForm.CurrentInstance?.CompleteExternalTableCharge(tableId, SaleService.TicketNumber(saleId), total);AuditService.Log(MobileUserId(),"MOBILE_TABLE_CHARGE","MESAS",$"Mesa {tableId} · Ticket #{SaleService.TicketNumber(saleId)} · Total ${total:N2}");await JsonAsync(stream,new{ok=true,id=saleId,ticket=SaleService.TicketNumber(saleId),total},token);return;
            }
            var openTicket=Regex.Match(target,@"^/api/mobile/tickets-abiertos/(\d+)$");
            if(openTicket.Success && method=="DELETE"){EnsureMobileOpenTickets();using var cn=Database.Open();using var cmd=cn.CreateCommand();cmd.CommandText="DELETE FROM mobile_open_tickets WHERE table_id=$t";cmd.Parameters.AddWithValue("$t",int.Parse(openTicket.Groups[1].Value));cmd.ExecuteNonQuery();await JsonAsync(stream,new{ok=true},token);return;}
            var releaseTable=Regex.Match(target,@"^/api/mobile/mesas/(\d+)/liberar$");
            if(releaseTable.Success&&method=="POST"){var tableId=int.Parse(releaseTable.Groups[1].Value,CultureInfo.InvariantCulture);RemoveMobileOpenTicket(tableId);FerrarisPOS.Forms.MainForm.CurrentInstance?.CompleteExternalTableCharge(tableId);AuditService.Log(MobileUserId(),"MOBILE_TABLE_CHARGE","MESAS",$"Mesa {tableId} cobrada y liberada desde Manager");await JsonAsync(stream,new{ok=true},token);return;}
            if(target=="/api/mobile/proveedores" && method=="GET"){await JsonAsync(stream,GetMobileSuppliers(),token);return;}
            if(target=="/api/mobile/proveedores" && method=="POST"){var body=await ReadRequestBodyAsync(stream,request,token);var dto=JsonSerializer.Deserialize<MobileSupplierDto>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException("Proveedor inválido.");if(string.IsNullOrWhiteSpace(dto.Name))throw new InvalidOperationException("El nombre es obligatorio.");using var cn=Database.Open();using var cmd=cn.CreateCommand();cmd.CommandText="INSERT INTO suppliers(name,document,phone,email,address,active) VALUES($n,$d,$p,$e,$a,1);SELECT last_insert_rowid();";cmd.Parameters.AddWithValue("$n",dto.Name.Trim());cmd.Parameters.AddWithValue("$d",dto.Document??"");cmd.Parameters.AddWithValue("$p",dto.Phone??"");cmd.Parameters.AddWithValue("$e",dto.Email??"");cmd.Parameters.AddWithValue("$a",dto.Address??"");var id=Convert.ToInt32(cmd.ExecuteScalar());AuditService.Log(MobileUserId(),"MOBILE_SUPPLIER_CREATE","PROVEEDORES",$"Proveedor ID {id}");await JsonAsync(stream,new{ok=true,id},token);return;}
            var supplierEdit=Regex.Match(target,@"^/api/mobile/proveedores/(\d+)$");
            if(supplierEdit.Success && method=="PUT")
            {
                var id=int.Parse(supplierEdit.Groups[1].Value,CultureInfo.InvariantCulture);var body=await ReadRequestBodyAsync(stream,request,token);var dto=JsonSerializer.Deserialize<MobileSupplierDto>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException("Proveedor inválido.");if(string.IsNullOrWhiteSpace(dto.Name))throw new InvalidOperationException("El nombre es obligatorio.");using var cn=Database.Open();using var cmd=cn.CreateCommand();cmd.CommandText="UPDATE suppliers SET name=$n,document=$d,phone=$p,email=$e,address=$a WHERE id=$id AND active=1";cmd.Parameters.AddWithValue("$id",id);cmd.Parameters.AddWithValue("$n",dto.Name.Trim());cmd.Parameters.AddWithValue("$d",dto.Document??"");cmd.Parameters.AddWithValue("$p",dto.Phone??"");cmd.Parameters.AddWithValue("$e",dto.Email??"");cmd.Parameters.AddWithValue("$a",dto.Address??"");if(cmd.ExecuteNonQuery()!=1)throw new InvalidOperationException("Proveedor no encontrado o inactivo.");AuditService.Log(MobileUserId(),"MOBILE_SUPPLIER_UPDATE","PROVEEDORES",$"Proveedor ID {id}");await JsonAsync(stream,new{ok=true,id},token);return;
            }
            if(supplierEdit.Success && method=="DELETE")
            {
                var id=int.Parse(supplierEdit.Groups[1].Value,CultureInfo.InvariantCulture);
                var body=await ReadRequestBodyAsync(stream,request,token);
                var reason=JsonSerializer.Deserialize<Dictionary<string,string>>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})?.GetValueOrDefault("reason")?.Trim()??"";
                if(string.IsNullOrWhiteSpace(reason))throw new InvalidOperationException("Indicá el motivo de eliminación.");
                using var cn=Database.Open(); using var tx=cn.BeginTransaction(); string supplierName="";
                using(var find=cn.CreateCommand()){find.Transaction=tx;find.CommandText="SELECT name FROM suppliers WHERE id=$id AND active=1";find.Parameters.AddWithValue("$id",id);supplierName=Convert.ToString(find.ExecuteScalar())??"";}
                if(string.IsNullOrWhiteSpace(supplierName))throw new InvalidOperationException("Proveedor no encontrado o ya inactivo.");
                using(var log=cn.CreateCommand()){log.Transaction=tx;log.CommandText="INSERT INTO supplier_deletion_log(supplier_id,supplier_name,reason,user_id) VALUES($id,$n,$r,$u)";log.Parameters.AddWithValue("$id",id);log.Parameters.AddWithValue("$n",supplierName);log.Parameters.AddWithValue("$r",reason);log.Parameters.AddWithValue("$u",MobileUserId());log.ExecuteNonQuery();}
                using(var cmd=cn.CreateCommand()){cmd.Transaction=tx;cmd.CommandText="UPDATE suppliers SET active=0 WHERE id=$id AND active=1";cmd.Parameters.AddWithValue("$id",id);if(cmd.ExecuteNonQuery()!=1)throw new InvalidOperationException("Proveedor no encontrado o ya inactivo.");}
                tx.Commit(); AuditService.Log(MobileUserId(),"MOBILE_SUPPLIER_DELETE","PROVEEDORES",$"Proveedor {supplierName} · Motivo: {reason}"); await JsonAsync(stream,new{ok=true,id},token);return;
            }
            var supplierProductsMatch=Regex.Match(target,@"^/api/mobile/proveedores/(\d+)/productos$");
            if(supplierProductsMatch.Success && method=="GET")
            {
                var sid=int.Parse(supplierProductsMatch.Groups[1].Value,CultureInfo.InvariantCulture);
                var qs=rawTarget.Contains('?')?rawTarget.Split('?',2)[1]:"";
                var q=Regex.Match(qs,@"(?:^|&)q=([^&]*)",RegexOptions.IgnoreCase).Groups[1].Value;
                q=Uri.UnescapeDataString((q??"").Replace("+"," ")).Trim();
                using var cn=Database.Open(); using var cmd=cn.CreateCommand();
                cmd.CommandText=@"SELECT p.id,p.description,p.barcode,p.stock,COALESCE(sp.unit_cost,p.cost_price)
                                  FROM products p LEFT JOIN supplier_products sp ON sp.product_id=p.id AND sp.supplier_id=$sid
                                  WHERE p.active=1
                                    AND (EXISTS(SELECT 1 FROM supplier_products sx WHERE sx.supplier_id=$sid) AND sp.product_id IS NOT NULL
                                         OR NOT EXISTS(SELECT 1 FROM supplier_products sx WHERE sx.supplier_id=$sid))
                                    AND ($q='' OR p.description LIKE $like OR p.barcode LIKE $like)
                                  ORDER BY p.description";
                cmd.Parameters.AddWithValue("$sid",sid); cmd.Parameters.AddWithValue("$q",q); cmd.Parameters.AddWithValue("$like","%"+q+"%");
                var result=new List<object>(); using var r=cmd.ExecuteReader();
                while(r.Read()) result.Add(new{id=r.GetInt32(0),description=r.GetString(1),barcode=r.GetString(2),stock=r.GetDouble(3),costPrice=r.GetDouble(4)});
                await JsonAsync(stream,result,token); return;
            }
            if(target=="/api/mobile/compras" && method=="GET"){await JsonAsync(stream,GetMobilePurchases(),token);return;}
            var purchaseMatch=Regex.Match(target,@"^/api/mobile/compras/(\d+)$");
            if(purchaseMatch.Success && method=="DELETE")
            {
                var purchaseId=long.Parse(purchaseMatch.Groups[1].Value,CultureInfo.InvariantCulture);
                using var cn=Database.Open(); using var tx=cn.BeginTransaction();
                using var chk=cn.CreateCommand(); chk.Transaction=tx; chk.CommandText="SELECT status FROM purchase_orders WHERE id=$id"; chk.Parameters.AddWithValue("$id",purchaseId);
                var status=chk.ExecuteScalar()?.ToString();
                if(status==null) throw new InvalidOperationException("Orden de compra no encontrada.");
                if(string.Equals(status,"RECEIVED",StringComparison.OrdinalIgnoreCase) || string.Equals(status,"COMPLETED",StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("La orden ya fue recibida y no puede eliminarse desde Manager.");
                using(var di=cn.CreateCommand()){di.Transaction=tx;di.CommandText="DELETE FROM purchase_order_items WHERE order_id=$id";di.Parameters.AddWithValue("$id",purchaseId);di.ExecuteNonQuery();}
                using var del=cn.CreateCommand(); del.Transaction=tx; del.CommandText="DELETE FROM purchase_orders WHERE id=$id"; del.Parameters.AddWithValue("$id",purchaseId);
                if(del.ExecuteNonQuery()!=1) throw new InvalidOperationException("No se pudo eliminar la orden.");
                tx.Commit(); await JsonAsync(stream,new{ok=true},token); return;
            }
            if(purchaseMatch.Success && method=="GET")
            {
                var purchaseId=long.Parse(purchaseMatch.Groups[1].Value,CultureInfo.InvariantCulture);
                using var cn=Database.Open(); using var cmd=cn.CreateCommand();
                cmd.CommandText=@"SELECT po.id,po.order_no,po.supplier_id,s.name,po.status,po.notes,COALESCE(po.expected_date,'')
                                  FROM purchase_orders po JOIN suppliers s ON s.id=po.supplier_id WHERE po.id=$id";
                cmd.Parameters.AddWithValue("$id",purchaseId);
                long orderId;string orderNo,supplierName,status,notes,expectedDate;int supplierId;
                using(var r=cmd.ExecuteReader()){if(!r.Read()){await JsonAsync(stream,new{error="Orden no encontrada."},token,404);return;}orderId=r.GetInt64(0);orderNo=r.GetString(1);supplierId=r.GetInt32(2);supplierName=r.GetString(3);status=r.GetString(4);notes=r.IsDBNull(5)?"":r.GetString(5);expectedDate=r.IsDBNull(6)?"":r.GetString(6);}
                var items=new List<object>(); using(var q=cn.CreateCommand()){q.CommandText=@"SELECT poi.product_id,p.description,poi.quantity,poi.unit_cost,COALESCE(poi.notes,''),COALESCE(poi.received_quantity,0),MAX(0,poi.quantity-COALESCE(poi.received_quantity,0)) FROM purchase_order_items poi JOIN products p ON p.id=poi.product_id WHERE poi.order_id=$id ORDER BY poi.id";q.Parameters.AddWithValue("$id",purchaseId);using var ir=q.ExecuteReader();while(ir.Read())items.Add(new{productId=ir.GetInt32(0),description=ir.GetString(1),quantity=ir.GetDouble(2),unitCost=ir.GetDouble(3),notes=ir.GetString(4),receivedQuantity=ir.GetDouble(5),remainingQuantity=ir.GetDouble(6)});}
                await JsonAsync(stream,new{id=orderId,orderNo,supplierId,supplier=supplierName,status,notes,expectedDate,items},token);return;
            }
            var purchaseReceiveMatch=Regex.Match(target, @"^/api/mobile/compras/(\d+)/recibir$");
            if(purchaseReceiveMatch.Success && method=="POST")
            {
                var purchaseId=long.Parse(purchaseReceiveMatch.Groups[1].Value,CultureInfo.InvariantCulture);
                var body=await ReadRequestBodyAsync(stream,request,token);
                var dto=JsonSerializer.Deserialize<MobilePurchaseReceiveDto>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException("Recepción inválida.");
                if(dto.Items==null || dto.Items.Count==0) throw new InvalidOperationException("Indicá al menos una cantidad recibida.");
                using var cn=Database.Open(); using var tx=cn.BeginTransaction();
                string status;
                string currentNotes="";
                using(var chk=cn.CreateCommand())
                {
                    chk.Transaction=tx; chk.CommandText="SELECT status,COALESCE(notes,'') FROM purchase_orders WHERE id=$id"; chk.Parameters.AddWithValue("$id",purchaseId);
                    using var rr=chk.ExecuteReader();
                    if(!rr.Read()) throw new InvalidOperationException("Orden de compra no encontrada.");
                    status=rr.GetString(0); currentNotes=rr.GetString(1);
                }
                if(string.Equals(status,"RECEIVED",StringComparison.OrdinalIgnoreCase) || string.Equals(status,"RECEIVED_WITH_DIFFERENCE",StringComparison.OrdinalIgnoreCase) || string.Equals(status,"COMPLETED",StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("La orden ya fue recibida completamente.");
                if(string.Equals(status,"CANCELLED",StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("La orden está cancelada.");

                double receivedNow=0; bool hasOverage=false; var overages=new List<string>();
                foreach(var x in dto.Items.Where(x=>x.Quantity>0))
                {
                    if(x.ProductId<=0) throw new InvalidOperationException("Producto inválido en la recepción.");
                    if(x.Quantity<0) throw new InvalidOperationException("La cantidad recibida no puede ser negativa.");
                    if(x.UnitCost<0) throw new InvalidOperationException("El costo no puede ser negativo.");
                    double ordered=0, already=0; string productDescription=$"Producto {x.ProductId}";
                    using(var q=cn.CreateCommand())
                    {
                        q.Transaction=tx; q.CommandText="SELECT poi.quantity,COALESCE(poi.received_quantity,0),COALESCE(p.description,'') FROM purchase_order_items poi JOIN products p ON p.id=poi.product_id WHERE poi.order_id=$o AND poi.product_id=$p"; q.Parameters.AddWithValue("$o",purchaseId); q.Parameters.AddWithValue("$p",x.ProductId);
                        using var r=q.ExecuteReader();
                        if(!r.Read()) throw new InvalidOperationException($"El producto {x.ProductId} no pertenece a la orden.");
                        ordered=r.GetDouble(0); already=r.GetDouble(1);
                        var description=r.IsDBNull(2)?"":r.GetString(2);
                        if(!string.IsNullOrWhiteSpace(description)) productDescription=description;
                    }
                    var remaining=Math.Max(0,ordered-already);
                    if(x.Quantity>remaining+0.000001)
                    {
                        hasOverage=true;
                        overages.Add($"{productDescription}: pendiente {remaining:0.###}, recibido {x.Quantity:0.###}");
                        if(!dto.DifferenceMode) throw new InvalidOperationException($"La cantidad recibida supera lo pendiente para el producto {x.ProductId}. Usá 'RECIBIR CON DIFERENCIA'.");
                    }
                    if(InventoryControlService.IsGlobalEnabled)
                    {
                        using var up=cn.CreateCommand(); up.Transaction=tx;
                        up.CommandText="UPDATE products SET stock=stock+$q,cost_price=$c,updated_at=CURRENT_TIMESTAMP WHERE id=$id";
                        up.Parameters.AddWithValue("$q",x.Quantity); up.Parameters.AddWithValue("$c",x.UnitCost); up.Parameters.AddWithValue("$id",x.ProductId); up.ExecuteNonQuery();
                        using var sm=cn.CreateCommand(); sm.Transaction=tx;
                        sm.CommandText="INSERT INTO stock_movements(product_id,movement_type,quantity,reference,user_id) VALUES($p,'COMPRA',$q,$ref,$u)";
                        sm.Parameters.AddWithValue("$p",x.ProductId); sm.Parameters.AddWithValue("$q",x.Quantity); sm.Parameters.AddWithValue("$ref","OC #"+purchaseId); sm.Parameters.AddWithValue("$u",MobileUserId()); sm.ExecuteNonQuery();
                    }
                    using(var upi=cn.CreateCommand())
                    {
                        upi.Transaction=tx; upi.CommandText="UPDATE purchase_order_items SET received_quantity=received_quantity+$q,unit_cost=$c,notes=CASE WHEN $n='' THEN notes ELSE $n END WHERE order_id=$o AND product_id=$p";
                        upi.Parameters.AddWithValue("$q",x.Quantity); upi.Parameters.AddWithValue("$c",x.UnitCost); upi.Parameters.AddWithValue("$n",x.Notes??""); upi.Parameters.AddWithValue("$o",purchaseId); upi.Parameters.AddWithValue("$p",x.ProductId); upi.ExecuteNonQuery();
                    }
                    receivedNow += x.Quantity*x.UnitCost;
                }
                if(receivedNow<=0) throw new InvalidOperationException("Indicá al menos una cantidad REAL recibida.");
                bool pendingAfter;
                using(var pending=cn.CreateCommand())
                {
                    pending.Transaction=tx; pending.CommandText="SELECT EXISTS(SELECT 1 FROM purchase_order_items WHERE order_id=$id AND received_quantity < quantity)"; pending.Parameters.AddWithValue("$id",purchaseId); pendingAfter=Convert.ToInt32(pending.ExecuteScalar())!=0;
                }
                if(dto.DifferenceMode && (hasOverage || pendingAfter) && string.IsNullOrWhiteSpace(dto.Note)) throw new InvalidOperationException("Indicá el motivo de la diferencia o del cierre con pendientes.");
                var finalStatus=!pendingAfter ? (hasOverage ? "RECEIVED_WITH_DIFFERENCE" : "RECEIVED") : (dto.CloseComplete ? "RECEIVED_WITH_DIFFERENCE" : "PARTIAL");
                var finalNotes=currentNotes;
                if(!string.IsNullOrWhiteSpace(dto.Note)) finalNotes=string.IsNullOrWhiteSpace(finalNotes)?dto.Note.Trim():finalNotes+"\n"+dto.Note.Trim();
                if(hasOverage) finalNotes=(string.IsNullOrWhiteSpace(finalNotes)?"":""+finalNotes+"\n")+"EXCEDENTE RECIBIDO DESDE MANAGER: "+string.Join(" | ",overages);
                using(var st=cn.CreateCommand())
                {
                    st.Transaction=tx; st.CommandText="UPDATE purchase_orders SET status=$status,notes=$notes,received_total=COALESCE((SELECT SUM(received_quantity*unit_cost) FROM purchase_order_items WHERE order_id=$id),0),received_date=CURRENT_TIMESTAMP,updated_at=CURRENT_TIMESTAMP WHERE id=$id";
                    st.Parameters.AddWithValue("$status",finalStatus); st.Parameters.AddWithValue("$notes",finalNotes); st.Parameters.AddWithValue("$id",purchaseId); st.ExecuteNonQuery();
                }
                tx.Commit(); AuditService.Log(MobileUserId(),"MOBILE_PURCHASE_RECEIVE","COMPRAS", "Orden ID " + purchaseId + " · " + finalStatus + " · Recibido ahora $" + receivedNow.ToString("N2", CultureInfo.InvariantCulture));
                await JsonAsync(stream,new{ok=true,id=purchaseId,status=finalStatus,receivedNow},token); return;
            }
            if(purchaseMatch.Success && method=="PUT")
            {
                var purchaseId=long.Parse(purchaseMatch.Groups[1].Value,CultureInfo.InvariantCulture);
                var body=await ReadRequestBodyAsync(stream,request,token); var dto=JsonSerializer.Deserialize<MobilePurchaseDto>(body,new JsonSerializerOptions{PropertyNameCaseInsensitive=true})??throw new InvalidOperationException("Compra inválida.");
                if(dto.SupplierId<=0||dto.Items==null||dto.Items.Count==0)throw new InvalidOperationException("Proveedor y productos son obligatorios.");
                using var cn=Database.Open();using var tx=cn.BeginTransaction();
                using(var chk=cn.CreateCommand()){chk.Transaction=tx;chk.CommandText="SELECT status FROM purchase_orders WHERE id=$id";chk.Parameters.AddWithValue("$id",purchaseId);var st=chk.ExecuteScalar()?.ToString();if(st==null)throw new InvalidOperationException("Orden no encontrada.");if(!string.Equals(st,"DRAFT",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Solo se puede modificar una orden en borrador.");}
                var total=dto.Items.Sum(x=>x.Quantity*x.UnitCost);
                using(var up=cn.CreateCommand()){up.Transaction=tx;up.CommandText="UPDATE purchase_orders SET supplier_id=$s,total=$t,notes=$n,expected_date=$d WHERE id=$id";up.Parameters.AddWithValue("$s",dto.SupplierId);up.Parameters.AddWithValue("$t",total);up.Parameters.AddWithValue("$n",dto.Notes??"");up.Parameters.AddWithValue("$d",string.IsNullOrWhiteSpace(dto.ExpectedDate)?DateTime.Today.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture):dto.ExpectedDate);up.Parameters.AddWithValue("$id",purchaseId);up.ExecuteNonQuery();}
                using(var di=cn.CreateCommand()){di.Transaction=tx;di.CommandText="DELETE FROM purchase_order_items WHERE order_id=$id";di.Parameters.AddWithValue("$id",purchaseId);di.ExecuteNonQuery();}
                foreach(var x in dto.Items){using var q=cn.CreateCommand();q.Transaction=tx;q.CommandText="INSERT INTO purchase_order_items(order_id,product_id,quantity,unit_cost,received_quantity,notes) VALUES($o,$p,$q,$c,0,$n)";q.Parameters.AddWithValue("$o",purchaseId);q.Parameters.AddWithValue("$p",x.ProductId);q.Parameters.AddWithValue("$q",x.Quantity);q.Parameters.AddWithValue("$c",x.UnitCost);q.Parameters.AddWithValue("$n",x.Notes??"");q.ExecuteNonQuery();}
                tx.Commit(); AuditService.Log(MobileUserId(),"MOBILE_PURCHASE_UPDATE","COMPRAS", "Orden ID " + purchaseId + " · Total $" + total.ToString("N2", CultureInfo.InvariantCulture)); await JsonAsync(stream,new{ok=true,id=purchaseId,total},token); return;
            }
            if (target == "/api/mobile/compras" && method == "POST")
            {
                var body = await ReadRequestBodyAsync(stream, request, token);
                var dto = JsonSerializer.Deserialize<MobilePurchaseDto>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                          ?? throw new InvalidOperationException("Compra inválida.");
                if (dto.SupplierId <= 0 || dto.Items == null || dto.Items.Count == 0)
                    throw new InvalidOperationException("Proveedor y productos son obligatorios.");

                using var cn = Database.Open();
                using var tx = cn.BeginTransaction();
                var total = dto.Items.Sum(x => x.Quantity * x.UnitCost);
                var orderNo = "OC-" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
                long id;
                using (var c = cn.CreateCommand())
                {
                    c.Transaction = tx;
                    c.CommandText = "INSERT INTO purchase_orders(order_no,supplier_id,status,order_date,expected_date,total,notes,created_by) VALUES($no,$s,'DRAFT',CURRENT_TIMESTAMP,$d,$t,$n,$u);SELECT last_insert_rowid();";
                    c.Parameters.AddWithValue("$no", orderNo);
                    c.Parameters.AddWithValue("$s", dto.SupplierId);
                    c.Parameters.AddWithValue("$d", string.IsNullOrWhiteSpace(dto.ExpectedDate) ? DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : dto.ExpectedDate);
                    c.Parameters.AddWithValue("$t", total);
                    c.Parameters.AddWithValue("$n", dto.Notes ?? "");
                    c.Parameters.AddWithValue("$u", MobileUserId());
                    id = Convert.ToInt64(c.ExecuteScalar());
                }

                foreach (var x in dto.Items)
                {
                    using var q = cn.CreateCommand();
                    q.Transaction = tx;
                    q.CommandText = "INSERT INTO purchase_order_items(order_id,product_id,quantity,unit_cost,received_quantity,notes) VALUES($o,$p,$q,$c,0,$n)";
                    q.Parameters.AddWithValue("$o", id);
                    q.Parameters.AddWithValue("$p", x.ProductId);
                    q.Parameters.AddWithValue("$q", x.Quantity);
                    q.Parameters.AddWithValue("$c", x.UnitCost);
                    q.Parameters.AddWithValue("$n", x.Notes ?? "");
                    q.ExecuteNonQuery();
                }

                tx.Commit();
                AuditService.Log(MobileUserId(), "MOBILE_PURCHASE_CREATE", "COMPRAS", $"Orden {orderNo} · Total ${total:N2}");
                await JsonAsync(stream, new { ok = true, id, orderNo, total }, token);
                return;
            }
            var ticketMatch=Regex.Match(target,@"^/api/mobile/ventas/(\d+)/ticket$");
            if(ticketMatch.Success&&method=="GET"){var saleId=long.Parse(ticketMatch.Groups[1].Value,CultureInfo.InvariantCulture);using var cn=Database.Open();using var cmd=cn.CreateCommand();cmd.CommandText=@"SELECT s.ticket_no,s.total,s.payment_method,s.created_at,COALESCE(c.name,'Público General') FROM sales s LEFT JOIN customers c ON c.id=s.customer_id WHERE s.id=$id";cmd.Parameters.AddWithValue("$id",saleId);using var r=cmd.ExecuteReader();if(!r.Read()){await JsonAsync(stream,new{error="Venta no encontrada"},token,404);return;}var ticket=$"FERRARI'SPOS\nTICKET #{r.GetInt64(0)}\nFecha: {r.GetString(3)}\nCliente: {r.GetString(4)}\nMedio: {r.GetString(2)}\nTOTAL: ${r.GetDouble(1):N2}\n\nGracias por su compra.";await JsonAsync(stream,new {ok=true,ticket},token);return;}
            await SendAsync(stream,404,"text/plain; charset=utf-8","No encontrado",token);
        }
        catch(Exception ex){await JsonAsync(stream,new {error=ex.Message},token,500);}
    }

    private sealed class MobilePriceUpdate { public double SalePrice { get; set; } }
    private sealed class MobilePromotionItemDto { public int ProductId {get;set;} public double Quantity {get;set;}=1; }
    private sealed class MobilePromotionDto { public long Id {get;set;} public string Name {get;set;}=""; public string? Description {get;set;} public double Price {get;set;} public bool Active {get;set;}=true; public string? StartAt {get;set;} public string? EndAt {get;set;} public List<MobilePromotionItemDto>? Items {get;set;} }
    private sealed class MobileOpenTicketDto { public int TableId {get;set;} public string? TableName {get;set;} public int CustomerId {get;set;}=1; public string? CustomerName {get;set;} public List<MobileSaleItemDto>? Items {get;set;} public string? Notes {get;set;} public string? UpdatedAt {get;set;} }
    private sealed class MobilePendingSaleDto { public int CustomerId {get;set;}=1; public string? CustomerName {get;set;} public List<MobileSaleItemDto>? Items {get;set;} public string? Notes {get;set;} }
    private sealed class MobileWindowsTicketAppendDto { public List<MobileSaleItemDto>? Items {get;set;} }
    private sealed class MobileWindowsTicketDto { public string Id {get;set;}=""; public string Name {get;set;}=""; public double Total {get;set;} public List<MobileSaleItemDto>? Items {get;set;} public int TableId {get;set;} public string TableName {get;set;}=""; }
    private sealed class MobileSupplierDto { public int Id {get;set;} public string Name {get;set;}=""; public string? Document {get;set;} public string? Phone {get;set;} public string? Email {get;set;} public string? Address {get;set;} }
    private sealed class MobilePurchaseItemDto { public int ProductId {get;set;} public double Quantity {get;set;} public double UnitCost {get;set;} public string? Notes {get;set;} }
    private sealed class MobilePurchaseDto { public int SupplierId {get;set;} public string? Notes {get;set;} public string? ExpectedDate {get;set;} public List<MobilePurchaseItemDto>? Items {get;set;} }
    private sealed class MobilePurchaseReceiveDto { public List<MobilePurchaseReceiveItemDto>? Items {get;set;} public bool DifferenceMode {get;set;} public bool CloseComplete {get;set;} public string? Note {get;set;} }
    private sealed class MobilePurchaseReceiveItemDto { public int ProductId {get;set;} public double Quantity {get;set;} public double UnitCost {get;set;} public string? Notes {get;set;} }
    private sealed class MobileCategoryDto { public string Name {get;set;}=""; }
    private sealed class MobileSupplierAssignDto { public int SupplierId {get;set;} public double UnitCost {get;set;} }
    private sealed class MobileProductDto { public string Barcode {get;set;}=""; public string Description {get;set;}=""; public double SalePrice {get;set;} public double WholesalePrice {get;set;} public double CostPrice {get;set;} public double Stock {get;set;} public double MinStock {get;set;} public string? Category {get;set;} public string? Unit {get;set;} public bool Bulk {get;set;} public bool UsesInventory {get;set;}=true; public bool Iva21 {get;set;} public string? StockReductionReason {get;set;} }
    private sealed class MobileStockDto { public double Quantity {get;set;} public string Type {get;set;}="ENTRADA"; public string? Reference {get;set;} }
    private sealed class MobileCustomerDto { public string Name {get;set;}=""; public string? Document {get;set;} public string? Phone {get;set;} public string? Email {get;set;} public string? Address {get;set;} public double CreditLimit {get;set;} }
    private sealed class MobileCustomerPaymentDto { public double Amount {get;set;} public string? PaymentMethod {get;set;}="EFECTIVO"; public string? Reference {get;set;} public string? Concept {get;set;} public List<MobilePaymentDto>? Payments {get;set;} }
    private sealed class MobileSaleItemDto { public int ProductId {get;set;} public double Quantity {get;set;} public double UnitPrice {get;set;} public double Discount {get;set;} public string? Description {get;set;} public bool IsCommon {get;set;} }
    private sealed class MobilePaymentDto { public string? Method {get;set;} public double Amount {get;set;} public string? Reference {get;set;} }
    private sealed class MobileSaleDto { public int CustomerId {get;set;}=1; public List<MobileSaleItemDto>? Items {get;set;} public List<MobilePaymentDto>? Payments {get;set;} public double Received {get;set;} public string? SaleChannel {get;set;} public string? Notes {get;set;} public string? DeliveryAddress {get;set;} public string? DeliveryStatus {get;set;} public string? DiscountReason {get;set;} }
    private sealed class MobileReturnDto { public double Quantity {get;set;} public string? Reason {get;set;} }
    private sealed class MobileCancelSaleDto { public string? Reason {get;set;} }
    private sealed class MobileTableChargeDto { public double Received {get;set;} public List<MobilePaymentDto>? Payments {get;set;} }
    private sealed class MobileCashOpenDto { public double Amount {get;set;} public bool MercadoPagoEnabled {get;set;} public double MercadoPagoOpening {get;set;} public double MercadoPagoRetentionPercent {get;set;} }
    private sealed class MobileCashMovementDto { public string? Type {get;set;} public string? Concept {get;set;} public double Amount {get;set;} public string? PaymentMethod {get;set;} }
    private sealed class MobileCashCloseDto { public double Counted {get;set;} public string? DifferenceReason {get;set;} public double? CountedMercadoPago {get;set;} }
    private sealed class MobileUserSwitchDto { public int UserId {get;set;} public string Password {get;set;}=""; }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using var tcpClient = client;
        using var stream = tcpClient.GetStream();
        stream.ReadTimeout = 5000;
        stream.WriteTimeout = 5000;
        var buffer = new byte[16384];
        var read = await stream.ReadAsync(buffer, token);
        if (read <= 0) return;

        var request = Encoding.ASCII.GetString(buffer, 0, read);
        var firstLine = request.Split("\r\n", StringSplitOptions.None)[0];
        var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !(string.Equals(parts[0], "GET", StringComparison.OrdinalIgnoreCase) || string.Equals(parts[0], "POST", StringComparison.OrdinalIgnoreCase) || string.Equals(parts[0], "PUT", StringComparison.OrdinalIgnoreCase) || string.Equals(parts[0], "DELETE", StringComparison.OrdinalIgnoreCase)))
        {
            await SendAsync(stream, 405, "text/plain; charset=utf-8", "Método no permitido", token);
            return;
        }

        var target = parts[1].Split('?', 2)[0];
        if (target.StartsWith("/api/mobile/", StringComparison.OrdinalIgnoreCase))
        {
            var rawTarget = parts[1];
            await HandleMobileAsync(client, stream, request, rawTarget, token);
            return;
        }
        try
        {
            if (target is "/" or "/index.html")
                await SendAsync(stream, 200, "text/html; charset=utf-8", BuildHtml(), token);
            else if (target == "/api/resumen")
                await JsonAsync(stream, GetSummary(), token);
            else if (target == "/api/ventas")
                await JsonAsync(stream, GetTodaySales(), token);
            else if (target == "/api/stock-bajo")
                await JsonAsync(stream, GetLowStock(), token);
            else if (target == "/api/clientes")
                await JsonAsync(stream, GetCustomers(), token);
            else if (target == "/api/productos-top")
                await JsonAsync(stream, GetTopProducts(), token);
            else if (target == "/api/caja")
                await JsonAsync(stream, GetCashStatus(), token);
            else if (target == "/api/arqueo")
                await JsonAsync(stream, GetRealCashCount(), token);
            else if (target == "/api/medios-pago")
                await JsonAsync(stream, GetPaymentMethods(), token);
            else if (target == "/api/movimientos-caja")
                await JsonAsync(stream, GetCashMovements(), token);
            else if (target == "/api/movimientos-stock")
                await JsonAsync(stream, GetStockMovements(), token);
            else if (target == "/api/tecnico")
                await JsonAsync(stream, GetTechnicalInfo(), token);
            else
                await SendAsync(stream, 404, "text/plain; charset=utf-8", "No encontrado", token);
        }
        catch (Exception ex)
        {
            await JsonAsync(stream, new { error = ex.Message }, token, 500);
        }
    }

    private static readonly JsonSerializerOptions MobileJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase
    };

    private static async Task JsonAsync(NetworkStream stream, object value, CancellationToken token, int status = 200)
        => await SendAsync(stream, status, "application/json; charset=utf-8", JsonSerializer.Serialize(value, MobileJsonOptions), token);

    private static async Task SendAsync(NetworkStream stream, int status, string contentType, string body, CancellationToken token)
    {
        var statusText = status switch { 200 => "OK", 404 => "Not Found", 405 => "Method Not Allowed", _ => "Internal Server Error" };
        var payload = Encoding.UTF8.GetBytes(body);
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {statusText}\r\nContent-Type: {contentType}\r\nContent-Length: {payload.Length}\r\nCache-Control: no-store\r\nAccess-Control-Allow-Origin: *\r\nAccess-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(payload, token);
    }

    private static long GetOpenSessionId()
    {
        using var cn = OpenReadOnly(); using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT id FROM cash_sessions WHERE status='OPEN' ORDER BY id DESC LIMIT 1;";
        var value = cmd.ExecuteScalar();
        return value == null || value == DBNull.Value ? 0 : Convert.ToInt64(value);
    }

    private static object GetSummary()
    {
        var sessionId = GetOpenSessionId();
        using var cn = OpenReadOnly(); using var cmd = cn.CreateCommand();
        cmd.CommandText = @"
SELECT
COALESCE((SELECT SUM(total) FROM sales WHERE session_id=$s AND status='COMPLETED'),0),
COALESCE((SELECT COUNT(*) FROM sales WHERE session_id=$s AND status='COMPLETED'),0),
COALESCE((SELECT SUM(stock) FROM products WHERE active=1),0),
COALESCE((SELECT COUNT(*) FROM products WHERE active=1),0),
COALESCE((SELECT COUNT(*) FROM products WHERE active=1 AND min_stock>0 AND stock<=min_stock),0),
COALESCE((SELECT SUM(CASE WHEN entry_type='SALE' THEN amount WHEN entry_type='PAYMENT' THEN -amount ELSE 0 END) FROM customer_accounts),0),
COALESCE((SELECT SUM(amount) FROM cash_movements WHERE session_id=$s AND movement_type='SALE' AND payment_method='EFECTIVO'),0);";
        cmd.Parameters.AddWithValue("$s", sessionId);
        using var r = cmd.ExecuteReader(); r.Read();
        return new { fecha=DateTime.Now.ToString("dd/MM/yyyy"), turno=sessionId, ventas=r.GetDecimal(0), tickets=r.GetInt64(1), unidadesStock=r.GetDecimal(2), productos=r.GetInt64(3), stockBajo=r.GetInt64(4), deudaClientes=r.GetDecimal(5), efectivoTurno=r.GetDecimal(6), hayTurno=sessionId>0 };
    }

    private static List<object> GetTodaySales()
    {
        var sessionId = GetOpenSessionId();
        using var cn=OpenReadOnly();
        var sales = new List<(long id,long ticket,decimal total,string medio,string canal,string fecha,string cliente,string cajero)>();
        using (var cmd=cn.CreateCommand())
        {
            cmd.CommandText = @"SELECT s.id,s.ticket_no,s.total,s.payment_method,s.sale_channel,datetime(s.created_at,'localtime'),
                CASE WHEN EXISTS(SELECT 1 FROM payments pc WHERE pc.sale_id=s.id AND pc.status='APPROVED' AND (pc.method LIKE 'CRÉDITO%' OR pc.method LIKE 'CREDITO%'))
                     THEN COALESCE(c.name,'Consumidor final') ELSE 'Consumidor final' END,
                COALESCE(u.full_name,'')
                FROM sales s
                LEFT JOIN customers c ON c.id=s.customer_id
                LEFT JOIN users u ON u.id=s.user_id
                WHERE s.session_id=$s AND s.status='COMPLETED'
                ORDER BY s.id DESC LIMIT 120;";
            cmd.Parameters.AddWithValue("$s", sessionId);
            using var r=cmd.ExecuteReader();
            while(r.Read()) sales.Add((r.GetInt64(0),r.GetInt64(1),r.GetDecimal(2),Text(r,3),Text(r,4),Text(r,5),Text(r,6),Text(r,7)));
        }
        var itemsBySale = new Dictionary<long,List<object>>();
        using (var itemCmd=cn.CreateCommand())
        {
            itemCmd.CommandText = @"SELECT si.sale_id,si.id,si.barcode,si.description,si.quantity,si.unit_price,si.discount,si.total FROM sale_items si JOIN sales s ON s.id=si.sale_id WHERE s.session_id=$s AND s.status='COMPLETED' AND si.sale_id IN (SELECT id FROM sales WHERE session_id=$s AND status='COMPLETED' ORDER BY id DESC LIMIT 120) ORDER BY si.sale_id DESC,si.id ASC;";
            itemCmd.Parameters.AddWithValue("$s", sessionId);
            using var ir=itemCmd.ExecuteReader();
            while(ir.Read())
            {
                var saleId=ir.GetInt64(0);
                if(!itemsBySale.TryGetValue(saleId,out var list)){list=new List<object>();itemsBySale[saleId]=list;}
                list.Add(new {id=ir.GetInt64(1),codigo=Text(ir,2),producto=Text(ir,3),cantidad=Dec(ir,4),precioUnitario=Dec(ir,5),descuento=Dec(ir,6),subtotal=Dec(ir,7)});
            }
        }
        return sales.Select(x => (object)new { id=x.id,ticket=x.ticket,total=x.total,medio=x.medio,canal=x.canal,fecha=x.fecha,cliente=x.cliente,cajero=x.cajero,articulos=itemsBySale.TryGetValue(x.id,out var its)?its:new List<object>() }).ToList();
    }

    private static List<object> GetLowStock()
    {
        using var cn=OpenReadOnly(); using var cmd=cn.CreateCommand();
        cmd.CommandText="SELECT barcode,description,stock,min_stock,sale_price,category,unit FROM products WHERE active=1 AND min_stock>0 AND stock<=min_stock ORDER BY stock ASC,description ASC LIMIT 120;";
        using var r=cmd.ExecuteReader(); var list=new List<object>();
        while(r.Read()) list.Add(new {codigo=Text(r,0),producto=Text(r,1),stock=Dec(r,2),minimo=Dec(r,3),precio=Dec(r,4),categoria=Text(r,5),unidad=Text(r,6)});
        return list;
    }

    private static List<object> GetMobileCustomers()
    {
        using var cn=OpenReadOnly(); using var cmd=cn.CreateCommand();
        cmd.CommandText=@"SELECT c.id,c.name,c.document,c.phone,c.email,c.credit_limit,ROUND(COALESCE(SUM(CASE WHEN ca.entry_type='SALE' THEN ca.amount WHEN ca.entry_type='PAYMENT' THEN -ca.amount ELSE 0 END),0),2) FROM customers c LEFT JOIN customer_accounts ca ON ca.customer_id=c.id WHERE c.active=1 GROUP BY c.id ORDER BY CASE WHEN c.id=1 THEN 0 ELSE 1 END,c.name LIMIT 500;";
        using var r=cmd.ExecuteReader(); var list=new List<object>();
        while(r.Read()) list.Add(new {id=r.GetInt32(0),name=Text(r,1),document=Text(r,2),phone=Text(r,3),email=Text(r,4),creditLimit=Dec(r,5),deuda=Dec(r,6),abonosHoy=0m});
        return list;
    }

    private static List<object> GetCustomers()
    {
        using var cn=OpenReadOnly(); using var cmd=cn.CreateCommand();
        cmd.CommandText=@"
SELECT c.id,c.name,c.document,c.phone,c.email,c.credit_limit,
       ROUND(COALESCE(SUM(CASE WHEN ca.entry_type='SALE' THEN ca.amount WHEN ca.entry_type='PAYMENT' THEN -ca.amount ELSE 0 END),0),2) AS deuda,
       ROUND(COALESCE((SELECT SUM(p2.amount) FROM customer_accounts p2 WHERE p2.customer_id=c.id AND p2.entry_type='PAYMENT' AND date(p2.created_at,'localtime')=date('now','localtime')),0),2) AS abonos_hoy,
       (SELECT p3.payment_method FROM customer_accounts p3 WHERE p3.customer_id=c.id AND p3.entry_type='PAYMENT' ORDER BY p3.id DESC LIMIT 1) AS ultimo_medio,
       (SELECT datetime(p4.created_at,'localtime') FROM customer_accounts p4 WHERE p4.customer_id=c.id AND p4.entry_type='PAYMENT' ORDER BY p4.id DESC LIMIT 1) AS ultimo_abono
FROM customers c
LEFT JOIN customer_accounts ca ON ca.customer_id=c.id
WHERE c.active=1
GROUP BY c.id
HAVING deuda > 0.005 OR abonos_hoy > 0.005
ORDER BY CASE WHEN abonos_hoy > 0.005 THEN 0 ELSE 1 END, deuda DESC,c.name
LIMIT 150;";
        using var r=cmd.ExecuteReader(); var list=new List<object>();
        while(r.Read()) list.Add(new {id=r.GetInt64(0),nombre=Text(r,1),documento=Text(r,2),telefono=Text(r,3),email=Text(r,4),limite=Dec(r,5),deuda=Dec(r,6),abonosHoy=Dec(r,7),ultimoMedio=Text(r,8),ultimoAbono=Text(r,9)});
        return list;
    }

    private static List<object> GetTopProducts()
    {
        var sessionId=GetOpenSessionId(); using var cn=OpenReadOnly(); using var cmd=cn.CreateCommand();
        cmd.CommandText=@"SELECT si.description,ROUND(SUM(si.quantity),2),ROUND(SUM(si.total),2) FROM sale_items si JOIN sales s ON s.id=si.sale_id WHERE s.session_id=$s AND s.status='COMPLETED' GROUP BY si.product_id,si.description ORDER BY SUM(si.quantity) DESC LIMIT 12;";
        cmd.Parameters.AddWithValue("$s",sessionId);
        using var r=cmd.ExecuteReader(); var list=new List<object>();
        while(r.Read()) list.Add(new {producto=Text(r,0),cantidad=Dec(r,1),importe=Dec(r,2)});
        return list;
    }

    private static object GetCashStatus()
    {
        using var cn=OpenReadOnly(); using var cmd=cn.CreateCommand();
        cmd.CommandText=@"SELECT cs.id,cs.status,cs.opening_amount,datetime(cs.opened_at,'localtime'),datetime(cs.closed_at,'localtime'),cs.closing_amount,cs.expected_amount,cs.difference,COALESCE(u.full_name,''),COALESCE(cs.mercado_pago_enabled,0),COALESCE(cs.mercado_pago_opening_amount,0),COALESCE(cs.mercado_pago_expected_amount,0) FROM cash_sessions cs LEFT JOIN users u ON u.id=cs.user_id WHERE cs.status='OPEN' ORDER BY cs.id DESC LIMIT 1;";
        using var r=cmd.ExecuteReader();
        if(!r.Read()) return new {id=0L,estado="SIN CAJA ABIERTA",apertura=0m,aperturaAt="",cierreAt="",cierre=(decimal?)null,esperado=(decimal?)null,diferencia=(decimal?)null,cajero="",mercadoPagoEnabled=false,mercadoPagoOpening=0m,mercadoPagoExpected=0m};
        return new {id=r.GetInt64(0),estado=r.GetString(1),apertura=r.GetDecimal(2),aperturaAt=Text(r,3),cierreAt=r.IsDBNull(4)?"":r.GetString(4),cierre=r.IsDBNull(5)?(decimal?)null:r.GetDecimal(5),esperado=r.IsDBNull(6)?(decimal?)null:r.GetDecimal(6),diferencia=r.IsDBNull(7)?(decimal?)null:r.GetDecimal(7),cajero=Text(r,8),mercadoPagoEnabled=r.GetInt32(9)!=0,mercadoPagoOpening=r.GetDecimal(10),mercadoPagoExpected=r.GetDecimal(11)};
    }

    /// <summary>
    /// Arqueo real del turno abierto. Reproduce la misma lógica de CashService.Summary(),
    /// pero consulta la base en modo solo lectura para que el panel web jamás modifique datos.
    /// Efectivo esperado = fondo inicial + ventas en efectivo + ingresos - egresos.
    /// Mercado Pago esperado = saldo inicial MP + ventas MP + ingresos - egresos - retención configurada.
    /// </summary>
    private static object GetRealCashCount()
    {
        var sessionId = GetOpenSessionId();
        if (sessionId <= 0)
            return new { hayTurno=false, estado="SIN CAJA ABIERTA", sesion=0L, cajero="", aperturaAt="", efectivoInicial=0m, efectivoVentas=0m, efectivoIngresos=0m, efectivoEgresos=0m, efectivoEsperado=0m, mercadoPagoHabilitado=false, mercadoPagoInicial=0m, mercadoPagoVentas=0m, mercadoPagoIngresos=0m, mercadoPagoEgresos=0m, mercadoPagoRetencionPorcentaje=0m, mercadoPagoRetencion=0m, mercadoPagoEsperado=0m, totalEsperado=0m };

        using var cn = OpenReadOnly();
        double Scalar(string sql)
        {
            using var c = cn.CreateCommand();
            c.CommandText = sql;
            c.Parameters.AddWithValue("$s", sessionId);
            return Convert.ToDouble(c.ExecuteScalar() ?? 0);
        }

        var opening = Scalar("SELECT COALESCE(opening_amount,0) FROM cash_sessions WHERE id=$s");
        var mpEnabled = Scalar("SELECT COALESCE(mercado_pago_enabled,0) FROM cash_sessions WHERE id=$s") > 0;
        var mpOpening = Scalar("SELECT COALESCE(mercado_pago_opening_amount,0) FROM cash_sessions WHERE id=$s");
        var mpRetention = Scalar("SELECT COALESCE(mercado_pago_retention_percent,0) FROM cash_sessions WHERE id=$s");
        var salesCash = Scalar("SELECT COALESCE(SUM(amount),0) FROM cash_movements WHERE session_id=$s AND movement_type='SALE' AND payment_method='EFECTIVO'");
        var incomeCash = Scalar("SELECT COALESCE(SUM(amount),0) FROM cash_movements WHERE session_id=$s AND movement_type='INCOME' AND COALESCE(voided,0)=0 AND payment_method='EFECTIVO'");
        var expenseCash = Scalar("SELECT COALESCE(SUM(amount),0) FROM cash_movements WHERE session_id=$s AND movement_type='EXPENSE' AND COALESCE(voided,0)=0 AND payment_method='EFECTIVO'");
        var mpIncome = Scalar("SELECT COALESCE(SUM(amount),0) FROM cash_movements WHERE session_id=$s AND movement_type='INCOME' AND COALESCE(voided,0)=0 AND payment_method='MERCADO PAGO'");
        var mpExpenses = Scalar("SELECT COALESCE(SUM(amount),0) FROM cash_movements WHERE session_id=$s AND movement_type='EXPENSE' AND COALESCE(voided,0)=0 AND payment_method='MERCADO PAGO'");

        using var payments = cn.CreateCommand();
        payments.CommandText = "SELECT COALESCE(SUM(p.amount),0) FROM payments p JOIN sales s ON s.id=p.sale_id WHERE s.session_id=$s AND s.status='COMPLETED' AND p.status='APPROVED' AND p.method='MERCADO PAGO'";
        payments.Parameters.AddWithValue("$s", sessionId);
        var mpSales = Convert.ToDouble(payments.ExecuteScalar() ?? 0);

        var expectedCash = opening + salesCash + incomeCash - expenseCash;
        var mpRetentionAmount = Math.Round(mpSales * mpRetention / 100.0, 2, MidpointRounding.AwayFromZero);
        var expectedMp = mpEnabled ? mpOpening + mpSales + mpIncome - mpExpenses - mpRetentionAmount : 0;

        using var info = cn.CreateCommand();
        info.CommandText = "SELECT COALESCE(u.full_name,''), COALESCE(cs.opened_at,'') FROM cash_sessions cs LEFT JOIN users u ON u.id=cs.user_id WHERE cs.id=$s";
        info.Parameters.AddWithValue("$s", sessionId);
        using var ri = info.ExecuteReader();
        var cajero = ""; var openedAt = "";
        if (ri.Read()) { cajero = Text(ri,0); openedAt = Text(ri,1); }

        return new
        {
            hayTurno=true, estado="OPEN", sesion=sessionId, cajero, aperturaAt=openedAt,
            efectivoInicial=Math.Round(opening,2), efectivoVentas=Math.Round(salesCash,2), efectivoIngresos=Math.Round(incomeCash,2), efectivoEgresos=Math.Round(expenseCash,2), efectivoEsperado=Math.Round(expectedCash,2),
            mercadoPagoHabilitado=mpEnabled, mercadoPagoInicial=Math.Round(mpOpening,2), mercadoPagoVentas=Math.Round(mpSales,2), mercadoPagoIngresos=Math.Round(mpIncome,2), mercadoPagoEgresos=Math.Round(mpExpenses,2),
            mercadoPagoRetencionPorcentaje=Math.Round(mpRetention,2), mercadoPagoRetencion=Math.Round(mpRetentionAmount,2), mercadoPagoEsperado=Math.Round(expectedMp,2),
            totalEsperado=Math.Round(expectedCash + expectedMp,2)
        };
    }

    private static List<object> GetPaymentMethods()
    {
        var s=GetOpenSessionId(); using var cn=OpenReadOnly(); using var cmd=cn.CreateCommand();
        cmd.CommandText=@"SELECT p.method,COUNT(*),COALESCE(SUM(p.amount),0) FROM payments p JOIN sales s ON s.id=p.sale_id WHERE s.session_id=$s AND s.status='COMPLETED' AND p.status='APPROVED' GROUP BY p.method ORDER BY SUM(p.amount) DESC;";
        cmd.Parameters.AddWithValue("$s",s); using var r=cmd.ExecuteReader(); var list=new List<object>();
        while(r.Read()) list.Add(new {medio=Text(r,0),operaciones=r.GetInt64(1),importe=Dec(r,2)}); return list;
    }

    private static List<object> GetCashMovements()
    {
        var s=GetOpenSessionId(); using var cn=OpenReadOnly(); using var cmd=cn.CreateCommand();
        cmd.CommandText=@"SELECT cm.created_at,cm.movement_type,cm.payment_method,cm.concept,cm.amount,COALESCE(u.full_name,''),COALESCE(cm.voided,0),COALESCE(cm.void_reason,''),COALESCE(v.full_name,v.username,''),cm.id
FROM cash_movements cm
LEFT JOIN users u ON u.id=cm.user_id
LEFT JOIN users v ON v.id=cm.voided_by
WHERE cm.session_id=$s AND cm.movement_type IN ('INCOME','EXPENSE')
ORDER BY cm.id DESC;";
        cmd.Parameters.AddWithValue("$s",s); using var r=cmd.ExecuteReader(); var list=new List<object>();
        while(r.Read()) list.Add(new {id=r.GetInt64(9),fecha=Text(r,0),tipo=Text(r,1),medio=Text(r,2),concepto=Text(r,3),importe=Dec(r,4),usuario=Text(r,5),anulado=r.GetInt32(6)!=0,motivoAnulacion=Text(r,7),anulo=Text(r,8)}); return list;
    }

    private static List<object> GetStockMovements()
    {
        var s=GetOpenSessionId(); using var cn=OpenReadOnly(); using var cmd=cn.CreateCommand();
        cmd.CommandText=@"SELECT sm.created_at,p.description,p.barcode,sm.movement_type,sm.quantity,sm.reference,COALESCE(u.full_name,'') FROM stock_movements sm JOIN products p ON p.id=sm.product_id LEFT JOIN users u ON u.id=sm.user_id WHERE sm.created_at >= (SELECT opened_at FROM cash_sessions WHERE id=$s) ORDER BY sm.id DESC LIMIT 150;";
        cmd.Parameters.AddWithValue("$s",s); using var r=cmd.ExecuteReader(); var list=new List<object>();
        while(r.Read()) list.Add(new {fecha=Text(r,0),producto=Text(r,1),codigo=Text(r,2),tipo=Text(r,3),cantidad=Dec(r,4),referencia=Text(r,5),usuario=Text(r,6)}); return list;
    }

    private object GetTechnicalInfo()
    {
        var dbSize=0L; try { if(File.Exists(Database.DbPath)) dbSize=new FileInfo(Database.DbPath).Length; } catch { }
        var business=Database.GetSetting("business_name", "FerrariPOS").Trim();
        var type=Database.GetSetting("business_type", "Punto de Venta").Trim();
        var pairingPayload = GetMobilePairingPayload();
        var pairingCode = GetMobilePairingCode();
        var pairingQrBase64 = "";
        try
        {
            using var qrGenerator = new QRCodeGenerator();
            using var qrData = qrGenerator.CreateQrCode(pairingPayload, QRCodeGenerator.ECCLevel.Q);
            pairingQrBase64 = Convert.ToBase64String(new PngByteQRCode(qrData).GetGraphic(8));
        }
        catch { }

        return new
        {
            aplicacion="FerrariPOS",comercio=business,rubro=type,titulo=$"{business} {type}".Trim(),version="90 días · Panel Web",puerto=Port,servidor=GetLanIPv4()??"localhost",url=AccessUrl??"",baseDatos=Database.DbPath,baseDatosMB=Math.Round(dbSize/1024d/1024d,2),sistema=Environment.OSVersion.VersionString,arquitectura=Environment.Is64BitOperatingSystem?"x64":"x86",hora=DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"),soloLectura=true,cloudflared="Automático · Quick Tunnel",cloudflaredVersion=CloudflaredVersion,
            mobile_pairing=new { code=pairingCode, payload=pairingPayload, qr_png_base64=pairingQrBase64, generated_utc=DateTimeOffset.UtcNow }
        };
    }

    private static SqliteConnection OpenReadOnly()
    {
        // El panel web nunca escribe en SQLite. Usamos timeout + busy_timeout para
        // que una venta/apertura de caja que esté escribiendo no congele las
        // consultas del panel. Cache=Shared se conserva para reducir aperturas.
        var cn = new SqliteConnection($"Data Source={Database.DbPath};Mode=ReadOnly;Cache=Shared;Foreign Keys=True;Default Timeout=5;");
        cn.Open();
        try
        {
            using var busy = cn.CreateCommand();
            busy.CommandText = "PRAGMA busy_timeout=5000;";
            busy.ExecuteNonQuery();
        }
        catch { }
        return cn;
    }

    private static string Text(SqliteDataReader r, int index) => r.IsDBNull(index) ? string.Empty : r.GetString(index);
    private static decimal Dec(SqliteDataReader r, int index) => r.IsDBNull(index) ? 0m : r.GetDecimal(index);

    private string BuildHtml()
    {
        var businessName = Database.GetSetting("business_name", "FerrariPOS").Trim();
        var businessType = Database.GetSetting("business_type", "Punto de Venta").Trim();
        if (string.IsNullOrWhiteSpace(businessName)) businessName = "FerrariPOS";
        if (string.IsNullOrWhiteSpace(businessType)) businessType = "Punto de Venta";
        var pageTitle = $"{businessName} {businessType}".Trim();
        var safeBusiness = System.Net.WebUtility.HtmlEncode(businessName);
        var safeType = System.Net.WebUtility.HtmlEncode(businessType);
        var safeTitle = System.Net.WebUtility.HtmlEncode(pageTitle);
        var html = """
<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<meta name="theme-color" content="#080b12">
<title>__PAGE_TITLE__</title>
<style>
:root{font-family:"Segoe UI",Arial,sans-serif;color:#eef4ff;background:#070a10;--bg:#070a10;--panel:#0e1520;--panel2:#121c29;--line:#263548;--muted:#8998aa;--orange:#ff8a00;--green:#35e39a;--cyan:#38c9ff;--violet:#9b6cff;--red:#ff6878;--yellow:#ffd34d}
*{box-sizing:border-box}html{scroll-behavior:smooth}body{margin:0;min-height:100vh;background:radial-gradient(circle at 10% -10%,#1d3553 0,#0c1420 32%,#070a10 72%);overflow-x:hidden}body:before{content:"";position:fixed;inset:0;pointer-events:none;background:linear-gradient(120deg,rgba(56,201,255,.025),transparent 35%,rgba(155,108,255,.035));z-index:-1}
.top{position:sticky;top:0;z-index:50;background:rgba(7,10,16,.88);backdrop-filter:blur(18px);border-bottom:1px solid #243244;box-shadow:0 8px 35px #0005}.topin{max-width:1500px;margin:auto;padding:12px 18px;display:flex;align-items:center;justify-content:space-between;gap:14px}.brand{display:flex;align-items:center;gap:12px;min-width:0}.mark{width:46px;height:46px;border-radius:14px;background:linear-gradient(145deg,#ff9b19,#ff3d00);display:grid;place-items:center;font-size:22px;font-weight:950;color:white;box-shadow:0 0 28px #ff6a0035}.brand b{font-size:18px;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.brand small{display:block;color:#91a0b1;margin-top:2px}.actions{display:flex;align-items:center;gap:9px}.btn{border:1px solid #304156;background:linear-gradient(145deg,#172333,#101923);color:#eef4ff;border-radius:11px;padding:9px 13px;font-weight:800;cursor:pointer}.btn:hover{border-color:var(--orange);box-shadow:0 0 18px #ff8a0018}.status{font-size:12px;font-weight:800;color:var(--green)}.status.loading{color:var(--yellow)}.status.error{color:var(--red)}
.wrap{max-width:1500px;margin:auto;padding:20px 18px 45px}.hero{display:flex;justify-content:space-between;align-items:end;gap:18px;margin-bottom:16px}.eyebrow{display:inline-flex;align-items:center;gap:7px;color:#9eb0c4;font-size:11px;font-weight:850;letter-spacing:.12em;text-transform:uppercase}.eyebrow i{width:8px;height:8px;border-radius:50%;background:var(--cyan);box-shadow:0 0 12px var(--cyan);display:inline-block}.hero h1{font-size:32px;line-height:1.12;margin:7px 0 0;font-weight:950;letter-spacing:-.02em}.hero p{margin:7px 0 0;color:#92a0b0}.livebox{display:flex;align-items:center;gap:10px;padding:10px 13px;border:1px solid #26364a;background:rgba(13,21,31,.75);border-radius:13px;color:#aab8c8;font-size:12px}.pulse{width:9px;height:9px;border-radius:50%;background:var(--green);box-shadow:0 0 0 0 #35e39a66;animation:pulse 1.8s infinite}@keyframes pulse{70%{box-shadow:0 0 0 9px transparent}100%{box-shadow:0 0 0 0 transparent}}
.tabs{display:flex;gap:7px;overflow:auto;padding:5px 2px 14px;position:sticky;top:71px;z-index:40;background:linear-gradient(rgba(7,10,16,.98),rgba(7,10,16,.88));scrollbar-width:thin}.tab{white-space:nowrap;border:1px solid #2a394b;background:linear-gradient(145deg,#111b27,#0d151e);color:#9dacbd;border-radius:11px;padding:10px 13px;font-size:11px;font-weight:900;letter-spacing:.03em;cursor:pointer;transition:.15s}.tab.active{color:#fff;border-color:var(--orange);background:linear-gradient(145deg,#3a2615,#171a20);box-shadow:0 0 20px #ff8a0015}.tab:hover{color:#fff;border-color:#52708f}
.androidqr{display:grid;grid-template-columns:220px 1fr;gap:22px;align-items:center;padding:20px}.androidqr-img{width:210px;height:210px;background:#fff;border-radius:16px;padding:10px;object-fit:contain;box-shadow:0 0 28px #38c9ff22}.androidqr-title{font-size:22px;font-weight:950;margin:0 0 8px}.androidqr-code{display:inline-block;font-size:20px;font-weight:950;letter-spacing:.08em;color:var(--cyan);padding:9px 12px;border:1px solid #2e6078;border-radius:10px;background:#07131d;margin:8px 0}.androidqr-url{word-break:break-all;color:#a9b8c8;font-size:12px}.androidqr-help{color:#8998aa;line-height:1.55;font-size:13px}.qr-empty{padding:22px;color:var(--yellow)}
.tabpage{display:none}.tabpage.active{display:block;animation:show .18s ease}@keyframes show{from{opacity:.4;transform:translateY(3px)}to{opacity:1;transform:none}}
.cards{display:grid;grid-template-columns:repeat(6,1fr);gap:11px}.card{background:linear-gradient(145deg,rgba(18,29,43,.96),rgba(10,16,24,.96));border:1px solid #27384d;border-radius:16px;padding:15px;box-shadow:0 14px 38px #0003;position:relative;overflow:hidden}.card:after{content:"";position:absolute;inset:auto -25px -35px auto;width:90px;height:90px;border-radius:50%;background:#38c9ff08}.card.accent-orange{border-top:2px solid var(--orange)}.card.accent-green{border-top:2px solid var(--green)}.card.accent-cyan{border-top:2px solid var(--cyan)}.card.accent-violet{border-top:2px solid var(--violet)}.label{font-size:10px;letter-spacing:.09em;color:#8d9caf;font-weight:900}.value{font-size:25px;font-weight:950;margin-top:7px}.orange{color:var(--orange)}.green{color:var(--green)}.cyan{color:var(--cyan)}.violet{color:#b291ff}.red{color:var(--red)}.yellow{color:var(--yellow)}.muted{color:#8290a1}
.grid2{display:grid;grid-template-columns:1.35fr .65fr;gap:13px;margin-top:13px}.grid3{display:grid;grid-template-columns:repeat(3,1fr);gap:13px;margin-top:13px}.panel{background:rgba(14,21,32,.94);border:1px solid #27384d;border-radius:16px;overflow:hidden;box-shadow:0 13px 35px #0002}.ph{padding:14px 16px;border-bottom:1px solid #253548;display:flex;justify-content:space-between;align-items:center;gap:10px}.ph h2{font-size:16px;margin:0;font-weight:900}.ph small{color:#7f90a4}.content{padding:14px 16px}.bars{height:245px;display:flex;align-items:end;gap:8px;padding:0 5px}.bar{flex:1;min-width:12px;border-radius:8px 8px 2px 2px;background:linear-gradient(180deg,#ffb24c,#f04b12);position:relative;box-shadow:0 5px 18px #ff6a0020}.bar span{position:absolute;bottom:-20px;width:100%;text-align:center;font-size:9px;color:#77889a;overflow:hidden}.tablewrap{overflow:auto}.table{width:100%;border-collapse:collapse;min-width:650px}.table th,.table td{text-align:left;padding:9px;border-bottom:1px solid #202d3d;font-size:12px}.table tbody tr:hover{background:#ffffff04}.table th{color:#8091a5;font-size:10px;letter-spacing:.07em}.pill{display:inline-block;padding:4px 8px;border-radius:99px;background:#182536;color:#b6c4d4}.search{background:#0a111a;border:1px solid #2b3b4f;border-radius:10px;color:#fff;padding:9px 11px;min-width:190px;outline:none}.search:focus{border-color:var(--cyan);box-shadow:0 0 0 3px #38c9ff12}.empty{padding:30px;text-align:center;color:#7f8ea0}.metriclist{display:grid;gap:8px}.metricrow{display:flex;justify-content:space-between;gap:16px;padding:9px 0;border-bottom:1px solid #202d3d}.metricrow:last-child{border-bottom:0}.piearea{min-height:300px;padding:18px;display:flex;align-items:center;justify-content:center;gap:26px;flex-wrap:wrap}.pie{width:190px;height:190px;border-radius:50%;position:relative;box-shadow:0 12px 40px #0007}.pie:after{content:"";position:absolute;inset:48px;border-radius:50%;background:#101720;border:1px solid #2a394a}.legend{display:grid;gap:9px;min-width:190px}.legendrow{display:flex;align-items:center;gap:8px;justify-content:space-between;border-bottom:1px solid #202d3d;padding:6px 0;font-size:12px}.dot{width:10px;height:10px;border-radius:50%;display:inline-block;flex:none}.legendname{display:flex;align-items:center;gap:7px}.legendvalue{font-weight:850}.insight{display:grid;grid-template-columns:repeat(2,1fr);gap:9px}.insightbox{background:#0a111a;border:1px solid #243448;border-radius:12px;padding:12px}.insightbox b{display:block;color:#8998aa;font-size:10px;margin-bottom:5px}.insightbox span{font-size:17px;font-weight:950}.tech{display:grid;grid-template-columns:repeat(2,1fr);gap:10px}.techbox{background:#0a111a;border:1px solid #243448;border-radius:12px;padding:12px}.techbox b{display:block;color:#8b9aac;font-size:10px;margin-bottom:5px}.techbox span{word-break:break-word}.connection-ok{border-color:#2b8b68;box-shadow:0 0 24px #35e39a0a}.connection-warn{border-color:#8c6b26}.footer{color:#667589;text-align:center;padding:22px;font-size:11px}.skeleton{height:68px;border-radius:14px;background:linear-gradient(90deg,#111b26,#1b2736,#111b26);background-size:200% 100%;animation:shimmer 1.2s infinite}@keyframes shimmer{to{background-position:-200% 0}}.errorbox{display:none;margin:12px 0;padding:13px 15px;border:1px solid #8f3946;background:#30151b;color:#ffadb7;border-radius:12px;font-size:12px}.errorbox.show{display:flex;justify-content:space-between;gap:12px;align-items:center}.retry{border:1px solid #a64b58;background:#431b23;color:#fff;border-radius:9px;padding:7px 10px;font-weight:800;cursor:pointer}.sectionhint{font-size:11px;color:#7e8fa3}
@media(max-width:1200px){.cards{grid-template-columns:repeat(3,1fr)}.grid2,.grid3{grid-template-columns:1fr}}@media(max-width:700px){.topin{padding:10px 11px}.wrap{padding:14px 10px 30px}.hero{align-items:flex-start}.hero h1{font-size:24px}.livebox{display:none}.cards{grid-template-columns:repeat(2,1fr)}.value{font-size:20px}.brand small{display:none}.brand b{font-size:15px}.actions .status{display:none}.tabs{top:66px}.tech{grid-template-columns:1fr}.ph{align-items:flex-start;flex-direction:column}.search{width:100%;min-width:0}.bars{height:210px}.insight{grid-template-columns:1fr}}
</style>
</head>
<body>
<header class="top"><div class="topin"><div class="brand"><div class="mark">F</div><div><b>__BUSINESS_NAME__</b><small>__BUSINESS_TYPE__ · Panel administrador · solo lectura</small></div></div><div class="actions"><span id="status" class="status loading">● iniciando</span><button class="btn" onclick="cargar(true)">↻ Actualizar</button></div></div></header>
<main class="wrap">
<div class="hero"><div><div class="eyebrow"><i></i> PANEL DE CONTROL EN TIEMPO REAL</div><h1>__PAGE_TITLE__</h1><p id="fecha">Conectando con FerrariPOS…</p></div><div class="livebox"><span class="pulse"></span><span id="liveText">Conexión segura mediante Quick Tunnel</span></div></div>
<div id="errorBox" class="errorbox"><span id="errorText">No se pudo cargar la información.</span><button class="retry" onclick="cargar(true)">Reintentar</button></div>
<nav class="tabs"><button class="tab active" onclick="tab('resumen',this)">RESUMEN</button><button class="tab" onclick="tab('ventas',this)">VENTAS DEL TURNO</button><button class="tab" onclick="tab('caja',this)">CAJA Y PAGOS</button><button class="tab" onclick="tab('arqueo',this)">ARQUEO REAL</button><button class="tab" onclick="tab('analitica',this)">GRÁFICOS</button><button class="tab" onclick="tab('productos',this)">PRODUCTOS / STOCK</button><button class="tab" onclick="tab('clientes',this)">CLIENTES</button><button class="tab" onclick="tab('tecnico',this)">DATOS TÉCNICOS</button><button class="tab" onclick="tab('actividad',this)">ACTIVIDAD</button><button class="tab" onclick="tab('conexion',this)">CONEXIÓN</button><button class="tab" onclick="tab('qrandroid',this)">QR ANDROID</button></nav>
<section id="resumen" class="tabpage active"><div id="cards" class="cards"><div class="skeleton"></div><div class="skeleton"></div><div class="skeleton"></div><div class="skeleton"></div><div class="skeleton"></div><div class="skeleton"></div></div><div class="grid2"><section class="panel"><div class="ph"><h2>Ventas del turno</h2><small>Tickets recientes</small></div><div class="content"><div id="chart" class="bars"><div class="empty">Esperando datos…</div></div></div></section><section class="panel"><div class="ph"><h2>Estado de caja</h2><small>Sesión activa</small></div><div id="cajaMini" class="content"><div class="empty">Conectando…</div></div><div class="ph"><h2>Productos más vendidos</h2></div><div id="topMini" class="content"><div class="empty">Conectando…</div></div></section></div></section>
<section id="ventas" class="tabpage"><section class="panel"><div class="ph"><h2>Ventas del turno</h2><input id="qventas" class="search" placeholder="Buscar ticket, cliente o cajero" oninput="renderVentas()"></div><div id="ventasTable" class="content tablewrap"></div></section></section>
<section id="caja" class="tabpage"><div class="grid3"><section class="panel"><div class="ph"><h2>Sesión de caja</h2></div><div id="cajaFull" class="content"></div></section><section class="panel"><div class="ph"><h2>Medios de pago</h2></div><div id="pagos" class="content"></div></section><section class="panel"><div class="ph"><h2>Movimientos</h2></div><div id="movCaja" class="content tablewrap"></div></section></div></section>
<section id="arqueo" class="tabpage"><section class="panel"><div class="ph"><h2>Arqueo real del turno</h2><small>Calculado en tiempo real</small></div><div id="arqueoCards" class="content"></div></section><div class="grid2"><section class="panel"><div class="ph"><h2>EFECTIVO ESPERADO</h2><small>Dinero físico esperado</small></div><div id="arqueoEfectivo" class="content"></div></section><section class="panel"><div class="ph"><h2>MERCADO PAGO ESPERADO</h2><small>Saldo esperado</small></div><div id="arqueoMP" class="content"></div></section></div></section>
<section id="analitica" class="tabpage"><div class="grid2"><section class="panel"><div class="ph"><h2>Ventas por medio de pago</h2><small>Composición del turno</small></div><div id="piePagos" class="piearea"></div></section><section class="panel"><div class="ph"><h2>Movimientos de caja</h2><small>Ingresos vs egresos</small></div><div id="pieCaja" class="piearea"></div></section></div><div class="grid2"><section class="panel"><div class="ph"><h2>Indicadores relevantes</h2></div><div id="indicadores" class="content"></div></section><section class="panel"><div class="ph"><h2>Resumen financiero</h2></div><div id="financiero" class="content"></div></section></div></section>
<section id="productos" class="tabpage"><section class="panel"><div class="ph"><h2>Productos más vendidos del turno</h2></div><div id="topFull" class="content"></div></section><section class="panel" style="margin-top:13px"><div class="ph"><h2>Stock bajo</h2><small>Requiere reposición</small></div><div id="stock" class="content tablewrap"></div></section><section class="panel" style="margin-top:13px"><div class="ph"><h2>Movimientos de stock del turno</h2></div><div id="movStock" class="content tablewrap"></div></section></section>
<section id="clientes" class="tabpage"><section class="panel"><div class="ph"><h2>Clientes y cuentas</h2><input id="qclientes" class="search" placeholder="Buscar cliente" oninput="renderClientes()"></div><div id="clientesTable" class="content tablewrap"></div></section></section>
<section id="tecnico" class="tabpage"><section class="panel"><div class="ph"><h2>Información técnica</h2><small>Servidor y diagnóstico</small></div><div id="tecnicoBox" class="content"></div></section></section>
<section id="actividad" class="tabpage"><div class="grid2"><section class="panel"><div class="ph"><h2>Últimas ventas del turno</h2><small>Actualización automática</small></div><div id="actividadVentas" class="content tablewrap"></div></section><section class="panel"><div class="ph"><h2>Resumen operativo</h2></div><div id="actividadResumen" class="content"></div></section></div></section>
<section id="conexion" class="tabpage"><section id="conexionPanel" class="panel"><div class="ph"><h2>Conexión POS</h2><small>Acceso público y servidor</small></div><div id="conexionBox" class="content"></div></section></section><section id="qrandroid" class="tabpage"><section class="panel"><div class="ph"><h2>Conectar FerrariPOS Manager Android</h2><small>QR oficial de vinculación</small></div><div id="qrAndroidBox" class="content"><div class="empty">Esperando QR de vinculación…</div></div></section></section>
<div class="footer">__PAGE_TITLE__ · Panel web de solo lectura · Datos del turno de caja abierto · Actualización automática cada 30 segundos.</div>
</main>
<script>
let ventas=[],clientes=[],datos={},apiErrors=[];
const money=v=>new Intl.NumberFormat('es-AR',{style:'currency',currency:'ARS',maximumFractionDigits:2}).format(Number(v||0));
const esc=v=>String(v??'').replace(/[&<>"']/g,m=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[m]));
function tab(id,el){document.querySelectorAll('.tabpage').forEach(x=>x.classList.remove('active'));document.querySelectorAll('.tab').forEach(x=>x.classList.remove('active'));document.getElementById(id).classList.add('active');el.classList.add('active')}
async function get(u){const ctl=new AbortController();const timer=setTimeout(()=>ctl.abort(),8000);try{const r=await fetch(u+'?t='+Date.now(),{cache:'no-store',signal:ctl.signal,headers:{'Accept':'application/json'}});const text=await r.text();if(!r.ok)throw new Error(text||('HTTP '+r.status));try{return JSON.parse(text)}catch{throw new Error('Respuesta inválida de '+u)}}catch(e){if(e.name==='AbortError')throw new Error('Tiempo de espera agotado en '+u);throw e}finally{clearTimeout(timer)}}
async function safe(u,fallback){try{return await get(u)}catch(e){apiErrors.push(u+': '+(e.message||'error'));console.warn('Panel:',u,e);return fallback}}
function showError(msg){document.getElementById('errorBox').classList.add('show');document.getElementById('errorText').textContent=msg}
function hideError(){document.getElementById('errorBox').classList.remove('show')}
function setStatus(kind,text){const el=document.getElementById('status');el.className='status '+(kind||'');el.textContent=text}
function renderClientes(){const q=(document.getElementById('qclientes')?.value||'').toLowerCase();const a=clientes.filter(x=>[x.nombre,x.documento,x.telefono,x.email].some(v=>String(v||'').toLowerCase().includes(q)));document.getElementById('clientesTable').innerHTML=a.length?`<table class='table'><thead><tr><th>CLIENTE</th><th>DOCUMENTO</th><th>TELÉFONO</th><th>LÍMITE</th><th>DEUDA</th><th>ABONOS HOY</th><th>ÚLTIMO MEDIO</th><th>ESTADO</th></tr></thead><tbody>${a.map(x=>`<tr><td><b>${esc(x.nombre)}</b><br><small>${esc(x.email||'')}</small></td><td>${esc(x.documento||'-')}</td><td>${esc(x.telefono||'-')}</td><td>${money(x.limite)}</td><td class='${x.deuda>0?'red':'green'}'><b>${money(x.deuda)}</b></td><td class='${x.abonosHoy>0?'orange':''}'><b>${money(x.abonosHoy)}</b></td><td>${esc(x.ultimoMedio||'-')}</td><td><span class='pill'>${x.deuda>0?'Pendiente':'Al día'}</span></td></tr>`).join('')}</tbody></table>`:`<div class='empty'>No hay clientes que coincidan.</div>`}
function renderPie(target,items){const box=document.getElementById(target);const clean=(items||[]).map(x=>({name:String(x.name||'Sin datos'),value:Number(x.value)||0})).filter(x=>x.value>0);if(!clean.length){box.innerHTML=`<div class='empty'>Sin datos suficientes para el gráfico.</div>`;return}const palette=['#ff8a00','#35e39a','#ff5a36','#b7f34a','#ffd34d','#38c9ff','#9b6cff'];const total=clean.reduce((a,x)=>a+x.value,0);let acc=0;const stops=clean.map((x,i)=>{const start=acc/total*100;acc+=x.value;const end=acc/total*100;return `${palette[i%palette.length]} ${start}% ${end}%`}).join(',');box.innerHTML=`<div class='pie' style="background:conic-gradient(${stops})"></div><div class='legend'>${clean.map((x,i)=>`<div class='legendrow'><span class='legendname'><i class='dot' style="background:${palette[i%palette.length]}"></i>${esc(x.name)}</span><span class='legendvalue'>${money(x.value)} · ${(x.value/total*100).toFixed(1)}%</span></div>`).join('')}<div class='legendrow'><span class='legendname'><b>TOTAL</b></span><span class='legendvalue'>${money(total)}</span></div></div>`}
function listTop(arr,target){document.getElementById(target).innerHTML=arr.length?`<div class='metriclist'>${arr.map((x,i)=>`<div class='metricrow'><span>${i+1}. ${esc(x.producto)}</span><b>${x.cantidad} · ${money(x.importe)}</b></div>`).join('')}</div>`:`<div class='empty'>Sin ventas de productos en el turno.</div>`}
function renderVentas(){const q=(document.getElementById('qventas')?.value||'').toLowerCase();const a=ventas.filter(x=>[x.ticket,x.cliente,x.cajero,x.medio,x.canal].some(v=>String(v||'').toLowerCase().includes(q)));document.getElementById('ventasTable').innerHTML=a.length?`<div class='ticketlist'>${a.map(x=>`<details class='ticketcard'><summary><span>#${esc(x.ticket)} · ${esc(x.cliente)}</span><b class='orange'>${money(x.total)}</b></summary><div class='ticketmeta'><span>${esc(x.fecha)}</span><span>${esc(x.medio||'-')}</span><span>${esc(x.canal||'-')}</span><span>${esc(x.cajero||'-')}</span></div><table class='table'><thead><tr><th>PRODUCTO</th><th>CANT.</th><th>PRECIO</th><th>DESCUENTO</th><th>SUBTOTAL</th></tr></thead><tbody>${(x.articulos||[]).map(i=>`<tr><td><b>${esc(i.producto)}</b><br><small>${esc(i.codigo||'')}</small></td><td>${i.cantidad}</td><td>${money(i.precioUnitario)}</td><td>${money(i.descuento)}</td><td>${money(i.subtotal)}</td></tr>`).join('')}</tbody></table></details>`).join('')}</div>`:`<div class='empty'>No hay ventas que coincidan.</div>`}
async function cargar(manual=false){
  if(cargar.busy)return;cargar.busy=true;setStatus('loading','● actualizando');document.getElementById('liveText').textContent=manual?'Actualización solicitada':'Sincronizando con FerrariPOS…';if(manual)hideError();
  try{
    apiErrors=[];
    const [s,v,st,c,arqueo,top,pagos,movCaja,movStock,t,cli]=await Promise.all([
      safe('/api/resumen',null),safe('/api/ventas',[]),safe('/api/stock-bajo',[]),safe('/api/caja',{id:0,estado:'SIN DATOS',apertura:0}),safe('/api/arqueo',{hayTurno:false}),safe('/api/productos-top',[]),safe('/api/medios-pago',[]),safe('/api/movimientos-caja',[]),safe('/api/movimientos-stock',[]),safe('/api/tecnico',{}),safe('/api/clientes',[])
    ]);
    if(!s)throw new Error('El servidor respondió, pero no pudo obtener el resumen de la base de datos.');
    datos={s,c,t,arqueo};ventas=v||[];clientes=cli||[];
    document.getElementById('fecha').textContent=s.hayTurno?`Turno #${esc(s.turno)} · ${esc(s.fecha)} · actualización automática cada 30 segundos`:`${esc(s.fecha)} · NO HAY CAJA ABIERTA`;
    document.getElementById('cards').innerHTML=`<div class='card accent-orange'><div class='label'>VENTAS DEL TURNO</div><div class='value orange'>${money(s.ventas)}</div></div><div class='card accent-cyan'><div class='label'>TICKETS DEL TURNO</div><div class='value'>${s.tickets}</div></div><div class='card accent-violet'><div class='label'>PRODUCTOS ACTIVOS</div><div class='value violet'>${s.productos}</div></div><div class='card accent-green'><div class='label'>UNIDADES EN STOCK</div><div class='value green'>${s.unidadesStock}</div></div><div class='card accent-orange'><div class='label'>STOCK BAJO</div><div class='value ${s.stockBajo?'red':'green'}'>${s.stockBajo}</div></div><div class='card accent-violet'><div class='label'>DEUDA CLIENTES</div><div class='value ${s.deudaClientes?'red':'green'}'>${money(s.deudaClientes)}</div></div>`;
    const vals=ventas.slice(0,14).map(x=>Number(x.total)||0);const mx=Math.max(...vals,1);document.getElementById('chart').innerHTML=vals.length?vals.map((x,i)=>`<div class='bar' style='height:${Math.max(8,x/mx*92)}%'><span>#${esc(ventas[i].ticket)}</span></div>`).join(''):`<div class='empty'>Sin ventas en el turno</div>`;
    const cstate=c.estado==='OPEN'?'green':'red';document.getElementById('cajaMini').innerHTML=`<div class='card'><div class='label'>ESTADO</div><div class='value ${cstate}'>${esc(c.estado)}</div><p class='muted'>Cajero: ${esc(c.cajero||'-')}<br>Apertura: ${money(c.apertura)}<br>${esc(c.aperturaAt||'')}</p></div>`;
    document.getElementById('cajaFull').innerHTML=`<div class='metriclist'><div class='metricrow'><span>Sesión</span><b>#${c.id||'-'}</b></div><div class='metricrow'><span>Cajero</span><b>${esc(c.cajero||'-')}</b></div><div class='metricrow'><span>Apertura</span><b>${money(c.apertura)}</b></div><div class='metricrow'><span>Fecha apertura</span><b>${esc(c.aperturaAt||'-')}</b></div><div class='metricrow'><span>Estado</span><b class='${cstate}'>${esc(c.estado)}</b></div></div>`;
    document.getElementById('arqueoCards').innerHTML=arqueo.hayTurno?`<div class='cards' style='grid-template-columns:repeat(3,1fr)'><div class='card accent-orange'><div class='label'>EFECTIVO ESPERADO</div><div class='value orange'>${money(arqueo.efectivoEsperado)}</div></div><div class='card accent-green'><div class='label'>MERCADO PAGO ESPERADO</div><div class='value green'>${money(arqueo.mercadoPagoEsperado)}</div></div><div class='card accent-cyan'><div class='label'>TOTAL ESPERADO</div><div class='value'>${money(arqueo.totalEsperado)}</div></div></div>`:`<div class='empty'>No hay una caja abierta. El arqueo aparecerá al abrir el turno.</div>`;
    document.getElementById('arqueoEfectivo').innerHTML=arqueo.hayTurno?`<div class='metriclist'><div class='metricrow'><span>Fondo inicial</span><b>${money(arqueo.efectivoInicial)}</b></div><div class='metricrow'><span>Ventas en efectivo</span><b>${money(arqueo.efectivoVentas)}</b></div><div class='metricrow'><span>Ingresos</span><b class='green'>+ ${money(arqueo.efectivoIngresos)}</b></div><div class='metricrow'><span>Egresos</span><b class='red'>- ${money(arqueo.efectivoEgresos)}</b></div><div class='metricrow'><span>EFECTIVO ESPERADO</span><b class='orange'>${money(arqueo.efectivoEsperado)}</b></div></div>`:`<div class='empty'>Sin caja abierta.</div>`;
    document.getElementById('arqueoMP').innerHTML=arqueo.hayTurno&&arqueo.mercadoPagoHabilitado?`<div class='metriclist'><div class='metricrow'><span>Saldo inicial Mercado Pago</span><b>${money(arqueo.mercadoPagoInicial)}</b></div><div class='metricrow'><span>Ventas Mercado Pago</span><b>${money(arqueo.mercadoPagoVentas)}</b></div><div class='metricrow'><span>Ingresos</span><b class='green'>+ ${money(arqueo.mercadoPagoIngresos)}</b></div><div class='metricrow'><span>Egresos</span><b class='red'>- ${money(arqueo.mercadoPagoEgresos)}</b></div><div class='metricrow'><span>Retención (${arqueo.mercadoPagoRetencionPorcentaje}%)</span><b class='red'>- ${money(arqueo.mercadoPagoRetencion)}</b></div><div class='metricrow'><span>MERCADO PAGO ESPERADO</span><b class='green'>${money(arqueo.mercadoPagoEsperado)}</b></div></div>`:`<div class='empty'>Mercado Pago no está habilitado en este turno.</div>`;
    listTop(top||[],'topMini');listTop(top||[],'topFull');renderPie('piePagos',(pagos||[]).map(x=>({name:x.medio,value:x.importe})));const movimientosActivos=(movCaja||[]).filter(x=>!x.anulado);const ingresos=movimientosActivos.filter(x=>String(x.tipo||'').toLowerCase().includes('ing')).reduce((a,x)=>a+(Number(x.importe)||0),0);const egresos=movimientosActivos.filter(x=>String(x.tipo||'').toLowerCase().includes('egr')).reduce((a,x)=>a+(Number(x.importe)||0),0);renderPie('pieCaja',[{name:'Ingresos',value:ingresos},{name:'Egresos',value:egresos}]);
    const promedio=s.tickets?(Number(s.ventas)||0)/Number(s.tickets):0;document.getElementById('indicadores').innerHTML=`<div class='insight'><div class='insightbox'><b>TICKET PROMEDIO</b><span class='orange'>${money(promedio)}</span></div><div class='insightbox'><b>TICKETS</b><span>${s.tickets||0}</span></div><div class='insightbox'><b>STOCK BAJO</b><span class='${s.stockBajo?'red':'green'}'>${s.stockBajo||0}</span></div><div class='insightbox'><b>DEUDA CLIENTES</b><span class='${s.deudaClientes?'red':'green'}'>${money(s.deudaClientes)}</span></div></div>`;
    document.getElementById('financiero').innerHTML=arqueo.hayTurno?`<div class='metriclist'><div class='metricrow'><span>Efectivo esperado</span><b class='orange'>${money(arqueo.efectivoEsperado)}</b></div><div class='metricrow'><span>Mercado Pago esperado</span><b class='green'>${money(arqueo.mercadoPagoEsperado)}</b></div><div class='metricrow'><span>Total esperado</span><b>${money(arqueo.totalEsperado)}</b></div><div class='metricrow'><span>Ventas del turno</span><b>${money(s.ventas)}</b></div></div>`:`<div class='empty'>No hay caja abierta.</div>`;
    document.getElementById('pagos').innerHTML=pagos.length?pagos.map(x=>`<div class='metricrow'><span>${esc(x.medio)} · ${x.operaciones}</span><b>${money(x.importe)}</b></div>`).join(''):`<div class='empty'>Sin pagos registrados en el turno.</div>`;
    document.getElementById('movCaja').innerHTML=movCaja.length?`<table class='table'><thead><tr><th>HORA</th><th>TIPO</th><th>MEDIO</th><th>CONCEPTO</th><th>IMPORTE</th><th>USUARIO</th><th>ESTADO</th><th>MOTIVO</th></tr></thead><tbody>${movCaja.map(x=>`<tr class='${x.anulado?'voided':''}'><td>${esc(x.fecha)}</td><td>${esc(x.tipo)}</td><td>${esc(x.medio)}</td><td>${esc(x.concepto)}</td><td>${money(x.importe)}</td><td>${esc(x.usuario||'-')}</td><td>${x.anulado?'<b class="red">ANULADO</b>':'<b class="green">ACTIVO</b>'}</td><td>${esc(x.anulado?(x.motivoAnulacion||'-'):'-')}</td></tr>`).join('')}</tbody></table>`:`<div class='empty'>Sin movimientos de caja.</div>`;
    document.getElementById('movStock').innerHTML=movStock.length?`<table class='table'><thead><tr><th>FECHA</th><th>PRODUCTO</th><th>CÓDIGO</th><th>MOVIMIENTO</th><th>CANT.</th><th>REFERENCIA</th><th>USUARIO</th></tr></thead><tbody>${movStock.map(x=>`<tr><td>${esc(x.fecha)}</td><td>${esc(x.producto)}</td><td>${esc(x.codigo)}</td><td>${esc(x.tipo)}</td><td>${x.cantidad}</td><td>${esc(x.referencia)}</td><td>${esc(x.usuario||'-')}</td></tr>`).join('')}</tbody></table>`:`<div class='empty'>Sin movimientos de stock en el turno.</div>`;
    document.getElementById('stock').innerHTML=st.length?`<table class='table'><thead><tr><th>CÓDIGO</th><th>PRODUCTO</th><th>CATEGORÍA</th><th>STOCK</th><th>MÍNIMO</th><th>PRECIO</th></tr></thead><tbody>${st.map(x=>`<tr><td>${esc(x.codigo)}</td><td><b>${esc(x.producto)}</b></td><td>${esc(x.categoria||'-')}</td><td class='red'><b>${x.stock}</b></td><td>${x.minimo}</td><td>${money(x.precio)}</td></tr>`).join('')}</tbody></table>`:`<div class='empty'>✓ No hay productos con stock bajo.</div>`;
    document.getElementById('tecnicoBox').innerHTML=`<div class='tech'>${[['Aplicación',t.aplicacion],['Versión',t.version],['Servidor LAN',t.servidor],['Puerto',t.puerto],['Link público',t.url],['Base de datos',t.baseDatos],['Tamaño BD',t.baseDatosMB+' MB'],['Sistema',t.sistema],['Arquitectura',t.arquitectura],['Cloudflare',t.cloudflared+' · '+t.cloudflaredVersion],['Hora servidor',t.hora],['Modo','Solo lectura']].map(x=>`<div class='techbox'><b>${esc(x[0])}</b><span>${esc(x[1]||'-')}</span></div>`).join('')}</div>`;
    renderVentas();renderClientes();document.getElementById('actividadVentas').innerHTML=ventas.slice(0,20).map(x=>`<div class='metricrow'><span>#${esc(x.ticket)} · ${esc(x.cliente)}</span><b>${money(x.total)}</b></div>`).join('')||`<div class='empty'>Sin actividad.</div>`;document.getElementById('actividadResumen').innerHTML=`<div class='metriclist'><div class='metricrow'><span>Comercio</span><b>${esc(t.comercio||'-')}</b></div><div class='metricrow'><span>Rubro</span><b>${esc(t.rubro||'-')}</b></div><div class='metricrow'><span>Turno</span><b>#${esc(s.turno||'-')}</b></div><div class='metricrow'><span>Ventas</span><b class='orange'>${money(s.ventas)}</b></div><div class='metricrow'><span>Tickets</span><b>${s.tickets}</b></div></div>`;
    const publicOnline=String(t.url||'').includes('trycloudflare.com');document.getElementById('conexionPanel').className='panel '+(publicOnline?'connection-ok':'connection-warn');document.getElementById('conexionBox').innerHTML=`<div class='tech'><div class='techbox'><b>ESTADO PÚBLICO</b><span class='${publicOnline?'green':'yellow'}'>${publicOnline?'ONLINE':'ESPERANDO'}</span></div><div class='techbox'><b>LINK PÚBLICO</b><span>${esc(t.url||'Esperando enlace público...')}</span></div><div class='techbox'><b>SERVIDOR LOCAL</b><span>http://${esc(t.servidor||'localhost')}:${esc(t.puerto||'')}</span></div><div class='techbox'><b>QR ANDROID</b><span class='green'>Disponible en “QR ANDROID”</span></div><div class='techbox'><b>ÚLTIMA ACTUALIZACIÓN</b><span>${esc(t.hora||'-')}</span></div></div>`;
    const mp=t.mobile_pairing||{}; const qr=String(mp.qr_png_base64||''); document.getElementById('qrAndroidBox').innerHTML=qr?`<div class='androidqr'><img class='androidqr-img' src='data:image/png;base64,${qr}' alt='QR para conectar FerrariPOS Manager Android'><div><h3 class='androidqr-title'>Escaneá este QR desde FerrariPOS Manager Android</h3><p class='androidqr-help'>Este es el mismo QR de vinculación generado por FerrariPOS Windows. Incluye la conexión pública de Quick Tunnel cuando está disponible y la configuración necesaria para enlazar el Manager Android.</p><div class='androidqr-code'>${esc(mp.code||'-')}</div><div class='androidqr-url'>Quick Tunnel: ${esc(t.url||'Esperando enlace público...')}</div><p class='androidqr-help'>Si el Quick Tunnel cambia por una reconexión, el QR se actualiza automáticamente en este panel.</p></div></div>`:`<div class='qr-empty'>El QR todavía no está disponible. Esperá a que FerrariPOS genere el Quick Tunnel y actualizá el panel.</div>`;
    const partial=apiErrors.length>0; if(partial){showError('Conexión establecida, pero algunos módulos no respondieron: '+apiErrors.map(x=>x.split(':')[0].replace('/api/','')).join(', ')+'. Los demás datos siguen disponibles.')}else hideError();setStatus(partial?'loading':'','● '+(partial?'conectado · datos parciales':'conectado'));document.getElementById('liveText').textContent=publicOnline?(partial?'Quick Tunnel activo · sincronización parcial':'Quick Tunnel activo · datos sincronizados'):'Servidor activo · esperando Quick Tunnel';
  }catch(e){setStatus('error','● error de conexión');document.getElementById('liveText').textContent='No se pudo completar la sincronización';showError(e.message||'No se pudo cargar la información.');console.error(e)}finally{cargar.busy=false}}
cargar();setInterval(()=>cargar(false),30000);
</script>
</body></html>
""";
        return html.Replace("__BUSINESS_NAME__", safeBusiness).Replace("__BUSINESS_TYPE__", safeType).Replace("__PAGE_TITLE__", safeTitle);
    }
    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        try { _listener?.Stop(); } catch { }
        _listener = null;
        try { if (_cloudflared != null && !_cloudflared.HasExited) _cloudflared.Kill(true); } catch { }
        try { _cloudflared?.Dispose(); } catch { }
        _cloudflared = null;
        _cts?.Dispose();
        _cts = null;
        if (ReferenceEquals(Current, this)) Current = null;
    }
}
