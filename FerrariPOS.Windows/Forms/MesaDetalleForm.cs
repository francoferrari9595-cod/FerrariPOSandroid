using FerrarisPOS.Models;

namespace FerrarisPOS.Forms;

public sealed class MesaDetalleForm : Form
{
    public MesaDetalleForm(string tableName, IReadOnlyList<CartItem> items, Action charge, Action openTicket)
    {
        Text = tableName; Width = 520; Height = 560; StartPosition = FormStartPosition.CenterParent; Padding = new Padding(18);
        var title = new Label { Text = tableName, Dock = DockStyle.Top, Height = 42, Font = new Font("Segoe UI", 18, FontStyle.Bold) }; Controls.Add(title);
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false, AllowUserToAddRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "PRODUCTO", FillWeight = 55 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "CANT.", FillWeight = 20 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "TOTAL", FillWeight = 25, DefaultCellStyle = new DataGridViewCellStyle { Format = "N2", Alignment = DataGridViewContentAlignment.MiddleRight } });
        foreach (var i in items) grid.Rows.Add(i.Description, i.Quantity, i.Total);
        Controls.Add(grid);
        var total = items.Sum(x => x.Total);
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 92, FlowDirection = FlowDirection.RightToLeft };
        bottom.Controls.Add(new Label { Text = $"TOTAL: ${total:N2}", AutoSize = true, Font = new Font("Segoe UI", 16, FontStyle.Bold), Margin = new Padding(12) });
        var pay = new Button { Text = "COBRAR MESA", Width = 135, Height = 42 }; pay.Click += (_,_) => { Close(); charge(); };
        var open = new Button { Text = "ABRIR TICKET", Width = 135, Height = 42 }; open.Click += (_,_) => { Close(); openTicket(); };
        bottom.Controls.Add(pay); bottom.Controls.Add(open); Controls.Add(bottom);
    }
}
