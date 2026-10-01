using System.Drawing.Printing;
using FerrarisPOS.Data;

namespace FerrarisPOS.Services;

public static class PurchaseReceiptTicketService
{
    public static void Print(int orderId)
    {
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = @"
SELECT po.order_no, po.order_date, po.received_date, po.status, po.notes, s.name
FROM purchase_orders po
JOIN suppliers s ON s.id=po.supplier_id
WHERE po.id=$id";
        cmd.Parameters.AddWithValue("$id", orderId);

        using var r = cmd.ExecuteReader();
        if (!r.Read()) return;

        var orderNo = r.IsDBNull(0) ? $"OC-{orderId:000000}" : r.GetString(0);
        var orderDate = r.IsDBNull(1) ? "" : r.GetString(1);
        var receivedDate = r.IsDBNull(2) ? "" : r.GetString(2);
        var status = r.IsDBNull(3) ? "" : r.GetString(3);
        var notes = r.IsDBNull(4) ? "" : r.GetString(4);
        var supplier = r.IsDBNull(5) ? "" : r.GetString(5);
        r.Close();

        using var items = cn.CreateCommand();
        items.CommandText = @"
SELECT p.description, poi.quantity, poi.received_quantity, poi.unit_cost
FROM purchase_order_items poi
JOIN products p ON p.id=poi.product_id
WHERE poi.order_id=$id
ORDER BY p.description";
        items.Parameters.AddWithValue("$id", orderId);

        var lines = new List<string>();
        using var ir = items.ExecuteReader();
        while (ir.Read())
        {
            var description = ir.IsDBNull(0) ? "" : ir.GetString(0);
            var ordered = ir.GetDouble(1);
            var received = ir.GetDouble(2);
            var cost = ir.GetDouble(3);
            var label = ordered <= 0.000001 ? "ADICIONAL" : "";
            lines.Add($"{(string.IsNullOrWhiteSpace(label) ? description : label + " · " + description)}\n  Pedido: {ordered:0.###}  Recibido: {received:0.###}  $ {cost:N2}");
        }

        var statusText = status == "RECEIVED_WITH_DIFFERENCE"
            ? "COMPLETA CON DIFERENCIA"
            : status == "RECEIVED"
                ? "COMPLETA"
                : "PARCIAL";

        var text =
            $"{Database.GetSetting("business_name", "Ferrari's Punto de Venta")}\n" +
            "TICKET DE RECEPCIÓN DE MERCADERÍA\n" +
            $"Orden: {orderNo}\n" +
            $"Proveedor: {supplier}\n" +
            $"Pedido: {orderDate}\n" +
            $"Recepción: {receivedDate}\n" +
            $"Estado: {statusText}\n" +
            "------------------------------\n" +
            string.Join("\n", lines) +
            "\n------------------------------\n" +
            "NOTA / MOTIVO:\n" +
            (string.IsNullOrWhiteSpace(notes) ? "Sin observaciones" : notes) +
            "\n\n" +
            Database.GetSetting("ticket_footer", "Gracias por su compra");

        using var doc = new PrintDocument();
        doc.PrintPage += (_, e) =>
        {
            using var font = new Font("Consolas", 8.5f);
            e.Graphics.DrawString(
                text,
                font,
                Brushes.Black,
                new RectangleF(10, 10, e.PageBounds.Width - 20, e.PageBounds.Height - 20));
        };

        try
        {
            doc.Print();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"La recepción se registró correctamente, pero no se pudo imprimir el ticket.\n\n{ex.Message}",
                "Impresión",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
}
