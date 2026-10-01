using FerrarisPOS.Data;
using FerrarisPOS.Services;

namespace FerrarisPOS.Forms;

/// <summary>
/// Venta de productos por granel. Permite introducir peso o importe y calcula
/// automáticamente el otro valor usando el precio de venta por kg.
/// </summary>
public sealed class BulkWeightForm : Form
{
    private readonly NumericUpDown weight = new();
    private readonly NumericUpDown amount = new();
    private readonly Label priceLabel = new();
    private readonly Label convertedLabel = new();
    private readonly Label stockLabel = new();
    private readonly Label productLabel = new();
    private readonly string currency;
    private readonly decimal pricePerKg;
    private readonly decimal stockKg;
    private bool updating;

    public double Kilos => (double)weight.Value;

    public BulkWeightForm(string productName, double salePrice, double stock)
    {
        Text = "¿Cantidad del Producto?";
        Width = 820;
        Height = 500;
        MinimumSize = new Size(720, 450);
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;
        currency = Database.GetSetting("currency", "$");
        pricePerKg = Math.Max(0m, (decimal)salePrice);
        stockKg = Math.Max(0m, (decimal)stock);

        var title = new Label
        {
            Text = "⚖️  VENTA POR GRANEL",
            Font = new Font("Segoe UI", 20, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Top,
            Height = 55
        };
        Controls.Add(title);

        productLabel.Text = productName;
        productLabel.Font = new Font("Segoe UI", 17, FontStyle.Bold);
        productLabel.TextAlign = ContentAlignment.MiddleCenter;
        productLabel.AutoEllipsis = true;
        productLabel.Dock = DockStyle.Top;
        productLabel.Height = 46;
        Controls.Add(productLabel);

        var info = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 56,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(24, 4, 24, 4)
        };
        info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        priceLabel.Text = $"PRECIO POR KILO  {currency}{pricePerKg:N2}";
        priceLabel.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        priceLabel.TextAlign = ContentAlignment.MiddleLeft;
        info.Controls.Add(priceLabel, 0, 0);

        stockLabel.Text = stockKg > 0 ? $"EXISTENCIA  {stockKg:N3} kg" : "EXISTENCIA  SIN CONTROL";
        stockLabel.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        stockLabel.TextAlign = ContentAlignment.MiddleRight;
        info.Controls.Add(stockLabel, 1, 0);
        Controls.Add(info);

        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 155,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(24, 4, 24, 4)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        var weightTitle = new Label { Text = "CANTIDAD DEL PRODUCTO · PESO", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10, FontStyle.Bold), TextAlign = ContentAlignment.BottomLeft };
        var amountTitle = new Label { Text = "IMPORTE ACTUAL · DINERO", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10, FontStyle.Bold), TextAlign = ContentAlignment.BottomLeft };
        fields.Controls.Add(weightTitle, 0, 0);
        fields.Controls.Add(amountTitle, 1, 0);

        ConfigureNumeric(weight, 3, 0.001m, stockKg > 0 ? stockKg : 1000000m);
        ConfigureNumeric(amount, 2, 0.01m, pricePerKg > 0 && stockKg > 0 ? pricePerKg * stockKg : 100000000m);
        fields.Controls.Add(weight, 0, 1);
        fields.Controls.Add(amount, 1, 1);

        fields.Controls.Add(new Label { Text = "kg  ·  Ej.: 0,500 = 500 g", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9, FontStyle.Italic), TextAlign = ContentAlignment.TopLeft }, 0, 2);
        fields.Controls.Add(new Label { Text = "Escribí el dinero y se calcula el peso", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9, FontStyle.Italic), TextAlign = ContentAlignment.TopLeft }, 1, 2);
        Controls.Add(fields);

        convertedLabel.Text = "Ingresá PESO o IMPORTE";
        convertedLabel.Font = new Font("Segoe UI", 18, FontStyle.Bold);
        convertedLabel.TextAlign = ContentAlignment.MiddleCenter;
        convertedLabel.Dock = DockStyle.Top;
        convertedLabel.Height = 62;
        Controls.Add(convertedLabel);

        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 78,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(24, 10, 24, 10)
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));

        var ok = new Button { Text = "✓  ACEPTAR", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11, FontStyle.Bold), Margin = new Padding(0, 0, 8, 0) };
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Text = "CANCELAR", Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10, FontStyle.Bold), Margin = new Padding(8, 0, 0, 0) };
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        buttons.Controls.Add(ok, 0, 0);
        buttons.Controls.Add(cancel, 1, 0);
        Controls.Add(buttons);

        weight.ValueChanged += (_, _) => UpdateFromWeight();
        amount.ValueChanged += (_, _) => UpdateFromAmount();
        AcceptButton = ok;
        CancelButton = cancel;

        ThemeService.Apply(this);
        Shown += (_, _) =>
        {
            weight.Focus();
            weight.Select(0, weight.Text.Length);
            UpdateFromWeight();
        };
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        };
    }

    private static void ConfigureNumeric(NumericUpDown control, int decimals, decimal increment, decimal maximum)
    {
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 4, 12, 4);
        control.DecimalPlaces = decimals;
        control.Increment = increment;
        control.Minimum = 0m;
        control.Maximum = Math.Max(1m, maximum);
        control.Font = new Font("Segoe UI", 21, FontStyle.Bold);
        control.TextAlign = HorizontalAlignment.Right;
        control.ThousandsSeparator = true;
    }

    private void UpdateFromWeight()
    {
        if (updating || pricePerKg <= 0) return;
        try
        {
            updating = true;
            var value = Math.Round(weight.Value * pricePerKg, 2);
            amount.Value = Math.Min(amount.Maximum, Math.Max(amount.Minimum, value));
            convertedLabel.Text = $"PESO {weight.Value:N3} kg   =   {currency}{amount.Value:N2}";
        }
        finally { updating = false; }
    }

    private void UpdateFromAmount()
    {
        if (updating || pricePerKg <= 0) return;
        try
        {
            updating = true;
            var value = Math.Round(amount.Value / pricePerKg, 3);
            weight.Value = Math.Min(weight.Maximum, Math.Max(weight.Minimum, value));
            convertedLabel.Text = $"{currency}{amount.Value:N2}   =   {weight.Value:N3} kg";
        }
        finally { updating = false; }
    }

    private void Accept()
    {
        if (Kilos <= 0)
        {
            MessageBox.Show("Ingresá un peso o importe mayor a cero.", "Venta por granel", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (stockKg > 0m && (decimal)Kilos > stockKg + 0.000001m)
        {
            MessageBox.Show($"El peso supera el stock disponible ({stockKg:N3} kg).", "Venta por granel", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
