using System.Drawing.Printing;
using FerrarisPOS.Data;

namespace FerrarisPOS.Services;

/// <summary>
/// Configuración centralizada de salida de impresión. No modifica la lógica de
/// los comprobantes: solamente determina impresora y si se muestra la vista previa.
/// </summary>
public static class PrintConfigurationService
{
    private const string PrinterKey = "print_default_printer";
    private const string PreviewKey = "print_preview_enabled";

    public static IReadOnlyList<string> InstalledPrinters()
    {
        var result = new List<string>();
        foreach (string printer in PrinterSettings.InstalledPrinters)
            result.Add(printer);
        return result;
    }

    public static string SavedPrinter => Database.GetSetting(PrinterKey, "");

    public static bool PreviewEnabled => Database.GetSetting(PreviewKey, "1") == "1";

    public static void Save(string printerName, bool preview)
    {
        Database.SetSetting(PrinterKey, printerName?.Trim() ?? "");
        Database.SetSetting(PreviewKey, preview ? "1" : "0");
    }

    public static bool IsInstalled(string printerName)
    {
        if (string.IsNullOrWhiteSpace(printerName)) return false;
        return InstalledPrinters().Any(p => string.Equals(p, printerName, StringComparison.OrdinalIgnoreCase));
    }

    public static void ApplyTo(PrintDocument document)
    {
        var saved = SavedPrinter;
        if (IsInstalled(saved))
            document.PrinterSettings.PrinterName = saved;
    }

    public static string? FindPdfPrinter()
    {
        return InstalledPrinters().FirstOrDefault(p =>
            p.Contains("Microsoft Print to PDF", StringComparison.OrdinalIgnoreCase));
    }
}
