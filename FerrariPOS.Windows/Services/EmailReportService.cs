using FerrarisPOS.Data;
using System.Net;
using System.Net.Mail;
using System.Security.Authentication;

namespace FerrarisPOS.Services;

public static class EmailReportService
{
    private sealed record EmailProfile(string Label, string To, string Host, int Port, string User, string Password, bool Ssl);

    private static bool ProfileConfigured(string suffix = "")
    {
        var enabledKey = string.IsNullOrEmpty(suffix) ? "report_email_enabled" : $"report_email{suffix}_enabled";
        var toKey = string.IsNullOrEmpty(suffix) ? "report_email_to" : $"report_email_to{suffix}";
        var hostKey = string.IsNullOrEmpty(suffix) ? "smtp_host" : $"smtp_host{suffix}";
        var userKey = string.IsNullOrEmpty(suffix) ? "smtp_user" : $"smtp_user{suffix}";
        var passwordKey = string.IsNullOrEmpty(suffix) ? "smtp_password" : $"smtp_password{suffix}";

        return Database.GetSetting(enabledKey, "0") == "1" &&
               !string.IsNullOrWhiteSpace(Database.GetSetting(toKey, "")) &&
               !string.IsNullOrWhiteSpace(Database.GetSetting(hostKey, "")) &&
               !string.IsNullOrWhiteSpace(Database.GetSetting(userKey, "")) &&
               !string.IsNullOrWhiteSpace(Database.GetSetting(passwordKey, ""));
    }

    public static bool IsConfigured => ProfileConfigured() || ProfileConfigured("2");

    private static EmailProfile ReadProfile(string suffix, string label)
    {
        var toKey = string.IsNullOrEmpty(suffix) ? "report_email_to" : $"report_email_to{suffix}";
        var hostKey = string.IsNullOrEmpty(suffix) ? "smtp_host" : $"smtp_host{suffix}";
        var portKey = string.IsNullOrEmpty(suffix) ? "smtp_port" : $"smtp_port{suffix}";
        var userKey = string.IsNullOrEmpty(suffix) ? "smtp_user" : $"smtp_user{suffix}";
        var passwordKey = string.IsNullOrEmpty(suffix) ? "smtp_password" : $"smtp_password{suffix}";
        var sslKey = string.IsNullOrEmpty(suffix) ? "smtp_ssl" : $"smtp_ssl{suffix}";
        var portText = Database.GetSetting(portKey, "587");
        if (!int.TryParse(portText, out var port)) port = 587;
        return new EmailProfile(
            label,
            Database.GetSetting(toKey, "").Trim(),
            Database.GetSetting(hostKey, "").Trim(),
            port,
            Database.GetSetting(userKey, "").Trim(),
            Database.GetSetting(passwordKey, ""),
            Database.GetSetting(sslKey, "1") == "1");
    }

    public static void SendToSecond(string subject, string body, string? attachmentPath = null)
    {
        if (!ProfileConfigured("2"))
            throw new InvalidOperationException("El segundo correo no está completamente configurado. Verificá destinatario, servidor SMTP 2, usuario y contraseña.");
        SendUsingProfile(ReadProfile("2", "Correo 2"), subject, body, attachmentPath == null ? null : new[] { attachmentPath });
    }

    public static void Send(string subject, string body, string? attachmentPath = null)
    {
        var profiles = new List<EmailProfile>();
        if (ProfileConfigured()) profiles.Add(ReadProfile("", "Correo 1"));
        if (ProfileConfigured("2")) profiles.Add(ReadProfile("2", "Correo 2"));

        if (profiles.Count == 0)
            throw new InvalidOperationException("El correo automático no está completamente configurado. Verificá al menos un destinatario, servidor SMTP, usuario y contraseña en F7 → Configuración.");

        var errors = new List<string>();
        var sent = 0;
        foreach (var profile in profiles)
        {
            try
            {
                SendUsingProfile(profile, subject, body, attachmentPath == null ? null : new[] { attachmentPath });
                sent++;
            }
            catch (Exception ex)
            {
                errors.Add($"{profile.Label} ({profile.To}): {ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            var prefix = sent > 0
                ? $"El informe se envió correctamente a {sent} destinatario(s), pero falló otro envío:"
                : "No se pudo enviar el informe a los destinatarios configurados:";
            throw new InvalidOperationException(prefix + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }
    }

    /// <summary>
    /// Envía automáticamente el enlace público del panel cada vez que un usuario
    /// ingresa correctamente al programa. No bloquea el inicio del POS.
    /// </summary>
    public static async Task SendPosLinkOnLoginAsync(int userId)
    {
        if (Database.GetSetting("pos_link_email_enabled", "1") != "1")
        {
            WriteMailLog("LOGIN: envío del link POS desactivado en Configuración.");
            return;
        }

        try
        {
            var dashboard = WebDashboardServer.Current;
            if (dashboard == null)
            {
                WriteMailLog("LOGIN: WebDashboardServer no está disponible.");
                return;
            }

            WriteMailLog($"LOGIN: esperando URL pública de Cloudflare para userId={userId}.");
            var publicUrl = await dashboard.WaitForPublicUrlAsync(TimeSpan.FromSeconds(180));
            if (string.IsNullOrWhiteSpace(publicUrl))
            {
                WriteMailLog("LOGIN: no se envió el link POS porque Cloudflare no entregó una URL pública dentro del tiempo de espera.");
                return;
            }

            var userName = GetUserName(userId);
            var subject = "FerrarisPOS® · Link POS · " + Database.GetSetting("business_name", "FerrariPOS") + " · ingreso al sistema";
            var body =
                "FERRARISPOS® · LINK POS FERRARI\r\n" +
                "================================\r\n\r\n" +
                "Se ingresó correctamente a FerrariPOS y el acceso público del panel está disponible.\r\n\r\n" +
                $"USUARIO: {userName}\r\n" +
                $"FECHA Y HORA: {DateTime.Now:dd/MM/yyyy HH:mm:ss}\r\n\r\n" +
                "LINK POS FERRARI · ADMINISTRADOR:\r\n" +
                publicUrl + "\r\n\r\n" +
                "Abrí este enlace desde un celular, otra PC o cualquier otra red.\r\n" +
                "El cliente NO necesita instalar Cloudflare ni realizar ninguna configuración técnica.\r\n\r\n" +
                "El enlace es temporal y puede cambiar al reiniciar FerrariPOS.";

            SendPosLinkEmail(subject, body);
            WriteMailLog($"LOGIN: link POS enviado correctamente. URL={publicUrl}; usuario={userName}.");
        }
        catch (Exception ex)
        {
            WriteMailLog("LOGIN: error al enviar el link POS: " + ex);
        }
    }

    /// <summary>
    /// Envía automáticamente el enlace público del panel cada vez que se abre una caja.
    /// Se ejecuta en segundo plano y espera a que Cloudflare termine de generar el Quick Tunnel.
    /// </summary>
    public static async Task SendPosLinkOnCashOpenAsync(long sessionId, double openingAmount, int userId)
    {
        if (Database.GetSetting("pos_link_email_enabled", "1") != "1") { WriteMailLog("Envío del link POS desactivado en Configuración."); return; }

        try
        {
            var dashboard = WebDashboardServer.Current;
            if (dashboard == null) return;

            var publicUrl = await dashboard.WaitForPublicUrlAsync(TimeSpan.FromSeconds(150));
            if (string.IsNullOrWhiteSpace(publicUrl))
            {
                WriteMailLog("No se envió el link POS: el Quick Tunnel público no estuvo disponible dentro del tiempo de espera.");
                return;
            }

            var userName = GetUserName(userId);
            var body =
                "FERRARISPOS® · LINK POS FERRARI\r\n" +
                "================================\r\n\r\n" +
                "Se abrió una nueva caja y el acceso público del panel POS ya está disponible.\r\n\r\n" +
                $"CAJA / TURNO: #{sessionId}\r\n" +
                $"CAJERO: {userName}\r\n" +
                $"FONDO INICIAL: ${openingAmount:N2}\r\n" +
                $"FECHA Y HORA: {DateTime.Now:dd/MM/yyyy HH:mm:ss}\r\n\r\n" +
                "LINK POS FERRARI · ADMINISTRADOR:\r\n" +
                publicUrl + "\r\n\r\n" +
                "Abrí este enlace desde un celular, otra PC o cualquier otra red.\r\n" +
                "El cliente NO necesita instalar Cloudflare ni realizar ninguna configuración técnica.\r\n\r\n" +
                "El enlace es temporal y puede cambiar al reiniciar FerrariPOS.";

            var subject = "FerrarisPOS® · Link POS · " + Database.GetSetting("business_name", "FerrariPOS") + " · turno #" + sessionId;
            SendPosLinkEmail(subject, body);
        }
        catch (Exception ex)
        {
            WriteMailLog("Error al enviar el link POS del turno #" + sessionId + ": " + ex.Message);
        }
    }

    private static string GetUserName(int userId)
    {
        try
        {
            using var cn = Database.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(full_name, username, '') FROM users WHERE id=$id LIMIT 1";
            cmd.Parameters.AddWithValue("$id", userId);
            return Convert.ToString(cmd.ExecuteScalar())?.Trim() ?? "-";
        }
        catch { return "-"; }
    }

    private static void WriteMailLog(string text)
    {
        try
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var folder = Path.Combine(documents, "Ferrari'sPOS");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, "FerrariPOS_email.log");
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}{Environment.NewLine}");
        }
        catch { }

        try
        {
            var fallback = Path.Combine(AppContext.BaseDirectory, "FerrarisPOS_email.log");
            File.AppendAllText(fallback, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {text}{Environment.NewLine}");
        }
        catch { }
    }

    private static void SendPosLinkEmail(string subject, string body)
    {
        var profiles = new List<EmailProfile>();
        if (CanUseProfile("")) profiles.Add(ReadProfile("", "Correo 1"));
        if (CanUseProfile("2")) profiles.Add(ReadProfile("2", "Correo 2"));
        if (profiles.Count == 0)
            throw new InvalidOperationException("No hay un correo SMTP completo para enviar el link POS. Completá destinatario, usuario, servidor, puerto y contraseña/APP PASSWORD en F7 → Configuración.");

        var errors = new List<string>();
        var sent = 0;
        foreach (var profile in profiles)
        {
            try { SendUsingProfile(profile, subject, body, null); sent++; }
            catch (Exception ex) { errors.Add($"{profile.Label} ({profile.To}): {ex.Message}"); }
        }
        if (sent == 0)
            throw new InvalidOperationException("No se pudo enviar el link POS por correo:" + Environment.NewLine + string.Join(Environment.NewLine, errors));
        if (errors.Count > 0)
            WriteMailLog("El link POS se envió a " + sent + " destinatario(s), pero falló otro: " + string.Join(" | ", errors));
    }

    private static bool CanUseProfile(string suffix)
    {
        var toKey = string.IsNullOrEmpty(suffix) ? "report_email_to" : $"report_email_to{suffix}";
        var hostKey = string.IsNullOrEmpty(suffix) ? "smtp_host" : $"smtp_host{suffix}";
        var userKey = string.IsNullOrEmpty(suffix) ? "smtp_user" : $"smtp_user{suffix}";
        var passwordKey = string.IsNullOrEmpty(suffix) ? "smtp_password" : $"smtp_password{suffix}";
        return !string.IsNullOrWhiteSpace(Database.GetSetting(toKey, "")) &&
               !string.IsNullOrWhiteSpace(Database.GetSetting(hostKey, "")) &&
               !string.IsNullOrWhiteSpace(Database.GetSetting(userKey, "")) &&
               !string.IsNullOrWhiteSpace(Database.GetSetting(passwordKey, ""));
    }

    public static void Send(string subject, string body, IEnumerable<string> attachmentPaths)
    {
        var profiles = new List<EmailProfile>();
        if (ProfileConfigured()) profiles.Add(ReadProfile("", "Correo 1"));
        if (ProfileConfigured("2")) profiles.Add(ReadProfile("2", "Correo 2"));
        if (profiles.Count == 0) throw new InvalidOperationException("El correo automático no está completamente configurado. Verificá al menos un destinatario, servidor SMTP, usuario y contraseña en F7 → Configuración.");
        var errors = new List<string>(); var sent = 0;
        foreach (var profile in profiles) { try { SendUsingProfile(profile, subject, body, attachmentPaths); sent++; } catch(Exception ex) { errors.Add($"{profile.Label} ({profile.To}): {ex.Message}"); } }
        if(errors.Count>0) throw new InvalidOperationException((sent>0 ? $"El informe se envió correctamente a {sent} destinatario(s), pero falló otro envío:" : "No se pudo enviar el informe a los destinatarios configurados:") + Environment.NewLine + Environment.NewLine + string.Join(Environment.NewLine, errors));
    }

    private static void SendUsingProfile(EmailProfile profile, string subject, string body, IEnumerable<string>? attachmentPaths)
    {
        using var message = new MailMessage
        {
            From = new MailAddress(profile.User),
            Subject = subject,
            Body = body,
            IsBodyHtml = false
        };
        message.To.Add(profile.To);

        if (attachmentPaths != null)
            foreach (var attachmentPath in attachmentPaths.Where(x => !string.IsNullOrWhiteSpace(x) && File.Exists(x)))
                message.Attachments.Add(new Attachment(attachmentPath));

        System.Net.ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;

        using var client = new SmtpClient(profile.Host, profile.Port)
        {
            EnableSsl = profile.Ssl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(profile.User, profile.Password),
            Timeout = 30000
        };

        try
        {
            client.Send(message);
        }
        catch (SmtpException ex) when (ex.Message.Contains("Authentication Required", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("5.7.0", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "El servidor SMTP rechazó la autenticación. Verificá que el usuario SMTP sea la cuenta que envía el correo y que la contraseña sea una contraseña de aplicación cuando el proveedor la requiera. En Gmail no funciona la contraseña normal de la cuenta.", ex);
        }
    }

    public static string SuggestHost(string email)
    {
        var domain = email.Trim().ToLowerInvariant().Split('@').LastOrDefault() ?? "";
        return domain switch
        {
            "gmail.com" => "smtp.gmail.com",
            "outlook.com" or "hotmail.com" or "live.com" or "msn.com" => "smtp-mail.outlook.com",
            "yahoo.com" or "yahoo.com.ar" => "smtp.mail.yahoo.com",
            "icloud.com" or "me.com" => "smtp.mail.me.com",
            _ => ""
        };
    }
}
