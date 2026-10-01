using FerrarisPOS.Data;
using FerrarisPOS.Models;
using FerrarisPOS.Services;
using System.Globalization;

namespace FerrarisPOS.Forms;

public class ProductsForm : Form
{
    private readonly DataGridView grid = new();
    private TextBox barcode = new(), description = new(), cost = new(), price = new(), wholesale = new(), stock = new(), min = new(), category = new(), unit = new(), customProfit = new();
    private readonly ComboBox profitPercent = new();
    private readonly Label profitLiveLabel = new();
    private readonly Button addIvaButton = new();
    private readonly Button roundPriceButton = new();
    private readonly ComboBox supplier = new();
    private readonly Button supplierPlus = new();
    private bool syncingProfitSelector;
    private bool updatingCalculatedPrice;
    private bool addsIva21;
    private bool roundSaleTo5;
    private TextBox searchBox = new();
    private readonly CheckBox bulk = new() { Text = "SE VENDE POR GRANEL (PRECIO POR KILO)", AutoSize = true };
    private readonly CheckBox noInventory = new() { Text = "ESTE PRODUCTO NO USA INVENTARIO", AutoSize = true };
    private int id;
    private double originalStock;
    private readonly bool selectMode;
    public Product? SelectedProduct { get; private set; }

    public ProductsForm(bool selectMode = false)
    {
        this.selectMode = selectMode;
        Text = selectMode ? "FerrarisPOS - Buscar producto" : "FerrarisPOS - Productos";
        Width = 1320; Height = 960; MinimumSize = new Size(1180, 860); AutoScroll = true; BackColor = Color.Gainsboro;
        KeyPreview = true;
        Build();
        ThemeService.Apply(this);
        KeyDown += ProductsFormKeyDown;
        if (selectMode) LoadSearchGrid("");
        else
        {
            LoadGrid();
            Shown += (_, _) =>
            {
                barcode.Focus();
                barcode.SelectAll();
            };
        }
    }

    private TextBox Field(string label, int y, string hint)
    {
        Controls.Add(new Label { Text = label, Location = new Point(20, y + 5), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        var t = new TextBox { Location = new Point(210, y), Width = 300, BackColor = Color.White, PlaceholderText = hint };
        Controls.Add(t); return t;
    }

    private Button Btn(string text, int x, int y, EventHandler h)
    {
        var b = new Button { Text = text, Location = new Point(x, y), Width = 145, Height = 40, BackColor = Color.White };
        b.Click += h; Controls.Add(b); return b;
    }

    private void Build()
    {
        if (selectMode)
        {
            BuildSelector();
            return;
        }

        Controls.Add(new Label { Text = "ALTA Y EDICIÓN DE PRODUCTOS", Location = new Point(20, 15), AutoSize = true, Font = new Font("Segoe UI", 16, FontStyle.Bold) });
        barcode = Field("CÓDIGO DE BARRAS", 55, "Ej.: 7501234567890");

        // El lector de códigos de barras normalmente funciona como teclado y
        // termina el escaneo con ENTER. Si el código ya existe, cargar el
        // producto completo en modo edición en lugar de crear uno nuevo.
        barcode.KeyDown += Barcode_KeyDown;
        barcode.Leave += (_, _) => LoadProductByBarcode(false);

        description = Field("DESCRIPCIÓN DEL PRODUCTO", 100, "Nombre que aparecerá en la venta");
        cost = Field("PRECIO DE COSTO", 145, "Ej.: 6,50");
        cost.TextChanged += (_, _) => CostChangedLive();

        // Porcentaje de ganancia: opcional. Si queda en "SIN CAMBIO", el precio
        // de venta se conserva exactamente como antes. Al elegir un porcentaje,
        // se calcula PRECIO DE VENTA = COSTO + porcentaje de ganancia.
        Controls.Add(new Label
        {
            Text = "GANANCIA %",
            Location = new Point(20, 195),
            AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        });
        profitPercent.Location = new Point(210, 190);
        profitPercent.Width = 140;
        profitPercent.DropDownStyle = ComboBoxStyle.DropDownList;
        profitPercent.Items.Add("SIN CAMBIO");
        for (int p = 10; p <= 100; p += 10) profitPercent.Items.Add(p.ToString(CultureInfo.InvariantCulture) + "%");
        profitPercent.SelectedIndex = 0;
        profitPercent.SelectedIndexChanged += (_, _) => ApplyProfitPercent();
        Controls.Add(profitPercent);

        customProfit = new TextBox
        {
            Location = new Point(360, 190),
            Width = 150,
            PlaceholderText = "Otro % (ej.: 37,5)",
            BackColor = Color.White
        };
        customProfit.TextChanged += (_, _) => ApplyCustomProfit();
        Controls.Add(customProfit);

        profitLiveLabel.Text = "GANANCIA ACTUAL: 0,00%";
        profitLiveLabel.Location = new Point(20, 218);
        profitLiveLabel.AutoSize = true;
        profitLiveLabel.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        profitLiveLabel.ForeColor = Color.DimGray;
        Controls.Add(profitLiveLabel);

        price = Field("PRECIO DE VENTA", 235, "Ej.: 10,00");
        price.TextChanged += (_, _) => PriceChangedLive();
        wholesale = Field("PRECIO DE MAYOREO", 280, "Precio con tecla INSERT");
        stock = Field("EXISTENCIA / STOCK", 325, "Cantidad actual");
        min = Field("STOCK MÍNIMO", 370, "Aviso de reposición");
        category = Field("CATEGORÍA / DEPARTAMENTO", 415, "Elegí una categoría creada");
        category.Width = 245;
        category.ReadOnly = true;
        var categoryButton = new Button
        {
            Text = "▼",
            Location = new Point(465, 415),
            Width = 42,
            Height = 28,
            BackColor = Color.White,
            Cursor = Cursors.Hand,
            TabStop = false
        };
        categoryButton.Click += (_, _) => OpenCategorySelector();
        Controls.Add(categoryButton);
        var categoryPlus = new Button
        {
            Text = "＋",
            Location = new Point(512, 410),
            Width = 42,
            Height = 38,
            BackColor = Color.White,
            Font = new Font("Segoe UI", 16, FontStyle.Bold),
            Cursor = Cursors.Hand,
            TabStop = false
        };
        categoryPlus.Click += (_, _) => CreateCategoryFromProduct();
        Controls.Add(categoryPlus);
        var categoryTip = new ToolTip();
        categoryTip.SetToolTip(categoryButton, "Ver todas las categorías creadas");
        categoryTip.SetToolTip(categoryPlus, "Crear una categoría nueva");
        category.DoubleClick += (_, _) => OpenCategorySelector();

        unit = Field("UNIDAD", 460, "UN, KG, LT..."); unit.Text = "UN";
        bulk.Location = new Point(210, 500);
        bulk.CheckedChanged += (_, _) => { if (bulk.Checked) unit.Text = "KG"; };
        Controls.Add(bulk);
        noInventory.Location = new Point(210, 525);
        noInventory.Enabled = InventoryControlService.IsGlobalEnabled;
        noInventory.CheckedChanged += (_, _) => UpdateInventoryFieldsState();
        Controls.Add(noInventory);
        UpdateInventoryFieldsState();

        // El proveedor queda inmediatamente debajo de las opciones de granel/inventario.
        // Se evita el hueco vertical que había entre los campos del producto y el selector.
        Controls.Add(new Label { Text = "PROVEEDOR", Location = new Point(20, 558), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        supplier.Location = new Point(210, 552);
        supplier.Width = 300;
        supplier.Height = 32;
        supplier.DropDownStyle = ComboBoxStyle.DropDownList;
        supplier.DropDownWidth = 360;
        supplier.MaxDropDownItems = 18;
        supplier.FormattingEnabled = true;
        Controls.Add(supplier);
        supplierPlus.Text = "+";
        supplierPlus.Location = new Point(515, 550);
        supplierPlus.Width = 42;
        supplierPlus.Height = 36;
        supplierPlus.Font = new Font("Segoe UI", 16, FontStyle.Bold);
        supplierPlus.BackColor = Color.White;
        supplierPlus.Cursor = Cursors.Hand;
        supplierPlus.Click += (_, _) => CreateSupplierFromProduct();
        Controls.Add(supplierPlus);
        var supplierTip = new ToolTip();
        supplierTip.SetToolTip(supplier, "Seleccioná el proveedor que suministra este producto");
        supplierTip.SetToolTip(supplierPlus, "Crear un proveedor nuevo");
        LoadSuppliers();

        addIvaButton.Text = "SUMAR IVA 21% AL COSTO";
        addIvaButton.Location = new Point(190, 620);
        addIvaButton.Width = 145;
        addIvaButton.Height = 34;
        addIvaButton.BackColor = Color.White;
        addIvaButton.Cursor = Cursors.Hand;
        var ivaTip = new ToolTip();
        ivaTip.SetToolTip(addIvaButton, "Agrega o quita el 21% exclusivamente al PRECIO DE COSTO. El precio de venta lo determina la GANANCIA %." );
        addIvaButton.Click += (_, _) => ToggleIva21();
        Controls.Add(addIvaButton);

        roundPriceButton.Text = "REDONDEO A 0 / 5";
        roundPriceButton.Location = new Point(360, 620);
        roundPriceButton.Width = 145;
        roundPriceButton.Height = 34;
        roundPriceButton.BackColor = Color.White;
        roundPriceButton.Cursor = Cursors.Hand;
        roundPriceButton.Click += (_, _) => ToggleRoundSaleTo5();
        Controls.Add(roundPriceButton);

        // Botones de gestión organizados en filas compactas debajo de los campos.
        Btn("NUEVO", 20, 680, (_, _) => Clear());
        Btn("GUARDAR", 190, 680, (_, _) => Save());
        Btn("ELIMINAR", 360, 680, (_, _) => Delete());

        var modifiers = new Button { Text = "MODIFICADORES", Location = new Point(20, 725), Width = 160, Height = 40, BackColor = Color.White };
        modifiers.Click += (_, _) => { if (id <= 0) { MessageBox.Show("Seleccioná y guardá un producto antes de administrar sus modificadores."); return; } using var f = new ProductModifierForm(id, description.Text); ThemeService.Apply(f); f.ShowDialog(this); };
        Controls.Add(modifiers);

        var favorite = new Button { Text = "☆ FAVORITO", Location = new Point(190, 725), Width = 160, Height = 40, BackColor = Color.White };
        favorite.Click += (_, _) => { if (id <= 0) { MessageBox.Show("Seleccioná un producto."); return; } FavoriteService.Toggle(id); favorite.Text = FavoriteService.IsFavorite(id) ? "★ FAVORITO" : "☆ FAVORITO"; };
        Controls.Add(favorite);

        var barcodeAssign = new Button
        {
            Text = "▣ ASIGNAR CÓDIGO DE BARRAS",
            Location = new Point(360, 725),
            Width = 170,
            Height = 40,
            BackColor = Color.White
        };
        barcodeAssign.Click += (_, _) =>
        {
            using var f = new BarcodeAssignmentForm(id > 0 ? id : null);
            ThemeService.Apply(f);
            if (f.ShowDialog(this) == DialogResult.OK)
            {
                LoadGrid();
                SelectProductInGrid(id);
                if (id > 0)
                {
                    var current = ProductService.Search().FirstOrDefault(p => p.Id == id);
                    if (current is not null)
                    {
                        barcode.Text = current.Barcode;
                        description.Text = current.Description;
                    }
                }
            }
        };
        Controls.Add(barcodeAssign);

        var barcodeGenerate = new Button
        {
            Text = "✦ CREAR CÓDIGO INTERNO",
            Location = new Point(20, 775),
            Width = 160,
            Height = 42,
            BackColor = Color.White
        };
        barcodeGenerate.Click += (_, _) =>
        {
            using var f = new BarcodeGeneratorForm(id > 0 ? id : null);
            ThemeService.Apply(f);
            if (f.ShowDialog(this) == DialogResult.OK)
            {
                LoadGrid();
                SelectProductInGrid(id);
                if (id > 0)
                {
                    var current = ProductService.Search().FirstOrDefault(p => p.Id == id);
                    if (current is not null) barcode.Text = current.Barcode;
                }
            }
        };
        Controls.Add(barcodeGenerate);

        // IMPORTAR / EXPORTAR INVENTARIO: exclusivos del usuario ADMINISTRADOR.
        // No se modifica el resto de botones ni las demás solapas.
        if (Session.IsAdmin)
        {
            var export = new Button
            {
                Text = "EXPORTAR INVENTARIO",
                Location = new Point(190, 775),
                Width = 160,
                Height = 42,
                BackColor = Color.White
            };
            export.Click += (_, _) => ExportInventory();
            Controls.Add(export);

            var import = new Button
            {
                Text = "IMPORTAR INVENTARIO",
                Location = new Point(360, 775),
                Width = 170,
                Height = 42,
                BackColor = Color.White
            };
            import.Click += (_, _) => ImportInventory();
            Controls.Add(import);
        }

        var searchProducts = new Button
        {
            Text = "F10 · BUSCAR PRODUCTO",
            Location = new Point(20, 620),
            Width = 160,
            Height = 42,
            BackColor = Color.White
        };
        searchProducts.Click += (_, _) => OpenProductSearchForEdit();
        Controls.Add(searchProducts);

        grid.Location = new Point(780, 30); grid.Size = new Size(520, 650);
        grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        grid.ReadOnly = true; grid.AutoGenerateColumns = true; grid.BackgroundColor = Color.White;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.DoubleBuffered(true); grid.SelectionChanged += Selected;
        grid.CellDoubleClick += (_, _) => { if (selectMode) SelectCurrent(); };
        Controls.Add(grid);
    }

    private void BuildSelector()
    {
        Controls.Add(new Label
        {
            Text = "BUSCAR PRODUCTO PARA LA VENTA",
            Location = new Point(25, 18), AutoSize = true,
            Font = new Font("Segoe UI", 18, FontStyle.Bold)
        });

        Controls.Add(new Label
        {
            Text = "Escaneá un código de barras para localizarlo o escribí las primeras letras de la descripción. ↑ ↓ para seleccionar · ENTER para agregar · ESC para volver.",
            Location = new Point(25, 55), AutoSize = true,
            Font = new Font("Segoe UI", 10), ForeColor = Color.DimGray
        });

        searchBox = new TextBox
        {
            Location = new Point(25, 88), Width = 900, Height = 42,
            Font = new Font("Segoe UI", 17), BackColor = Color.White,
            PlaceholderText = "Escaneá código de barras o buscá producto..."
        };
        searchBox.TextChanged += (_, _) => LoadSearchGrid(searchBox.Text);
        searchBox.KeyDown += SearchKeyDown;
        Controls.Add(searchBox);

        var close = new Button
        {
            Text = "ESC · CANCELAR", Location = new Point(945, 88),
            Width = 180, Height = 42, BackColor = Color.White
        };
        close.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(close);

        grid.Location = new Point(25, 150);
        grid.Size = new Size(1100, 525);
        grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        grid.Dock = DockStyle.None;
        grid.ReadOnly = true;
        grid.AutoGenerateColumns = false;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.BackgroundColor = Color.White;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.RowHeadersVisible = false;
        grid.Font = new Font("Segoe UI", 11);
        grid.RowTemplate.Height = 34;
        grid.ColumnHeadersHeight = 38;
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SearchBarcode", HeaderText = "CÓDIGO DE BARRAS", DataPropertyName = "Barcode", Width = 220 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SearchDescription", HeaderText = "DESCRIPCIÓN", DataPropertyName = "Description", Width = 450 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SearchPrice", HeaderText = "PRECIO", DataPropertyName = "SalePrice", Width = 140, DefaultCellStyle = new DataGridViewCellStyle { Format = "N2", Alignment = DataGridViewContentAlignment.MiddleRight } });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SearchWholesale", HeaderText = "MAYOREO", DataPropertyName = "WholesalePrice", Width = 140, DefaultCellStyle = new DataGridViewCellStyle { Format = "N2", Alignment = DataGridViewContentAlignment.MiddleRight } });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "SearchStock", HeaderText = "STOCK", DataPropertyName = "Stock", Width = 110, DefaultCellStyle = new DataGridViewCellStyle { Format = "N2", Alignment = DataGridViewContentAlignment.MiddleRight } });
        grid.KeyDown += SearchKeyDown;
        grid.CellDoubleClick += (_, _) => SelectCurrent();
        Controls.Add(grid);

        Shown += (_, _) => { searchBox.Focus(); searchBox.SelectAll(); };
    }

    private void LoadSearchGrid(string search)
    {
        if (!selectMode) return;

        // F10 tiene dos modos, sin mezclar criterios:
        // 1) Si el texto coincide EXACTAMENTE con un código de barras, muestra
        //    ese único producto para que el operador pueda verlo y seleccionarlo.
        // 2) Si no es un código de barras exacto, busca EXCLUSIVAMENTE por
        //    PREFIJO de la DESCRIPCIÓN: C -> C..., CO -> CO..., COC -> COC...
        // Nunca se buscan coincidencias en el medio, proveedor o categoría.
        var typed = search.Trim();
        var byBarcode = typed.Length > 0 ? ProductService.FindByBarcode(typed) : null;
        var list = byBarcode is not null
            ? new List<Product> { byBarcode }
            : ProductService.SearchByDescriptionPrefix(typed);
        grid.DataSource = list;

        if (list.Count > 0)
        {
            grid.ClearSelection();
            grid.Rows[0].Selected = true;
            grid.CurrentCell = grid.Rows[0].Cells[0];
        }
    }

    private void SearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up)
        {
            MoveSelection(e.KeyCode == Keys.Down ? 1 : -1);
            e.Handled = true; e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Tab)
        {
            // ENTER confirma tanto una búsqueda por descripción como un
            // código de barras escaneado. Si el lector está configurado para
            // terminar con TAB, también queda soportado.
            SelectCurrent();
            e.Handled = true; e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            e.Handled = true; e.SuppressKeyPress = true;
        }
    }

    private void MoveSelection(int delta)
    {
        if (grid.Rows.Count == 0) return;
        var current = grid.CurrentRow?.Index ?? 0;
        var next = Math.Max(0, Math.Min(grid.Rows.Count - 1, current + delta));
        grid.CurrentCell = grid.Rows[next].Cells[0];
        grid.Rows[next].Selected = true;
    }

    private void ProductsFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (!selectMode && e.KeyCode == Keys.F10)
        {
            OpenProductSearchForEdit();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void Barcode_KeyDown(object? sender, KeyEventArgs e)
    {
        if (selectMode || e.KeyCode != Keys.Enter) return;

        // ENTER es el sufijo habitual de los lectores USB/Bluetooth.
        // Si el código existe, pasa inmediatamente a edición con todos los
        // campos completos. Si no existe, se conserva el código para permitir
        // dar de alta un producto nuevo.
        LoadProductByBarcode(true);
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private void LoadProductByBarcode(bool notifyIfNotFound)
    {
        if (selectMode || IsDisposed || Disposing) return;

        var code = barcode.Text.Trim();
        if (string.IsNullOrWhiteSpace(code)) return;

        try
        {
            var product = ProductService.FindByBarcode(code);
            if (product is null)
            {
                if (notifyIfNotFound)
                {
                    // No se limpia el código: queda listo para crear el nuevo
                    // producto con ese mismo código.
                    id = 0;
                    if (!string.IsNullOrWhiteSpace(description.Text) &&
                        MessageBox.Show(
                            $"No existe un producto con el código de barras {code}.\n\n¿Querés iniciar un producto nuevo con ese código?",
                            "Código de barras",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question) == DialogResult.No)
                    {
                        return;
                    }
                }
                return;
            }

            // Producto encontrado: cargar TODO (costo, venta, mayoreo, stock,
            // mínimo, categoría, unidad, granel e inventario) y mantener su ID
            // para que GUARDAR ejecute UPDATE y no INSERT.
            LoadProductForEditing(product);
            SelectProductInGrid(product.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "No se pudo buscar el código de barras:\n" + ex.Message,
                "Productos",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void OpenProductSearchForEdit()
    {
        using var f = new ProductDescriptionSearchForm();
        ThemeService.Apply(f);
        if (f.ShowDialog(this) == DialogResult.OK && f.SelectedProduct is { } product)
        {
            LoadProductForEditing(product);
            SelectProductInGrid(product.Id);
        }
    }

    private void LoadProductForEditing(Product product)
    {
        id = product.Id;
        originalStock = product.Stock;
        barcode.Text = product.Barcode;
        description.Text = product.Description;
        cost.Text = product.CostPrice.ToString("N2");
        price.Text = product.SalePrice.ToString("N2");
        addsIva21 = ProductService.GetAddsIva21(product.Id);
        roundSaleTo5 = ProductService.GetRoundSaleTo5(product.Id);
        SetIvaButtonState();
        SetRoundButtonState();
        SetProfitSelectorFromProduct(product.CostPrice, product.SalePrice);
        wholesale.Text = product.WholesalePrice.ToString("N2");
        stock.Text = product.Stock.ToString("N2");
        min.Text = product.MinStock.ToString("N2");
        category.Text = product.Category;
        unit.Text = product.Unit;
        bulk.Checked = product.IsBulk;
        noInventory.Checked = !product.UsesInventory;
        UpdateInventoryFieldsState();
        SelectSupplierForProduct(product.Id);
        noInventory.Enabled = InventoryControlService.IsGlobalEnabled;
        description.Focus();
        description.SelectAll();
    }

    private void SelectProductInGrid(int productId)
    {
        if (productId <= 0 || grid.Rows.Count == 0) return;
        for (var i = 0; i < grid.Rows.Count; i++)
        {
            if (grid.Rows[i].DataBoundItem is Product p && p.Id == productId)
            {
                grid.ClearSelection();
                grid.Rows[i].Selected = true;
                grid.CurrentCell = grid.Rows[i].Cells[0];
                if (i >= 0 && i < grid.RowCount)
                    grid.FirstDisplayedScrollingRowIndex = Math.Max(0, Math.Min(i, grid.RowCount - 1));
                return;
            }
        }
    }

    private void LoadGrid(string search = "")
    {
        grid.DataSource = ProductService.Search(search);
    }

    private void Selected(object? s, EventArgs e)
    {
        if (grid.CurrentRow?.DataBoundItem is Product p)
        {
            id = p.Id; originalStock = p.Stock; barcode.Text = p.Barcode; description.Text = p.Description;
            cost.Text = p.CostPrice.ToString("N2"); price.Text = p.SalePrice.ToString("N2");
            wholesale.Text = p.WholesalePrice.ToString("N2"); stock.Text = p.Stock.ToString("N2");
            min.Text = p.MinStock.ToString("N2"); category.Text = p.Category; unit.Text = p.Unit; bulk.Checked = p.IsBulk; noInventory.Checked = !p.UsesInventory; UpdateInventoryFieldsState(); SelectSupplierForProduct(p.Id); noInventory.Enabled = InventoryControlService.IsGlobalEnabled;
            addsIva21 = ProductService.GetAddsIva21(p.Id); roundSaleTo5 = ProductService.GetRoundSaleTo5(p.Id); SetIvaButtonState(); SetRoundButtonState(); SetProfitSelectorFromProduct(p.CostPrice, p.SalePrice);
        }
    }

    private void Clear()
    {
        id = 0; originalStock = 0; barcode.Clear(); description.Clear(); cost.Clear(); price.Clear();
        wholesale.Clear(); stock.Clear(); min.Clear(); category.Clear(); unit.Text = "UN"; bulk.Checked = false; noInventory.Checked = !InventoryControlService.DefaultUsesInventory;
        profitPercent.SelectedIndex = 0; customProfit.Clear(); LoadSuppliers(0); UpdateInventoryFieldsState(); addsIva21 = false; roundSaleTo5 = false; SetIvaButtonState(); SetRoundButtonState(); UpdateProfitLabel(); noInventory.Enabled = InventoryControlService.IsGlobalEnabled; barcode.Focus();
    }

    private void CreateCategoryFromProduct()
    {
        using var form = new CategorySelectorForm(category.Text, true);
        ThemeService.Apply(form);
        if (form.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(form.SelectedCategory))
            category.Text = form.SelectedCategory;
    }

    private void OpenCategorySelector()
    {
        using var form = new CategorySelectorForm(category.Text);
        ThemeService.Apply(form);

        if (form.ShowDialog(this) == DialogResult.OK &&
            !string.IsNullOrWhiteSpace(form.SelectedCategory))
        {
            category.Text = form.SelectedCategory;
            category.Focus();
            category.SelectAll();
        }
    }

    private void ApplyProfitPercent()
    {
        if (syncingProfitSelector || profitPercent.SelectedIndex <= 0) return;
        var raw = profitPercent.SelectedItem?.ToString()?.Replace("%", "") ?? "";
        if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var pct))
            ApplyProfitToPrice(pct);
        customProfit.Clear();
    }

    private void ApplyCustomProfit()
    {
        if (syncingProfitSelector || profitPercent.SelectedIndex > 0 || string.IsNullOrWhiteSpace(customProfit.Text))
        {
            UpdateProfitLabel();
            return;
        }
        var raw = customProfit.Text.Trim().Replace("%", "").Replace(',', '.');
        if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var pct)
            && pct >= 0 && pct <= 1000)
            ApplyProfitToPrice(pct);
        else
            UpdateProfitLabel();
    }

    private void CostChangedLive()
    {
        if (updatingCalculatedPrice || syncingProfitSelector) return;

        // Si el usuario eligió un porcentaje, el precio se recalcula mientras escribe el costo.
        if (profitPercent.SelectedIndex > 0)
        {
            var raw = profitPercent.SelectedItem?.ToString()?.Replace("%", "") ?? "";
            if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var pct))
                ApplyProfitToPrice(pct);
        }
        else if (!string.IsNullOrWhiteSpace(customProfit.Text))
        {
            var raw = customProfit.Text.Trim().Replace("%", "").Replace(',', '.');
            if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var pct) && pct >= 0 && pct <= 1000)
                ApplyProfitToPrice(pct);
            else
                UpdateProfitLabel();
        }
        else
        {
            UpdateProfitLabel();
        }
    }

    private void PriceChangedLive()
    {
        if (updatingCalculatedPrice) return;

        // Si este producto tiene redondeo permanente, cualquier precio escrito
        // manualmente se lleva inmediatamente al múltiplo de $5 más cercano.
        if (roundSaleTo5)
        {
            var value = Num(price);
            if (value > 0)
            {
                var rounded = RoundToFive(value);
                if (Math.Abs(rounded - value) > 0.000001)
                {
                    updatingCalculatedPrice = true;
                    try { price.Text = rounded.ToString("N2"); }
                    finally { updatingCalculatedPrice = false; }
                }
            }
        }

        UpdateProfitLabel();
    }

    private void ApplyProfitToPrice(double pct)
    {
        var costValue = Num(cost);
        if (costValue <= 0)
        {
            UpdateProfitLabel();
            return;
        }

        // El IVA 21% pertenece al COSTO, no al precio de venta.
        // El porcentaje de ganancia es el único que determina el precio de venta.
        // Si IVA está activo, costValue ya contiene el 21% agregado por ToggleIva21.
        var finalSale = costValue * (1 + pct / 100.0);
        finalSale = NormalizeSalePrice(finalSale);

        updatingCalculatedPrice = true;
        try
        {
            price.Text = finalSale.ToString("N2");
        }
        finally
        {
            updatingCalculatedPrice = false;
        }
        UpdateProfitLabel();
    }

    private void UpdateProfitLabel()
    {
        if (profitLiveLabel is null || cost is null || price is null) return;
        try
        {
            var costValue = Num(cost);
            var saleValue = Num(price);
            if (costValue <= 0 || saleValue <= 0)
            {
                profitLiveLabel.Text = "GANANCIA ACTUAL: 0,00%";
                return;
            }

            // El costo mostrado ya representa el costo efectivo: con IVA activo
            // contiene el 21%. La ganancia se calcula siempre contra ese costo.
            var pct = ((saleValue / costValue) - 1.0) * 100.0;
            profitLiveLabel.Text = $"GANANCIA ACTUAL: {pct:0.00}%";
        }
        catch
        {
            profitLiveLabel.Text = "GANANCIA ACTUAL: —";
        }
    }

    private void SetProfitSelectorFromProduct(double costValue, double saleValue)
    {
        syncingProfitSelector = true;
        try
        {
            profitPercent.SelectedIndex = 0;
            customProfit.Clear();

            if (costValue <= 0) { UpdateProfitLabel(); return; }
            var pct = Math.Round((saleValue / costValue - 1) * 100, 2);
            for (int i = 1; i < profitPercent.Items.Count; i++)
            {
                var item = profitPercent.Items[i]?.ToString()?.Replace("%", "");
                if (double.TryParse(item, NumberStyles.Any, CultureInfo.InvariantCulture, out var option)
                    && Math.Abs(option - pct) < 0.01)
                {
                    profitPercent.SelectedIndex = i;
                    return;
                }
            }

            if (Math.Abs(pct) > 0.001)
                customProfit.Text = pct.ToString("0.##", CultureInfo.InvariantCulture);
        }
        finally
        {
            syncingProfitSelector = false;
            UpdateProfitLabel();
        }
    }

    private void ToggleIva21()
    {
        var currentCost = Num(cost);
        if (currentCost <= 0)
        {
            MessageBox.Show("Ingresá primero un PRECIO DE COSTO para aplicar el IVA del 21%.", "IVA 21%", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        updatingCalculatedPrice = true;
        try
        {
            if (!addsIva21)
            {
                // El IVA se agrega EXCLUSIVAMENTE al precio de costo.
                addsIva21 = true;
                cost.Text = (currentCost * 1.21).ToString("N2");
            }
            else
            {
                // Al desactivar, quitamos únicamente el IVA del costo.
                addsIva21 = false;
                cost.Text = (currentCost / 1.21).ToString("N2");
            }
        }
        finally
        {
            updatingCalculatedPrice = false;
        }

        // El precio de venta se modifica únicamente por el % de ganancia.
        // Si hay un porcentaje seleccionado, recalculamos inmediatamente.
        if (profitPercent.SelectedIndex > 0)
        {
            var raw = profitPercent.SelectedItem?.ToString()?.Replace("%", "") ?? "";
            if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var pct))
                ApplyProfitToPrice(pct);
        }
        else if (!string.IsNullOrWhiteSpace(customProfit.Text))
        {
            var raw = customProfit.Text.Trim().Replace("%", "").Replace(',', '.');
            if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var pct) && pct >= 0 && pct <= 1000)
                ApplyProfitToPrice(pct);
            else
                UpdateProfitLabel();
        }
        else
        {
            UpdateProfitLabel();
        }

        SetIvaButtonState();
    }

    private void ToggleRoundSaleTo5()
    {
        roundSaleTo5 = !roundSaleTo5;
        if (roundSaleTo5)
        {
            var currentSale = Num(price);
            if (currentSale > 0)
            {
                updatingCalculatedPrice = true;
                try { price.Text = RoundToFive(currentSale).ToString("N2"); }
                finally { updatingCalculatedPrice = false; }
            }
        }

        SetRoundButtonState();
        UpdateProfitLabel();
    }

    private void SetIvaButtonState()
    {
        addIvaButton.Text = addsIva21 ? "✓ IVA 21% EN COSTO" : "SUMAR IVA 21% AL COSTO";
        addIvaButton.Enabled = true;
    }

    private void SetRoundButtonState()
    {
        roundPriceButton.Text = roundSaleTo5 ? "✓ REDONDEO 0 / 5" : "REDONDEO A 0 / 5";
        roundPriceButton.Enabled = true;
    }

    private double NormalizeSalePrice(double value)
        => roundSaleTo5 ? RoundToFive(value) : value;

    private static double RoundToFive(double value)
    {
        if (value <= 0) return value;
        // 397,55 -> 400 y 124,22 -> 125.
        return Math.Round(value / 5.0, MidpointRounding.AwayFromZero) * 5.0;
    }

    private static double Num(TextBox t)
    {
        var raw = (t.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw)) return 0;

        var lastComma = raw.LastIndexOf(',');
        var lastDot = raw.LastIndexOf('.');

        if (lastComma >= 0 && lastDot >= 0)
        {
            // El separador que aparece último se interpreta como decimal.
            if (lastComma > lastDot)
                raw = raw.Replace(".", string.Empty).Replace(',', '.');
            else
                raw = raw.Replace(",", string.Empty);
        }
        else if (lastComma >= 0)
        {
            raw = raw.Replace(',', '.');
        }

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return value;

        // Nunca guardar silenciosamente 0 si el usuario escribió un número
        // inválido: es preferible abortar el guardado y conservar los datos.
        throw new FormatException($"El valor numérico '{t.Text}' no es válido.");
    }

    private void LoadSuppliers(int selectedId = 0)
    {
        try
        {
            using var cn = Database.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT id, name FROM suppliers WHERE active=1 ORDER BY name";
            var list = new List<SupplierItem> { new(0, "SIN PROVEEDOR") };
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(new SupplierItem(r.GetInt32(0), r.IsDBNull(1) ? "" : r.GetString(1)));
            supplier.DataSource = list;
            supplier.DisplayMember = "Name";
            supplier.ValueMember = "Id";
            supplier.SelectedValue = selectedId;
            if (supplier.SelectedIndex < 0) supplier.SelectedIndex = 0;
        }
        catch { supplier.DataSource = new List<SupplierItem> { new(0, "SIN PROVEEDOR") }; supplier.DisplayMember = "Name"; supplier.ValueMember = "Id"; }
    }

    private int SelectedSupplierId =>
        supplier.SelectedValue is int value ? value :
        int.TryParse(Convert.ToString(supplier.SelectedValue), out var id) ? id : 0;

    private void SelectSupplierForProduct(int productId)
    {
        if (productId <= 0) { LoadSuppliers(0); return; }
        try
        {
            using var cn = Database.Open(); using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT supplier_id FROM supplier_products WHERE product_id=$pid ORDER BY unit_cost, supplier_id LIMIT 1";
            cmd.Parameters.AddWithValue("$pid", productId);
            var v = cmd.ExecuteScalar();
            LoadSuppliers(v is null ? 0 : Convert.ToInt32(v));
        }
        catch { LoadSuppliers(0); }
    }

    private void SaveProductSupplier(int productId)
    {
        if (productId <= 0) return;
        using var cn = Database.Open(); using var tx = cn.BeginTransaction();
        using (var del = cn.CreateCommand()) { del.Transaction = tx; del.CommandText = "DELETE FROM supplier_products WHERE product_id=$pid"; del.Parameters.AddWithValue("$pid", productId); del.ExecuteNonQuery(); }
        if (SelectedSupplierId > 0)
        {
            using var ins = cn.CreateCommand(); ins.Transaction = tx; ins.CommandText = "INSERT INTO supplier_products(supplier_id,product_id,unit_cost,created_at,updated_at) VALUES($sid,$pid,$cost,CURRENT_TIMESTAMP,CURRENT_TIMESTAMP) ON CONFLICT(supplier_id,product_id) DO UPDATE SET unit_cost=excluded.unit_cost,updated_at=CURRENT_TIMESTAMP";
            ins.Parameters.AddWithValue("$sid", SelectedSupplierId); ins.Parameters.AddWithValue("$pid", productId); ins.Parameters.AddWithValue("$cost", Num(cost)); ins.ExecuteNonQuery();
        }
        tx.Commit();
    }

    private void CreateSupplierFromProduct()
    {
        using var f = new Form { Text = "Nuevo proveedor", Width = 470, Height = 270, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false };
        var name = new TextBox { Left = 150, Top = 20, Width = 270 };
        var phone = new TextBox { Left = 150, Top = 60, Width = 270 };
        var email = new TextBox { Left = 150, Top = 100, Width = 270 };
        f.Controls.AddRange(new Control[] { new Label { Text = "NOMBRE / RAZÓN SOCIAL", Left = 20, Top = 24, AutoSize = true }, name, new Label { Text = "TELÉFONO", Left = 20, Top = 64, AutoSize = true }, phone, new Label { Text = "EMAIL", Left = 20, Top = 104, AutoSize = true }, email });
        var ok = new Button { Text = "CREAR Y USAR", Left = 170, Top = 155, Width = 130, Height = 38, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "CANCELAR", Left = 310, Top = 155, Width = 110, Height = 38, DialogResult = DialogResult.Cancel };
        f.Controls.AddRange(new Control[] { ok, cancel }); f.AcceptButton = ok; f.CancelButton = cancel;
        if (f.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(name.Text)) return;
        try
        {
            using var cn = Database.Open(); using var cmd = cn.CreateCommand();
            cmd.CommandText = "INSERT INTO suppliers(name,document,phone,email,address,active) VALUES($n,'',$p,$e,'',1); SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("$n", name.Text.Trim()); cmd.Parameters.AddWithValue("$p", phone.Text.Trim()); cmd.Parameters.AddWithValue("$e", email.Text.Trim());
            var id = Convert.ToInt32(cmd.ExecuteScalar());
            AuditService.Log(Session.UserId, "SUPPLIER_CREATE_FROM_PRODUCT", "PROVEEDORES", $"Proveedor ID {id} creado desde producto");
            LoadSuppliers(id);
        }
        catch (Exception ex) { MessageBox.Show("No se pudo crear el proveedor.\n\n" + ex.Message, "Proveedor", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private sealed record SupplierItem(int Id, string Name);

    private bool PromptStockReductionReason(double previousStock, double newStock, out string reason)
    {
        reason = string.Empty;
        using var form = new Form
        {
            Text = "Motivo de reducción de stock",
            Width = 500, Height = 230, StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false
        };
        ThemeService.Apply(form);

        var info = new Label
        {
            Text = $"El stock se reducirá de {previousStock:N2} a {newStock:N2}.\nIndicá el motivo para registrarlo en el reporte final enviado por email.",
            Location = new Point(20, 20), Width = 440, Height = 55
        };
        var input = new TextBox { Location = new Point(20, 85), Width = 440 };
        var capturedReason = string.Empty;
        var ok = new Button { Text = "CONFIRMAR Y GUARDAR", Location = new Point(250, 130), Width = 210, Height = 38, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "CANCELAR", Location = new Point(20, 130), Width = 150, Height = 38, DialogResult = DialogResult.Cancel };
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(input.Text))
            {
                MessageBox.Show("El motivo de reducción es obligatorio.", "Motivo requerido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                form.DialogResult = DialogResult.None;
                input.Focus();
            }
            else
            {
                capturedReason = input.Text.Trim();
            }
        };
        form.Controls.Add(info); form.Controls.Add(input); form.Controls.Add(ok); form.Controls.Add(cancel);
        form.AcceptButton = ok; form.CancelButton = cancel;
        var result = form.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(capturedReason);
        reason = capturedReason;
        return result;
    }

    private void UpdateInventoryFieldsState()
    {
        var usesInventory = InventoryControlService.IsGlobalEnabled && !noInventory.Checked;
        stock.Enabled = usesInventory;
        stock.ReadOnly = !usesInventory;
        stock.BackColor = usesInventory ? Color.White : Color.Gainsboro;
        min.Enabled = usesInventory;
        min.ReadOnly = !usesInventory;
        min.BackColor = usesInventory ? Color.White : Color.Gainsboro;
        if (!usesInventory)
        {
            stock.Text = "NO APLICA";
            min.Text = "NO APLICA";
        }
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(barcode.Text) || string.IsNullOrWhiteSpace(description.Text))
        {
            MessageBox.Show("Los campos CÓDIGO DE BARRAS y DESCRIPCIÓN son obligatorios."); return;
        }

        try
        {
            var usesInventory = InventoryControlService.IsGlobalEnabled && !noInventory.Checked;
            var newStock = usesInventory ? Num(stock) : 0d;
            var newMinStock = usesInventory ? Num(min) : 0d;
            var stockReductionReason = string.Empty;
            if (id > 0 && newStock < originalStock - 0.0000001)
            {
                if (!PromptStockReductionReason(originalStock, newStock, out stockReductionReason))
                    return;
            }

            var productToSave = new Product(id, barcode.Text.Trim(), description.Text.Trim(), Num(price), Num(wholesale),
                Num(cost), newStock, newMinStock, category.Text.Trim(),
                string.IsNullOrWhiteSpace(unit.Text) ? "UN" : unit.Text.Trim(), true, bulk.Checked, InventoryControlService.IsGlobalEnabled && !noInventory.Checked);
            ProductService.Save(productToSave, stockReductionReason);
            var saved = productToSave.Id > 0 ? productToSave.Id : (ProductService.FindByBarcode(productToSave.Barcode)?.Id ?? 0);
            if (saved > 0)
            {
                ProductService.SetAddsIva21(saved, addsIva21);
                ProductService.SetRoundSaleTo5(saved, roundSaleTo5);
                SaveProductSupplier(saved);
            }
            MessageBox.Show("Producto guardado correctamente en la base de datos.");
            LoadGrid(); Clear();
        }
        catch (ProductService.ProductSaveException ex)
        {
            // Nunca mostrar errores internos de SQLite al usuario. La validación
            // de ProductService convierte el conflicto de UNIQUE en un mensaje
            // claro y accionable.
            MessageBox.Show(
                ex.Message,
                "No se pudo guardar el producto",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        catch (FormatException ex)
        {
            MessageBox.Show(
                ex.Message,
                "Dato inválido",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            // Última barrera de UX: no exponer detalles técnicos de SQLite,
            // índices o nombres internos de tablas.
            MessageBox.Show(
                "No se pudo guardar el producto.\n\nRevisá los datos ingresados e intentá nuevamente.",
                "Guardar producto",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void ImportInventory()
    {
        if (!Session.IsAdmin)
        {
            MessageBox.Show(
                "Solo el usuario ADMINISTRADOR puede importar inventario.",
                "Permiso restringido",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var result = ExcelExportService.ImportInventory();
            if (result is null) return;

            MessageBox.Show(
                $"Importación completada correctamente.\n\nNuevos productos: {result.Inserted}\nProductos actualizados: {result.Updated}\nFilas omitidas: {result.Skipped}",
                "Importar inventario",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            LoadGrid();
            Clear();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "No se pudo importar el inventario desde Excel:\n\n" + ex.Message,
                "Importar inventario",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ExportInventory()
    {
        if (!Session.IsAdmin)
        {
            MessageBox.Show(
                "Solo el usuario ADMINISTRADOR puede exportar inventario.",
                "Permiso restringido",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var path = ExcelExportService.ExportInventory();
            if (!string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show(
                    $"Inventario exportado correctamente.\n\nArchivo:\n{path}",
                    "Exportar inventario",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "No se pudo exportar el inventario a Excel:\n\n" + ex.Message,
                "Exportar inventario",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private string PromptReason()
    {
        using var f = new Form { Text = "Motivo obligatorio · Eliminar producto", Width = 520, Height = 190, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false };
        var label = new Label { Text = "Indicá el motivo por el que se elimina/desactiva el producto:", Left = 16, Top = 15, AutoSize = true };
        var box = new TextBox { Left = 16, Top = 48, Width = 470 };
        var ok = new Button { Text = "CONFIRMAR", Left = 300, Top = 90, Width = 110, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "CANCELAR", Left = 416, Top = 90, Width = 90, DialogResult = DialogResult.Cancel };
        f.Controls.AddRange(new Control[] { label, box, ok, cancel }); f.AcceptButton = ok; f.CancelButton = cancel;
        return f.ShowDialog(this) == DialogResult.OK ? box.Text.Trim() : "";
    }

    private void Delete()
    {
        if (id == 0) return;
        var reason = PromptReason();
        if (string.IsNullOrWhiteSpace(reason))
            reason = "Eliminado desde gestión de productos.";
        if (MessageBox.Show($"¿Desactivar el producto seleccionado?\n\nMotivo: {reason}", "Confirmar eliminación", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
        {
            try { ProductService.Delete(id, reason, Session.UserId); LoadGrid(); Clear(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "No se pudo eliminar el producto", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }

    private void SelectCurrent()
    {
        if (grid.CurrentRow?.DataBoundItem is Product p)
        {
            SelectedProduct = p;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}

public sealed class ProductDescriptionSearchForm : Form
{
    private readonly TextBox search = new();
    private readonly DataGridView grid = new();
    public Product? SelectedProduct { get; private set; }

    public ProductDescriptionSearchForm()
    {
        Text = "FerrarisPOS - Buscar producto para editar";
        Width = 1050;
        Height = 650;
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;

        Controls.Add(new Label
        {
            Text = "BUSCAR PRODUCTO PARA EDITAR",
            Location = new Point(25, 20),
            AutoSize = true,
            Font = new Font("Segoe UI", 16, FontStyle.Bold)
        });
        Controls.Add(new Label
        {
            Text = "Escribí el comienzo de la descripción. Ej.: A → todos los que empiezan con A · AN → todos los que empiezan con AN",
            Location = new Point(25, 55),
            AutoSize = true,
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.DimGray
        });

        search.Location = new Point(25, 85);
        search.Width = 760;
        search.Height = 38;
        search.Font = new Font("Segoe UI", 14);
        search.PlaceholderText = "Descripción...";
        search.TextChanged += (_, _) => LoadProducts();
        search.KeyDown += Search_KeyDown;
        Controls.Add(search);

        var cancel = new Button
        { Text = "ESC · CERRAR", Location = new Point(805, 85), Width = 170, Height = 38, BackColor = Color.White };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(cancel);

        grid.Location = new Point(25, 140);
        grid.Size = new Size(950, 430);
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.RowHeadersVisible = false;
        grid.AutoGenerateColumns = false;
        grid.BackgroundColor = Color.White;
        grid.Font = new Font("Segoe UI", 10);
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "CÓDIGO", DataPropertyName = "Barcode", Width = 190 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "DESCRIPCIÓN", DataPropertyName = "Description", Width = 430 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "PRECIO", DataPropertyName = "SalePrice", Width = 140, DefaultCellStyle = new DataGridViewCellStyle { Format = "N2", Alignment = DataGridViewContentAlignment.MiddleRight } });
        grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "STOCK", DataPropertyName = "Stock", Width = 120, DefaultCellStyle = new DataGridViewCellStyle { Format = "N2", Alignment = DataGridViewContentAlignment.MiddleRight } });
        grid.KeyDown += Grid_KeyDown;
        grid.CellDoubleClick += (_, _) => SelectCurrent();
        Controls.Add(grid);

        Controls.Add(new Label
        { Text = "↑ ↓ seleccionar · ENTER editar · DOBLE CLICK editar · ESC cerrar", Location = new Point(25, 585), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) });

        Shown += (_, _) => { search.Focus(); };
        LoadProducts();
    }

    private void LoadProducts()
    {
        var list = ProductService.SearchByDescriptionPrefix(search.Text);
        grid.DataSource = list;
        if (list.Count > 0)
        {
            grid.ClearSelection();
            grid.Rows[0].Selected = true;
            grid.CurrentCell = grid.Rows[0].Cells[0];
        }
    }

    private void Search_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up)
        {
            MoveSelection(e.KeyCode == Keys.Down ? 1 : -1);
            e.Handled = true; e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Enter)
        {
            SelectCurrent();
            e.Handled = true; e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel; Close();
        }
    }

    private void Grid_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            SelectCurrent(); e.Handled = true; e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel; Close();
        }
    }

    private void MoveSelection(int delta)
    {
        if (grid.Rows.Count == 0) return;
        var current = grid.CurrentRow?.Index ?? 0;
        var next = Math.Max(0, Math.Min(grid.Rows.Count - 1, current + delta));
        grid.CurrentCell = grid.Rows[next].Cells[0];
        grid.Rows[next].Selected = true;
    }

    private void SelectCurrent()
    {
        if (grid.CurrentRow?.DataBoundItem is Product p)
        {
            SelectedProduct = p;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}

internal static class DataGridViewExtensions
{
    public static void DoubleBuffered(this DataGridView grid, bool value)
    {
        var prop = typeof(DataGridView).GetProperty("DoubleBuffered",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        prop?.SetValue(grid, value, null);
    }
}
