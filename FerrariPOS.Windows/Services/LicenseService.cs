using FerrarisPOS.Data;
using Microsoft.Win32;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FerrarisPOS.Services;

/// <summary>
/// Gestión local de licencias FerrarisPOS.
/// Las licencias son tokens firmados con RSA y vinculados al Machine ID.
/// El cliente contiene únicamente la clave pública; la clave privada permanece en LicenseAdmin.
/// Acciones soportadas: ACTIVATE, RENEW y DEACTIVATE.
/// </summary>
public static class LicenseService
{
    private const int TrialDays = 10;
    private const string LicensePrefix = "FPOS-LIC-3";

    private const string PublicKeyPem = """
-----BEGIN PUBLIC KEY-----
MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEA5wo6ctuo6jegOskJOonP
oxPd/ka6NF/K5m96rK/Pe65yfkAlb6xN3xx5tmek0JW66b1Ai9x/u2ltQeY+gUS2
xRXloRlTx3JzJQs3QQMr5w9nlBPSDIh9r/bxS0tEStGQynrpx4KNH95Wycp9pMPg
CG6n0/n96tPM5nW2hBfRfXpolkmNnelu3MIJWV0069nV9z/KiQUM/TxhG+2yONC8
73ia932vDumh9tTZyzzI27PHKY5Q+3W2NJabHEMqpNfL4nHKG9Lj3fH3YtR+3Zx0
RzwEBrY8SJbBkgOm2DFj89mnKxklzD8DpJ4FYcXYPNAvHbLXQtA0tUQTOHabtcci
hPsBJNb1AHhJcxt3TcHVxtzUq1ESAcLclOt8FKRtPAZC7SQoKy8FVzwrl0rG2Xdh
72wGq0v7XjeQ/aU4NDziafYuyYp/5NIe0JlR01kpRtQmmtp6NwG7fGHEL8rB4Bbv
9uvTFTsjKPJrutR/b9SqXpm79aUb6LdP7ND7FlSZGf+5AgMBAAE=
-----END PUBLIC KEY-----
""";

    private static readonly string LicenseDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "FerrarisPOS_License");
    private static readonly string LicenseFile = Path.Combine(LicenseDirectory, "license.dat");
    private static readonly string LicenseServerConfigFile = Path.Combine(AppContext.BaseDirectory, "license_server.json");
    private static bool _onlineRevoked;
    private static System.Threading.Timer? _onlineTimer;

    private const string RegistryPath = @"Software\FerrarisPOS_License";
    private const string RegistryInstalledAt = "InstalledAtUtc";
    private const string RegistryActivated = "Activated";
    private const string RegistryMachineId = "MachineId";
    private const string RegistryLicenseId = "LicenseId";
    private const string RegistryExpiresAt = "ExpiresAtUtc";
    private const string RegistryType = "Type";
    private const string RegistryCustomerName = "CustomerName";

    private static LicenseState _state = new();
    private static bool _initialized;

    public static int DaysRemaining
    {
        get
        {
            EnsureInitialized();
            if (IsPermanent) return -1;
            return Math.Max(0, (int)Math.Ceiling((_state.ExpiresAtUtc - SafeNowUtc()).TotalDays));
        }
    }

    public static bool IsPermanent =>
        EnsureAndGet(s => s.Activated &&
            string.Equals(s.Type, "PERMANENT", StringComparison.OrdinalIgnoreCase));

    public static bool IsActivated => EnsureAndGet(s => s.Activated);

    public static string MachineId
    {
        get { EnsureInitialized(); return _state.MachineId; }
    }

    public static string LicenseId
    {
        get { EnsureInitialized(); return _state.LicenseId; }
    }

    public static string CustomerName
    {
        get { EnsureInitialized(); return _state.CustomerName; }
    }

    public static string LicenseType
    {
        get { EnsureInitialized(); return _state.Type; }
    }

    public static DateTime ExpiresAtUtc
    {
        get { EnsureInitialized(); return _state.ExpiresAtUtc; }
    }

    public static bool IsExpired => !IsPermanent && SafeNowUtc() >= _state.ExpiresAtUtc;

    public static void Initialize()
    {
        if (_initialized) return;

        Directory.CreateDirectory(LicenseDirectory);
        var machineId = GetMachineId();

        if (File.Exists(LicenseFile) && TryLoad(out var saved) &&
            string.Equals(saved.MachineId, machineId, StringComparison.OrdinalIgnoreCase))
        {
            _state = saved;
        }
        else if (TryLoadRegistry(machineId, out var registryState))
        {
            _state = registryState;
            Save();
        }
        else
        {
            var now = DateTime.UtcNow;
            _state = new LicenseState
            {
                MachineId = machineId,
                InstalledAtUtc = now,
                ExpiresAtUtc = now.AddDays(TrialDays),
                LastSeenUtc = now,
                Activated = false,
                Type = "TRIAL",
                LicenseId = "",
                CustomerName = ""
            };
            Save();
            SaveRegistry();
        }

        _initialized = true;
        TouchAndPersist();
        SyncDatabase();
    }

    public static bool CanRun()
    {
        EnsureInitialized();
        TouchAndPersist();
        return IsPermanent || SafeNowUtc() < _state.ExpiresAtUtc;
    }

    /// <summary>
    /// Aplica una licencia firmada. Se mantiene este método para que la UI existente
    /// pueda seguir usando el botón de activación.
    /// </summary>
    /// <summary>Consulta el servidor de licencias cuando hay Internet. Solo una respuesta explícita de revocación bloquea el equipo.</summary>
    /// <summary>
    /// Modo 100% offline: no consulta servidores ni requiere Internet.
    /// Se conserva la API para compatibilidad con versiones anteriores.
    /// </summary>
    public static bool CheckOnlineStatus() => false;

    /// <summary>
    /// Modo 100% offline: no inicia ningún temporizador de red.
    /// Se conserva la API para compatibilidad.
    /// </summary>
    public static void StartOnlineMonitor()
    {
        EnsureInitialized();
        _onlineTimer?.Dispose();
        _onlineTimer = null;
    }

    public static bool WasRevokedOnline => _onlineRevoked;

    public static bool Activate(string token) => ApplyLicenseToken(token, out _);

    public static bool ApplyLicenseToken(string token, out string message)
    {
        EnsureInitialized();
        message = "";

        if (!TryReadSignedLicense(token, out var license, out message))
            return false;

        if (!string.Equals(license.MachineId, _state.MachineId, StringComparison.OrdinalIgnoreCase))
        {
            message = "El código pertenece a otra computadora (Machine ID diferente).";
            return false;
        }

        // No se valida la hora de emisión contra el reloj local del cliente.
        // El cliente puede tener una hora/zona horaria diferente a la PC del administrador.
        // La licencia continúa protegida por firma, Machine ID y vencimiento.
        switch ((license.Action ?? "ACTIVATE").Trim().ToUpperInvariant())
        {
            case "ACTIVATE":
                return ApplyActivation(license, out message);

            case "RENEW":
                return ApplyRenewal(license, out message);

            case "DEACTIVATE":
                return ApplyDeactivation(license, out message);

            default:
                message = "La acción de la licencia no es reconocida.";
                return false;
        }
    }

    private static bool ApplyActivation(LicensePayload license, out string message)
    {
        message = "";

        if (string.IsNullOrWhiteSpace(license.LicenseId))
        {
            message = "La licencia no tiene un ID válido.";
            return false;
        }

        if (!IsValidLicenseType(license.Type))
        {
            message = "El tipo de licencia no es válido.";
            return false;
        }

        // Una ACTIVATE firmada por el administrador siempre puede reemplazar
        // la licencia actualmente instalada en ESTA computadora.
        // No se obliga al cliente a usar RENEW para cambiar de licencia.
        DateTime expires;
        if (IsPermanentType(license.Type))
        {
            expires = DateTime.MaxValue;
        }
        else if (license.DurationDays.HasValue && license.DurationDays.Value > 0)
        {
            // La duración de una licencia nueva se cuenta desde la instalación,
            // no desde el momento en que el administrador genera el token.
            expires = _state.InstalledAtUtc.AddDays(license.DurationDays.Value);
        }
        else if (license.ExpiresAtUtc.HasValue)
        {
            // Compatibilidad con tokens anteriores que ya traían una fecha exacta.
            expires = license.ExpiresAtUtc.Value.ToUniversalTime();
        }
        else
        {
            message = "La licencia no tiene duración ni fecha de vencimiento.";
            return false;
        }

        if (expires <= DateTime.UtcNow)
        {
            message = $"La licencia ya venció según la fecha de instalación ({FormatLocal(expires)}).";
            return false;
        }

        license.ExpiresAtUtc = expires;
        ApplyState(license, activated: true);
        message = IsPermanent
            ? "Licencia reemplazada y activada correctamente. Licencia permanente."
            : $"Licencia reemplazada y activada correctamente. Vence el {FormatLocal(_state.ExpiresAtUtc)}.";
        return true;
    }

    private static bool ApplyRenewal(LicensePayload license, out string message)
    {
        message = "";

        if (string.IsNullOrWhiteSpace(_state.LicenseId) ||
            !string.Equals(license.LicenseId, _state.LicenseId, StringComparison.OrdinalIgnoreCase))
        {
            message = "El ID de licencia de renovación no coincide con el de esta computadora.";
            return false;
        }

        if (!IsValidLicenseType(license.Type))
        {
            message = "El tipo de licencia no es válido.";
            return false;
        }

        DateTime expires;
        if (IsPermanentType(license.Type))
            expires = DateTime.MaxValue;
        else if (license.DurationDays.HasValue && license.DurationDays.Value > 0)
        {
            var baseDate = _state.ExpiresAtUtc > DateTime.UtcNow ? _state.ExpiresAtUtc : DateTime.UtcNow;
            expires = baseDate.AddDays(license.DurationDays.Value);
        }
        else if (license.ExpiresAtUtc.HasValue)
            expires = license.ExpiresAtUtc.Value.ToUniversalTime();
        else
        {
            message = "La renovación no tiene duración ni fecha de vencimiento.";
            return false;
        }

        if (expires <= DateTime.UtcNow)
        {
            message = "La nueva fecha de vencimiento ya pasó.";
            return false;
        }

        license.ExpiresAtUtc = expires;
        ApplyState(license, activated: true);
        message = IsPermanent
            ? "Licencia renovada y establecida como permanente."
            : $"Licencia renovada. Nuevo vencimiento: {FormatLocal(_state.ExpiresAtUtc)}.";
        return true;
    }

    private static bool ApplyDeactivation(LicensePayload license, out string message)
    {
        message = "";

        if (string.IsNullOrWhiteSpace(_state.LicenseId) ||
            !string.Equals(license.LicenseId, _state.LicenseId, StringComparison.OrdinalIgnoreCase))
        {
            message = "El ID de licencia de desactivación no coincide con el de esta computadora.";
            return false;
        }

        _state.Activated = false;
        _state.Type = "DEACTIVATED";
        _state.ExpiresAtUtc = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(license.CustomerName))
            _state.CustomerName = license.CustomerName;
        _state.LastSeenUtc = SafeNowUtc();

        Save();
        SaveRegistry();
        SyncDatabase();

        message = "La licencia fue desactivada correctamente. Para volver a usar el sistema necesitás una nueva activación o renovación.";
        return true;
    }

    private static void ApplyState(LicensePayload license, bool activated)
    {
        _state.Activated = activated;
        _state.Type = (license.Type ?? "CUSTOM").Trim().ToUpperInvariant();
        _state.LicenseId = license.LicenseId;
        _state.CustomerName = license.CustomerName ?? "";
        _state.ExpiresAtUtc = license.ExpiresAtUtc ?? DateTime.MaxValue;
        _state.LastSeenUtc = SafeNowUtc();

        Save();
        SaveRegistry();
        SyncDatabase();
    }

    private static bool IsValidLicenseType(string? type)
    {
        var value = (type ?? "").Trim().ToUpperInvariant();
        return value is "PERMANENT" or "ANNUAL" or "CUSTOM";
    }

    private static bool IsPermanentType(string? type) =>
        string.Equals(type, "PERMANENT", StringComparison.OrdinalIgnoreCase);

    public static string GetStatusText()
    {
        EnsureInitialized();

        if (_state.Activated)
        {
            var suffix = string.IsNullOrWhiteSpace(_state.CustomerName)
                ? ""
                : $" · {_state.CustomerName}";

            if (IsPermanent)
                return $"LICENCIA: ACTIVADA · PERMANENTE{suffix}";

            var typeLabel = _state.Type == "ANNUAL" ? "ANUAL" : "ACTIVA";
            return $"LICENCIA {typeLabel}: {DaysRemaining} DÍAS RESTANTES · VENCE {FormatLocal(_state.ExpiresAtUtc)}{suffix}";
        }

        if (string.Equals(_state.Type, "DEACTIVATED", StringComparison.OrdinalIgnoreCase))
            return "LICENCIA: DESACTIVADA";

        return "LICENCIA: SIN ACTIVAR · ID MACHINE DISPONIBLE EN CONFIGURACIÓN";
    }

    private static bool TryReadSignedLicense(string token, out LicensePayload license, out string message)
    {
        license = new();
        message = "";

        try
        {
            token = NormalizePastedToken(token);
            var parts = token.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 || (parts[0] != LicensePrefix && parts[0] != "FPOS-LIC-2" && parts[0] != "FPOS-LIC-1"))
            {
                message = "El código no tiene el formato de una licencia FerrarisPOS V72.";
                return false;
            }

            var payloadBytes = Base64UrlDecode(parts[1]);
            var signature = Base64UrlDecode(parts[2]);
            var payloadText = Encoding.UTF8.GetString(payloadBytes);

            license = JsonSerializer.Deserialize<LicensePayload>(payloadText) ?? new();
            if (string.IsNullOrWhiteSpace(license.MachineId) ||
                string.IsNullOrWhiteSpace(license.LicenseId))
            {
                message = "La licencia está incompleta.";
                return false;
            }

            using var rsa = RSA.Create();
            rsa.ImportFromPem(PublicKeyPem);

            if (!rsa.VerifyData(
                    payloadBytes,
                    signature,
                    HashAlgorithmName.SHA256,
                    RSASignaturePadding.Pss))
            {
                message = "La firma digital de la licencia no es válida.";
                return false;
            }

            return true;
        }
        catch
        {
            message = "No se pudo leer o verificar el código de licencia.";
            return false;
        }
    }

    private static string NormalizePastedToken(string? raw)
    {
        var value = (raw ?? "").Trim().Trim('\"');
        // Acepta el token aunque se haya copiado desde un .txt, correo, consola
        // o se haya partido visualmente en varias líneas.
        var compact = Regex.Replace(value, @"\s+", "");
        var match = Regex.Match(compact, @"(FPOS-LIC-(?:1|2|3))\.([A-Za-z0-9_-]+)\.([A-Za-z0-9_-]+)", RegexOptions.CultureInvariant);
        if (match.Success) return $"{match.Groups[1].Value}.{match.Groups[2].Value}.{match.Groups[3].Value}";

        // Segundo intento: buscar el token en el texto original sin exigir que el
        // contenido anterior/posterior sea parte del código.
        match = Regex.Match(value, @"FPOS-LIC-(?:1|2|3)\s*\.\s*[A-Za-z0-9_\-\s]+\.\s*[A-Za-z0-9_\-\s]+", RegexOptions.CultureInvariant);
        if (match.Success)
        {
            compact = Regex.Replace(match.Value, @"\s+", "");
            return compact;
        }

        return compact;
    }

    private static string ReadLicenseServerUrl()
    {
        try
        {
            if (!File.Exists(LicenseServerConfigFile)) return "";
            using var doc = JsonDocument.Parse(File.ReadAllText(LicenseServerConfigFile));
            if (doc.RootElement.TryGetProperty("ServerUrl", out var url)) return url.GetString() ?? "";
        }
        catch { }
        return "";
    }

    private static byte[] Base64UrlDecode(string value)
    {
        value = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(
            value.PadRight(value.Length + (4 - value.Length % 4) % 4, '='));
    }

    private static void EnsureInitialized()
    {
        if (!_initialized) Initialize();
    }

    private static T EnsureAndGet<T>(Func<LicenseState, T> selector)
    {
        EnsureInitialized();
        return selector(_state);
    }

    private static DateTime SafeNowUtc()
    {
        var now = DateTime.UtcNow;
        return _state.LastSeenUtc > now ? _state.LastSeenUtc : now;
    }

    private static void TouchAndPersist()
    {
        var now = DateTime.UtcNow;
        if (now > _state.LastSeenUtc)
        {
            _state.LastSeenUtc = now;
            Save();
            SaveRegistry();
        }
    }

    private static string GetMachineId()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            var guid = key?.GetValue("MachineGuid")?.ToString();
            if (!string.IsNullOrWhiteSpace(guid))
                return Hash(guid.Trim());
        }
        catch { }

        return Hash(Environment.MachineName + "|" + Environment.ProcessorCount);
    }

    private static bool TryLoadRegistry(string machineId, out LicenseState state)
    {
        state = new();

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
            if (key == null) return false;

            var storedMachine = key.GetValue(RegistryMachineId)?.ToString();
            var installedText = key.GetValue(RegistryInstalledAt)?.ToString();

            if (!string.Equals(storedMachine, machineId, StringComparison.OrdinalIgnoreCase) ||
                !DateTime.TryParse(
                    installedText,
                    null,
                    System.Globalization.DateTimeStyles.RoundtripKind,
                    out var installed))
                return false;

            state = new LicenseState
            {
                MachineId = machineId,
                InstalledAtUtc = installed.ToUniversalTime(),
                ExpiresAtUtc =
                    DateTime.TryParse(
                        Convert.ToString(key.GetValue(RegistryExpiresAt, "")),
                        null,
                        System.Globalization.DateTimeStyles.RoundtripKind,
                        out var exp)
                        ? exp.ToUniversalTime()
                        : installed.ToUniversalTime().AddDays(TrialDays),
                LastSeenUtc = DateTime.UtcNow < installed
                    ? installed.ToUniversalTime()
                    : DateTime.UtcNow,
                Activated = Convert.ToInt32(key.GetValue(RegistryActivated, 0)) != 0,
                Type = Convert.ToString(key.GetValue(RegistryType, "TRIAL")) ?? "TRIAL",
                LicenseId = Convert.ToString(key.GetValue(RegistryLicenseId, "")) ?? "",
                CustomerName = Convert.ToString(key.GetValue(RegistryCustomerName, "")) ?? ""
            };

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void SaveRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath);
            key?.SetValue(RegistryMachineId, _state.MachineId);
            key?.SetValue(RegistryInstalledAt, _state.InstalledAtUtc.ToString("o"));
            key?.SetValue(RegistryActivated, _state.Activated ? 1 : 0, RegistryValueKind.DWord);
            key?.SetValue(RegistryLicenseId, _state.LicenseId);
            key?.SetValue(RegistryExpiresAt, _state.ExpiresAtUtc.ToString("o"));
            key?.SetValue(RegistryType, _state.Type);
            key?.SetValue(RegistryCustomerName, _state.CustomerName);
        }
        catch { }
    }

    private static string Hash(string value)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }

    private static bool TryLoad(out LicenseState state)
    {
        state = new();

        try
        {
            var loaded = JsonSerializer.Deserialize<LicenseState>(
                File.ReadAllText(LicenseFile));

            if (loaded == null ||
                loaded.InstalledAtUtc == default ||
                loaded.ExpiresAtUtc == default ||
                string.IsNullOrWhiteSpace(loaded.MachineId))
                return false;

            state = loaded;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(LicenseDirectory);

            var temp = LicenseFile + ".tmp";
            File.WriteAllText(
                temp,
                JsonSerializer.Serialize(
                    _state,
                    new JsonSerializerOptions { WriteIndented = true }));

            File.Move(temp, LicenseFile, true);

            try
            {
                File.SetAttributes(
                    LicenseFile,
                    FileAttributes.Hidden | FileAttributes.System);
            }
            catch { }
        }
        catch { }
    }

    private static void SyncDatabase()
    {
        try
        {
            using var cn = Database.Open();
            using var cmd = cn.CreateCommand();

            var status = IsPermanent
                ? "PERMANENT"
                : _state.Activated
                    ? _state.Type
                    : _state.Type == "DEACTIVATED"
                        ? "DEACTIVATED"
                        : "TRIAL";

            cmd.CommandText =
                "UPDATE license SET license_key=$k, installed_at=$i, expires_at=$e, status=$s WHERE id=1";

            cmd.Parameters.AddWithValue("$k", _state.LicenseId);
            cmd.Parameters.AddWithValue("$i", _state.InstalledAtUtc.ToString("o"));
            cmd.Parameters.AddWithValue("$e", _state.ExpiresAtUtc.ToString("o"));
            cmd.Parameters.AddWithValue("$s", status);
            cmd.ExecuteNonQuery();
        }
        catch { }
    }

    private static string FormatLocal(DateTime utc)
    {
        if (utc >= DateTime.MaxValue.AddDays(-1))
            return "PERMANENTE";

        return utc.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    }

    private sealed class LicenseState
    {
        public string MachineId { get; set; } = "";
        public DateTime InstalledAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }
        public bool Activated { get; set; }
        public string Type { get; set; } = "TRIAL";
        public string LicenseId { get; set; } = "";
        public string CustomerName { get; set; } = "";
    }

    private sealed class LicensePayload
    {
        public string Action { get; set; } = "ACTIVATE";
        public string LicenseId { get; set; } = "";
        public string MachineId { get; set; } = "";
        public string Type { get; set; } = "CUSTOM";
        public DateTime IssuedAtUtc { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
        public int? DurationDays { get; set; }
        public string CustomerName { get; set; } = "";
    }
}
