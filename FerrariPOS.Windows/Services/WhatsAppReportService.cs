using FerrarisPOS.Data;
using System.Diagnostics;
using System.Text;

namespace FerrarisPOS.Services;

public static class WhatsAppReportService
{
    public static bool IsConfigured =>
        Database.GetSetting("whatsapp_enabled", "0") == "1" &&
        !string.IsNullOrWhiteSpace(Database.GetSetting("whatsapp_country", "54")) &&
        !string.IsNullOrWhiteSpace(Database.GetSetting("whatsapp_number", ""));

    public static string FullNumber()
    {
        var country = new string(Database.GetSetting("whatsapp_country", "54").Where(char.IsDigit).ToArray());
        var number = new string(Database.GetSetting("whatsapp_number", "").Where(char.IsDigit).ToArray());
        if (number.StartsWith("0")) number = number.TrimStart('0');
        return country + number;
    }

    public static void OpenWhatsApp(string subject, string report, string? attachmentPath = null)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Configurá el país y número de WhatsApp en F7 → Configuración.");

        var sb = new StringBuilder();
        sb.Append(subject).AppendLine().AppendLine();
        sb.Append(report);
        if (!string.IsNullOrWhiteSpace(attachmentPath) && File.Exists(attachmentPath))
            sb.AppendLine().AppendLine("El Excel detallado fue guardado en la PC y puede adjuntarse al chat.");

        var url = "https://wa.me/" + FullNumber() + "?text=" + Uri.EscapeDataString(sb.ToString());
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public static void OpenSms(string subject, string report)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Configurá el país y número de WhatsApp en F7 → Configuración.");
        var text = Uri.EscapeDataString(subject + Environment.NewLine + Environment.NewLine + report);
        var uri = "sms:" + FullNumber() + "?body=" + text;
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }
}
