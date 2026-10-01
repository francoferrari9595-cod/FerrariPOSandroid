using FerrarisPOS.Data;
using FerrarisPOS.Services;
using System.Globalization;

namespace FerrarisPOS.Forms;

public class DailyTicketsForm : Form
{
    private readonly DataGridView tickets = new();
    private readonly DataGridView items = new();
    private readonly Label detailTitle = new();
    private readonly Label detailTotal = new();
    private long selectedSaleId;
    private readonly TextBox ticketSearch = new();

    public DailyTicketsForm()
    {
        Text = "FerrarisPOS · Tickets y devoluciones";
        Width = 1280;
        Height = 820;
        MinimumSize = new Size(1120, 700);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.Gainsboro;
        Build();
        ThemeService.Apply(this);
        LoadTickets();
    }

    private void Build()
    {
        var title = new Label
        {
            Text = "TICKETS DE HOY · CONSULTA Y DEVOLUCIONES",
            Font = new Font("Segoe UI", 20, FontStyle.Bold),
            Location = new Point(20, 15),
            AutoSize = true
        };
        Controls.Add(title);

        ticketSearch.Location = new Point(610, 14);
        ticketSearch.Size = new Size(270, 38);
        ticketSearch.Font = new Font("Segoe UI", 11);
        ticketSearch.PlaceholderText = "Buscar ticket por número";
        ticketSearch.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                LoadTickets();
            }
        };
        Controls.Add(ticketSearch);

        var searchButton = new Button
        {
            Text = "BUSCAR TICKET",
            Location = new Point(890, 14),
            Width = 125,
            Height = 38
        };
        searchButton.Click += (_, _) => LoadTickets();
        Controls.Add(searchButton);

        var refresh = new Button
        {
            Text = "ACTUALIZAR",
            Location = new Point(1025, 14),
            Width = 120,
            Height = 38
        };
        refresh.Click += (_, _) => { ticketSearch.Clear(); LoadTickets(); };
        Controls.Add(refresh);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 285,
            Location = new Point(0, 65)
        };
        Controls.Add(split);

        tickets.Dock = DockStyle.Fill;
        tickets.ReadOnly = true;
        tickets.AllowUserToAddRows = false;
        tickets.AllowUserToDeleteRows = false;
        tickets.RowHeadersVisible = false;
        tickets.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        tickets.MultiSelect = false;
        tickets.AutoGenerateColumns = false;
        tickets.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        tickets.CellClick += (_, _) => LoadSelectedTicket();
        tickets.CellDoubleClick += (_, _) => LoadSelectedTicket();

        tickets.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ticket", HeaderText = "TICKET", FillWeight = 60 });
        tickets.Columns.Add(new DataGridViewTextBoxColumn { Name = "Hora", HeaderText = "HORA", FillWeight = 75 });
        tickets.Columns.Add(new DataGridViewTextBoxColumn { Name = "Cliente", HeaderText = "CLIENTE", FillWeight = 150 });
        tickets.Columns.Add(new DataGridViewTextBoxColumn { Name = "Usuario", HeaderText = "CAJERO", FillWeight = 120 });
        tickets.Columns.Add(new DataGridViewTextBoxColumn { Name = "Estado", HeaderText = "ESTADO", FillWeight = 90 });
        tickets.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Total", HeaderText = "TOTAL", FillWeight = 100,
            DefaultCellStyle = new DataGridViewCellStyle { Format = "N2", Alignment = DataGridViewContentAlignment.MiddleRight }
        });
        split.Panel1.Controls.Add(tickets);

        var lower = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        split.Panel2.Controls.Add(lower);

        detailTitle.Location = new Point(10, 5);
        detailTitle.AutoSize = true;
        detailTitle.Font = new Font("Segoe UI", 12, FontStyle.Bold);
        lower.Controls.Add(detailTitle);

        detailTotal.Location = new Point(10, 32);
        detailTotal.AutoSize = true;
        detailTotal.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        lower.Controls.Add(detailTotal);

        items.Location = new Point(10, 62);
        items.Size = new Size(930, 290);
        items.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        items.ReadOnly = true;
        items.AllowUserToAddRows = false;
        items.RowHeadersVisible = false;
        items.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        items.MultiSelect = false;
        items.AutoGenerateColumns = false;
        items.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        items.Columns.Add(new DataGridViewTextBoxColumn { Name = "ItemId", HeaderText = "ID", FillWeight = 40 });
        items.Columns.Add(new DataGridViewTextBoxColumn { Name = "Descripcion", HeaderText = "PRODUCTO", FillWeight = 180 });
        items.Columns.Add(new DataGridViewTextBoxColumn { Name = "Cantidad", HeaderText = "ORIGINAL", FillWeight = 95 });
        items.Columns.Add(new DataGridViewTextBoxColumn { Name = "Devuelto", HeaderText = "DEVUELTO", FillWeight = 90 });
        items.Columns.Add(new DataGridViewTextBoxColumn { Name = "Disponible", HeaderText = "RESTANTE EN VENTA", FillWeight = 115 });
        items.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Precio", HeaderText = "PRECIO", FillWeight = 85,
            DefaultCellStyle = new DataGridViewCellStyle { Format = "N2", Alignment = DataGridViewContentAlignment.MiddleRight }
        });
        items.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Importe", HeaderText = "IMPORTE", FillWeight = 90,
            DefaultCellStyle = new DataGridViewCellStyle { Format = "N2", Alignment = DataGridViewContentAlignment.MiddleRight }
        });
        lower.Controls.Add(items);

        var returnButton = new Button
        {
            Text = "DEVOLVER PRODUCTO · SOLO UNIDADES",
            Location = new Point(10, 365),
            Width = 300,
            Height = 48,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        returnButton.Click += (_, _) => ReturnSelected();
        lower.Controls.Add(returnButton);

        var close = new Button
        {
            Text = "CERRAR",
            Location = new Point(820, 365),
            Width = 120,
            Height = 48,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
            DialogResult = DialogResult.OK
        };
        lower.Controls.Add(close);

        items.CellDoubleClick += (_, _) => ReturnSelected();
    }

    private void LoadTickets()
    {
        selectedSaleId = 0;
        tickets.Rows.Clear();
        items.Rows.Clear();
        detailTitle.Text = "SELECCIONÁ UN TICKET PARA VER EL DETALLE";
        detailTotal.Text = "";

        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        var search = ticketSearch.Text.Trim();

        cmd.CommandText = """
            SELECT s.id, s.ticket_no, s.created_at,
                   COALESCE(c.name,'Público General'),
                   COALESCE(u.full_name,u.username,''),
                   s.status,
                   MAX(0, s.total - COALESCE((SELECT SUM(sr.amount) FROM sale_returns sr WHERE sr.sale_id=s.id),0)),
                   EXISTS(SELECT 1 FROM sale_returns sr2 WHERE sr2.sale_id=s.id)
            FROM sales s
            LEFT JOIN customers c ON c.id=s.customer_id
            LEFT JOIN users u ON u.id=s.user_id
            WHERE s.status IN ('COMPLETED','CANCELLED')
              AND date(s.created_at)=date('now','localtime')
              AND ($search='' OR CAST(s.ticket_no AS TEXT) LIKE '%' || $search || '%')
            ORDER BY s.id DESC
            """;
        cmd.Parameters.AddWithValue("$search", search);

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var row = tickets.Rows.Add(
                r.GetInt64(1),
                FormatTime(r.GetString(2)),
                r.GetString(3),
                r.GetString(4),
                r.GetString(5)=="CANCELLED"?"CANCELADO":"ACTIVO",
                r.GetDouble(6));
            tickets.Rows[row].Tag = r.GetInt64(0);
            var returned = Convert.ToInt32(r.GetValue(7)) == 1;
            if (r.GetString(5)=="CANCELLED") { tickets.Rows[row].DefaultCellStyle.Font = new Font(tickets.Font, FontStyle.Strikeout); tickets.Rows[row].DefaultCellStyle.ForeColor = Color.DarkRed; }
            else if (returned) { tickets.Rows[row].DefaultCellStyle.BackColor = Color.Khaki; tickets.Rows[row].DefaultCellStyle.ForeColor = Color.DarkGoldenrod; }
        }

        if (tickets.Rows.Count > 0)
        {
            tickets.Rows[0].Selected = true;
            LoadSelectedTicket();
        }
    }

    private void LoadSelectedTicket()
    {
        if (tickets.CurrentRow?.Tag is not long saleId)
            return;

        selectedSaleId = saleId;
        items.Rows.Clear();

        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            SELECT si.id, si.description, si.quantity,
                   COALESCE(si.returned_quantity,0),
                   si.unit_price, si.total,
                   COALESCE(p.is_bulk,0), COALESCE(p.uses_inventory,1)
            FROM sale_items si
            LEFT JOIN products p ON p.id=si.product_id
            WHERE si.sale_id=$sid
            ORDER BY si.id
            """;
        cmd.Parameters.AddWithValue("$sid", saleId);

        using var r = cmd.ExecuteReader();
        double totalOriginal = 0;
        double totalReturned = 0;

        while (r.Read())
        {
            var sold = r.GetDouble(2);
            var returned = r.GetDouble(3);
            var available = Math.Max(0, sold - returned);
            var bulk = r.GetInt32(6) == 1;
            var usesInventory = InventoryControlService.IsGlobalEnabled && r.GetInt32(7) == 1;

            var row = items.Rows.Add(
                r.GetInt64(0),
                r.GetString(1),
                QuantityText(sold, bulk),
                QuantityText(returned, bulk),
                QuantityText(available, bulk),
                r.GetDouble(4),
                r.GetDouble(5));

            items.Rows[row].Tag = new TicketItemInfo(
                r.GetInt64(0), r.GetString(1), available, bulk, r.GetDouble(4), usesInventory);

            totalOriginal += r.GetDouble(5);
            totalReturned += Math.Round(returned * r.GetDouble(4), 2);
        }

        using var ticketCmd = cn.CreateCommand();
        ticketCmd.CommandText = "SELECT ticket_no FROM sales WHERE id=$id";
        ticketCmd.Parameters.AddWithValue("$id", saleId);
        var ticketNo = Convert.ToInt64(ticketCmd.ExecuteScalar() ?? saleId);

        var totalActual = Math.Max(0, totalOriginal - totalReturned);
        detailTitle.Text = $"DETALLE DEL TICKET #{ticketNo}";
        detailTotal.Text = $"TOTAL ORIGINAL: ${totalOriginal:N2}    ·    DEVUELTO: ${totalReturned:N2}    ·    TOTAL ACTUAL: ${totalActual:N2}";

        if (items.Rows.Count > 0)
            items.Rows[0].Selected = true;
    }

    private void ReturnSelected()
    {
        if (selectedSaleId <= 0 || items.CurrentRow?.Tag is not TicketItemInfo info)
        {
            MessageBox.Show("Seleccioná un producto del ticket.", "Devolución",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (info.Available <= 0.000001)
        {
            MessageBox.Show("Este producto ya fue devuelto en su totalidad.", "Devolución",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var quantity = new ReturnQuantityForm(info.Description, info.Available, info.IsBulk);
        if (quantity.ShowDialog(this) != DialogResult.OK)
            return;

        using var reasonForm = new ReturnReasonForm(info.Description);
        if (reasonForm.ShowDialog(this) != DialogResult.OK)
            return;

        var reason = reasonForm.Reason;
        var returnText = info.UsesInventory ? "La cantidad volverá al stock del producto." : "Este producto está configurado como SIN INVENTARIO; la devolución se registrará pero no modificará el stock.";
        var answer = MessageBox.Show(
            $"¿Confirmás devolver {QuantityText(quantity.Quantity, info.IsBulk)} de '{info.Description}'?\n\nMOTIVO: {reason}\n\n{returnText}",
            "Confirmar devolución",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (answer != DialogResult.Yes)
            return;

        try
        {
            SaleReturnService.ReturnItem(info.ItemId, quantity.Quantity, Session.UserId, reason);
            // Recargar el mismo ticket: se conserva la selección para que
            // ORIGINAL / DEVUELTO / RESTANTE se actualicen sin saltar a otro ticket.
            LoadSelectedTicket();
            MessageBox.Show(info.UsesInventory
                ? "Devolución registrada correctamente.\nLa cantidad volvió al inventario y quedó registrada en los movimientos de stock."
                : "Devolución registrada correctamente.\nEl producto está configurado como SIN INVENTARIO y no se modificó el stock.",
                "Devolución", MessageBoxButtons.OK, MessageBoxIcon.Information);
            LoadTicketsPreserveSelection();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "No se pudo realizar la devolución",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadTicketsPreserveSelection()
    {
        var keepSaleId = selectedSaleId;
        LoadTickets();
        if (keepSaleId <= 0) return;
        foreach (DataGridViewRow row in tickets.Rows)
        {
            if (row.Tag is long id && id == keepSaleId)
            {
                row.Selected = true;
                tickets.CurrentCell = row.Cells[0];
                LoadSelectedTicket();
                break;
            }
        }
    }

    private static string FormatTime(string value)
    {
        return DateTime.TryParse(value, out var dt) ? dt.ToString("HH:mm") : value;
    }

    private static string QuantityText(double value, bool bulk)
    {
        if (!bulk)
            return value.ToString("N0", CultureInfo.CurrentCulture);

        return value < 1
            ? $"{value * 1000:N0} g"
            : $"{value:N3} kg";
    }

    private sealed record TicketItemInfo(long ItemId, string Description, double Available, bool IsBulk, double UnitPrice, bool UsesInventory);
}

internal sealed class ReturnReasonForm : Form
{
    private readonly TextBox reason = new();
    public string Reason => reason.Text.Trim();

    public ReturnReasonForm(string product)
    {
        Text = "Motivo de la devolución";
        Width = 620;
        Height = 330;
        MinimumSize = new Size(620, 330);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        Controls.Add(new Label
        {
            Text = $"INDICÁ OBLIGATORIAMENTE POR QUÉ SE DEVUELVE:\n{product}",
            Location = new Point(24, 20),
            Size = new Size(555, 55),
            Font = new Font("Segoe UI", 11, FontStyle.Bold)
        });

        reason.Location = new Point(24, 85);
        reason.Size = new Size(555, 120);
        reason.Multiline = true;
        reason.ScrollBars = ScrollBars.Vertical;
        reason.MaxLength = 500;
        reason.Font = new Font("Segoe UI", 11);
        reason.PlaceholderText = "Ej.: producto defectuoso, cliente cambió de opinión, talle incorrecto...";
        Controls.Add(reason);

        var cancel = new Button
        {
            Text = "CANCELAR",
            Location = new Point(330, 225),
            Size = new Size(115, 42),
            DialogResult = DialogResult.Cancel
        };
        Controls.Add(cancel);

        var accept = new Button
        {
            Text = "CONTINUAR",
            Location = new Point(464, 225),
            Size = new Size(115, 42)
        };
        accept.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(Reason))
            {
                MessageBox.Show(this, "El motivo de la devolución es obligatorio.", "Devolución", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                reason.Focus();
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        Controls.Add(accept);
        AcceptButton = accept;
        CancelButton = cancel;
        Shown += (_, _) => reason.Focus();
    }
}

internal sealed class ReturnQuantityForm : Form
{
    private readonly NumericUpDown quantity = new();
    private readonly bool bulk;
    private readonly double availableKg;
    public double Quantity => bulk ? (double)quantity.Value / 1000.0 : (double)quantity.Value;

    public ReturnQuantityForm(string description, double available, bool isBulk)
    {
        bulk = isBulk;
        availableKg = available;
        Text = "Cantidad a devolver";
        Width = 500;
        Height = 250;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        Controls.Add(new Label
        {
            Text = $"DEVOLUCIÓN · {description}",
            Location = new Point(20, 20),
            AutoSize = true,
            Font = new Font("Segoe UI", 11, FontStyle.Bold)
        });

        Controls.Add(new Label
        {
            Text = isBulk
                ? $"Disponible para devolver: {QuantityText(available, true)} · ingresá GRAMOS"
                : $"Disponible para devolver: {QuantityText(available, false)}",
            Location = new Point(20, 58),
            AutoSize = true
        });

        quantity.Location = new Point(20, 92);
        quantity.Width = 260;
        if (isBulk)
        {
            // Para granel el operador ingresa gramos, no kilos.
            // Ej.: una devolución de 100 g se guarda como 0,100 kg.
            quantity.Minimum = 1M;
            quantity.Maximum = Math.Max(1M, (decimal)Math.Round(available * 1000.0, 3));
            quantity.DecimalPlaces = 0;
            quantity.Increment = 10M;
            quantity.Value = Math.Min(quantity.Maximum, 100M);
        }
        else
        {
            quantity.Minimum = 1M;
            quantity.Maximum = Math.Max(1M, (decimal)Math.Floor(available));
            quantity.DecimalPlaces = 0;
            quantity.Increment = 1M;
            quantity.Value = Math.Min(quantity.Maximum, 1M);
        }
        Controls.Add(quantity);

        var ok = new Button
        {
            Text = "DEVOLVER",
            Location = new Point(20, 145),
            Width = 200,
            Height = 42,
            DialogResult = DialogResult.OK
        };
        var cancel = new Button
        {
            Text = "CANCELAR",
            Location = new Point(240, 145),
            Width = 200,
            Height = 42,
            DialogResult = DialogResult.Cancel
        };
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private static string QuantityText(double value, bool bulk) =>
        bulk
            ? (value < 1 ? $"{value * 1000:N0} g" : $"{value:N3} kg")
            : value.ToString("N0", CultureInfo.CurrentCulture);
}
