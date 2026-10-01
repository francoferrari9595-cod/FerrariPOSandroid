using FerrarisPOS.Data;
using FerrarisPOS.Services;
using System.Globalization;

namespace FerrarisPOS.Forms;

public sealed class CashMovementsHistoryForm : Form
{
    private readonly DataGridView grid = new();
    private readonly ComboBox filter = new();
    private readonly TextBox search = new();
    private readonly Label countLabel = new();

    public CashMovementsHistoryForm()
    {
        Text = "FerrarisPOS · Ingresos y Egresos del turno";
        Width = 1050;
        Height = 650;
        MinimumSize = new Size(850, 520);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(18, 22, 28);
        KeyPreview = true;
        Build();
        ThemeService.Apply(this);
        LoadData();
    }

    private void Build()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14),
            BackColor = Color.FromArgb(18, 22, 28)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var header = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(25, 30, 38) };
        var title = new Label
        {
            Text = "INGRESOS / EGRESOS DEL TURNO",
            Location = new Point(14, 10),
            AutoSize = true,
            Font = new Font("Segoe UI", 16, FontStyle.Bold)
        };
        var subtitle = new Label
        {
            Text = "Acá podés consultar TODOS los movimientos. Los anulados quedan guardados con su explicación.",
            Location = new Point(16, 40),
            AutoSize = true,
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.Gainsboro
        };
        header.Controls.Add(title); header.Controls.Add(subtitle); root.Controls.Add(header, 0, 0);

        var tools = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5, RowCount = 1, Padding = new Padding(0, 4, 0, 4) };
        tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        tools.Controls.Add(new Label { Text = "MOSTRAR", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9, FontStyle.Bold) }, 0, 0);
        filter.Dock = DockStyle.Fill; filter.DropDownStyle = ComboBoxStyle.DropDownList;
        filter.Items.AddRange(new object[] { "TODOS", "INGRESOS", "EGRESOS", "ANULADOS" }); filter.SelectedIndex = 0;
        filter.SelectedIndexChanged += (_, _) => LoadData(); tools.Controls.Add(filter, 1, 0);
        var searchLabel = new Label { Text = "BUSCAR", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9, FontStyle.Bold) };
        tools.Controls.Add(searchLabel, 2, 0);
        search.Dock = DockStyle.Fill; search.PlaceholderText = "Concepto, medio, usuario..."; search.TextChanged += (_, _) => LoadData(); tools.Controls.Add(search, 3, 0);
        var refresh = new Button { Text = "ACTUALIZAR", Dock = DockStyle.Fill, Margin = new Padding(5, 0, 0, 0) }; refresh.Click += (_, _) => LoadData(); tools.Controls.Add(refresh, 4, 0);
        root.Controls.Add(tools, 0, 1);

        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = Padding.Empty };
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.Controls.Add(content, 0, 2);

        grid.Dock = DockStyle.Fill;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.BackgroundColor = Color.FromArgb(25, 30, 38);
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "ID", Visible = false });
        grid.Columns.Add("Hora", "FECHA / HORA");
        grid.Columns.Add("Tipo", "TIPO");
        grid.Columns.Add("Medio", "MEDIO");
        grid.Columns.Add("Concepto", "CONCEPTO");
        grid.Columns.Add("Importe", "IMPORTE");
        grid.Columns.Add("Usuario", "USUARIO");
        grid.Columns.Add("Estado", "ESTADO");
        grid.Columns.Add("Motivo", "EXPLICACIÓN DE ANULACIÓN");
        content.Controls.Add(grid, 0, 0);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        countLabel.Dock = DockStyle.Fill; countLabel.TextAlign = ContentAlignment.MiddleLeft; countLabel.Font = new Font("Segoe UI", 9, FontStyle.Bold); bottom.Controls.Add(countLabel, 0, 0);
        var voidButton = new Button { Text = "ANULAR MOVIMIENTO", Dock = DockStyle.Fill, Margin = new Padding(4, 5, 4, 5), Font = new Font("Segoe UI", 9, FontStyle.Bold) };
        voidButton.Click += (_, _) => VoidSelected(); bottom.Controls.Add(voidButton, 1, 0);
        var close = new Button { Text = "CERRAR", Dock = DockStyle.Fill, Margin = new Padding(4, 5, 0, 5) }; close.Click += (_, _) => Close(); bottom.Controls.Add(close, 2, 0);
        content.Controls.Add(bottom, 0, 1);

        AcceptButton = close;
    }

    private void LoadData()
    {
        grid.Rows.Clear();
        var session = CashService.CurrentSessionId();
        if (session is null) { countLabel.Text = "No hay una caja abierta."; return; }

        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        var where = "cm.session_id=$s AND cm.movement_type IN ('INCOME','EXPENSE')";
        switch (filter.SelectedItem?.ToString())
        {
            case "INGRESOS": where += " AND cm.movement_type='INCOME' AND COALESCE(cm.voided,0)=0"; break;
            case "EGRESOS": where += " AND cm.movement_type='EXPENSE' AND COALESCE(cm.voided,0)=0"; break;
            case "ANULADOS": where += " AND COALESCE(cm.voided,0)=1"; break;
            default: break;
        }
        if (!string.IsNullOrWhiteSpace(search.Text))
        {
            where += " AND (cm.concept LIKE $q OR cm.payment_method LIKE $q OR COALESCE(u.full_name,'') LIKE $q)";
            cmd.Parameters.AddWithValue("$q", "%" + search.Text.Trim() + "%");
        }
        cmd.CommandText = $"""
            SELECT cm.id, cm.created_at, cm.movement_type, cm.payment_method, cm.concept, cm.amount,
                   COALESCE(u.full_name,u.username,''), COALESCE(cm.voided,0), COALESCE(cm.void_reason,''), COALESCE(v.full_name,v.username,'')
            FROM cash_movements cm
            LEFT JOIN users u ON u.id=cm.user_id
            LEFT JOIN users v ON v.id=cm.voided_by
            WHERE {where}
            ORDER BY cm.id DESC
            """;
        cmd.Parameters.AddWithValue("$s", session.Value);
        using var r = cmd.ExecuteReader();
        var count = 0;
        while (r.Read())
        {
            count++;
            var dt = DateTime.TryParse(r.GetString(1), out var parsed) ? parsed.ToString("dd/MM/yyyy HH:mm") : r.GetString(1);
            var voided = r.GetInt32(7) != 0;
            var tipo = r.GetString(2) == "INCOME" ? "INGRESO" : "EGRESO";
            var reason = voided ? $"{r.GetString(8)} · Anuló: {r.GetString(9)}" : "";
            var row = grid.Rows.Add(r.GetInt64(0), dt, tipo, r.GetString(3), r.GetString(4), $"${r.GetDouble(5):N2}", r.GetString(6), voided ? "ANULADO" : "ACTIVO", reason);
            if (voided) grid.Rows[row].DefaultCellStyle.Font = new Font(grid.Font, FontStyle.Strikeout);
        }
        countLabel.Text = $"Movimientos encontrados: {count:N0} · Los anulados no afectan la caja.";
    }

    private void VoidSelected()
    {
        if (grid.SelectedRows.Count != 1) { MessageBox.Show("Seleccioná un movimiento para anular.", "Ingresos / Egresos", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        var row = grid.SelectedRows[0];
        if (!long.TryParse(Convert.ToString(row.Cells["Id"].Value), out var id)) return;
        if (Convert.ToString(row.Cells["Estado"].Value) == "ANULADO") { MessageBox.Show("Ese movimiento ya está anulado.", "Ingresos / Egresos", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        var type = Convert.ToString(row.Cells["Tipo"].Value);
        var amount = Convert.ToString(row.Cells["Importe"].Value);
        var concept = Convert.ToString(row.Cells["Concepto"].Value);
        using var prompt = new ReasonPromptForm("EXPLICACIÓN DE ANULACIÓN", $"¿Por qué se anula este {type?.ToLowerInvariant()}?\n\n{concept}\nImporte: {amount}\n\nLa explicación quedará en el cierre y en el reporte enviado por mail.");
        if (prompt.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            CashService.VoidManualMovement(id, Session.UserId, prompt.Reason);
            LoadData();
            MessageBox.Show("Movimiento anulado correctamente. Ya no afecta la caja y la explicación quedó registrada.", "Ingresos / Egresos", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Ingresos / Egresos", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
}

internal sealed class ReasonPromptForm : Form
{
    private readonly TextBox reason = new();
    public string Reason => reason.Text.Trim();
    public ReasonPromptForm(string title, string message)
    {
        Text = title; Width = 560; Height = 310; MinimumSize = new Size(500, 280); StartPosition = FormStartPosition.CenterParent; BackColor = Color.FromArgb(18,22,28); Padding = new Padding(18);
        var layout = new TableLayoutPanel { Dock=DockStyle.Fill, ColumnCount=1, RowCount=4, Padding=Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 34)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.Controls.Add(new Label { Text=message, Dock=DockStyle.Fill, Font=new Font("Segoe UI",10), AutoEllipsis=true },0,0);
        layout.Controls.Add(new Label { Text="EXPLICACIÓN O MOTIVO", Dock=DockStyle.Fill, TextAlign=ContentAlignment.BottomLeft, Font=new Font("Segoe UI",9,FontStyle.Bold) },0,1);
        reason.Dock=DockStyle.Fill; reason.Multiline=true; reason.ScrollBars=ScrollBars.Vertical; reason.PlaceholderText="Ej.: se cargó por error, importe incorrecto, movimiento duplicado..."; layout.Controls.Add(reason,0,2);
        var buttons=new FlowLayoutPanel { Dock=DockStyle.Fill, FlowDirection=FlowDirection.RightToLeft };
        var cancel=new Button { Text="CANCELAR", Width=120, Height=38, DialogResult=DialogResult.Cancel }; var ok=new Button { Text="CONFIRMAR ANULACIÓN", Width=190, Height=38 }; ok.Click += (_,_)=>{ if(string.IsNullOrWhiteSpace(reason.Text)){MessageBox.Show("Ingresá una explicación.","Anulación",MessageBoxButtons.OK,MessageBoxIcon.Warning); reason.Focus(); return;} DialogResult=DialogResult.OK; Close();}; buttons.Controls.Add(cancel); buttons.Controls.Add(ok); layout.Controls.Add(buttons,0,3);
        Controls.Add(layout); AcceptButton=ok; CancelButton=cancel; Shown += (_,_)=>reason.Focus();
    }
}
