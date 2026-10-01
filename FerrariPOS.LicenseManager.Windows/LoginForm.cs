using System.Drawing.Drawing2D;

namespace FerrariPOS.LicenseManager;

public sealed class LoginForm : Form
{
    private const string AdminUser = "admin";
    private const string MasterPassword = "600613";
    private readonly Color Window = Color.FromArgb(20, 22, 25);
    private readonly Color Surface = Color.FromArgb(29, 32, 36);
    private readonly Color Input = Color.FromArgb(15, 17, 20);
    private readonly Color TextColor = Color.FromArgb(239, 242, 245);
    private readonly Color Muted = Color.FromArgb(160, 166, 174);
    private readonly Color Orange = Color.FromArgb(242, 139, 42);
    private TextBox userBox = null!, passBox = null!;
    private Label status = null!;

    public LoginForm()
    {
        Text = "FerrariPOS · Acceso administrativo";
        ClientSize = new Size(560, 390);
        MinimumSize = new Size(560, 390);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Window;
        ForeColor = TextColor;
        Font = new Font("Segoe UI", 10);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        var icon = Path.Combine(AppContext.BaseDirectory, "FerrariPOS_icono.ico");
        if (File.Exists(icon)) Icon = new Icon(icon);
        Build();
    }

    private void Build()
    {
        var card = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(42) };
        var logo = new PictureBox { Location = new Point(42, 22), Size = new Size(62, 62), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
        try { var p = Path.Combine(AppContext.BaseDirectory, "FerrariPOS_logo.png"); if (File.Exists(p)) using (var img = Image.FromFile(p)) logo.Image = new Bitmap(img); } catch { }
        card.Controls.Add(logo);
        card.Paint += (_, e) => { using var pen = new Pen(Orange, 3); e.Graphics.DrawLine(pen, 0, 0, card.Width, 0); };
        Controls.Add(card);

        card.Controls.Add(new Label { Text = "FerrariPOS", Font = new Font("Segoe UI Semibold", 25, FontStyle.Bold), ForeColor = TextColor, Location = new Point(118, 35), AutoSize = true });
        card.Controls.Add(new Label { Text = "DESARROLLADOR DE LICENCIAS", Font = new Font("Segoe UI", 10, FontStyle.Bold), ForeColor = Orange, Location = new Point(121, 78), AutoSize = true });
        card.Controls.Add(new Label { Text = "Acceso administrativo seguro", ForeColor = Muted, Location = new Point(121, 112), AutoSize = true });

        card.Controls.Add(new Label { Text = "USUARIO", ForeColor = Muted, Location = new Point(45, 155), AutoSize = true });
        userBox = new TextBox { Text = "admin", Location = new Point(45, 178), Width = 460, Height = 32, BackColor = Input, ForeColor = TextColor, BorderStyle = BorderStyle.FixedSingle };
        card.Controls.Add(userBox);

        card.Controls.Add(new Label { Text = "CONTRASEÑA MAESTRA", ForeColor = Muted, Location = new Point(45, 220), AutoSize = true });
        passBox = new TextBox { Location = new Point(45, 243), Width = 460, Height = 32, BackColor = Input, ForeColor = TextColor, BorderStyle = BorderStyle.FixedSingle, UseSystemPasswordChar = true };
        passBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) Login(); };
        card.Controls.Add(passBox);

        var login = new RoundedLoginButton { Text = "INGRESAR", Location = new Point(45, 292), Size = new Size(220, 42), BackColor = Orange, ForeColor = Color.White, Font = new Font("Segoe UI", 10, FontStyle.Bold), Cursor = Cursors.Hand, FlatStyle = FlatStyle.Flat };
        login.FlatAppearance.BorderSize = 0;
        login.Click += (_, _) => Login();
        card.Controls.Add(login);

        status = new Label { Text = "Ingrese sus credenciales administrativas.", ForeColor = Muted, Location = new Point(285, 303), AutoSize = true };
        card.Controls.Add(status);
    }

    private void Login()
    {
        if (string.Equals(userBox.Text.Trim(), AdminUser, StringComparison.OrdinalIgnoreCase) && passBox.Text == MasterPassword)
        {
            DialogResult = DialogResult.OK;
            Close();
            return;
        }
        status.Text = "Usuario o contraseña incorrectos.";
        status.ForeColor = Color.FromArgb(222, 75, 75);
        passBox.SelectAll();
        passBox.Focus();
    }

    private sealed class RoundedLoginButton : Button
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(BackColor);
            using var path = new GraphicsPath();
            var r = 10;
            path.AddArc(0, 0, r * 2, r * 2, 180, 90);
            path.AddArc(Width - r * 2, 0, r * 2, r * 2, 270, 90);
            path.AddArc(Width - r * 2, Height - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(0, Height - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            e.Graphics.FillPath(brush, path);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}
