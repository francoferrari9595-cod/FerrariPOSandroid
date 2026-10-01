using FerrarisPOS.Models;
using FerrarisPOS.Services;
using System.Diagnostics;

namespace FerrarisPOS.Forms;

public sealed class BarcodeGeneratorForm : Form
{
    private readonly TextBox nameBox = new();
    private readonly TextBox searchBox = new();
    private readonly TextBox savedSearchBox = new();
    private readonly DataGridView productGrid = new();
    private readonly DataGridView savedGrid = new();
    private readonly PictureBox preview = new();
    private readonly Label selectedProductLabel = new();
    private readonly Label selectedCodeLabel = new();
    private int? preferredProductId;
    private Bitmap? previewBitmap;

    public BarcodeGeneratorForm(int? productId = null)
    {
        preferredProductId = productId;
        Text = "FerrarisPOS - Códigos de barras internos";
        Width = 1280; Height = 820; MinimumSize = new Size(1100, 720);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.Gainsboro;
        KeyPreview = true;

        Build();
        LoadProducts();
        LoadSaved();

        Shown += (_, _) =>
        {
            if (preferredProductId.HasValue) SelectPreferredProduct();
            nameBox.Focus();
        };

        FormClosed += (_, _) => previewBitmap?.Dispose();

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };
    }

    private Button Btn(string text, int x, int y, int w, EventHandler action)
    {
        var b = new Button
        {
            Text = text, Location = new Point(x, y), Width = w, Height = 42,
            BackColor = Color.White, Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        b.Click += action;
        Controls.Add(b);
        return b;
    }

    private void Build()
    {
        Controls.Add(new Label
        {
            Text = "CÓDIGOS DE BARRAS INTERNOS",
            Location = new Point(22, 18), AutoSize = true,
            Font = new Font("Segoe UI", 18, FontStyle.Bold)
        });
        Controls.Add(new Label
        {
            Text = "Para artículos que no traen código. Creá el código, asignalo al producto y guardá/imprimí su gráfico.",
            Location = new Point(22, 52), AutoSize = true, ForeColor = Color.DimGray
        });

        // CREACIÓN
        var creation = new GroupBox
        {
            Text = " 1 · CREAR CÓDIGO INTERNO ",
            Location = new Point(22, 82), Size = new Size(1230, 165),
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        Controls.Add(creation);

        creation.Controls.Add(new Label { Text = "NOMBRE DEL ARTÍCULO / CÓDIGO", Location = new Point(18, 32), AutoSize = true });
        nameBox.SetBounds(18, 58, 360, 40);
        nameBox.Font = new Font("Segoe UI", 12);
        nameBox.PlaceholderText = "Ej.: PANCHO ESCOLAR";
        creation.Controls.Add(nameBox);

        var generate = new Button { Text = "GENERAR CÓDIGO", Location = new Point(400, 58), Width = 170, Height = 40, BackColor = Color.White };
        generate.Click += (_, _) => { /* siempre genera al guardar */ };
        creation.Controls.Add(generate);

        var save = new Button { Text = "CREAR Y GUARDAR", Location = new Point(585, 58), Width = 190, Height = 40, BackColor = Color.White };
        save.Click += (_, _) => CreateAndSave();
        creation.Controls.Add(save);

        selectedProductLabel.SetBounds(795, 34, 410, 48);
        selectedProductLabel.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        selectedProductLabel.Text = "PRODUCTO DESTINO: ninguno";
        creation.Controls.Add(selectedProductLabel);

        var assignNew = new Button { Text = "CREAR + ASIGNAR AL PRODUCTO", Location = new Point(795, 88), Width = 270, Height = 40, BackColor = Color.White };
        assignNew.Click += (_, _) => CreateAndSave(true);
        creation.Controls.Add(assignNew);

        var instructions = new Label
        {
            Text = "El código se genera automáticamente como EAN-13 interno y su PNG se guarda en la carpeta de Ferrari-PDV.",
            Location = new Point(18, 126), AutoSize = true, ForeColor = Color.DimGray
        };
        creation.Controls.Add(instructions);

        // PRODUCTOS
        var productsBox = new GroupBox
        {
            Text = " 2 · ASIGNAR A UN PRODUCTO SIN CÓDIGO ",
            Location = new Point(22, 245), Size = new Size(550, 500),
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        Controls.Add(productsBox);

        searchBox.SetBounds(18, 35, 510, 36);
        searchBox.Font = new Font("Segoe UI", 10);
        searchBox.PlaceholderText = "Buscar producto sin código...";
        searchBox.TextChanged += (_, _) => LoadProducts();
        productsBox.Controls.Add(searchBox);

        productGrid.SetBounds(18, 82, 510, 330);
        productGrid.ReadOnly = true; productGrid.AllowUserToAddRows = false;
        productGrid.RowHeadersVisible = false; productGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        productGrid.MultiSelect = false; productGrid.AutoGenerateColumns = false;
        productGrid.BackgroundColor = Color.White; productGrid.RowTemplate.Height = 32;
        productGrid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName="Id", Visible=false });
        productGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText="PRODUCTO", DataPropertyName="Description", Width=330 });
        productGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText="STOCK", DataPropertyName="Stock", Width=100 });
        productGrid.SelectionChanged += (_, _) => UpdateSelectedProduct();
        productGrid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { nameBox.Focus(); e.Handled = true; e.SuppressKeyPress = true; }
        };
        productsBox.Controls.Add(productGrid);

        var assignSaved = new Button
        {
            Text = "ASIGNAR CÓDIGO SELECCIONADO",
            Location = new Point(18, 425), Width = 250, Height = 42, BackColor = Color.White
        };
        assignSaved.Click += (_, _) => AssignSavedToProduct();
        productsBox.Controls.Add(assignSaved);

        productsBox.Controls.Add(new Label
        {
            Text = "Elegí el producto de esta lista y luego seleccioná un código guardado.",
            Location = new Point(18, 472), AutoSize = true, ForeColor = Color.DimGray
        });

        // CÓDIGOS GUARDADOS
        var savedBox = new GroupBox
        {
            Text = " 3 · CÓDIGOS GUARDADOS ",
            Location = new Point(590, 245), Size = new Size(662, 500),
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        Controls.Add(savedBox);

        savedSearchBox.SetBounds(18, 35, 390, 36);
        savedSearchBox.Font = new Font("Segoe UI", 10);
        savedSearchBox.PlaceholderText = "Buscar por nombre o código...";
        savedSearchBox.TextChanged += (_, _) => LoadSaved();
        savedBox.Controls.Add(savedSearchBox);

        savedGrid.SetBounds(18, 82, 390, 285);
        savedGrid.ReadOnly = true; savedGrid.AllowUserToAddRows = false;
        savedGrid.RowHeadersVisible = false; savedGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        savedGrid.MultiSelect = false; savedGrid.AutoGenerateColumns = false;
        savedGrid.BackgroundColor = Color.White; savedGrid.RowTemplate.Height = 32;
        savedGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText="NOMBRE", DataPropertyName="Name", Width=190 });
        savedGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText="CÓDIGO", DataPropertyName="Barcode", Width=170 });
        savedGrid.SelectionChanged += (_, _) => UpdatePreview();
        savedGrid.DoubleClick += (_, _) => PrintSelected();
        savedGrid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { PrintSelected(); e.Handled=true; e.SuppressKeyPress=true; }
        };
        savedBox.Controls.Add(savedGrid);

        preview.SetBounds(430, 82, 215, 160);
        preview.BackColor = Color.White;
        preview.SizeMode = PictureBoxSizeMode.Zoom;
        preview.BorderStyle = BorderStyle.FixedSingle;
        savedBox.Controls.Add(preview);

        selectedCodeLabel.SetBounds(430, 252, 215, 45);
        selectedCodeLabel.Text = "SIN CÓDIGO SELECCIONADO";
        selectedCodeLabel.TextAlign = ContentAlignment.MiddleCenter;
        selectedCodeLabel.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        savedBox.Controls.Add(selectedCodeLabel);

        var printOne = new Button { Text="🖨 IMPRIMIR CÓDIGO", Location=new Point(430, 310), Width=215, Height=40, BackColor=Color.White };
        printOne.Click += (_, _) => PrintSelected();
        savedBox.Controls.Add(printOne);

        var saveOne = new Button { Text="💾 GUARDAR GRÁFICO PNG", Location=new Point(430, 357), Width=215, Height=40, BackColor=Color.White };
        saveOne.Click += (_, _) => SaveSelectedGraphic();
        savedBox.Controls.Add(saveOne);

        var printAll = new Button { Text="🖨 IMPRIMIR TODOS", Location=new Point(18, 385), Width=190, Height=42, BackColor=Color.White };
        printAll.Click += (_, _) => PrintAll();
        savedBox.Controls.Add(printAll);

        var saveAll = new Button { Text="💾 GUARDAR TODOS LOS PNG", Location=new Point(218, 385), Width=190, Height=42, BackColor=Color.White };
        saveAll.Click += (_, _) => SaveAllGraphics();
        savedBox.Controls.Add(saveAll);

        var saveProduct = new Button
        {
            Text = "💾 GUARDAR PRODUCTO CON CÓDIGO",
            Location = new Point(18, 432), Width = 390, Height = 42,
            BackColor = Color.White
        };
        saveProduct.Click += (_, _) => SaveSelectedAsProduct();
        savedBox.Controls.Add(saveProduct);

        var folder = new Button { Text="ABRIR CARPETA", Location=new Point(430, 432), Width=215, Height=42, BackColor=Color.White };
        folder.Click += (_, _) => OpenFolder();
        savedBox.Controls.Add(folder);

        var close = new Button { Text="CERRAR", Location=new Point(1060, 760), Width=190, Height=38, BackColor=Color.White };
        close.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        Controls.Add(close);

        // Al crear, el botón genera un código nuevo; se deja el formulario limpio.
        generate.Click += (_, _) => GenerateAndShowPreview();
        GenerateAndShowPreview();
    }

    private void GenerateAndShowPreview()
    {
        try
        {
            // Solo genera visualmente; el código definitivo se genera al guardar.
            var code = GeneratedBarcodeService.GenerateUniqueEan13();
            using var bmp = GeneratedBarcodeService.RenderEan13(code, nameBox.Text.Trim(), 900, 300);
            previewBitmap?.Dispose();
            previewBitmap = new Bitmap(bmp);
            preview.Image = previewBitmap;
            selectedCodeLabel.Text = code;
            preview.Tag = code;
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo generar el código:\n\n" + ex.Message, "Códigos internos",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CreateAndSave(bool assign = false)
    {
        var name = nameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            MessageBox.Show("Escribí primero el nombre del artículo.", "Códigos internos",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            nameBox.Focus(); return;
        }

        var product = CurrentProduct();
        if (assign && product is null)
        {
            MessageBox.Show("Seleccioná primero el producto al que querés asignar el código.",
                "Códigos internos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            // Se genera nuevamente al guardar para garantizar unicidad en ese instante.
            var code = GeneratedBarcodeService.GenerateUniqueEan13();
            var id = GeneratedBarcodeService.Save(name, code, null);

            if (assign && product is not null)
                GeneratedBarcodeService.AssignToProduct(id, product.Id);

            GeneratedBarcodeService.SaveBarcodeImage(code, name);

            MessageBox.Show(assign
                ? $"Código creado, guardado, impreso como PNG y asignado a:\n\n{product!.Description}\n\n{code}"
                : $"Código creado y guardado:\n\n{name}\n{code}\n\nEl gráfico PNG también quedó guardado.",
                "Códigos internos", MessageBoxButtons.OK, MessageBoxIcon.Information);

            LoadProducts(); LoadSaved();
            nameBox.Clear();
            GenerateAndShowPreview();
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo crear el código:\n\n" + ex.Message,
                "Códigos internos", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadProducts()
    {
        var list = ProductService.SearchWithoutBarcode(searchBox.Text);
        productGrid.DataSource = list;
        if (list.Count > 0 && !preferredProductId.HasValue)
        {
            productGrid.ClearSelection();
            productGrid.Rows[0].Selected = true;
            productGrid.CurrentCell = productGrid.Rows[0].Cells[1];
        }
        UpdateSelectedProduct();
        SelectPreferredProduct();
    }

    private void SelectPreferredProduct()
    {
        if (preferredProductId is not int id) return;
        for (var i=0; i<productGrid.Rows.Count; i++)
        {
            if (productGrid.Rows[i].DataBoundItem is Product p && p.Id == id)
            {
                productGrid.ClearSelection();
                productGrid.Rows[i].Selected = true;
                productGrid.CurrentCell = productGrid.Rows[i].Cells[1];
                preferredProductId = null;
                UpdateSelectedProduct();
                break;
            }
        }
    }

    private Product? CurrentProduct() => productGrid.CurrentRow?.DataBoundItem as Product;

    private void UpdateSelectedProduct()
    {
        var p = CurrentProduct();
        selectedProductLabel.Text = p is null
            ? "PRODUCTO DESTINO: ninguno"
            : $"PRODUCTO DESTINO:\n{p.Description}";
    }

    private GeneratedBarcode? CurrentSaved() => savedGrid.CurrentRow?.DataBoundItem as GeneratedBarcode;

    private void LoadSaved()
    {
        savedGrid.DataSource = GeneratedBarcodeService.Search(savedSearchBox.Text);
        if (savedGrid.Rows.Count > 0)
        {
            savedGrid.ClearSelection();
            savedGrid.Rows[0].Selected = true;
            savedGrid.CurrentCell = savedGrid.Rows[0].Cells[0];
        }
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var item = CurrentSaved();
        previewBitmap?.Dispose();
        previewBitmap = null;

        if (item is null)
        {
            preview.Image = null;
            selectedCodeLabel.Text = "SIN CÓDIGO SELECCIONADO";
            return;
        }

        previewBitmap = GeneratedBarcodeService.RenderEan13(item.Barcode, item.Name, 900, 300);
        preview.Image = previewBitmap;
        selectedCodeLabel.Text = $"{item.Name}\n{item.Barcode}";
    }

    private void PrintSelected()
    {
        var item = CurrentSaved();
        if (item is null) { MessageBox.Show("Seleccioná un código guardado."); return; }
        try { GeneratedBarcodeService.PrintOne(item); }
        catch (Exception ex) { MessageBox.Show("No se pudo imprimir:\n\n" + ex.Message, "Impresión", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void PrintAll()
    {
        var items = GeneratedBarcodeService.Search();
        try { GeneratedBarcodeService.PrintAll(items); }
        catch (Exception ex) { MessageBox.Show("No se pudieron imprimir los códigos:\n\n" + ex.Message, "Impresión", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void SaveSelectedGraphic()
    {
        var item = CurrentSaved();
        if (item is null) { MessageBox.Show("Seleccioná un código guardado."); return; }
        try
        {
            var path = GeneratedBarcodeService.SaveBarcodeImage(item.Barcode, item.Name);
            MessageBox.Show($"Gráfico guardado en:\n\n{path}", "Código de barras", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show("No se pudo guardar el gráfico:\n\n" + ex.Message, "Código de barras", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void SaveSelectedAsProduct()
    {
        var item = CurrentSaved();
        if (item is null)
        {
            MessageBox.Show("Seleccioná primero un código guardado.", "Producto con código",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var product = GeneratedBarcodeService.CreateProductFromGenerated(item.Id);
            MessageBox.Show(
                $"El producto quedó guardado en el inventario normal.\n\n" +
                $"PRODUCTO: {product.Description}\n" +
                $"CÓDIGO: {product.Barcode}\n\n" +
                "Ahora aparece en Productos y puede venderse escaneando ese código.\n" +
                "El precio y el stock pueden completarse desde Productos.",
                "Producto guardado", MessageBoxButtons.OK, MessageBoxIcon.Information);

            LoadProducts();
            LoadSaved();
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo guardar el producto con código:\n\n" + ex.Message,
                "Producto con código", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveAllGraphics()
    {
        var items = GeneratedBarcodeService.Search();
        if (items.Count == 0) { MessageBox.Show("No hay códigos guardados."); return; }

        try
        {
            foreach (var item in items) GeneratedBarcodeService.SaveBarcodeImage(item.Barcode, item.Name);
            MessageBox.Show($"Se guardaron {items.Count} gráficos PNG en:\n\n{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FerrarisPOS", "CodigosBarras")}",
                "Códigos de barras", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show("No se pudieron guardar todos los gráficos:\n\n" + ex.Message, "Códigos de barras", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void OpenFolder()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FerrarisPOS", "CodigosBarras");
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show("No se pudo abrir la carpeta:\n\n" + ex.Message); }
    }

    private void AssignSavedToProduct()
    {
        var item = CurrentSaved();
        var product = CurrentProduct();

        if (item is null || product is null)
        {
            MessageBox.Show("Seleccioná un código guardado y un producto sin código.",
                "Códigos internos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            GeneratedBarcodeService.AssignToProduct(item.Id, product.Id);
            MessageBox.Show($"Código {item.Barcode} asignado a:\n\n{product.Description}",
                "Códigos internos", MessageBoxButtons.OK, MessageBoxIcon.Information);
            LoadProducts(); LoadSaved();
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo asignar el código:\n\n" + ex.Message,
                "Códigos internos", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
