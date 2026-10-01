using FerrarisPOS.Data;
using FerrarisPOS.Services;

namespace FerrarisPOS.Forms;

/// <summary>
/// Selector de impresión: permite imprimir la venta actual o buscar cualquier venta anterior.
/// No modifica ni reabre ventas; solo consulta y envía el ticket seleccionado a la impresora.
/// </summary>
public sealed class TicketPrintForm : Form
{
    private readonly long currentSaleId;
    private readonly DataGridView sales = new();
    private readonly TextBox search = new();
    private long selectedSaleId;

    public TicketPrintForm(long currentSaleId = 0)
    {
        this.currentSaleId = currentSaleId;
        Text = "FerrarisPOS · Imprimir ticket";
        Width = 1100; Height = 700; MinimumSize = new Size(950, 600);
        StartPosition = FormStartPosition.CenterParent;
        Build(); LoadSales();
        ThemeService.Apply(this);
    }

    private void Build()
    {
        Controls.Add(new Label { Text = "IMPRIMIR TICKET", Location = new Point(24, 20), AutoSize = true, Font = new Font("Segoe UI", 22, FontStyle.Bold) });
        Controls.Add(new Label { Text = "Elegí la venta actual o buscá cualquier venta anterior por número, cliente o fecha.", Location = new Point(26, 62), AutoSize = true });

        search.Location = new Point(25, 95); search.Width = 540; search.Height = 38; search.Font = new Font("Segoe UI", 11); search.PlaceholderText = "Buscar ticket, cliente o fecha";
        search.TextChanged += (_, _) => LoadSales(); Controls.Add(search);
        var refresh = new Button { Text = "ACTUALIZAR", Location = new Point(580, 95), Width = 120, Height = 38 };
        refresh.Click += (_, _) => LoadSales(); Controls.Add(refresh);

        sales.Location = new Point(25, 150); sales.Size = new Size(1030, 400); sales.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        sales.ReadOnly = true; sales.AllowUserToAddRows = false; sales.RowHeadersVisible = false; sales.SelectionMode = DataGridViewSelectionMode.FullRowSelect; sales.MultiSelect = false; sales.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        sales.Columns.Add(new DataGridViewTextBoxColumn { Name="Ticket", HeaderText="TICKET", FillWeight=70 });
        sales.Columns.Add(new DataGridViewTextBoxColumn { Name="Fecha", HeaderText="FECHA", FillWeight=130 });
        sales.Columns.Add(new DataGridViewTextBoxColumn { Name="Cliente", HeaderText="CLIENTE", FillWeight=190 });
        sales.Columns.Add(new DataGridViewTextBoxColumn { Name="Medio", HeaderText="MEDIO", FillWeight=110 });
        sales.Columns.Add(new DataGridViewTextBoxColumn { Name="Estado", HeaderText="ESTADO", FillWeight=85 });
        sales.Columns.Add(new DataGridViewTextBoxColumn { Name="Total", HeaderText="TOTAL", FillWeight=100, DefaultCellStyle = new DataGridViewCellStyle { Format="N2", Alignment=DataGridViewContentAlignment.MiddleRight } });
        sales.CellClick += (_, _) => CaptureSelection(); sales.CellDoubleClick += (_, _) => ShowTicketActions();
        Controls.Add(sales);

        var printCurrent = new Button { Text = currentSaleId > 0 ? "🖨 IMP-TICKET ESTA VENTA" : "🖨 IMP-TICKET SELECCIONADO", Location = new Point(25, 575), Width = 285, Height = 52, Anchor = AnchorStyles.Bottom | AnchorStyles.Left, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
        printCurrent.Click += (_, _) => ShowTicketActions(); Controls.Add(printCurrent);
        var close = new Button { Text = "CERRAR", Location = new Point(935, 575), Width = 120, Height = 52, Anchor = AnchorStyles.Bottom | AnchorStyles.Right, DialogResult = DialogResult.Cancel }; Controls.Add(close);

        if (currentSaleId > 0)
        {
            var current = new Button { Text = "USAR VENTA ACTUAL", Location = new Point(330, 575), Width = 190, Height = 52, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
            current.Click += (_, _) => { selectedSaleId = currentSaleId; ShowTicketActions(); };
            Controls.Add(current);
        }
    }

    private void LoadSales()
    {
        selectedSaleId = 0; sales.Rows.Clear();
        using var cn = Database.Open(); using var cmd = cn.CreateCommand();
        cmd.CommandText = @"SELECT s.id,s.ticket_no,s.created_at,CASE WHEN EXISTS(SELECT 1 FROM payments pc WHERE pc.sale_id=s.id AND pc.status='APPROVED' AND (pc.method LIKE 'CRÉDITO%' OR pc.method LIKE 'CREDITO%')) THEN COALESCE(c.name,'Público General') ELSE 'Público General' END,s.payment_method,s.status,s.total,EXISTS(SELECT 1 FROM sale_returns sr WHERE sr.sale_id=s.id)
                            FROM sales s LEFT JOIN customers c ON c.id=s.customer_id
                            WHERE s.status IN ('COMPLETED','CANCELLED')
                              AND ($q='' OR CAST(s.ticket_no AS TEXT) LIKE '%'||$q||'%' OR COALESCE(c.name,'') LIKE '%'||$q||'%' OR s.created_at LIKE '%'||$q||'%')
                            ORDER BY s.id DESC LIMIT 500;";
        cmd.Parameters.AddWithValue("$q", search.Text.Trim());
        using var r=cmd.ExecuteReader();
        while(r.Read()) { var row=sales.Rows.Add(r.GetInt64(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetString(5)=="CANCELLED"?"CANCELADO":"ACTIVO",r.GetDouble(6)); sales.Rows[row].Tag=r.GetInt64(0); if(r.GetString(5)=="CANCELLED"){sales.Rows[row].DefaultCellStyle.Font=new Font(sales.Font,FontStyle.Strikeout);sales.Rows[row].DefaultCellStyle.ForeColor=Color.DarkRed;} else if(Convert.ToInt32(r.GetValue(7)) == 1){sales.Rows[row].DefaultCellStyle.BackColor=Color.Khaki;sales.Rows[row].DefaultCellStyle.ForeColor=Color.DarkGoldenrod;} }
        if (sales.Rows.Count > 0) { sales.Rows[0].Selected=true; CaptureSelection(); }
    }

    private void CaptureSelection() { if (sales.CurrentRow?.Tag is long id) selectedSaleId=id; }

    private long SelectedId() => selectedSaleId > 0 ? selectedSaleId : (sales.CurrentRow?.Tag is long rowId ? rowId : 0);

    private void ShowTicketActions()
    {
        var id = SelectedId();
        if (id <= 0) { MessageBox.Show(this, "Seleccioná una venta primero.", "IMP-TICKET", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var action = new Form
        {
            Text = "Ticket · ¿Qué querés hacer?", Width = 470, Height = 260,
            StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false
        };
        var title = new Label { Text = "¿QUÉ QUERÉS HACER CON ESTE TICKET?", Location = new Point(28, 25), AutoSize = true, Font = new Font("Segoe UI", 13, FontStyle.Bold) };
        action.Controls.Add(title);
        var info = new Label { Text = $"Ticket seleccionado: #{TicketService.GetTicket(id)?.TicketNo}", Location = new Point(30, 63), AutoSize = true };
        action.Controls.Add(info);
        var print = new Button { Text = "🖨  IMPRIMIR", Location = new Point(25, 105), Width = 125, Height = 55, DialogResult = DialogResult.Yes, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
        var wa = new Button { Text = "💬  WHATSAPP", Location = new Point(170, 105), Width = 140, Height = 55, DialogResult = DialogResult.No, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
        var cancel = new Button { Text = "CANCELAR", Location = new Point(325, 105), Width = 115, Height = 55, DialogResult = DialogResult.Cancel };
        action.Controls.AddRange(new Control[] { print, wa, cancel }); action.AcceptButton = print; action.CancelButton = cancel;
        ThemeService.Apply(action);
        var result = action.ShowDialog(this);
        if (result == DialogResult.Yes) TicketService.Print(id);
        else if (result == DialogResult.No) OpenWhatsAppPicker(id);
    }

    private void OpenWhatsAppPicker(long saleId)
    {
        using var form = new Form { Text = "Enviar ticket por WhatsApp", Width = 620, Height = 360, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
        var customerBox = new ComboBox { Location = new Point(25, 75), Width = 550, DropDownStyle = ComboBoxStyle.DropDownList };
        customerBox.Items.Add("Público general / ingresar número manualmente");
        using (var cn = Database.Open()) using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = "SELECT id,name,phone FROM customers WHERE active=1 AND TRIM(COALESCE(phone,''))<>'' ORDER BY name";
            using var r = cmd.ExecuteReader();
            while (r.Read()) customerBox.Items.Add(new ContactOption(r.GetInt64(0), r.GetString(1), r.GetString(2)));
        }
        customerBox.SelectedIndex = 0;
        var number = new TextBox { Location = new Point(25, 145), Width = 550, Font = new Font("Segoe UI", 11), PlaceholderText = "Número con código de país · ej. 5492615407856" };
        var selected = TicketService.GetTicket(saleId);
        if (selected is not null && !string.IsNullOrWhiteSpace(selected.CustomerPhone)) number.Text = selected.CustomerPhone;
        customerBox.SelectedIndexChanged += (_, _) => { if (customerBox.SelectedItem is ContactOption c) number.Text = c.Phone; };
        form.Controls.Add(new Label { Text = "ELEGÍ UN CONTACTO O ESCRIBÍ EL NÚMERO", Location = new Point(25, 28), AutoSize = true, Font = new Font("Segoe UI", 13, FontStyle.Bold) });
        form.Controls.Add(new Label { Text = "CONTACTO", Location = new Point(25, 58), AutoSize = true }); form.Controls.Add(customerBox);
        form.Controls.Add(new Label { Text = "WHATSAPP", Location = new Point(25, 120), AutoSize = true }); form.Controls.Add(number);
        form.Controls.Add(new Label { Text = "Se genera un PDF del ticket, se prepara el mensaje y el PDF queda listo para pegar con Ctrl+V en WhatsApp.", Location = new Point(25, 190), Size = new Size(550, 48) });
        var send = new Button { Text = "💬 PREPARAR Y ENVIAR", Location = new Point(25, 255), Width = 250, Height = 45, Font = new Font("Segoe UI", 9, FontStyle.Bold), DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "CANCELAR", Location = new Point(450, 255), Width = 125, Height = 45, DialogResult = DialogResult.Cancel };
        form.Controls.Add(send); form.Controls.Add(cancel); form.AcceptButton = send; form.CancelButton = cancel; ThemeService.Apply(form);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        try { TicketService.SendWhatsApp(saleId, number.Text.Trim()); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "WhatsApp", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private sealed record ContactOption(long Id, string Name, string Phone)
    {
        public override string ToString() => $"{Name} · {Phone}";
    }
}
