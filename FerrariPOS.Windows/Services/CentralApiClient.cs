using FerrarisPOS.Data;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace FerrarisPOS.Services;

/// <summary>
/// Conexión no bloqueante con FerrariPOS Central.
/// Registro y heartbeat automáticos. Si el servidor no está disponible,
/// el POS continúa trabajando localmente y reintenta sin bloquear la interfaz.
/// </summary>
public static class CentralApiClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private static CancellationTokenSource? _heartbeatCts;
    private static int _heartbeatStarted;

    private static readonly string BaseUrl =
        (Environment.GetEnvironmentVariable("FERRARIPOS_CENTRAL_URL")
         ?? Database.GetSetting("central_url", "https://ferraripos-central.onrender.com"))
        .TrimEnd('/');

    public static string CentralUrl => BaseUrl;
    public static string StoreId => Database.GetSetting("central_store_id", "");
    public static string Token => Database.GetSetting("central_token", "");
    public static string WebUsername => Database.GetSetting("central_web_username", "");
    public static string WebPassword => Database.GetSetting("central_web_password", "");
    public static string WebPanelUrl => CentralUrl;
    public static bool IsConnected => Database.GetSetting("central_connected", "0") == "1";

    public static async Task RegisterInstallationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var installationId = GetOrCreateInstallationId();
            var storeName = Database.GetSetting("business_name", "FerrariPOS");

            using var response = await Http.PostAsJsonAsync(
                $"{BaseUrl}/api/v1/stores/register",
                new { installationId, storeName },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                Database.SetSetting("central_connected", "0");
                return;
            }

            using var doc = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken));
            var root = doc.RootElement;

            if (root.TryGetProperty("store_id", out var storeId))
                Database.SetSetting("central_store_id", storeId.GetString() ?? "");

            if (root.TryGetProperty("token", out var token))
                Database.SetSetting("central_token", token.GetString() ?? "");

            if (root.TryGetProperty("web_username", out var webUsername))
                Database.SetSetting("central_web_username", webUsername.GetString() ?? "");

            if (root.TryGetProperty("web_password", out var webPassword) &&
                webPassword.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(webPassword.GetString()))
                Database.SetSetting("central_web_password", webPassword.GetString()!);

            Database.SetSetting("central_url", BaseUrl);
            Database.SetSetting("central_connected", "1");
            Database.SetSetting("central_last_ok_utc", DateTimeOffset.UtcNow.ToString("O"));

            StartHeartbeat();
        }
        catch
        {
            Database.SetSetting("central_connected", "0");
        }
    }

    public static async Task EnsureWebAccountAsync(CancellationToken cancellationToken = default)
    {
        await RegisterInstallationAsync(cancellationToken);
    }

    public static async Task<string?> ResetWebPasswordAsync(CancellationToken cancellationToken = default)
    {
        var storeId = StoreId;
        var token = Token;
        if (string.IsNullOrWhiteSpace(storeId) || string.IsNullOrWhiteSpace(token))
            return null;

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{BaseUrl}/api/v1/stores/{Uri.EscapeDataString(storeId)}/web-password/reset");
        request.Headers.TryAddWithoutValidation("X-FerrariPOS-Token", token);

        using var response = await Http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        using var doc = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));
        var password = doc.RootElement.TryGetProperty("password", out var p)
            ? p.GetString()
            : null;

        if (!string.IsNullOrWhiteSpace(password))
            Database.SetSetting("central_web_password", password);

        return password;
    }

    public static void StartHeartbeat()
    {
        if (Interlocked.Exchange(ref _heartbeatStarted, 1) != 0)
            return;

        _heartbeatCts = new CancellationTokenSource();
        _ = Task.Run(() => HeartbeatLoopAsync(_heartbeatCts.Token));
    }

    private static async Task HeartbeatLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), ct);

                if (string.IsNullOrWhiteSpace(StoreId) || string.IsNullOrWhiteSpace(Token))
                {
                    await RegisterInstallationAsync(ct);
                    continue;
                }

                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"{BaseUrl}/api/v1/stores/{Uri.EscapeDataString(StoreId)}/heartbeat");
                request.Headers.TryAddWithoutValidation("X-FerrariPOS-Token", Token);

                using var response = await Http.SendAsync(request, ct);
                Database.SetSetting("central_connected", response.IsSuccessStatusCode ? "1" : "0");

                if (response.IsSuccessStatusCode)
                    Database.SetSetting("central_last_ok_utc", DateTimeOffset.UtcNow.ToString("O"));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                Database.SetSetting("central_connected", "0");
            }
        }
    }

    private static string GetOrCreateInstallationId()
    {
        var existing = Database.GetSetting("central_installation_id", "");
        if (!string.IsNullOrWhiteSpace(existing))
            return existing;

        var bytes = RandomNumberGenerator.GetBytes(24);
        var id = Convert.ToHexString(bytes).ToLowerInvariant();
        Database.SetSetting("central_installation_id", id);
        return id;
    }
}
