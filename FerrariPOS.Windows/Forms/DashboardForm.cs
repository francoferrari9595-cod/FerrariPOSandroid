using FerrarisPOS.Data;
using FerrarisPOS.Services;
using System.Data;

namespace FerrarisPOS.Forms;

public class DashboardForm : Form
{
    private readonly TableLayoutPanel cards = new();
    private readonly DataGridView topGrid = new();
    private readonly DataGridView stockGrid = new();
    private readonly Label subtitle = new();
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 10000 };

    public DashboardForm()
    {
        Text = "FerrarisPOS · Panel de gestión";
        Width = 1180; Height = 700; MinimumSize = new Size(980, 600);
        StartPosition = FormStartPosition.CenterParent;
        Build();
        LoadData();
        timer.Tick += (_, _) => LoadData();
        timer.Start();
        FormClosed += (_, _) => timer.Dispose();
        ThemeService.Apply(this);
    }

    private void Build()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        Controls.Add(root);

        var title = new Label { Text = "PANEL DE GESTIÓN", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 18, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft };
        root.Controls.Add(title, 0, 0);

        cards.Dock = DockStyle.Fill; cards.ColumnCount = 5; cards.RowCount = 1; cards.Padding = new Padding(0, 2, 0, 2);
        for (int i = 0; i < 5; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        root.Controls.Add(cards, 0, 1);

        var center = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(0, 8, 0, 0) };
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60)); center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        center.RowStyles.Add(new RowStyle(SizeType.Absolute, 32)); center.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        center.Controls.Add(new Label { Text = "PRODUCTOS MÁS VENDIDOS", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        center.Controls.Add(new Label { Text = "STOCK BAJO / CRÍTICO", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft }, 1, 0);
        ConfigureGrid(topGrid); ConfigureGrid(stockGrid);
        center.Controls.Add(topGrid, 0, 1); center.Controls.Add(stockGrid, 1, 1);
        root.Controls.Add(center, 0, 2);

        subtitle.Dock = DockStyle.Fill; subtitle.TextAlign = ContentAlignment.MiddleLeft; subtitle.Font = new Font("Segoe UI", 8.5f);
        root.Controls.Add(subtitle, 0, 3);
    }

    private static void ConfigureGrid(DataGridView g)
    {
        g.Dock = DockStyle.Fill; g.ReadOnly = true; g.AllowUserToAddRows = false; g.AllowUserToDeleteRows = false;
        g.AutoGenerateColumns = true; g.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        g.RowHeadersVisible = false; g.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
    }

    private Panel Card(string caption, string value, Color accent)
    {
        var p = new Panel { Dock = DockStyle.Fill, Margin = new Padding(3), Padding = new Padding(10), BorderStyle = BorderStyle.FixedSingle };
        var c = new Label { Text = caption, Dock = DockStyle.Top, Height = 20, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
        var v = new Label { Text = value, Dock = DockStyle.Fill, Font = new Font("Segoe UI", 16, FontStyle.Bold), ForeColor = accent, TextAlign = ContentAlignment.MiddleLeft };
        p.Controls.Add(v); p.Controls.Add(c); return p;
    }

    private void LoadData()
    {
        try
        {
            using var cn = Database.Open();
            double sales = Scalar(cn, "SELECT COALESCE(SUM(total),0) FROM sales WHERE date(created_at,'localtime')=date('now','localtime') AND status='COMPLETED'");
            int tickets = Convert.ToInt32(Scalar(cn, "SELECT COUNT(*) FROM sales WHERE date(created_at,'localtime')=date('now','localtime') AND status='COMPLETED'"));
            double profit = Scalar(cn, "SELECT COALESCE(SUM(si.quantity * (si.unit_price - p.cost_price)),0) FROM sale_items si JOIN sales s ON s.id=si.sale_id JOIN products p ON p.id=si.product_id WHERE date(s.created_at,'localtime')=date('now','localtime') AND s.status='COMPLETED'");
            int low = InventoryControlService.IsGlobalEnabled ? Convert.ToInt32(Scalar(cn, "SELECT COUNT(*) FROM products WHERE active=1 AND uses_inventory=1 AND stock<=min_stock")) : 0;
            double returns = Scalar(cn, "SELECT COALESCE(SUM(amount),0) FROM sale_returns WHERE date(created_at,'localtime')=date('now','localtime')");
            cards.Controls.Clear();
            cards.Controls.Add(Card("VENTAS HOY", $"${sales:N2}", Color.FromArgb(20,120,70)), 0, 0);
            cards.Controls.Add(Card("TICKETS", tickets.ToString("N0"), Color.FromArgb(45,100,180)), 1, 0);
            cards.Controls.Add(Card("GANANCIA ESTIMADA", $"${profit:N2}", Color.FromArgb(150,100,20)), 2, 0);
            cards.Controls.Add(Card("STOCK BAJO", low.ToString("N0"), low > 0 ? Color.Firebrick : Color.FromArgb(20,120,70)), 3, 0);
            cards.Controls.Add(Card("DEVOLUCIONES", $"${returns:N2}", Color.FromArgb(150,60,100)), 4, 0);

            var dt = new DataTable();
            using (var q = cn.CreateCommand())
            {
                q.CommandText = "SELECT si.description AS Producto, ROUND(SUM(si.quantity),3) AS Cantidad, ROUND(SUM(si.total),2) AS Importe FROM sale_items si JOIN sales s ON s.id=si.sale_id WHERE date(s.created_at,'localtime')=date('now','localtime') AND s.status='COMPLETED' GROUP BY si.product_id,si.description ORDER BY Cantidad DESC LIMIT 12";
                using var r = q.ExecuteReader(); dt.Load(r);
            }
            topGrid.DataSource = dt;
            var st = new DataTable();
            using (var q = cn.CreateCommand())
            {
                q.CommandText = "SELECT description AS Producto, ROUND(stock,3) AS Stock, ROUND(min_stock,3) AS Mínimo, unit AS Unidad FROM products WHERE active=1 AND uses_inventory=1 AND stock<=min_stock AND $inv=1 ORDER BY (stock-min_stock),description LIMIT 30";
                q.Parameters.AddWithValue("$inv", InventoryControlService.IsGlobalEnabled ? 1 : 0);
                using var r = q.ExecuteReader(); st.Load(r);
            }
            stockGrid.DataSource = st;
            subtitle.Text = $"Actualización automática cada 10 segundos · {DateTime.Now:dd/MM/yyyy HH:mm:ss} · Los valores de ganancia son estimativos según costo registrado.";
        }
        catch (Exception ex) { subtitle.Text = "No se pudo actualizar el panel: " + ex.Message; }
    }

    private static double Scalar(Microsoft.Data.Sqlite.SqliteConnection cn, string sql)
    { using var q = cn.CreateCommand(); q.CommandText = sql; return Convert.ToDouble(q.ExecuteScalar() ?? 0); }
}
