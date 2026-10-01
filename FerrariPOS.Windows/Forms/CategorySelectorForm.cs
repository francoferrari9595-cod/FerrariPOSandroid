using FerrarisPOS.Services;

namespace FerrarisPOS.Forms;

public sealed class CategorySelectorForm : Form
{
    private readonly ComboBox categories = new();
    private readonly TextBox newCategory = new();
    private string selectedCategory = string.Empty;

    public string SelectedCategory => selectedCategory;

    public CategorySelectorForm(string currentCategory = "", bool focusCreate = false)
    {
        Text = "FerrarisPOS · Categorías de productos";
        Width = 700;
        Height = 470;
        MinimumSize = new Size(650, 430);
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;
        BackColor = Color.Gainsboro;

        Build(currentCategory);
        LoadCategories(currentCategory);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };

        ThemeService.Apply(this);
        if (focusCreate) Shown += (_, _) => { newCategory.Focus(); };
    }

    private void Build(string currentCategory)
    {
        Controls.Add(new Label
        {
            Text = "CATEGORÍA DEL PRODUCTO",
            Location = new Point(28, 24),
            AutoSize = true,
            Font = new Font("Segoe UI", 18, FontStyle.Bold)
        });

        Controls.Add(new Label
        {
            Text = "Elegí una categoría existente o creá una nueva. Quedará guardada para usarla en otros productos.",
            Location = new Point(30, 62),
            Width = 620,
            AutoSize = false,
            Height = 42,
            ForeColor = Color.DimGray
        });

        Controls.Add(new Label
        {
            Text = "CATEGORÍAS GUARDADAS",
            Location = new Point(30, 125),
            AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        });

        categories.Location = new Point(30, 150);
        categories.Width = 590;
        categories.Height = 40;
        categories.DropDownStyle = ComboBoxStyle.DropDownList;
        categories.IntegralHeight = false;
        categories.MaxDropDownItems = 12;
        categories.DropDownHeight = 12 * categories.ItemHeight + 8;
        categories.Font = new Font("Segoe UI", 12);
        categories.Cursor = Cursors.Hand;
        Controls.Add(categories);

        Controls.Add(new Label
        {
            Text = "NUEVA CATEGORÍA",
            Location = new Point(30, 215),
            AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        });

        newCategory.Location = new Point(30, 240);
        newCategory.Width = 590;
        newCategory.Height = 38;
        newCategory.Font = new Font("Segoe UI", 12);
        newCategory.PlaceholderText = "Ej.: Yogures, Bebidas, Limpieza...";
        newCategory.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                CreateAndUse();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };
        Controls.Add(newCategory);

        var use = Button("USAR CATEGORÍA", 30, 325, 180);
        use.Click += (_, _) => UseSelected();
        Controls.Add(use);

        var create = Button("CREAR Y USAR", 225, 325, 180);
        create.Click += (_, _) => CreateAndUse();
        Controls.Add(create);

        var cancel = Button("CANCELAR", 420, 325, 200);
        cancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        Controls.Add(cancel);

        Controls.Add(new Label
        {
            Text = "ESC · cerrar",
            Location = new Point(30, 385),
            AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            ForeColor = Color.DimGray
        });
    }

    private Button Button(string text, int x, int y, int width)
    {
        return new Button
        {
            Text = text,
            Location = new Point(x, y),
            Width = width,
            Height = 42,
            BackColor = Color.White,
            Cursor = Cursors.Hand
        };
    }

    private void LoadCategories(string currentCategory)
    {
        categories.Items.Clear();
        foreach (var item in CategoryService.GetAll())
            categories.Items.Add(item);

        if (!string.IsNullOrWhiteSpace(currentCategory))
        {
            var normalized = CategoryService.Normalize(currentCategory);
            var index = categories.Items.Cast<string>()
                .ToList()
                .FindIndex(x => string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase));

            if (index >= 0)
                categories.SelectedIndex = index;
        }

        if (categories.SelectedIndex < 0 && categories.Items.Count > 0)
            categories.SelectedIndex = 0;
    }

    private void UseSelected()
    {
        if (categories.SelectedItem is not string value || string.IsNullOrWhiteSpace(value))
        {
            MessageBox.Show(
                "Seleccioná una categoría guardada o creá una nueva.",
                "Categorías",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        selectedCategory = CategoryService.Normalize(value);
        DialogResult = DialogResult.OK;
        Close();
    }

    private void CreateAndUse()
    {
        var value = CategoryService.Normalize(newCategory.Text);
        if (string.IsNullOrWhiteSpace(value))
        {
            MessageBox.Show(
                "Escribí el nombre de la nueva categoría.",
                "Categorías",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            newCategory.Focus();
            return;
        }

        try
        {
            selectedCategory = CategoryService.Ensure(value);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "No se pudo guardar la categoría.\n\n" + ex.Message,
                "Categorías",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
}
