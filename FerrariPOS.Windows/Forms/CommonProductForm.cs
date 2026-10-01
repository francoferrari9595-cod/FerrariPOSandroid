using FerrarisPOS.Services;

namespace FerrarisPOS.Forms;

public class CommonProductForm : Form
{
    private readonly TextBox description = new();
    private readonly NumericUpDown amount = new();
    public string ProductDescription => description.Text.Trim();
    public double Amount => (double)amount.Value;

    public CommonProductForm()
    {
        Text = "Producto común / Redondeo";
        Width = 560; Height = 330; StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.Gainsboro; KeyPreview = true;

        Controls.Add(new Label { Text = "AGREGAR PRODUCTO COMÚN", Location = new Point(25, 20), AutoSize = true, Font = new Font("Segoe UI", 16, FontStyle.Bold) });
        Controls.Add(new Label { Text = "Descripción", Location = new Point(25, 75), AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold) });
        description.Location = new Point(25, 100); description.Width = 470; description.Font = new Font("Segoe UI", 12); description.Text = "PRODUCTO COMÚN / REDONDEO"; Controls.Add(description);
        Controls.Add(new Label { Text = "Importe a agregar", Location = new Point(25, 145), AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold) });
        amount.Location = new Point(25, 170); amount.Width = 220; amount.DecimalPlaces = 2; amount.Maximum = 100000000; amount.Minimum = 0M; amount.Value = 0M; amount.Increment = 0.01M; amount.Font = new Font("Segoe UI", 14); Controls.Add(amount);
        Controls.Add(new Label { Text = "Usá esta opción para un importe manual o para ajustar/redondear el total.", Location = new Point(25, 215), AutoSize = true, ForeColor = Color.DimGray });
        var ok = new Button { Text = "AGREGAR · ENTER", Location = new Point(300, 165), Width = 195, Height = 45 }; ok.Click += (_, _) => Accept(); Controls.Add(ok);
        var cancel = new Button { Text = "ESC · CANCELAR", Location = new Point(300, 215), Width = 195, Height = 40 }; cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); }; Controls.Add(cancel);
        AcceptButton = ok; CancelButton = cancel;
        // El producto común usa exactamente el mismo tema que el resto del POS.
        // Esto evita que la ventana quede con la paleta clara predeterminada cuando
        // el sistema está en Grafito/Grafito Premium/Oscuro.
        ThemeService.Apply(this);

        Shown += (_, _) => { amount.Focus(); amount.Select(0, amount.Text.Length); };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
    }

    private void Accept()
    {
        if (Amount <= 0) { MessageBox.Show("Ingresá un importe mayor a cero.", "Producto común", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        DialogResult = DialogResult.OK; Close();
    }
}
