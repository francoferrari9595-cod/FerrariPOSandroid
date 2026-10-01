using FerrarisPOS.Data;
using FerrarisPOS.Services;

namespace FerrarisPOS.Forms;

public class StockMovementHistoryForm : Form
{
    private readonly DataGridView grid = new();
    private readonly TextBox search = new();

    public StockMovementHistoryForm()
    {
        Text = "FerrarisPOS · Historial de movimientos de inventario";
        Width = 1100; Height = 650;
        StartPosition = FormStartPosition.CenterParent;
        Build(); LoadData();
    }

    private void Build()
    {
        Controls.Add(new Label
        {
            Text = "HISTORIAL DE MOVIMIENTOS DE INVENTARIO",
            Location = new Point(20, 18), AutoSize = true,
            Font = new Font("Segoe UI", 17, FontStyle.Bold)
        });
        Controls.Add(new Label
        {
            Text = "Cada salida negativa muestra obligatoriamente el motivo y el cajero que realizó el movimiento.",
            Location = new Point(20, 52), AutoSize = true, ForeColor = Color.DimGray
        });
        search.Location = new Point(20, 85); search.Width = 350;
        search.PlaceholderText = "Buscar producto, motivo o cajero...";
        search.TextChanged += (_, _) => LoadData();
        Controls.Add(search);

        var refresh = new Button { Text = "ACTUALIZAR", Location = new Point(385, 82), Width = 120, Height = 32 };
        refresh.Click += (_, _) => LoadData();
        Controls.Add(refresh);

        grid.Location = new Point(20, 130); grid.Size = new Size(1040, 450);
        grid.ReadOnly = true; grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false;
        grid.RowHeadersVisible = false; grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.BackgroundColor = Color.White;
        Controls.Add(grid);
    }

    private void LoadData()
    {
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            SELECT datetime(sm.created_at,'localtime') AS FECHA,
                   p.description AS PRODUCTO,
                   sm.movement_type AS TIPO,
                   sm.quantity AS CANTIDAD,
                   COALESCE(sm.reference,'') AS MOTIVO,
                   COALESCE(u.full_name,'') AS CAJERO
            FROM stock_movements sm
            JOIN products p ON p.id=sm.product_id
            LEFT JOIN users u ON u.id=sm.user_id
            WHERE ($q='' OR p.description LIKE $like OR sm.reference LIKE $like OR COALESCE(u.full_name,'') LIKE $like)
            ORDER BY sm.id DESC LIMIT 300
            """;
        var q = search.Text.Trim();
        cmd.Parameters.AddWithValue("$q", q);
        cmd.Parameters.AddWithValue("$like", $"%{q}%");
        using var r = cmd.ExecuteReader();
        var dt = new System.Data.DataTable();
        dt.Load(r);
        grid.DataSource = dt;
        if (grid.Columns["CANTIDAD"] is DataGridViewColumn qty)
            qty.DefaultCellStyle.Format = "N3";
    }
}
