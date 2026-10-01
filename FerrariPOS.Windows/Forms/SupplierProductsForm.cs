using FerrarisPOS.Services;
using FerrarisPOS.Data;

namespace FerrarisPOS.Forms;

public sealed class SupplierProductsForm : Form
{
    private readonly int supplierId;
    private readonly string supplierName;
    private readonly DataGridView grid = new();
    private readonly TextBox search = new();
    private readonly Label countLabel = new();

    public SupplierProductsForm(int supplierId, string supplierName)
    {
        this.supplierId = supplierId;
        this.supplierName = supplierName;
        Text = $"FerrarisPOS - Productos de {supplierName}";
        Width = 1100;
        Height = 650;
        StartPosition = FormStartPosition.CenterParent;
        Build();
        LoadProducts();
        ThemeService.Apply(this);
    }

    private void Build()
    {
        Controls.Add(new Label
        {
            Text = $"PRODUCTOS QUE TRAE: {supplierName.ToUpperInvariant()}",
            Location = new Point(20, 18),
            AutoSize = true,
            Font = new Font("Segoe UI", 16, FontStyle.Bold)
        });

        Controls.Add(new Label { Text = "BUSCAR", Location = new Point(20, 60), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        search.Location = new Point(90, 55);
        search.Width = 330;
        search.TextChanged += (_, _) => LoadProducts();
        Controls.Add(search);

        var selectAll = new Button { Text = "SELECCIONAR TODO", Location = new Point(440, 53), Width = 155, Height = 30 };
        selectAll.Click += (_, _) => SetAll(true);
        Controls.Add(selectAll);
        var clearAll = new Button { Text = "QUITAR TODO", Location = new Point(605, 53), Width = 130, Height = 30 };
        clearAll.Click += (_, _) => SetAll(false);
        Controls.Add(clearAll);

        countLabel.Location = new Point(750, 60);
        countLabel.AutoSize = true;
        Controls.Add(countLabel);

        grid.Location = new Point(20, 100);
        grid.Size = new Size(1040, 440);
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AutoGenerateColumns = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.RowHeadersVisible = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.CellValueChanged += (_, _) => UpdateCount();
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        grid.DataError += (_, e) =>
        {
            e.ThrowException = false;
            e.Cancel = true;
        };

        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Asignado", HeaderText = "", FillWeight = 7 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "ID", FillWeight = 7, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Codigo", HeaderText = "CÓDIGO / BARRAS", FillWeight = 18, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Producto", HeaderText = "PRODUCTO", FillWeight = 32, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Categoria", HeaderText = "CATEGORÍA", FillWeight = 18, ReadOnly = true });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "CostoProveedor", HeaderText = "COSTO PROVEEDOR", FillWeight = 18 });
        Controls.Add(grid);

        var save = new Button { Text = "GUARDAR PRODUCTOS DEL PROVEEDOR", Location = new Point(20, 560), Width = 285, Height = 42 };
        save.Click += (_, _) => Save();
        Controls.Add(save);
        var close = new Button { Text = "CERRAR", Location = new Point(320, 560), Width = 120, Height = 42 };
        close.Click += (_, _) => Close();
        Controls.Add(close);
    }

    private void LoadProducts()
    {
        grid.Rows.Clear();
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = @"
SELECT p.id, p.barcode, p.description, p.category,
       CASE WHEN sp.product_id IS NULL THEN 0 ELSE 1 END assigned,
       COALESCE(sp.unit_cost, p.cost_price) supplier_cost
FROM products p
LEFT JOIN supplier_products sp ON sp.product_id=p.id AND sp.supplier_id=$sid
WHERE p.active=1 AND ($q='' OR p.description LIKE $like OR p.barcode LIKE $like)
ORDER BY p.description";
        cmd.Parameters.AddWithValue("$sid", supplierId);
        var q = search.Text.Trim();
        cmd.Parameters.AddWithValue("$q", q);
        cmd.Parameters.AddWithValue("$like", "%" + q + "%");
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var row = grid.Rows.Add(
                r.GetInt32(4) == 1,
                r.GetInt32(0),
                r.GetString(1),
                r.GetString(2),
                r.GetString(3),
                r.GetDouble(5).ToString("0.##")
            );
            grid.Rows[row].Tag = r.GetInt32(0);
        }
        UpdateCount();
    }

    private void SetAll(bool value)
    {
        foreach (DataGridViewRow row in grid.Rows) row.Cells[0].Value = value;
        UpdateCount();
    }

    private void UpdateCount()
    {
        var n = grid.Rows.Cast<DataGridViewRow>().Count(r => Convert.ToBoolean(r.Cells[0].Value ?? false));
        countLabel.Text = $"{n} producto(s) asignado(s)";
    }

    private static double ParseNumber(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        if (double.TryParse(raw, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.CurrentCulture, out var current)) return current;
        if (double.TryParse(raw, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var invariant)) return invariant;
        return 0;
    }

    private void Save()
    {
        try
        {
            using var cn = Database.Open();
            using var tx = cn.BeginTransaction();

            // Do not delete the whole supplier list when the user has a search
            // filter active. Only the products currently visible are changed;
            // hidden products keep their previous assignment.
            var existing = new Dictionary<int, double>();

            using (var load = cn.CreateCommand())
            {
                load.Transaction = tx;
                load.CommandText = "SELECT product_id, unit_cost FROM supplier_products WHERE supplier_id=$sid";
                load.Parameters.AddWithValue("$sid", supplierId);

                using var r = load.ExecuteReader();
                while (r.Read())
                    existing[r.GetInt32(0)] = r.GetDouble(1);
            }

            foreach (DataGridViewRow row in grid.Rows)
            {
                if (!int.TryParse(Convert.ToString(row.Cells[1].Value), out var productId))
                    continue;

                var assigned = Convert.ToBoolean(row.Cells[0].Value ?? false);
                var cost = ParseNumber(Convert.ToString(row.Cells[5].Value));

                if (assigned)
                {
                    using var cmd = cn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
INSERT INTO supplier_products(
    supplier_id, product_id, unit_cost, created_at, updated_at)
VALUES($sid,$pid,$cost,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP)
ON CONFLICT(supplier_id,product_id)
DO UPDATE SET unit_cost=$cost, updated_at=CURRENT_TIMESTAMP";
                    cmd.Parameters.AddWithValue("$sid", supplierId);
                    cmd.Parameters.AddWithValue("$pid", productId);
                    cmd.Parameters.AddWithValue("$cost", cost);
                    cmd.ExecuteNonQuery();
                }
                else if (existing.ContainsKey(productId))
                {
                    using var cmd = cn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = "DELETE FROM supplier_products WHERE supplier_id=$sid AND product_id=$pid";
                    cmd.Parameters.AddWithValue("$sid", supplierId);
                    cmd.Parameters.AddWithValue("$pid", productId);
                    cmd.ExecuteNonQuery();
                }
            }

            tx.Commit();

            MessageBox.Show(
                $"Se guardaron los productos que trae {supplierName}.",
                "Proveedores",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "No se pudieron guardar los productos del proveedor.\n\n" + ex.Message,
                "Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
