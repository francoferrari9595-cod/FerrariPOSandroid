using FerrarisPOS.Models;
using FerrarisPOS.Services;

namespace FerrarisPOS.Forms;

public sealed class BarcodeAssignmentForm : Form
{
    private readonly TextBox barcodeBox = new();
    private readonly TextBox searchBox = new();
    private readonly DataGridView grid = new();
    private readonly Label selectedLabel = new();
    private int? preferredProductId;

    public BarcodeAssignmentForm(int? productId = null)
    {
        preferredProductId = productId;
        Text = "FerrarisPOS - Asignar código de barras";
        Width = 1120; Height = 700; MinimumSize = new Size(980, 620);
        StartPosition = FormStartPosition.CenterParent; BackColor = Color.Gainsboro; KeyPreview = true;
        Build(); LoadProducts();

        Shown += (_, _) =>
        {
            if (preferredProductId.HasValue) SelectPreferredProduct();
            else if (grid.Rows.Count > 0) { grid.Focus(); grid.Rows[0].Selected = true; }
            barcodeBox.Focus();
        };

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
        };
    }

    private Button Btn(string text, int x, int y, int w, EventHandler action)
    {
        var b = new Button
        {
            Text = text, Location = new Point(x, y), Width = w, Height = 44,
            BackColor = Color.White, Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        b.Click += action; Controls.Add(b); return b;
    }

    private void Build()
    {
        Controls.Add(new Label
        {
            Text = "ASIGNAR CÓDIGO DE BARRAS",
            Location = new Point(25, 20), AutoSize = true,
            Font = new Font("Segoe UI", 18, FontStyle.Bold)
        });

        Controls.Add(new Label
        {
            Text = "1) Elegí el producto.  2) Escaneá o escribí el código.  3) Presioná ASIGNAR.",
            Location = new Point(25, 58), AutoSize = true,
            ForeColor = Color.DimGray, Font = new Font("Segoe UI", 10)
        });

        selectedLabel.SetBounds(25, 92, 1030, 30);
        selectedLabel.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        selectedLabel.Text = "PRODUCTO SELECCIONADO: ninguno";
        Controls.Add(selectedLabel);

        searchBox.SetBounds(25, 132, 620, 38);
        searchBox.Font = new Font("Segoe UI", 11);
        searchBox.PlaceholderText = "Buscar producto sin código...";
        searchBox.TextChanged += (_, _) => LoadProducts();
        Controls.Add(searchBox);

        grid.SetBounds(25, 180, 1035, 290);
        grid.ReadOnly = true; grid.AllowUserToAddRows = false; grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.RowHeadersVisible = false; grid.AutoGenerateColumns = false;
        grid.BackgroundColor = Color.White; grid.Font = new Font("Segoe UI", 10);
        grid.RowTemplate.Height = 32;
        grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName="Id", Visible=false });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText="PRODUCTO", DataPropertyName="Description", Width=560 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText="STOCK", DataPropertyName="Stock", Width=150 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText="PRECIO", DataPropertyName="SalePrice", Width=170, DefaultCellStyle = new DataGridViewCellStyle { Format="N2" } });
        grid.SelectionChanged += (_, _) => UpdateSelected();
        grid.DoubleBuffered(true);
        grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                barcodeBox.Focus(); barcodeBox.SelectAll();
                e.Handled = true; e.SuppressKeyPress = true;
            }
        };
        Controls.Add(grid);

        Controls.Add(new Label
        {
            Text = "CÓDIGO DEL ENVASE",
            Location = new Point(25, 495), AutoSize = true,
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        });

        barcodeBox.SetBounds(25, 522, 650, 50);
        barcodeBox.Font = new Font("Segoe UI", 20, FontStyle.Bold);
        barcodeBox.BackColor = Color.White;
        barcodeBox.PlaceholderText = "Escaneá aquí...";
        barcodeBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { Assign(); e.Handled = true; e.SuppressKeyPress = true; }
        };
        Controls.Add(barcodeBox);

        Btn("ASIGNAR CÓDIGO", 700, 522, 175, (_, _) => Assign());
        Btn("CERRAR", 885, 522, 175, (_, _) => { DialogResult = DialogResult.Cancel; Close(); });

        Controls.Add(new Label
        {
            Text = "El código se guarda directamente en el producto. No se modifica stock ni precio.",
            Location = new Point(25, 595), AutoSize = true, ForeColor = Color.DimGray
        });
    }

    private void LoadProducts()
    {
        var list = ProductService.SearchWithoutBarcode(searchBox.Text);
        grid.DataSource = list;
        if (list.Count > 0 && !preferredProductId.HasValue)
        {
            grid.ClearSelection();
            grid.Rows[0].Selected = true;
            grid.CurrentCell = grid.Rows[0].Cells[1];
        }
        UpdateSelected();
        SelectPreferredProduct();
    }

    private void SelectPreferredProduct()
    {
        if (preferredProductId is not int id) return;
        for (var i = 0; i < grid.Rows.Count; i++)
        {
            if (grid.Rows[i].DataBoundItem is Product p && p.Id == id)
            {
                grid.ClearSelection(); grid.Rows[i].Selected = true;
                grid.CurrentCell = grid.Rows[i].Cells[1];
                preferredProductId = null;
                UpdateSelected();
                return;
            }
        }
    }

    private Product? CurrentProduct() => grid.CurrentRow?.DataBoundItem as Product;

    private void UpdateSelected()
    {
        var p = CurrentProduct();
        selectedLabel.Text = p is null
            ? "PRODUCTO SELECCIONADO: ninguno"
            : $"PRODUCTO SELECCIONADO: {p.Description}";
    }

    private void Assign()
    {
        var product = CurrentProduct();
        var code = barcodeBox.Text.Trim();

        if (product is null)
        {
            MessageBox.Show("Seleccioná el producto al que querés asignar el código.", "Código de barras",
                MessageBoxButtons.OK, MessageBoxIcon.Warning); return;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            MessageBox.Show("Escaneá o escribí el código del envase.", "Código de barras",
                MessageBoxButtons.OK, MessageBoxIcon.Warning); barcodeBox.Focus(); return;
        }

        if (code.Any(char.IsWhiteSpace) || code.Length > 64)
        {
            MessageBox.Show("El código no puede contener espacios y no puede superar 64 caracteres.", "Código de barras",
                MessageBoxButtons.OK, MessageBoxIcon.Warning); barcodeBox.Focus(); return;
        }

        try
        {
            ProductService.AssignBarcode(product.Id, code);
            MessageBox.Show($"Código {code} asignado correctamente a:\n\n{product.Description}",
                "Código de barras", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK; Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo asignar el código:\n\n" + ex.Message,
                "Código de barras", MessageBoxButtons.OK, MessageBoxIcon.Error);
            barcodeBox.Focus(); barcodeBox.SelectAll();
        }
    }
}
