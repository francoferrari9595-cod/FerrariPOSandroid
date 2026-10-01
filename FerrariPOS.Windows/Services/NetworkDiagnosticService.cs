using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace FerrarisPOS.Services;

/// <summary>
/// Diagnóstico y recuperación de la conexión pública de FerrariPOS.
/// No cambia la configuración normal de Cloudflare: solamente comprueba,
/// intenta reparar lo que puede y deja un informe sencillo en el Escritorio.
/// </summary>
public static class NetworkDiagnosticService
{
    private const int CloudflarePort = 7844;
    private static readonly string[] CloudflareEndpoints =
    {
        "region1.v2.argotunnel.com",
        "region2.v2.argotunnel.com"
    };

    public sealed record DiagnosticResult(string Name, string Status, string Detail);

    public static string DesktopReportPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "FerrariPOS_Diagnostico.txt");

    public static async Task<string> RepairAndDiagnoseAsync(CancellationToken cancellationToken = default, bool repair = true)
    {
        var results = new List<DiagnosticResult>();
        var started = DateTime.Now;
        var repairActions = new List<string>();

        results.Add(await CheckInternetAsync(cancellationToken));
        results.Add(await CheckDnsAsync("region1.v2.argotunnel.com", cancellationToken));
        results.Add(await CheckDnsAsync("region2.v2.argotunnel.com", cancellationToken));

        var server = WebDashboardServer.Current;
        if (server != null)
        {
            results.Add(await CheckLocalServerAsync(server, cancellationToken));
        }
        else
        {
            results.Add(new DiagnosticResult("Servidor FerrariPOS", "ERROR", "El servidor local de FerrariPOS no está iniciado."));
        }

        var cloudflared = FindCloudflared();
        results.Add(await CheckCloudflaredAsync(cloudflared, cancellationToken));

        foreach (var endpoint in CloudflareEndpoints)
        {
            results.Add(await CheckTcpAsync(endpoint, CloudflarePort, cancellationToken));
        }

        results.Add(await CheckCloudflareApiAsync(cancellationToken));
        results.Add(await RunCloudflaredDiagAsync(cloudflared, cancellationToken));

        var publicUrl = server?.AccessUrl;
        var publicHealthy = false;
        if (!string.IsNullOrWhiteSpace(publicUrl) && publicUrl.Contains("trycloudflare.com", StringComparison.OrdinalIgnoreCase))
        {
            publicHealthy = await CheckPublicUrlAsync(publicUrl!, cancellationToken);
            results.Add(new DiagnosticResult("URL pública Cloudflare", publicHealthy ? "OK" : "ERROR",
                publicHealthy ? publicUrl! : $"La URL no respondió correctamente: {publicUrl}"));
        }
        else
        {
            results.Add(new DiagnosticResult("URL pública Cloudflare", "ERROR", "Todavía no existe una URL pública válida."));
        }

        // Reparación segura: no reinicia un túnel que está sano. Solo limpia DNS
        // y fuerza una recuperación si el diagnóstico encontró un problema.
        if (repair && results.Any(x => x.Status == "ERROR" && (x.Name.StartsWith("DNS", StringComparison.Ordinal) || x.Name == "Internet")))
        {
            var flushed = await RunProcessAsync("ipconfig.exe", "/flushdns", TimeSpan.FromSeconds(15), cancellationToken);
            repairActions.Add(flushed.Success ? "Se vació la caché DNS." : "No se pudo vaciar la caché DNS: " + flushed.Error);
            results.Add(new DiagnosticResult("Reparación DNS", flushed.Success ? "OK" : "AVISO",
                flushed.Success ? "ipconfig /flushdns ejecutado." : flushed.Error));
        }

        if (repair && server != null && !publicHealthy)
        {
            var repaired = await server.RepairCloudflareAsync(cancellationToken);
            repairActions.Add(repaired
                ? "Se solicitó una recuperación controlada de Cloudflare; el proceso seguirá reconectando automáticamente."
                : "No fue necesario detener el proceso de Cloudflare porque ya estaba en recuperación.");
        }

        // Segunda comprobación breve después de las reparaciones.
        if (repair && server != null)
        {
            await Task.Delay(1200, cancellationToken).ConfigureAwait(false);
            var after = server.AccessUrl;
            if (!string.IsNullOrWhiteSpace(after) && after.Contains("trycloudflare.com", StringComparison.OrdinalIgnoreCase))
            {
                var afterHealthy = await CheckPublicUrlAsync(after, cancellationToken);
                results.Add(new DiagnosticResult("Comprobación final pública", afterHealthy ? "OK" : "AVISO",
                    afterHealthy ? "La conexión pública responde después de la reparación." : "Cloudflare todavía está reconectando; puede tardar unos segundos."));
            }
        }

        var report = BuildReport(started, results, repairActions, server?.AccessUrl);
        var path = DesktopReportPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, report, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        return path;
    }

    private static async Task<DiagnosticResult> CheckInternetAsync(CancellationToken token)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            using var response = await client.GetAsync("https://www.cloudflare.com/", HttpCompletionOption.ResponseHeadersRead, token);
            return new DiagnosticResult("Internet", response.IsSuccessStatusCode ? "OK" : "AVISO",
                $"HTTPS respondió HTTP {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return new DiagnosticResult("Internet", "ERROR", ex.Message);
        }
    }

    private static async Task<DiagnosticResult> CheckDnsAsync(string host, CancellationToken token)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, token).ConfigureAwait(false);
            return new DiagnosticResult("DNS · " + host, addresses.Length > 0 ? "OK" : "ERROR",
                addresses.Length > 0 ? $"Resolvió {addresses.Length} dirección(es)." : "No devolvió direcciones.");
        }
        catch (Exception ex)
        {
            return new DiagnosticResult("DNS · " + host, "ERROR", ex.Message);
        }
    }

    private static async Task<DiagnosticResult> CheckLocalServerAsync(WebDashboardServer server, CancellationToken token)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            using var response = await client.GetAsync($"http://127.0.0.1:{server.Port}/", HttpCompletionOption.ResponseHeadersRead, token);
            return new DiagnosticResult("Servidor FerrariPOS", response.IsSuccessStatusCode ? "OK" : "AVISO",
                $"Puerto local {server.Port}; HTTP {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return new DiagnosticResult("Servidor FerrariPOS", "ERROR", ex.Message);
        }
    }

    private static async Task<DiagnosticResult> CheckCloudflaredAsync(string? exe, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            return new DiagnosticResult("cloudflared", "ERROR", "No se encontró cloudflared.exe en la instalación de FerrariPOS.");

        var r = await RunProcessAsync(exe, "--version", TimeSpan.FromSeconds(15), token);
        if (!r.Success)
            return new DiagnosticResult("cloudflared", "ERROR", r.Error);
        return new DiagnosticResult("cloudflared", "OK", FirstLine(r.Output));
    }

    private static async Task<DiagnosticResult> CheckTcpAsync(string host, int port, CancellationToken token)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, token).ConfigureAwait(false);
            foreach (var address in addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork))
            {
                using var tcp = new TcpClient(address.AddressFamily);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(4));
                try
                {
                    await tcp.ConnectAsync(address, port, timeout.Token).ConfigureAwait(false);
                    return new DiagnosticResult($"TCP {host}:{port}", "OK", $"Conexión TCP establecida contra {address}.");
                }
                catch { }
            }
            return new DiagnosticResult($"TCP {host}:{port}", "ERROR", "No se pudo establecer conexión TCP al puerto 7844.");
        }
        catch (Exception ex)
        {
            return new DiagnosticResult($"TCP {host}:{port}", "ERROR", ex.Message);
        }
    }

    private static async Task<DiagnosticResult> CheckCloudflareApiAsync(CancellationToken token)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            using var response = await client.GetAsync("https://api.cloudflare.com/", HttpCompletionOption.ResponseHeadersRead, token);
            return new DiagnosticResult("Cloudflare API · TCP 443", response.IsSuccessStatusCode || (int)response.StatusCode < 500 ? "OK" : "AVISO",
                $"HTTPS respondió HTTP {(int)response.StatusCode}.");
        }
        catch (Exception ex)
        {
            return new DiagnosticResult("Cloudflare API · TCP 443", "AVISO", ex.Message);
        }
    }

    private static async Task<DiagnosticResult> RunCloudflaredDiagAsync(string? exe, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            return new DiagnosticResult("Pre-chequeo Cloudflare", "ERROR", "cloudflared no está disponible para ejecutar el diagnóstico nativo.");

        var r = await RunProcessAsync(exe, "tunnel diag", TimeSpan.FromSeconds(45), token);
        var combined = string.Join("\n", new[] { r.Output, r.Error }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (r.Success)
            return new DiagnosticResult("Pre-chequeo Cloudflare", "OK", CompactOutput(combined));

        // Algunas versiones antiguas no tienen `tunnel diag`; en ese caso no
        // convertimos el diagnóstico completo en un error si los demás controles
        // ya entregaron información útil.
        if (combined.Contains("unknown command", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("unknown flag", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("unrecognized", StringComparison.OrdinalIgnoreCase))
            return new DiagnosticResult("Pre-chequeo Cloudflare", "AVISO", "La versión instalada de cloudflared no incluye `tunnel diag`; se utilizaron las comprobaciones manuales del informe.");

        return new DiagnosticResult("Pre-chequeo Cloudflare", "AVISO", CompactOutput(combined));
    }

    private static async Task<bool> CheckPublicUrlAsync(string url, CancellationToken token)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            using var request = new HttpRequestMessage(HttpMethod.Get, url.TrimEnd('/') + "/api/mobile/ping");
            request.Headers.TryAddWithoutValidation("X-FerrariPOS-Token", WebDashboardServer.GetMobileTokenForDiagnostics());
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            return response.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private static string? FindCloudflared()
    {
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FerrarisPOS", "cloudflared.exe");
        if (File.Exists(local)) return local;
        var bundled = Path.Combine(AppContext.BaseDirectory, "cloudflared.exe");
        return File.Exists(bundled) ? bundled : null;
    }

    private static async Task<(bool Success, string Output, string Error)> RunProcessAsync(string file, string args, TimeSpan timeout, CancellationToken token)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = Path.GetDirectoryName(file) ?? AppContext.BaseDirectory
                }
            };
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(token);
            var errorTask = process.StandardError.ReadToEndAsync(token);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeoutCts.CancelAfter(timeout);
            try { await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(true); } catch { }
                return (false, await outputTask.ConfigureAwait(false), "Tiempo de espera agotado.");
            }
            return (process.ExitCode == 0, await outputTask.ConfigureAwait(false), await errorTask.ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
    }

    private static string BuildReport(DateTime started, IReadOnlyList<DiagnosticResult> results, IReadOnlyList<string> repairs, string? publicUrl)
    {
        var errors = results.Count(x => x.Status == "ERROR");
        var warnings = results.Count(x => x.Status == "AVISO");
        var status = errors == 0 ? "CONEXIÓN OPERATIVA / SIN ERRORES BLOQUEANTES" : "SE DETECTARON PROBLEMAS — REVISAR DIAGNÓSTICO";
        var sb = new StringBuilder();
        sb.AppendLine("FERRARIPOS · DIAGNÓSTICO DE CONEXIÓN");
        sb.AppendLine("====================================");
        sb.AppendLine($"Fecha: {started:dd/MM/yyyy HH:mm:ss}");
        sb.AppendLine($"Equipo: {Environment.MachineName}");
        sb.AppendLine($"Windows: {Environment.OSVersion}");
        sb.AppendLine($"Arquitectura: {Environment.Is64BitOperatingSystem switch { true => "64 bits", false => "32 bits" }}");
        sb.AppendLine();
        sb.AppendLine("RESULTADO");
        sb.AppendLine("---------");
        sb.AppendLine(status);
        sb.AppendLine($"Errores: {errors} · Avisos: {warnings}");
        if (!string.IsNullOrWhiteSpace(publicUrl)) sb.AppendLine($"URL pública detectada: {publicUrl}");
        sb.AppendLine();
        sb.AppendLine("COMPROBACIONES");
        sb.AppendLine("--------------");
        foreach (var r in results)
            sb.AppendLine($"[{r.Status}] {r.Name}: {r.Detail}");
        sb.AppendLine();
        sb.AppendLine("REPARACIONES REALIZADAS");
        sb.AppendLine("-----------------------");
        if (repairs.Count == 0) sb.AppendLine("No fue necesario realizar reparaciones automáticas.");
        else foreach (var action in repairs) sb.AppendLine("- " + action);
        sb.AppendLine();
        sb.AppendLine("INTERPRETACIÓN RÁPIDA");
        sb.AppendLine("---------------------");
        sb.AppendLine("OK = comprobación correcta.");
        sb.AppendLine("AVISO = no necesariamente impide la conexión.");
        sb.AppendLine("ERROR = hay un problema que puede impedir la conexión pública.");
        sb.AppendLine("Si TCP y UDP 7844 están bloqueados, la red/firewall debe permitir la salida hacia Cloudflare.");
        sb.AppendLine();
        sb.AppendLine("Este archivo fue generado automáticamente por FerrariPOS para soporte técnico.");
        return sb.ToString();
    }

    private static string FirstLine(string text) => text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "Sin información.";

    private static string CompactOutput(string text)
    {
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Take(8)
            .ToArray();
        return lines.Length == 0 ? "Sin salida de diagnóstico." : string.Join(" | ", lines);
    }
}
