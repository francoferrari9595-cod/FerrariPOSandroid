using System.Diagnostics;
using System.Drawing.Printing;
using System.Globalization;
using System.Text;
using FerrarisPOS.Data;

namespace FerrarisPOS.Services;

/// <summary>
/// Generación, impresión y envío de tickets. Los datos de la venta son de solo lectura.
/// </summary>
public static class TicketService
{
    public sealed record TicketData(
        long SaleId, long TicketNo, string Date, string BusinessName, string BusinessType,
        string BusinessAddress, string BusinessPhone, string Footer, string Channel,
        string DeliveryAddress, string Notes, string PaymentMethod, double Received, double Change,
        double Subtotal, double Discount, double Total, string CustomerName, string CustomerPhone,
        List<TicketItem> Items);

    public sealed record TicketItem(string Description, double Quantity, double UnitPrice, double Discount, double Total);

    public static TicketData? GetTicket(long saleId)
    {
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = @"SELECT s.ticket_no,s.created_at,s.total,s.payment_method,s.amount_received,s.change_amount,
                                   COALESCE(s.sale_channel,'SALÓN'),COALESCE(s.delivery_address,''),COALESCE(s.notes,''),
                                   COALESCE(s.subtotal,0),COALESCE(s.discount,0),COALESCE(c.name,'Público General'),COALESCE(c.phone,'')
                            FROM sales s LEFT JOIN customers c ON c.id=s.customer_id WHERE s.id=$id";
        cmd.Parameters.AddWithValue("$id", saleId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var ticket = r.GetInt64(0); var date = r.GetString(1); var total = r.GetDouble(2); var method = r.GetString(3);
        var received = r.GetDouble(4); var change = r.GetDouble(5); var channel = r.GetString(6); var delivery = r.GetString(7);
        var notes = r.GetString(8); var subtotal = r.GetDouble(9); var discount = r.GetDouble(10); var customer = r.GetString(11); var phone = r.GetString(12);
        r.Close();

        var items = new List<TicketItem>();
        using var iq = cn.CreateCommand();
        iq.CommandText = "SELECT description,quantity,unit_price,discount,total FROM sale_items WHERE sale_id=$id ORDER BY id";
        iq.Parameters.AddWithValue("$id", saleId);
        using var ir = iq.ExecuteReader();
        while (ir.Read()) items.Add(new(ir.GetString(0), ir.GetDouble(1), ir.GetDouble(2), ir.GetDouble(3), ir.GetDouble(4)));

        return new(saleId, ticket, date,
            Database.GetSetting("business_name", "Ferrari's Punto de Venta"),
            Database.GetSetting("business_type", "Punto de Venta"),
            Database.GetSetting("ticket_business_address", ""),
            Database.GetSetting("ticket_business_phone", ""),
            Database.GetSetting("ticket_footer", "Gracias por su compra"),
            channel, delivery, notes, method, received, change, subtotal, discount, total, customer, phone, items);
    }

    public static string BuildText(long saleId)
    {
        var d = GetTicket(saleId) ?? throw new InvalidOperationException("No se encontró la venta seleccionada.");
        var b = new StringBuilder();
        b.AppendLine($"✨ {d.BusinessName} ✨");
        if (!string.IsNullOrWhiteSpace(d.BusinessType)) b.AppendLine(d.BusinessType);
        if (!string.IsNullOrWhiteSpace(d.BusinessAddress)) b.AppendLine($"📍 {d.BusinessAddress}");
        if (!string.IsNullOrWhiteSpace(d.BusinessPhone)) b.AppendLine($"☎ {d.BusinessPhone}");
        b.AppendLine($"🎟️ TICKET #{d.TicketNo}");
        b.AppendLine($"📅 {d.Date}");
        if (!string.IsNullOrWhiteSpace(d.CustomerName) && !d.CustomerName.Equals("Público General", StringComparison.OrdinalIgnoreCase)) b.AppendLine($"👤 Cliente: {d.CustomerName}");
        b.AppendLine(new string('─', 34));
        foreach (var x in d.Items)
            b.AppendLine($"{x.Description} x{x.Quantity:0.###}  ${x.Total:N2}");
        b.AppendLine(new string('─', 34));
        if (d.Discount > 0) b.AppendLine($"Descuento: ${d.Discount:N2}");
        b.AppendLine($"TOTAL: $ {d.Total:N2}");
        b.AppendLine($"Medio: {d.PaymentMethod}");
        if (d.Received > 0) b.AppendLine($"Recibido: ${d.Received:N2} · Cambio: ${d.Change:N2}");
        if (!string.IsNullOrWhiteSpace(d.DeliveryAddress)) b.AppendLine($"Entrega: {d.DeliveryAddress}");
        if (!string.IsNullOrWhiteSpace(d.Notes)) b.AppendLine($"Nota: {d.Notes}");
        b.AppendLine();
        b.AppendLine($"💛 {d.Footer}");
        b.AppendLine("Gracias por elegirnos.");
        return b.ToString().TrimEnd();
    }

    public static void Print(long saleId)
    {
        var d = GetTicket(saleId);
        if (d is null) return;
        using var doc = CreateDocument(d, null);
        PrintConfigurationService.ApplyTo(doc);
        try { doc.Print(); }
        catch (Exception ex) { MessageBox.Show($"La venta se registró correctamente, pero no se pudo imprimir el ticket.\n\n{ex.Message}", "Impresión", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    /// <summary>Genera un PDF con Microsoft Print to PDF si está instalado. No cambia la impresora guardada.</summary>
    public static string GeneratePdf(long saleId)
    {
        var d = GetTicket(saleId) ?? throw new InvalidOperationException("No se encontró la venta seleccionada.");
        var pdfPrinter = PrintConfigurationService.FindPdfPrinter();
        if (string.IsNullOrWhiteSpace(pdfPrinter))
            throw new InvalidOperationException("Windows no tiene disponible 'Microsoft Print to PDF'. Podés instalarlo desde Características de Windows.");

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "TICKETS");
        Directory.CreateDirectory(dir);
        var safeBusiness = Sanitize(d.BusinessName);
        var path = Path.Combine(dir, $"Ticket_{d.TicketNo}_{safeBusiness}.pdf");
        if (File.Exists(path)) path = Path.Combine(dir, $"Ticket_{d.TicketNo}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");

        using var doc = CreateDocument(d, path);
        doc.PrinterSettings.PrinterName = pdfPrinter;
        doc.PrinterSettings.PrintToFile = true;
        doc.PrinterSettings.PrintFileName = path;
        try { doc.Print(); }
        catch (Exception ex) { throw new InvalidOperationException("No se pudo generar el PDF. " + ex.Message, ex); }
        if (!File.Exists(path)) throw new InvalidOperationException("Windows no confirmó la creación del PDF.");
        return path;
    }

    public static void SendWhatsApp(long saleId, string phone)
    {
        var clean = new string((phone ?? "").Where(char.IsDigit).ToArray());
        if (clean.StartsWith("00")) clean = clean[2..];
        if (clean.StartsWith("0")) clean = clean.TrimStart('0');
        if (clean.Length < 8) throw new InvalidOperationException("Ingresá un número de WhatsApp válido con código de país. Ejemplo: 5492615407856.");

        var text = BuildText(saleId);
        var pdf = GeneratePdf(saleId);
        try
        {
            var files = new System.Collections.Specialized.StringCollection { pdf };
            Clipboard.SetFileDropList(files);
        }
        catch { /* WhatsApp se abre igual aunque el portapapeles esté ocupado. */ }

        var url = "https://wa.me/" + clean + "?text=" + Uri.EscapeDataString(text);
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        MessageBox.Show(
            $"WhatsApp quedó preparado para el número {clean}.\n\nEl mensaje del ticket ya está cargado.\nEl PDF quedó en:\n{pdf}\n\nTambién quedó copiado al portapapeles para que puedas pegarlo en el chat con Ctrl+V como archivo adjunto.",
            "Ticket preparado para WhatsApp", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }


    private static PrintDocument CreateDocument(TicketData d, string? outputPath)
    {
        var doc = new PrintDocument();
        // 80 mm: mantiene formato de ticket en impresoras térmicas y en PDF.
        var height = Math.Clamp(520 + d.Items.Count * 62, 850, 2400);
        try { doc.DefaultPageSettings.PaperSize = new PaperSize("Ticket 80mm", 315, height); } catch { }
        doc.DefaultPageSettings.Margins = new Margins(18, 18, 18, 18);
        doc.PrintPage += (_, e) => DrawTicket(e.Graphics, e.MarginBounds, d);
        return doc;
    }

    private static void DrawTicket(Graphics g, Rectangle bounds, TicketData d)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var title = new Font("Segoe UI", 15, FontStyle.Bold);
        using var subtitle = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        using var bold = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        using var normal = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        using var total = new Font("Segoe UI", 14, FontStyle.Bold);
        using var brush = new SolidBrush(Color.FromArgb(28, 32, 40));
        using var accent = new SolidBrush(Color.FromArgb(235, 108, 24));
        var x = bounds.Left; var y = bounds.Top; var w = bounds.Width;
        var sfCenter = new StringFormat { Alignment = StringAlignment.Center };
        g.DrawString(d.BusinessName, title, brush, new RectangleF(x, y, w, 28), sfCenter); y += 30;
        if (!string.IsNullOrWhiteSpace(d.BusinessType)) { g.DrawString(d.BusinessType, subtitle, brush, new RectangleF(x, y, w, 20), sfCenter); y += 18; }
        if (!string.IsNullOrWhiteSpace(d.BusinessAddress)) { g.DrawString(d.BusinessAddress, subtitle, brush, new RectangleF(x, y, w, 20), sfCenter); y += 18; }
        if (!string.IsNullOrWhiteSpace(d.BusinessPhone)) { g.DrawString(d.BusinessPhone, subtitle, brush, new RectangleF(x, y, w, 20), sfCenter); y += 18; }
        using var pen = new Pen(accent, 1.5f); g.DrawLine(pen, x, y + 2, x + w, y + 2); y += 10;
        g.DrawString($"TICKET #{d.TicketNo}", bold, brush, x, y); y += 18;
        g.DrawString(d.Date, subtitle, brush, x, y); y += 22;
        if (!string.IsNullOrWhiteSpace(d.CustomerName) && !d.CustomerName.Equals("Público General", StringComparison.OrdinalIgnoreCase)) { g.DrawString($"Cliente: {d.CustomerName}", subtitle, brush, x, y); y += 20; }
        foreach (var item in d.Items)
        {
            var itemText = $"{item.Description} x{item.Quantity:0.###}";
            g.DrawString(itemText, normal, brush, new RectangleF(x, y, w * .68f, 30));
            g.DrawString($"${item.Total:N2}", normal, brush, new RectangleF(x + w * .68f, y, w * .32f, 30), new StringFormat { Alignment = StringAlignment.Far });
            y += 30;
        }
        g.DrawLine(Pens.Gray, x, y, x + w, y); y += 8;
        if (d.Discount > 0) { g.DrawString("Descuento", normal, brush, x, y); g.DrawString($"-${d.Discount:N2}", normal, brush, new RectangleF(x, y, w, 20), new StringFormat { Alignment = StringAlignment.Far }); y += 22; }
        g.DrawString("TOTAL", total, accent, x, y); g.DrawString($"${d.Total:N2}", total, accent, new RectangleF(x, y, w, 28), new StringFormat { Alignment = StringAlignment.Far }); y += 34;
        g.DrawString($"Medio de pago: {d.PaymentMethod}", subtitle, brush, x, y); y += 18;
        if (d.Received > 0) { g.DrawString($"Recibido ${d.Received:N2} · Cambio ${d.Change:N2}", subtitle, brush, x, y); y += 18; }
        if (!string.IsNullOrWhiteSpace(d.DeliveryAddress)) { g.DrawString($"Entrega: {d.DeliveryAddress}", subtitle, brush, x, y); y += 20; }
        if (!string.IsNullOrWhiteSpace(d.Notes)) { g.DrawString($"Nota: {d.Notes}", subtitle, brush, x, y); y += 20; }
        y += 8; g.DrawLine(pen, x, y, x + w, y); y += 12;
        g.DrawString(d.Footer, bold, accent, new RectangleF(x, y, w, 25), sfCenter); y += 24;
        g.DrawString("Gracias por su compra · ¡Volvé pronto!", subtitle, brush, new RectangleF(x, y, w, 22), sfCenter);
    }

    private static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return string.IsNullOrWhiteSpace(s) ? "FerrarisPOS" : s.Replace(' ', '_');
    }
}
