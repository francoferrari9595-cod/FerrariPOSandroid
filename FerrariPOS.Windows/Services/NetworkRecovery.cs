using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net;
using System.Net.Sockets;

namespace FerrarisPOS.Services;

/// <summary>
/// Recuperación de red no interactiva para FerrariPOS.
/// Primero prueba la resolución normal. Si falla, intenta vaciar caché y,
/// solo cuando la aplicación está elevada, repara DNS de adaptadores activos.
/// El instalador ya realiza esta operación con privilegios administrativos.
/// </summary>
internal static class NetworkRecovery
{
    public static async Task TryRecoverAsync(CancellationToken token)
    {
        try
        {
            // Recuperación rápida y conservadora: primero dejamos que Windows
            // termine de asociar la nueva red/Wi-Fi. No cambiamos servidores DNS
            // automáticamente para no alterar la red del cliente.
            if (await HasInternetAndDnsAsync(token))
                return;

            await RunAsync("ipconfig.exe", "/flushdns", token);

            // Tras un cambio de Wi-Fi, una segunda comprobación inmediata suele
            // ser suficiente para que cloudflared pueda negociar nuevamente.
            for (var i = 0; i < 3 && !token.IsCancellationRequested; i++)
            {
                if (await HasInternetAndDnsAsync(token))
                    return;
                await Task.Delay(250, token);
            }
        }
        catch { }
    }

    private static async Task<bool> HasInternetAndDnsAsync(CancellationToken token)
    {
        try
        {
            var dns = await Dns.GetHostAddressesAsync("region1.v2.argotunnel.com", token).ConfigureAwait(false);
            if (dns.Length == 0) return false;

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            using var response = await client.GetAsync("https://api.cloudflare.com/", HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            return (int)response.StatusCode < 500;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> ResolvesCloudflareAsync(CancellationToken token)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            using var request = new HttpRequestMessage(HttpMethod.Head, "https://api.trycloudflare.com/");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            return response.StatusCode != System.Net.HttpStatusCode.ServiceUnavailable;
        }
        catch
        {
            return false;
        }
    }

    private static async Task RunAsync(string file, string args, CancellationToken token)
    {
        using var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = file,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        p.Start();
        await p.WaitForExitAsync(token);
    }
}
