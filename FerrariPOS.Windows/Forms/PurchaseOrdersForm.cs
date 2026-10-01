using FerrarisPOS.Data;
using FerrarisPOS.Services;

namespace FerrarisPOS.Forms;

public sealed class PurchaseOrdersForm : Form
{
    private readonly DataGridView grid = new();

    public PurchaseOrdersForm()
    {
        Text = "FerrarisPOS - Compras";
        Width = 1200;
        Height = 680;
        StartPosition = FormStartPosition.CenterParent;
        Build();
        LoadGrid();
        ThemeService.Apply(this);
    }

    private void Build()
    {
        Controls.Add(new Label
        {
            Text = "ÓRDENES DE COMPRA",
            Location = new Point(20, 18),
            AutoSize = true,
            Font = new Font("Segoe UI", 16, FontStyle.Bold)
        });

        var bNew = Btn("ARMAR PEDIDO", 20, 55, 145);
        bNew.Click += (_, _) => NewOrder();
        Controls.Add(bNew);

        var bReceive = Btn("RECIBIR MERCADERÍA", 175, 55, 170);
        bReceive.Click += (_, _) => ReceiveSelected(false);
        Controls.Add(bReceive);

        var bDifference = Btn("RECIBIDO CON DIFERENCIA", 355, 55, 190);
        bDifference.Click += (_, _) => ReceiveSelected(true);
        Controls.Add(bDifference);

        var bCancel = Btn("CANCELAR", 555, 55, 120);
        bCancel.Click += (_, _) => CancelSelected();
        Controls.Add(bCancel);

        var bRefresh = Btn("ACTUALIZAR", 685, 55, 120);
        bRefresh.Click += (_, _) => LoadGrid();
        Controls.Add(bRefresh);

        var bAccounts = Btn("CUENTAS", 815, 55, 110);
        bAccounts.Click += (_, _) =>
        {
            using var f = new SupplierAccountsForm();
            f.ShowDialog(this);
        };
        Controls.Add(bAccounts);

        var bInsights = Btn("SUGERIR COMPRAS", 935, 55, 145);
        bInsights.Click += (_, _) =>
        {
            using var f = new PurchaseInsightsForm();
            f.ShowDialog(this);
        };
        Controls.Add(bInsights);

        grid.Location = new Point(20, 105);
        grid.Size = new Size(1060, 500);
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.AutoGenerateColumns = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.DataError += (_, e) =>
        {
            e.ThrowException = false;
            e.Cancel = true;
        };

        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ID", HeaderText = "ID", Visible = false });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Orden", HeaderText = "ORDEN", FillWeight = 13 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Proveedor", HeaderText = "PROVEEDOR", FillWeight = 24 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Estado", HeaderText = "ESTADO", FillWeight = 13 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Fecha", HeaderText = "FECHA", FillWeight = 13 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Entrega", HeaderText = "ENTREGA", FillWeight = 13 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Total", HeaderText = "TOTAL", FillWeight = 12 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Recibido", HeaderText = "RECIBIDO", FillWeight = 12 });

        grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0)
                ShowDetails(SelectedId());
        };

        Controls.Add(grid);
    }

    private Button Btn(string text, int x, int y, int width) =>
        new()
        {
            Text = text,
            Location = new Point(x, y),
            Width = width,
            Height = 34
        };

    private void LoadGrid()
    {
        grid.Rows.Clear();

        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();

        cmd.CommandText = @"
SELECT
    po.id,
    po.order_no,
    s.name,
    po.status,
    po.order_date,
    po.expected_date,
    ROUND(po.total,2),
    ROUND(COALESCE(po.received_total,0),2)
FROM purchase_orders po
JOIN suppliers s ON s.id=po.supplier_id
ORDER BY po.id DESC";

        using var r = cmd.ExecuteReader();

        while (r.Read())
        {
            var row = grid.Rows.Add(
                r.GetInt32(0),
                r.IsDBNull(1) ? "" : r.GetString(1),
                r.IsDBNull(2) ? "" : r.GetString(2),
                FormatStatus(r.IsDBNull(3) ? "" : r.GetString(3)),
                r.IsDBNull(4) ? "" : r.GetString(4),
                r.IsDBNull(5) ? "" : r.GetString(5),
                FormatMoney(r.GetDouble(6)),
                FormatMoney(r.GetDouble(7))
            );

            grid.Rows[row].Tag = r.GetInt32(0);
        }
    }

    private static string FormatStatus(string status) => status switch
    {
        "DRAFT" => "BORRADOR",
        "PARTIAL" => "PARCIAL",
        "RECEIVED" => "COMPLETA",
        "RECEIVED_WITH_DIFFERENCE" => "COMPLETA CON DIFERENCIA",
        "CANCELLED" => "CANCELADA",
        _ => status
    };

    private static string FormatMoney(double value) =>
        value.ToString("N2", System.Globalization.CultureInfo.CurrentCulture);

    private int SelectedId()
    {
        if (grid.CurrentRow?.Cells["ID"].Value is null)
            return 0;

        return int.TryParse(Convert.ToString(grid.CurrentRow.Cells["ID"].Value), out var id)
            ? id
            : 0;
    }


    private void ShowDetails(int id)
    {
        if (id == 0) return;

        using var cn = Database.Open();
        using var head = cn.CreateCommand();
        head.CommandText = @"
SELECT po.order_no, s.name, po.status, po.order_date, po.expected_date,
       COALESCE(po.notes,''), ROUND(po.total,2), ROUND(COALESCE(po.received_total,0),2)
FROM purchase_orders po
JOIN suppliers s ON s.id=po.supplier_id
WHERE po.id=$id";
        head.Parameters.AddWithValue("$id", id);

        using var r = head.ExecuteReader();
        if (!r.Read()) return;

        var orderNo = Convert.ToString(r.GetValue(0)) ?? "";
        var supplier = Convert.ToString(r.GetValue(1)) ?? "";
        var rawStatus = Convert.ToString(r.GetValue(2)) ?? "";
        var orderDate = Convert.ToString(r.GetValue(3)) ?? "";
        var expectedDate = Convert.ToString(r.GetValue(4)) ?? "";
        var notes = Convert.ToString(r.GetValue(5)) ?? "";
        var total = r.IsDBNull(6) ? 0 : r.GetDouble(6);
        var receivedTotal = r.IsDBNull(7) ? 0 : r.GetDouble(7);
        r.Close();

        var status = FormatStatus(rawStatus);
        var completed = rawStatus is "RECEIVED" or "RECEIVED_WITH_DIFFERENCE";

        using var itemsCmd = cn.CreateCommand();
        itemsCmd.CommandText = @"
SELECT p.description, poi.quantity, poi.received_quantity,
       MAX(COALESCE(poi.notes,''))
FROM purchase_order_items poi
JOIN products p ON p.id=poi.product_id
WHERE poi.order_id=$id
GROUP BY poi.id, p.description, poi.quantity, poi.received_quantity
ORDER BY poi.id";
        itemsCmd.Parameters.AddWithValue("$id", id);

        var lines = new List<string>();
        var shareLines = new List<string>();
        using var ir = itemsCmd.ExecuteReader();
        while (ir.Read())
        {
            var description = ir.IsDBNull(0) ? "" : ir.GetString(0);
            var ordered = ir.IsDBNull(1) ? 0 : ir.GetDouble(1);
            var received = ir.IsDBNull(2) ? 0 : ir.GetDouble(2);
            var lineNote = ir.IsDBNull(3) ? "" : ir.GetString(3);
            var pending = Math.Max(0, ordered - received);

            if (completed)
                lines.Add($"{description}\r\n   Pedido: {ordered:N2}   Recibido: {received:N2}   Pendiente: {pending:N2}" +
                          (ordered <= 0 && received > 0 ? "\r\n   PRODUCTO ADICIONAL RECIBIDO" : "") +
                          (string.IsNullOrWhiteSpace(lineNote) ? "" : $"\r\n   Nota: {lineNote}"));
            else
                lines.Add($"{description}\r\n   Pedido: {ordered:N2}   Pendiente: {pending:N2}" +
                          (string.IsNullOrWhiteSpace(lineNote) ? "" : $"\r\n   Nota: {lineNote}"));

            shareLines.Add($"{description} | Pedido: {ordered:N2} | Recibido: {received:N2} | Pendiente: {pending:N2}" +
                           (ordered <= 0 && received > 0 ? " | PRODUCTO ADICIONAL RECIBIDO" : "") +
                           (string.IsNullOrWhiteSpace(lineNote) ? "" : $" | Nota: {lineNote}"));
        }

        var body =
            "FERRARIPOS · COMPROBANTE DE ORDEN DE COMPRA\r\n" +
            "========================================\r\n" +
            $"Orden: {orderNo}\r\n" +
            $"Proveedor: {supplier}\r\n" +
            $"Estado: {status}\r\n" +
            $"Fecha: {orderDate}\r\n" +
            $"Entrega: {expectedDate}\r\n" +
            $"Total pedido: ${total:N2}\r\n" +
            $"Total recibido: ${receivedTotal:N2}\r\n\r\n" +
            string.Join("\r\n\r\n", shareLines) +
            (string.IsNullOrWhiteSpace(notes) ? "" : $"\r\n\r\nNOTAS / MOTIVO:\r\n{notes}") +
            "\r\n";

        using var f = new Form
        {
            Text = $"FerrarisPOS - Detalle {orderNo}",
            Width = 820,
            Height = 680,
            StartPosition = FormStartPosition.CenterParent
        };

        var title = new Label
        {
            Text = $"ORDEN {orderNo} · {status}",
            Location = new Point(20, 18),
            AutoSize = true,
            Font = new Font("Segoe UI", 16, FontStyle.Bold)
        };
        f.Controls.Add(title);

        var info = new Label
        {
            Text = $"Proveedor: {supplier}\r\nPedido: ${total:N2}   Recibido: ${receivedTotal:N2}",
            Location = new Point(20, 52),
            AutoSize = true
        };
        f.Controls.Add(info);

        var text = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Location = new Point(20, 105),
            Size = new Size(760, 450),
            Font = new Font("Consolas", 10),
            Text = string.Join("\r\n\r\n", lines) +
                   (string.IsNullOrWhiteSpace(notes) ? "" : $"\r\n\r\nNOTAS / MOTIVO:\r\n{notes}")
        };
        f.Controls.Add(text);

        var send = Btn("ENVIAR COMPROBANTE", 20, 575, 190);
        send.Click += (_, _) =>
        {
            try
            {
                EmailReportService.Send($"FerrarisPOS · Comprobante {orderNo}", body);
                MessageBox.Show("El comprobante se envió correctamente a los correos configurados.", "Compras", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo enviar el comprobante.\r\n\r\n" + ex.Message, "Compras", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        f.Controls.Add(send);

        var close = Btn("CERRAR", 225, 575, 120);
        close.Click += (_, _) => f.Close();
        f.Controls.Add(close);

        ThemeService.Apply(f);
        f.ShowDialog(this);
    }

    private void NewOrder()
    {
        using var f = new PurchaseOrderForm();
        if (f.ShowDialog(this) == DialogResult.OK)
            LoadGrid();
    }

    private void ReceiveSelected(bool differenceMode)
    {
        var id = SelectedId();

        if (id == 0)
        {
            MessageBox.Show("Seleccioná una orden.", "Compras",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var cn = Database.Open();
        using var q = cn.CreateCommand();
        q.CommandText = "SELECT status FROM purchase_orders WHERE id=$id";
        q.Parameters.AddWithValue("$id", id);
        var status = Convert.ToString(q.ExecuteScalar()) ?? "";

        if (status == "CANCELLED")
        {
            MessageBox.Show("Esta orden está cancelada.", "Compras",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (status is "RECEIVED" or "RECEIVED_WITH_DIFFERENCE")
        {
            MessageBox.Show("Esta orden ya fue recibida completamente.", "Compras",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var f = new PurchaseOrderForm(id, true, differenceMode);
        if (f.ShowDialog(this) == DialogResult.OK)
            LoadGrid();
    }

    private void CancelSelected()
    {
        var id = SelectedId();
        if (id == 0)
            return;

        using var cn = Database.Open();

        using var q = cn.CreateCommand();
        q.CommandText = "SELECT status FROM purchase_orders WHERE id=$id";
        q.Parameters.AddWithValue("$id", id);
        var status = Convert.ToString(q.ExecuteScalar()) ?? "";

        if (status is "RECEIVED" or "CANCELLED")
        {
            MessageBox.Show("Esta orden no se puede cancelar.", "Compras",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (MessageBox.Show(
            "¿Cancelar la orden seleccionada?",
            "Compras",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        using var c = cn.CreateCommand();
        c.CommandText = @"
UPDATE purchase_orders
SET status='CANCELLED',
    updated_at=CURRENT_TIMESTAMP
WHERE id=$id";
        c.Parameters.AddWithValue("$id", id);
        c.ExecuteNonQuery();

        LoadGrid();
    }
}
