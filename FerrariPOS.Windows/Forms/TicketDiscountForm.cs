using FerrarisPOS.Services;

namespace FerrarisPOS.Forms;

public sealed class TicketDiscountForm : Form
{
    private readonly NumericUpDown percent = new() { DecimalPlaces = 2, Maximum = 100, Minimum = 0, Increment = 0.5M, Width = 180 };
    private readonly NumericUpDown amount = new() { DecimalPlaces = 2, Maximum = 999999999, Minimum = 0, Increment = 1, Width = 180 };
    private readonly double total;
    public double DiscountAmount { get; private set; }

    public TicketDiscountForm(double total, double current)
    {
        this.total = Math.Max(0, total);
        Text = "Descuento general de la venta";
        Width = 540; Height = 350; StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        Build(current);
        ThemeService.Apply(this);
    }

    private void Build(double current)
    {
        Controls.Add(new Label { Text = "DESCUENTO GENERAL DEL TICKET", Location = new Point(20, 20), AutoSize = true, Font = new Font("Segoe UI", 15, FontStyle.Bold) });
        Controls.Add(new Label { Text = $"Total actual: ${total:N2}", Location = new Point(20, 58), AutoSize = true });
        Controls.Add(new Label { Text = "PORCENTAJE (%)", Location = new Point(20, 105), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        percent.Location = new Point(190, 100); Controls.Add(percent);
        Controls.Add(new Label { Text = "DESCUENTO ($)", Location = new Point(20, 150), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        amount.Location = new Point(190, 145); Controls.Add(amount);

        var initial = Math.Clamp(current, 0, total);
        amount.Value = Math.Min(amount.Maximum, (decimal)initial);
        percent.Value = total <= 0 ? 0 : Math.Min(percent.Maximum, Math.Round((decimal)initial * 100M / (decimal)total, 2));

        percent.ValueChanged += (_, _) =>
        {
            var value = Math.Min(amount.Maximum, Math.Round(percent.Value * (decimal)total / 100M, 2));
            if (amount.Value != value) amount.Value = value;
        };
        amount.ValueChanged += (_, _) =>
        {
            if (total <= 0) return;
            var value = Math.Min(percent.Maximum, Math.Round(amount.Value * 100M / (decimal)total, 2));
            if (percent.Value != value) percent.Value = value;
        };

        var clear = new Button { Text = "QUITAR DESCUENTO", Location = new Point(20, 225), Width = 150, Height = 42 };
        clear.Click += (_, _) => { DiscountAmount = 0; DialogResult = DialogResult.OK; Close(); };
        Controls.Add(clear);
        var ok = new Button { Text = "APLICAR", Location = new Point(185, 225), Width = 140, Height = 42, DialogResult = DialogResult.OK };
        ok.Click += (_, _) => { DiscountAmount = Math.Min(total, (double)amount.Value); };
        Controls.Add(ok);
        var cancel = new Button { Text = "CANCELAR", Location = new Point(340, 225), Width = 140, Height = 42, DialogResult = DialogResult.Cancel };
        Controls.Add(cancel);
        AcceptButton = ok; CancelButton = cancel;
    }
}
