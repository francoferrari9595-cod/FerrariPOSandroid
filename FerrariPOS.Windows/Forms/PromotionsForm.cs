using FerrarisPOS.Data;
using FerrarisPOS.Models;
using FerrarisPOS.Services;
using Microsoft.Data.Sqlite;

namespace FerrarisPOS.Forms;

/// <summary>
/// Gestión completa de promociones/combos.
/// Las promociones no tienen vencimiento: permanecen disponibles hasta que se eliminen.
/// Flujo: NUEVA PROMOCIÓN -> nombre/precio -> tildar productos -> definir cantidades -> GUARDAR.
/// Una promoción guardada puede modificarse con ENTER o doble clic y puede incluirse en una venta.
/// </summary>
public sealed class PromotionsForm : Form
{
    private readonly DataGridView grid = new();
    private readonly CheckedListBox productChecklist = new();
    private readonly DataGridView selectedGrid = new();
    private readonly TextBox name = new();
    private readonly TextBox description = new();
    private readonly NumericUpDown promoPrice = new();
    private readonly NumericUpDown selectedQty = new();
    private readonly Label selectedProductLabel = new();
    private readonly Label selectedStockLabel = new();
    private readonly Label selectedNormalPriceLabel = new();
    private readonly Label normalTotal = new();
    private readonly Label promoTotal = new();
    private readonly Label status = new();
    private readonly Button saveButton = new();
    private readonly List<Product> products = new();
    private readonly Dictionary<int, double> quantities = new();
    private long selectedId;
    private bool loadingChecks;

    public List<CartItem> SelectedItems { get; private set; } = new();

    public PromotionsForm()
    {
        Text = "FerrarisPOS · Promociones";
        Width = 1800;
        Height = 860;
        MinimumSize = new Size(1450, 720);
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;

        Build();
        LoadProducts();
        LoadData();
        ThemeService.Apply(this);
        ApplyPromotionUi();

        Shown += (_, _) =>
        {
            if (IsDisposed) return;
            // Al abrir PROMOCIONES, el foco queda directamente en la lista.
            // Así las flechas cambian de promoción y ENTER la incorpora a la venta.
            if (grid.Rows.Count > 0 && grid.CanFocus)
            {
                grid.Focus();
                if (grid.CurrentRow == null)
                {
                    grid.CurrentCell = grid.Rows[0].Cells["NOMBRE"];
                    grid.Rows[0].Selected = true;
                }
            }
            else if (name.CanFocus)
            {
                name.Focus();
            }
        };
    }

    private void Build()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        Controls.Add(root);

        var header = new Panel { Dock = DockStyle.Fill };
        var title = new Label
        {
            Text = "PROMOCIONES",
            Dock = DockStyle.Top,
            Height = 38,
            Padding = new Padding(4, 0, 0, 0),
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        var subtitle = new Label
        {
            Text = "Creá un combo, tildá los productos que lo componen y después indicá la cantidad de cada uno.",
            Dock = DockStyle.Bottom,
            Height = 24,
            Padding = new Padding(6, 0, 0, 0),
            Font = new Font("Segoe UI", 9),
            TextAlign = ContentAlignment.MiddleLeft
        };
        header.Controls.Add(subtitle);
        header.Controls.Add(title);
        root.Controls.Add(header, 0, 0);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 940,
            FixedPanel = FixedPanel.Panel1,
            Padding = Padding.Empty
        };
        root.Controls.Add(split, 0, 1);
        BuildPromotionList(split);

        var editor = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(10, 0, 0, 0)
        };
        editor.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        split.Panel2.Controls.Add(editor);
        BuildPromotionData(editor);
        BuildProductsArea(editor);

        BuildFooter(root);
    }

    private void Grid_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            // ENTER desde la lista es la acción rápida de venta.
            UseSelected();
        }
    }

    private void BuildPromotionList(SplitContainer split)
    {
        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(0, 0, 10, 0)
        };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        split.Panel1.Controls.Add(left);

        var label = new Label
        {
            Text = "PROMOCIONES CREADAS",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            Padding = new Padding(4, 0, 0, 0)
        };
        left.Controls.Add(label, 0, 0);

        ConfigureGrid(grid);

        // IMPORTANTE:
        // La lista de promociones no debe autogenerar columnas desde el origen de datos.
        // En algunas instalaciones/versiones de WinForms una columna heredada o inferida
        // puede quedar tipada como Image y provocar:
        // "Invalid cast from System.String to System.Drawing.Image".
        // Definimos explícitamente todas las columnas como TextBox para que el valor
        // almacenado en SQLite nunca intente convertirse a Image.
        grid.AutoGenerateColumns = false;
        grid.Columns.Clear();
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ID",
            DataPropertyName = "ID",
            Visible = false
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "NOMBRE",
            HeaderText = "PROMOCIÓN",
            DataPropertyName = "NOMBRE",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 230
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "DESCRIPCION",
            HeaderText = "DESCRIPCIÓN",
            DataPropertyName = "DESCRIPCION",
            Visible = false
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "PRECIO",
            HeaderText = "PRECIO",
            DataPropertyName = "PRECIO",
            Width = 105,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                Format = "$ #,##0.00",
                Alignment = DataGridViewContentAlignment.MiddleRight
            }
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "ACTIVA",
            DataPropertyName = "ACTIVA",
            Visible = false
        });

        // Nunca dejar que el cuadro de diálogo genérico de DataGridView oculte
        // el origen del problema. Si una versión antigua tiene datos incompatibles,
        // se cancela la celda problemática y se informa en la barra de estado.
        grid.DataError += (_, e) =>
        {
            e.ThrowException = false;
            e.Cancel = true;
            status.Text = "No se pudo mostrar un dato de la promoción. La columna fue protegida contra conversiones incompatibles.";
        };

        grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) UseSelected(); };
        grid.KeyDown += Grid_KeyDown;
        left.Controls.Add(grid, 0, 1);

        var hint = new Label
        {
            Text = "↑ ↓ SELECCIONAR · ENTER / DOBLE CLICK = AGREGAR A LA VENTA",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
        };
        left.Controls.Add(hint, 0, 2);
    }

    private void BuildPromotionData(TableLayoutPanel editor)
    {
        var data = new GroupBox
        {
            Text = "1 · DATOS DE LA PROMOCIÓN",
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 22, 10, 10),
            Tag = "PromoYellow"
        };
        editor.Controls.Add(data, 0, 0);

        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            Padding = Padding.Empty
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        data.Controls.Add(panel);

        AddDataLabel(panel, 0, 0, "NOMBRE *");
        name.Dock = DockStyle.Fill;
        name.Font = new Font("Segoe UI", 12, FontStyle.Bold);
        name.PlaceholderText = "Ej.: PANCHO ESCOLAR";
        panel.Controls.Add(name, 1, 0);

        AddDataLabel(panel, 2, 0, "PRECIO DEL COMBO");
        promoPrice.Dock = DockStyle.Fill;
        promoPrice.DecimalPlaces = 2;
        promoPrice.ThousandsSeparator = true;
        promoPrice.Minimum = 0;
        promoPrice.Maximum = 100000000;
        promoPrice.Font = new Font("Segoe UI", 12, FontStyle.Bold);
        promoPrice.ValueChanged += (_, _) => UpdateTotals();
        panel.Controls.Add(promoPrice, 3, 0);

        AddDataLabel(panel, 0, 1, "DESCRIPCIÓN");
        description.Dock = DockStyle.Fill;
        description.PlaceholderText = "Ej.: Pancho + papas + gaseosa";
        panel.Controls.Add(description, 1, 1);

    }

    private void BuildProductsArea(TableLayoutPanel editor)
    {
        var productGroup = new GroupBox
        {
            Text = "2 · PRODUCTOS Y CANTIDADES DEL COMBO",
            Dock = DockStyle.Fill,
            Padding = new Padding(10, 22, 10, 10),
            Tag = "PromoGreen"
        };
        editor.Controls.Add(productGroup, 0, 1);

        var area = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2
        };
        // El selector de productos queda más compacto para darle más espacio
        // al detalle/resumen y evitar que los nombres queden cortados.
        area.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        area.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 78));
        area.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        area.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        productGroup.Controls.Add(area);

        var instruction = new Label
        {
            Text = "☑ Tildá cada producto que forme parte del combo. Luego seleccioná un producto tildado y definí su cantidad.",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Padding = new Padding(4, 7, 4, 0),
            TextAlign = ContentAlignment.MiddleLeft
        };
        area.Controls.Add(instruction, 0, 0);
        area.SetColumnSpan(instruction, 2);

        productChecklist.Dock = DockStyle.Fill;
        productChecklist.CheckOnClick = true;
        productChecklist.IntegralHeight = false;
        productChecklist.BorderStyle = BorderStyle.FixedSingle;
        productChecklist.Font = new Font("Segoe UI", 10.5f);
        productChecklist.DisplayMember = "Description";
        productChecklist.ItemCheck += ProductChecklist_ItemCheck;
        productChecklist.SelectedIndexChanged += ProductChecklist_SelectedIndexChanged;
        area.Controls.Add(productChecklist, 0, 1);

        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14, 0, 0, 0)
        };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        area.Controls.Add(right, 1, 1);

        var selectedBox = new GroupBox { Text = "CANTIDAD DEL PRODUCTO SELECCIONADO", Dock = DockStyle.Fill, Padding = new Padding(10, 22, 10, 10), Tag = "PromoCyan" };
        right.Controls.Add(selectedBox, 0, 0);
        var selectedPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2 };
        selectedPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        selectedPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        selectedPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        selectedPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        selectedPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        selectedPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        selectedBox.Controls.Add(selectedPanel);

        selectedProductLabel.Text = "Ningún producto seleccionado";
        selectedProductLabel.Dock = DockStyle.Fill;
        selectedProductLabel.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        selectedProductLabel.TextAlign = ContentAlignment.MiddleLeft;
        selectedPanel.Controls.Add(selectedProductLabel, 0, 0);
        selectedPanel.SetColumnSpan(selectedProductLabel, 2);

        selectedStockLabel.Text = "Stock: -";
        selectedStockLabel.Dock = DockStyle.Fill;
        selectedStockLabel.TextAlign = ContentAlignment.MiddleLeft;
        selectedPanel.Controls.Add(selectedStockLabel, 2, 0);
        selectedNormalPriceLabel.Text = "Precio: -";
        selectedNormalPriceLabel.Dock = DockStyle.Fill;
        selectedNormalPriceLabel.TextAlign = ContentAlignment.MiddleLeft;
        selectedPanel.Controls.Add(selectedNormalPriceLabel, 3, 0);

        selectedQty.Dock = DockStyle.Fill;
        selectedQty.DecimalPlaces = 3;
        selectedQty.Minimum = 0.001m;
        selectedQty.Maximum = 100000;
        selectedQty.Value = 1;
        selectedQty.ValueChanged += (_, _) => LiveQuantityChanged();
        selectedQty.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        selectedPanel.Controls.Add(selectedQty, 0, 1);

        var applyQty = Button("APLICAR CANTIDAD", 145, (_, _) => ApplySelectedQuantity());
        selectedPanel.Controls.Add(applyQty, 1, 1);
        var uncheck = Button("DESMARCAR", 105, (_, _) => UncheckSelectedProduct());
        selectedPanel.Controls.Add(uncheck, 2, 1);
        var help = new Label { Text = "1 = una unidad", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(5, 0, 0, 0) };
        selectedPanel.Controls.Add(help, 3, 1);

        var selectedBox2 = new GroupBox { Text = "PRODUCTOS TILDADOS EN LA PROMOCIÓN", Dock = DockStyle.Fill, Padding = new Padding(6) };
        right.Controls.Add(selectedBox2, 0, 1);
        ConfigureGrid(selectedGrid);
        selectedGrid.AutoGenerateColumns = false;
        selectedGrid.DataError += (_, e) =>
        {
            e.ThrowException = false;
            e.Cancel = true;
            status.Text = "No se pudo mostrar un producto de la promoción por un formato incompatible.";
        };
        selectedGrid.Columns.Clear();
        selectedGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ProductId", DataPropertyName = "ProductId", Visible = false });
        selectedGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "PRODUCTO", DataPropertyName = "Description", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        selectedGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "CANT.", DataPropertyName = "Quantity", Width = 70, DefaultCellStyle = new DataGridViewCellStyle { Format = "N3" } });
        selectedGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "NORMAL", DataPropertyName = "Subtotal", Width = 90, DefaultCellStyle = new DataGridViewCellStyle { Format = "$ #,##0.00" } });
        selectedGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "STOCK", DataPropertyName = "Stock", Width = 75, DefaultCellStyle = new DataGridViewCellStyle { Format = "N3" } });
        selectedGrid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.RowIndex < selectedGrid.Rows.Count)
            {
                var id = Convert.ToInt32(selectedGrid.Rows[e.RowIndex].Cells["ProductId"].Value);
                SelectProductById(id);
            }
        };
        selectedBox2.Controls.Add(selectedGrid);

        var totals = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(2, 3, 0, 0) };
        normalTotal.AutoSize = true;
        normalTotal.Padding = new Padding(0, 5, 24, 0);
        promoTotal.AutoSize = true;
        promoTotal.Padding = new Padding(0, 5, 0, 0);
        totals.Controls.Add(normalTotal);
        totals.Controls.Add(promoTotal);
        right.Controls.Add(totals, 0, 2);
    }

    private void BuildFooter(TableLayoutPanel root)
    {
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Color.Transparent, Padding = new Padding(0, 4, 0, 0) };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 75));
        root.Controls.Add(footer, 0, 2);

        status.Dock = DockStyle.Fill;
        status.Text = "GUÍA RÁPIDA · 1) Poné nombre y precio · 2) Tildá los productos · 3) Elegí cantidades · 4) GUARDAR. Luego podés modificar o incluir la promoción en una venta.";
        status.TextAlign = ContentAlignment.MiddleLeft;
        status.Padding = new Padding(5, 0, 5, 0);
        footer.Controls.Add(status, 0, 0);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var close = Button("CERRAR", 95, (_, _) => Close());
        var use = Button("INCLUIR EN VENTA", 145, (_, _) => UseSelected());
        var modify = Button("MODIFICAR", 105, (_, _) => ModifySelected());
        var delete = Button("ELIMINAR", 95, (_, _) => Delete());
        saveButton.Text = "GUARDAR";
        saveButton.Width = 105;
        saveButton.Height = 36;
        saveButton.Margin = new Padding(3);
        saveButton.FlatStyle = FlatStyle.Flat;
        saveButton.Click += (_, _) => Save();
        var fresh = Button("NUEVA PROMOCIÓN", 145, (_, _) => ClearForm());

        buttons.Controls.Add(close);
        buttons.Controls.Add(use);
        buttons.Controls.Add(modify);
        buttons.Controls.Add(delete);
        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(fresh);
        footer.Controls.Add(buttons, 1, 0);
    }

    private static void AddDataLabel(TableLayoutPanel panel, int col, int row, string text)
    {
        panel.Controls.Add(new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        }, col, row);
    }

    private static void ConfigureGrid(DataGridView view)
    {
        view.Dock = DockStyle.Fill;
        view.ReadOnly = true;
        view.AllowUserToAddRows = false;
        view.AllowUserToDeleteRows = false;
        view.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        view.MultiSelect = false;
        view.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        view.RowHeadersVisible = false;
    }


    private static Button Button(string text, int width, EventHandler click)
    {
        var b = new Button
        {
            Text = text,
            Width = width,
            Height = 36,
            Margin = new Padding(3),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            AutoSize = false
        };
        b.Click += click;
        return b;
    }

    private void ApplyPromotionUi()
    {
        var yellow = Color.FromArgb(255, 193, 7);
        var green = Color.FromArgb(80, 220, 120);
        var cyan = Color.FromArgb(85, 205, 255);
        foreach (Control control in ControlsRecursive(this))
        {
            if (control is GroupBox gb)
            {
                gb.ForeColor = gb.Tag?.ToString() switch
                {
                    "PromoYellow" => yellow,
                    "PromoGreen" => green,
                    "PromoCyan" => cyan,
                    _ => Color.White
                };
            }
            else if (control is Label label)
            {
                label.ForeColor = Color.WhiteSmoke;
            }
        }
    }

    private static IEnumerable<Control> ControlsRecursive(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (var child in ControlsRecursive(c))
                yield return child;
        }
    }

    private void LoadProducts()
    {
        try
        {
            products.Clear();
            products.AddRange(ProductService.Search().OrderBy(p => p.Description));
            productChecklist.Items.Clear();
            foreach (var p in products)
                productChecklist.Items.Add(p, false);
            productChecklist.DisplayMember = "Description";
            if (productChecklist.Items.Count > 0)
                productChecklist.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudieron cargar los productos del stock.\n\n" + ex.Message, "Promociones", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ProductChecklist_ItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (loadingChecks) return;
        if (e.Index < 0 || e.Index >= productChecklist.Items.Count) return;
        if (productChecklist.Items[e.Index] is not Product p) return;

        if (e.NewValue == CheckState.Checked)
        {
            if (!quantities.ContainsKey(p.Id)) quantities[p.Id] = 1;
            if (p.UsesInventory && quantities[p.Id] > p.Stock + 0.000001)
                quantities[p.Id] = p.Stock > 0 ? 1 : 0.001;
        }
        else
        {
            quantities.Remove(p.Id);
        }

        RefreshSelectedSummary(e.Index, e.NewValue);
    }

    private void ProductChecklist_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (productChecklist.SelectedItem is Product p)
        {
            selectedProductLabel.Text = p.Description;
            selectedStockLabel.Text = p.UsesInventory ? $"Stock: {p.Stock:N3}" : "Stock: NO APLICA";
            selectedNormalPriceLabel.Text = $"Precio: ${p.SalePrice:N2}";
            var q = quantities.TryGetValue(p.Id, out var value) ? value : 1;
            var clamped = Math.Min((double)selectedQty.Maximum, Math.Max((double)selectedQty.Minimum, q));
            selectedQty.Value = Convert.ToDecimal(clamped);
        }
        else
        {
            selectedProductLabel.Text = "Ningún producto seleccionado";
            selectedStockLabel.Text = "Stock: -";
            selectedNormalPriceLabel.Text = "Precio: -";
            selectedQty.Value = 1;
        }
    }

    private void LiveQuantityChanged()
    {
        if (loadingChecks) return;
        if (productChecklist.SelectedItem is not Product p) return;
        if (!productChecklist.GetItemChecked(productChecklist.SelectedIndex)) return;
        var q = Convert.ToDouble(selectedQty.Value);
        if (p.UsesInventory && q > p.Stock + 0.000001)
        {
            var safe = p.Stock > 0 ? p.Stock : 0.001;
            q = Math.Min(q, safe);
            loadingChecks = true;
            try { selectedQty.Value = Convert.ToDecimal(q); }
            finally { loadingChecks = false; }
        }
        quantities[p.Id] = q;
        RefreshSelectedSummary();
        status.Text = $"Cantidad actualizada automáticamente: {p.Description} × {q:N3}. El total se recalculó sin presionar APLICAR CANTIDAD.";
    }

    private void ApplySelectedQuantity()
    {
        if (productChecklist.SelectedItem is not Product p)
        {
            MessageBox.Show("Seleccioná un producto en la lista.", "Promociones", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!productChecklist.GetItemChecked(productChecklist.SelectedIndex))
        {
            MessageBox.Show("Primero tildá el producto para incluirlo en la promoción.", "Promociones", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var q = Convert.ToDouble(selectedQty.Value);
        if (p.UsesInventory && q > p.Stock + 0.000001)
        {
            MessageBox.Show($"La cantidad supera el stock disponible de {p.Description}.\n\nStock: {p.Stock:N3}", "Stock insuficiente", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        quantities[p.Id] = q;
        RefreshSelectedSummary();
        status.Text = $"Cantidad actualizada: {p.Description} × {q:N3}. Presioná GUARDAR para conservar los cambios.";
    }

    private void UncheckSelectedProduct()
    {
        if (productChecklist.SelectedIndex < 0) return;
        var idx = productChecklist.SelectedIndex;
        loadingChecks = true;
        try
        {
            productChecklist.SetItemCheckState(idx, CheckState.Unchecked);
        }
        finally
        {
            loadingChecks = false;
        }
        if (productChecklist.Items[idx] is Product p) quantities.Remove(p.Id);
        RefreshSelectedSummary();
    }

    private void RefreshSelectedSummary(int? pendingIndex = null, CheckState? pendingState = null)
    {
        var rows = new List<SelectedProductRow>();
        for (var i = 0; i < productChecklist.Items.Count; i++)
        {
            var isChecked = productChecklist.GetItemChecked(i);
            if (pendingIndex == i && pendingState.HasValue)
                isChecked = pendingState.Value == CheckState.Checked;
            if (!isChecked || productChecklist.Items[i] is not Product p) continue;
            var q = quantities.TryGetValue(p.Id, out var value) ? value : 1;
            rows.Add(new SelectedProductRow(p.Id, p.Description, q, p.SalePrice * q, p.Stock));
        }
        selectedGrid.DataSource = null;
        selectedGrid.DataSource = rows;
        if (selectedGrid.Columns.Contains("ProductId")) selectedGrid.Columns["ProductId"].Visible = false;
        UpdateTotals();
    }

    private sealed record SelectedProductRow(int ProductId, string Description, double Quantity, double Subtotal, double Stock);

    private void SelectProductById(int id)
    {
        for (var i = 0; i < productChecklist.Items.Count; i++)
        {
            if (productChecklist.Items[i] is Product p && p.Id == id)
            {
                productChecklist.SelectedIndex = i;
                return;
            }
        }
    }

    private List<(Product Product, double Quantity)> GetCheckedProducts(bool validate = true)
    {
        var result = new List<(Product Product, double Quantity)>();
        for (var i = 0; i < productChecklist.Items.Count; i++)
        {
            if (!productChecklist.GetItemChecked(i) || productChecklist.Items[i] is not Product p) continue;
            var q = quantities.TryGetValue(p.Id, out var value) ? value : 1;
            if (q <= 0) q = 1;
            if (validate && p.UsesInventory && q > p.Stock + 0.000001)
                throw new InvalidOperationException($"La cantidad de {p.Description} ({q:N3}) supera el stock disponible ({p.Stock:N3}).");
            result.Add((p, q));
        }
        return result;
    }

    private void UpdateTotals()
    {
        var regular = 0d;
        try { regular = GetCheckedProducts(false).Sum(x => x.Product.SalePrice * x.Quantity); } catch { }
        normalTotal.Text = $"PRECIO NORMAL ACTUAL: ${regular:N2}";
        promoTotal.Text = $"PRECIO FINAL DEL COMBO: ${promoPrice.Value:N2}";
    }

    private void LoadData(long? selectId = null)
    {
        try
        {
            using var cn = Database.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = @"SELECT id AS ID,
                                       COALESCE(name,'') AS NOMBRE,
                                       COALESCE(description,'') AS DESCRIPCION,
                                       CASE WHEN COALESCE(promotion_price,0) > 0 THEN promotion_price ELSE COALESCE(amount,0) END AS PRECIO,
                                       1 AS ACTIVA
                                FROM promotions
                                WHERE COALESCE(type,'PACK')='PACK'
                                ORDER BY id DESC";
            using var reader = cmd.ExecuteReader();
            var dt = new System.Data.DataTable();
            dt.Load(reader);
            grid.DataSource = null;
            grid.DataSource = dt;

            // Las columnas son explícitas (todas TextBox); solo reforzamos visibilidad
            // después de cada recarga para instalaciones que ya tenían una configuración
            // anterior guardada en memoria.
            if (grid.Columns.Contains("ID")) grid.Columns["ID"].Visible = false;
            if (grid.Columns.Contains("DESCRIPCION")) grid.Columns["DESCRIPCION"].Visible = false;
            if (grid.Columns.Contains("ACTIVA")) grid.Columns["ACTIVA"].Visible = false;

            if (grid.Rows.Count == 0)
            {
                grid.ClearSelection();
                ClearForm(false);
                return;
            }

            var target = selectId.GetValueOrDefault();
            var rowIndex = 0;
            if (target > 0)
            {
                for (var i = 0; i < grid.Rows.Count; i++)
                {
                    if (Convert.ToInt64(grid.Rows[i].Cells["ID"].Value) == target)
                    {
                        rowIndex = i;
                        break;
                    }
                }
            }
            grid.ClearSelection();
            grid.Rows[rowIndex].Selected = true;
            grid.CurrentCell = grid.Rows[rowIndex].Cells["NOMBRE"];
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudieron cargar las promociones.\n\n" + ex.Message, "Promociones", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadSelected()
    {
        if (grid.CurrentRow?.Cells["ID"].Value == null) return;
        selectedId = Convert.ToInt64(grid.CurrentRow.Cells["ID"].Value);
        try
        {
            using var cn = Database.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = @"SELECT COALESCE(name,''),COALESCE(description,''),
                                       CASE WHEN COALESCE(promotion_price,0) > 0 THEN promotion_price ELSE COALESCE(amount,0) END
                                FROM promotions WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", selectedId);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return;
            name.Text = r.GetString(0);
            description.Text = r.GetString(1);
            promoPrice.Value = Math.Min(promoPrice.Maximum, Math.Max(promoPrice.Minimum, Convert.ToDecimal(r.GetDouble(2))));
            r.Close();

            quantities.Clear();
            using var q = cn.CreateCommand();
            q.CommandText = "SELECT product_id,quantity FROM promotion_items WHERE promotion_id=$id";
            q.Parameters.AddWithValue("$id", selectedId);
            using var rr = q.ExecuteReader();
            while (rr.Read()) quantities[rr.GetInt32(0)] = rr.GetDouble(1);

            loadingChecks = true;
            try
            {
                for (var i = 0; i < productChecklist.Items.Count; i++)
                {
                    var check = productChecklist.Items[i] is Product p && quantities.ContainsKey(p.Id);
                    productChecklist.SetItemCheckState(i, check ? CheckState.Checked : CheckState.Unchecked);
                }
            }
            finally { loadingChecks = false; }

            RefreshSelectedSummary();
            status.Text = $"PROMOCIÓN: {name.Text} · ENTER / DOBLE CLICK = AGREGAR A LA VENTA · MODIFICAR desde el botón correspondiente.";
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo cargar la promoción seleccionada.\n\n" + ex.Message, "Promociones", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }


    private void ModifySelected()
    {
        if (grid.CurrentRow?.Cells["ID"].Value == null)
        {
            MessageBox.Show("Seleccioná una promoción para modificar.", "Promociones", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        LoadSelected();
        if (name.CanFocus)
        {
            name.Focus();
            name.SelectAll();
        }
    }

    private void ClearForm(bool focus = true)
    {
        selectedId = 0;
        name.Clear();
        description.Clear();
        promoPrice.Value = 0;
        quantities.Clear();
        loadingChecks = true;
        try
        {
            for (var i = 0; i < productChecklist.Items.Count; i++)
                productChecklist.SetItemCheckState(i, CheckState.Unchecked);
        }
        finally { loadingChecks = false; }
        grid.ClearSelection();
        RefreshSelectedSummary();
        status.Text = "NUEVA PROMOCIÓN: escribí el nombre y precio, después tildá los productos que llevará el combo.";
        if (focus) name.Focus();
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(name.Text))
        {
            MessageBox.Show("Ingresá un nombre para la promoción.\n\nEjemplo: PANCHO ESCOLAR", "Promociones", MessageBoxButtons.OK, MessageBoxIcon.Information);
            name.Focus();
            return;
        }
        if (promoPrice.Value <= 0)
        {
            MessageBox.Show("Ingresá el precio final que tendrá el combo.", "Promociones", MessageBoxButtons.OK, MessageBoxIcon.Information);
            promoPrice.Focus();
            return;
        }

        List<(Product Product, double Quantity)> checkedProducts;
        try { checkedProducts = GetCheckedProducts(); }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Stock insuficiente", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (checkedProducts.Count == 0)
        {
            MessageBox.Show("Tildá al menos un producto para formar la promoción.", "Promociones", MessageBoxButtons.OK, MessageBoxIcon.Information);
            productChecklist.Focus();
            return;
        }

        var regular = checkedProducts.Sum(x => x.Product.SalePrice * x.Quantity);
        if ((double)promoPrice.Value > regular + 0.000001 && MessageBox.Show("El precio del combo es mayor que el precio normal de los productos. ¿Querés guardarlo igual?", "Promoción", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        var isNew = selectedId == 0;
        var nameForAudit = name.Text.Trim();
        try
        {
            using var cn = Database.Open();
            using var tx = cn.BeginTransaction();
            long id;
            using (var cmd = cn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = isNew
                    ? "INSERT INTO promotions(name,description,type,amount,promotion_price,active,start_at,end_at) VALUES($n,$d,'PACK',$a,$pp,1,NULL,NULL); SELECT last_insert_rowid();"
                    : "UPDATE promotions SET name=$n,description=$d,type='PACK',amount=$a,promotion_price=$pp,active=1,start_at=NULL,end_at=NULL WHERE id=$id";
                cmd.Parameters.AddWithValue("$n", nameForAudit);
                cmd.Parameters.AddWithValue("$d", description.Text.Trim());
                cmd.Parameters.AddWithValue("$a", Convert.ToDouble(promoPrice.Value));
                cmd.Parameters.AddWithValue("$pp", Convert.ToDouble(promoPrice.Value));
                if (isNew) id = Convert.ToInt64(cmd.ExecuteScalar());
                else
                {
                    cmd.Parameters.AddWithValue("$id", selectedId);
                    cmd.ExecuteNonQuery();
                    id = selectedId;
                }
            }

            using (var del = cn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM promotion_items WHERE promotion_id=$id";
                del.Parameters.AddWithValue("$id", id);
                del.ExecuteNonQuery();
            }
            foreach (var item in checkedProducts)
            {
                using var ins = cn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = "INSERT INTO promotion_items(promotion_id,product_id,quantity) VALUES($id,$p,$q)";
                ins.Parameters.AddWithValue("$id", id);
                ins.Parameters.AddWithValue("$p", item.Product.Id);
                ins.Parameters.AddWithValue("$q", item.Quantity);
                ins.ExecuteNonQuery();
            }
            tx.Commit();

            selectedId = id;
            AuditService.Log(Session.UserId, isNew ? "PROMOTION_CREATE" : "PROMOTION_UPDATE", "PROMOCIONES", nameForAudit);
            LoadData(id);
            LoadSelected();
            status.Text = $"PROMOCIÓN '{nameForAudit}' guardada con {checkedProducts.Count} producto(s). Ya podés modificarla o incluirla en una venta.";
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo guardar la promoción.\n\n" + ex.Message, "Promociones", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Delete()
    {
        if (!TryGetSelectedId()) return;

        var promotionName = name.Text.Trim();
        if (string.IsNullOrWhiteSpace(promotionName)) promotionName = $"ID {selectedId}";

        using var reasonForm = new InputBoxForm(
            "Motivo obligatorio · Eliminar promoción",
            "",
            $"Promoción: {promotionName}\n\nIndicá el motivo por el que se elimina. Este motivo quedará registrado en la auditoría y en el cierre final de caja:");
        ThemeService.Apply(reasonForm);
        if (reasonForm.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(reasonForm.Value))
        {
            MessageBox.Show(this, "La eliminación fue cancelada: el motivo es obligatorio.", "Eliminar promoción", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var reason = reasonForm.Value.Trim();
        if (MessageBox.Show($"¿Eliminar la promoción '{promotionName}'?\n\nMotivo: {reason}\n\nTambién se eliminarán sus productos asociados.", "Confirmar eliminación", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        try
        {
            using var cn = Database.Open();
            using var tx = cn.BeginTransaction();
            using (var items = cn.CreateCommand())
            {
                items.Transaction = tx;
                items.CommandText = "DELETE FROM promotion_items WHERE promotion_id=$id";
                items.Parameters.AddWithValue("$id", selectedId);
                items.ExecuteNonQuery();
            }
            using (var promo = cn.CreateCommand())
            {
                promo.Transaction = tx;
                promo.CommandText = "DELETE FROM promotions WHERE id=$id";
                promo.Parameters.AddWithValue("$id", selectedId);
                promo.ExecuteNonQuery();
            }
            tx.Commit();
            AuditService.Log(Session.UserId, "PROMOTION_DELETE", "PROMOCIONES", $"Promoción '{promotionName}' (ID {selectedId}) eliminada desde Windows · Motivo: {reason}");
            ClearForm();
            LoadData();
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo eliminar la promoción.\n\n" + ex.Message, "Promociones", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool TryGetSelectedId()
    {
        if (selectedId != 0) return true;
        if (grid.CurrentRow?.Cells["ID"].Value != null)
        {
            selectedId = Convert.ToInt64(grid.CurrentRow.Cells["ID"].Value);
            LoadSelected();
            return selectedId != 0;
        }
        MessageBox.Show("Seleccioná una promoción de la lista.", "Promociones", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return false;
    }

    private void UseSelected()
    {
        if (!TryGetSelectedId()) return;
        try
        {
            var promotion = PromotionService.GetById(selectedId, onlyActive: true);
            if (promotion == null)
            {
                MessageBox.Show("La promoción está inactiva, fuera de vigencia o no existe.", "Promociones", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            SelectedItems = PromotionService.BuildCartItems(promotion);
            if (SelectedItems.Count == 0)
            {
                MessageBox.Show("La promoción todavía no tiene productos. Agregalos antes de venderla.", "Promociones", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se puede incluir la promoción en la venta.\n\n" + ex.Message, "Promociones", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Enter && grid.Focused)
        {
            UseSelected();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
