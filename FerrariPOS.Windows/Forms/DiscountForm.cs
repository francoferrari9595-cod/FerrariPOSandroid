using System.Globalization;
using FerrarisPOS.Services;

namespace FerrarisPOS.Forms;

public sealed class DiscountForm : Form
{
    private readonly NumericUpDown percent = new() { DecimalPlaces = 2, Maximum = 100, Minimum = 0, Increment = 0.5M, Width = 180 };
    private readonly NumericUpDown amount = new() { DecimalPlaces = 2, Maximum = 999999999, Minimum = 0, Increment = 1, Width = 180 };
    public double DiscountAmount { get; private set; }

    public DiscountForm(string description, double gross, double current)
    {
        Text = "Descuento por producto";
        Width = 520; Height = 320; StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        Build(description, gross, current);
        ThemeService.Apply(this);
    }

    private void Build(string description, double gross, double current)
    {
        Controls.Add(new Label { Text = "DESCUENTO DEL PRODUCTO", Location = new Point(20, 20), AutoSize = true, Font = new Font("Segoe UI", 15, FontStyle.Bold) });
        Controls.Add(new Label { Text = description, Location = new Point(20, 55), Width = 450 });
        Controls.Add(new Label { Text = $"Importe bruto: ${gross:N2}", Location = new Point(20, 80), AutoSize = true });

        Controls.Add(new Label { Text = "PORCENTAJE (%)", Location = new Point(20, 120), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        percent.Location = new Point(170, 115); Controls.Add(percent);
        Controls.Add(new Label { Text = "MONTO ($)", Location = new Point(20, 160), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        amount.Location = new Point(170, 155); Controls.Add(amount);

        if (gross > 0)
        {
            percent.Value = (decimal)Math.Clamp(current * 100.0 / gross, 0, 100);
            amount.Value = (decimal)Math.Clamp(current, 0, gross);
        }

        percent.ValueChanged += (_, _) => amount.Value = Math.Min(amount.Maximum, Math.Round(percent.Value * (decimal)gross / 100M, 2));
        amount.ValueChanged += (_, _) =>
        {
            if (gross > 0 && Math.Abs((double)amount.Value - (double)percent.Value * gross / 100) > 0.01)
                percent.Value = Math.Min(percent.Maximum, Math.Round(amount.Value * 100M / (decimal)gross, 2));
        };

        var clear = new Button { Text = "SIN DESCUENTO", Location = new Point(20, 215), Width = 140, Height = 40 };
        clear.Click += (_, _) => { DiscountAmount = 0; DialogResult = DialogResult.OK; Close(); };
        Controls.Add(clear);
        var ok = new Button { Text = "APLICAR", Location = new Point(180, 215), Width = 140, Height = 40, DialogResult = DialogResult.OK };
        ok.Click += (_, _) => { DiscountAmount = Math.Min(gross, (double)amount.Value); };
        Controls.Add(ok);
        var cancel = new Button { Text = "CANCELAR", Location = new Point(340, 215), Width = 140, Height = 40, DialogResult = DialogResult.Cancel };
        Controls.Add(cancel);
        AcceptButton = ok; CancelButton = cancel;
    }
}
