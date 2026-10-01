using System.Drawing.Drawing2D;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FerrariPOS.LicenseManager;

public sealed class LicenseManagerForm : Form
{
    private const string Prefix = "FPOS-LIC-3";
    private readonly string privateKeyPath = Path.Combine(AppContext.BaseDirectory, "private_key.pem");
    private readonly string historyPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FerrariPOS", "LicenseManager_Corregido", "licenses.json");

    private readonly Color Window = Color.FromArgb(18, 20, 23);
    private readonly Color Surface = Color.FromArgb(27, 30, 34);
    private readonly Color Surface2 = Color.FromArgb(36, 40, 45);
    private readonly Color Input = Color.FromArgb(13, 15, 18);
    private readonly Color TextColor = Color.FromArgb(242, 244, 247);
    private readonly Color Muted = Color.FromArgb(164, 170, 178);
    private readonly Color Orange = Color.FromArgb(242, 139, 42);
    private readonly Color Green = Color.FromArgb(55, 190, 112);
    private readonly Color Red = Color.FromArgb(225, 78, 78);
    private readonly Color Yellow = Color.FromArgb(235, 191, 55);
    private readonly Color Blue = Color.FromArgb(62, 124, 205);

    private TextBox machineId = null!, customer = null!, licenseId = null!, tokenBox = null!, verifyBox = null!;
    private NumericUpDown days = null!;
    private DateTimePicker startUtc = null!;
    private ComboBox action = null!, type = null!;
    private Label status = null!, statusDetail = null!, preview = null!, keyStatus = null!, verifyStatus = null!;
    private Label expiryAlert = null!;
    private DataGridView historyGrid = null!;
    private RichTextBox diagnosticOutput = null!;
    private Label totalCount = null!, activeCount = null!, expiringCount = null!, permanentCount = null!;
    private readonly List<LicenseRecord> records = new();

    public LicenseManagerForm()
    {
        Text = "Ferrari'sPOS · Desarrollador de Licencias PRO";
        Width = 1380;
        Height = 900;
        MinimumSize = new Size(1100, 720);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Window;
        ForeColor = TextColor;
        Font = new Font("Segoe UI", 9.5f);
        FormBorderStyle = FormBorderStyle.Sizable;
        var icon = Path.Combine(AppContext.BaseDirectory, "FerrariPOS_icono.ico");
        if (File.Exists(icon)) Icon = new Icon(icon);
        Build();
        LoadHistory();
        UpdateKeyStatus();
    }

    private void Build()
    {
        var tabs = new TabControl
        {
            Dock = DockStyle.Fill,
            BackColor = Window,
            ForeColor = TextColor,
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold)
        };

        tabs.TabPages.Add(MakePage("  PANEL PRINCIPAL  ", BuildDashboard()));
        tabs.TabPages.Add(MakePage("  GENERAR LICENCIA  ", BuildGenerator()));
        tabs.TabPages.Add(MakePage("  LICENCIAS  ", BuildLicenseCenter()));
        tabs.TabPages.Add(MakePage("  VERIFICAR / DIAGNÓSTICO  ", BuildVerifier()));
        tabs.TabPages.Add(MakePage("  AYUDA  ", BuildHelp()));

        Controls.Add(tabs);
    }

    private TabPage MakePage(string title, Control content)
    {
        var page = new TabPage(title) { BackColor = Window, ForeColor = TextColor, Padding = new Padding(10) };
        content.Dock = DockStyle.Fill;
        page.Controls.Add(content);
        return page;
    }

    private Control BuildDashboard()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, BackColor = Window, ColumnCount = 1, RowCount = 4, Padding = new Padding(4) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 115));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = Header("PANEL DE CONTROL", "Resumen del desarrollador, licencias generadas y próximos vencimientos.");
        root.Controls.Add(header, 0, 0);

        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, BackColor = Window };
        for (int i = 0; i < 4; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        cards.Controls.Add(StatCard("LICENCIAS GENERADAS", "0", Orange, out totalCount), 0, 0);
        cards.Controls.Add(StatCard("ACTIVAS", "0", Green, out activeCount), 1, 0);
        cards.Controls.Add(StatCard("POR VENCER ≤ 5 DÍAS", "0", Yellow, out expiringCount), 2, 0);
        cards.Controls.Add(StatCard("PERMANENTES", "0", Blue, out permanentCount), 3, 0);
        root.Controls.Add(cards, 0, 1);

        expiryAlert = new Label
        {
            Dock = DockStyle.Fill,
            Visible = false,
            BackColor = Color.FromArgb(58, 49, 22),
            ForeColor = Yellow,
            Font = new Font("Segoe UI Semibold", 10, FontStyle.Bold),
            Padding = new Padding(14, 12, 14, 8),
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.Controls.Add(expiryAlert, 0, 2);

        var recentPanel = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(18) };
        recentPanel.Controls.Add(new Label { Text = "ÚLTIMAS LICENCIAS", ForeColor = TextColor, Font = new Font("Segoe UI Semibold", 13, FontStyle.Bold), Dock = DockStyle.Top, Height = 34 });
        var recent = new DataGridView
        {
            Dock = DockStyle.Fill, BackgroundColor = Surface, BorderStyle = BorderStyle.None,
            ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AutoGenerateColumns = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            EnableHeadersVisualStyles = false
        };
        ConfigureGrid(recent);
        recent.Columns.Add(TextColumn("Cliente", nameof(LicenseRecord.Customer), 180));
        recent.Columns.Add(TextColumn("Licencia", nameof(LicenseRecord.LicenseId), 180));
        recent.Columns.Add(TextColumn("Tipo", nameof(LicenseRecord.Type), 110));
        recent.Columns.Add(TextColumn("Vencimiento", nameof(LicenseRecord.ExpiresUtc), 180));
        recent.Columns.Add(TextColumn("Estado", nameof(LicenseRecord.Status), 110));
        recent.Columns.Add(TextColumn("Generada", nameof(LicenseRecord.GeneratedLocal), 140));
        recent.CellFormatting += HistoryGrid_CellFormatting;
        recent.DoubleBuffered(true);
        recentPanel.Controls.Add(recent);
        recent.Tag = "dashboardRecent";
        root.Controls.Add(recentPanel, 0, 3);
        return root;
    }

    private Control BuildGenerator()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Window, Padding = new Padding(4) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 500));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(Header("NUEVA LICENCIA", "Generá una licencia exacta y compatible sin enviar DurationDays al cliente existente."), 0, 0);

        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(18), AutoScroll = true };
        panel.Controls.Add(new Label { Text = "DATOS DE LA LICENCIA", ForeColor = Orange, Font = new Font("Segoe UI Semibold", 11, FontStyle.Bold), Location = new Point(18, 14), AutoSize = true });

        machineId = Field(panel, "Machine ID", 18, 48, 440, "Pegá el Machine ID completo del cliente");
        customer = Field(panel, "Cliente / empresa", 476, 48, 260, "Nombre opcional");
        licenseId = Field(panel, "ID de licencia", 754, 48, 260, "Vacío = generar automáticamente");

        action = Combo(panel, "Acción", 18, 116, 180, new[] { "ACTIVATE", "RENEW", "DEACTIVATE" });
        type = Combo(panel, "Tipo", 214, 116, 180, new[] { "CUSTOM", "ANNUAL", "PERMANENT" });
        AddLabel(panel, "Días exactos", 410, 116);
        days = new NumericUpDown { Minimum = 1, Maximum = 36500, Value = 90, Width = 130, Location = new Point(410, 139), BackColor = Input, ForeColor = TextColor, BorderStyle = BorderStyle.FixedSingle };
        panel.Controls.Add(days);
        AddLabel(panel, "Inicio de vigencia", 558, 116);
        startUtc = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd/MM/yyyy HH:mm:ss", ShowUpDown = true, Value = DateTime.Now, Width = 190, Location = new Point(558, 139) };
        panel.Controls.Add(startUtc);
        var now = Button("AHORA", Blue, 758, 138, 90, 34); now.Click += (_, _) => startUtc.Value = DateTime.Now; panel.Controls.Add(now);
        var generate = Button("GENERAR LICENCIA", Orange, 860, 132, 180, 46); generate.Click += (_, _) => Generate(); panel.Controls.Add(generate);

        preview = new Label { Text = "Vista previa: —", ForeColor = Yellow, Location = new Point(18, 188), Size = new Size(1020, 40), AutoEllipsis = true };
        panel.Controls.Add(preview);
        status = new Label { Text = "LISTO", ForeColor = Green, Font = new Font("Segoe UI Semibold", 11, FontStyle.Bold), Location = new Point(18, 232), AutoSize = true };
        panel.Controls.Add(status);
        statusDetail = new Label { Text = "El token generado se autoverifica antes de ser entregado.", ForeColor = Muted, Location = new Point(18, 260), AutoSize = true };
        panel.Controls.Add(statusDetail);

        panel.Controls.Add(new Label { Text = "TOKEN DE LICENCIA GENERADO", ForeColor = Orange, Font = new Font("Segoe UI Semibold", 11, FontStyle.Bold), Location = new Point(18, 300), AutoSize = true });

        var tokenArea = new TableLayoutPanel
        {
            Location = new Point(18, 330), Size = new Size(1040, 112), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            ColumnCount = 2, RowCount = 1, BackColor = Input, Padding = new Padding(1)
        };
        tokenArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tokenArea.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        tokenBox = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, BackColor = Input, ForeColor = TextColor, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10.5f), ShortcutsEnabled = true, Margin = new Padding(10) };
        tokenArea.Controls.Add(tokenBox, 0, 0);

        var tokenButtons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(6), BackColor = Surface2 };
        tokenButtons.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        tokenButtons.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var copy = Button("COPIAR TOKEN", Blue, 0, 0, 160, 42); copy.Dock = DockStyle.Fill; copy.Click += (_, _) => CopyToken();
        var save = Button("GUARDAR TOKEN", Green, 0, 0, 160, 42); save.Dock = DockStyle.Fill; save.Click += (_, _) => SaveToken();
        tokenButtons.Controls.Add(copy, 0, 0); tokenButtons.Controls.Add(save, 0, 1);
        tokenArea.Controls.Add(tokenButtons, 1, 0);
        panel.Controls.Add(tokenArea);

        var hint = new Label { Text = "Los botones quedan siempre visibles a la derecha del token. También podés seleccionar/copiar el texto directamente.", ForeColor = Muted, Location = new Point(18, 452), AutoSize = true };
        panel.Controls.Add(hint);

        action.SelectedIndexChanged += (_, _) => UpdateGeneratorState();
        type.SelectedIndexChanged += (_, _) => UpdateGeneratorState();
        panel.Resize += (_, _) => tokenArea.Width = Math.Max(760, panel.ClientSize.Width - 36);
        UpdateGeneratorState();
        root.Controls.Add(panel, 0, 1);

        var info = new Panel { Dock = DockStyle.Fill, BackColor = Window, Padding = new Padding(0, 8, 0, 0) };
        info.Controls.Add(new Label { Text = "REGLA DE VENCIMIENTO", ForeColor = TextColor, Font = new Font("Segoe UI Semibold", 11, FontStyle.Bold), Dock = DockStyle.Top, Height = 28 });
        info.Controls.Add(new Label { Text = "Las licencias finitas guardan ExpiresAtUtc como fecha absoluta. DurationDays se mantiene NULL para evitar que el cliente descuente la antigüedad de la instalación.", ForeColor = Muted, Dock = DockStyle.Fill, Padding = new Padding(0, 4, 0, 0) });
        root.Controls.Add(info, 0, 2);
        return root;
    }

    private Control BuildLicenseCenter()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Window, Padding = new Padding(4) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(Header("CENTRO DE LICENCIAS", "Consultá, filtrá y abrí el detalle de cualquier licencia generada."), 0, 0);

        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(14) };
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 45, BackColor = Surface, WrapContents = false };
        var refresh = Button("ACTUALIZAR", Blue, 0, 0, 120, 34); refresh.Click += (_, _) => { LoadHistory(); RefreshAllViews(); }; toolbar.Controls.Add(refresh);
        var detail = Button("VER DETALLE", Orange, 0, 0, 130, 34); detail.Click += (_, _) => ShowSelectedLicense(); toolbar.Controls.Add(detail);
        var copy = Button("COPIAR HASH", Green, 0, 0, 130, 34); copy.Click += (_, _) => CopySelectedHash(); toolbar.Controls.Add(copy);
        panel.Controls.Add(toolbar);

        historyGrid = new DataGridView { Dock = DockStyle.Fill, BackgroundColor = Surface, BorderStyle = BorderStyle.None, AllowUserToAddRows = false, AllowUserToDeleteRows = false, ReadOnly = true, AutoGenerateColumns = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, RowHeadersVisible = false, EnableHeadersVisualStyles = false, Margin = new Padding(0, 8, 0, 0) };
        ConfigureGrid(historyGrid);
        historyGrid.Columns.Add(TextColumn("Cliente", nameof(LicenseRecord.Customer), 170));
        historyGrid.Columns.Add(TextColumn("Licencia", nameof(LicenseRecord.LicenseId), 175));
        historyGrid.Columns.Add(TextColumn("Acción", nameof(LicenseRecord.Action), 90));
        historyGrid.Columns.Add(TextColumn("Tipo", nameof(LicenseRecord.Type), 95));
        historyGrid.Columns.Add(TextColumn("Días", nameof(LicenseRecord.DurationDays), 65));
        historyGrid.Columns.Add(TextColumn("Vencimiento UTC", nameof(LicenseRecord.ExpiresUtc), 180));
        historyGrid.Columns.Add(TextColumn("Estado", nameof(LicenseRecord.Status), 105));
        historyGrid.Columns.Add(TextColumn("Huella", nameof(LicenseRecord.TokenHashShort), 105));
        historyGrid.Columns.Add(TextColumn("Generada", nameof(LicenseRecord.GeneratedLocal), 140));
        historyGrid.CellFormatting += HistoryGrid_CellFormatting;
        historyGrid.DoubleClick += (_, _) => ShowSelectedLicense();
        panel.Controls.Add(historyGrid);
        root.Controls.Add(panel, 0, 1);
        return root;
    }

    private Control BuildVerifier()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Window, Padding = new Padding(4) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(Header("VERIFICACIÓN / DIAGNÓSTICO", "Comprobá firma, Machine ID, fechas, días restantes y contenido real del token."), 0, 0);

        var top = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(14) };
        top.Controls.Add(new Label { Text = "PEGAR TOKEN", ForeColor = Orange, Font = new Font("Segoe UI Semibold", 10, FontStyle.Bold), Location = new Point(14, 10), AutoSize = true });
        verifyBox = new TextBox { Location = new Point(14, 35), Size = new Size(950, 58), Multiline = true, BackColor = Input, ForeColor = TextColor, BorderStyle = BorderStyle.FixedSingle, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 9.5f), PlaceholderText = "FPOS-LIC-3..." };
        top.Controls.Add(verifyBox);
        var verify = Button("VERIFICAR TOKEN", Orange, 985, 35, 165, 58); verify.Click += (_, _) => VerifyToken(); top.Controls.Add(verify);
        root.Controls.Add(top, 0, 1);

        var result = new Panel { Dock = DockStyle.Fill, BackColor = Surface };
        verifyStatus = new Label { Text = "ESPERANDO TOKEN", ForeColor = Muted, Font = new Font("Segoe UI Semibold", 12, FontStyle.Bold), Location = new Point(16, 14), AutoSize = true };
        result.Controls.Add(verifyStatus);
        diagnosticOutput = new RichTextBox { Location = new Point(16, 48), Size = new Size(1100, 500), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, ReadOnly = true, BackColor = Input, ForeColor = TextColor, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 10), Padding = new Padding(10) };
        result.Controls.Add(diagnosticOutput);
        root.Controls.Add(result, 0, 2);
        return root;
    }

    private Control BuildHelp()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Padding = new Padding(28), AutoScroll = true };
        var text = new StringBuilder();
        text.AppendLine("FERRARI'SPOS · DESARROLLADOR DE LICENCIAS");
        text.AppendLine();
        text.AppendLine("1. GENERAR LICENCIA");
        text.AppendLine("Pegá el Machine ID, elegí acción, tipo y cantidad de días. Las licencias finitas se calculan mediante una fecha de vencimiento absoluta.");
        text.AppendLine();
        text.AppendLine("2. COPIAR / GUARDAR TOKEN");
        text.AppendLine("Los botones están permanentemente visibles al lado derecho del token generado.");
        text.AppendLine();
        text.AppendLine("3. CENTRO DE LICENCIAS");
        text.AppendLine("Doble clic sobre una fila para ver el detalle completo de la licencia, vencimiento y huella SHA-256.");
        text.AppendLine();
        text.AppendLine("4. AVISOS");
        text.AppendLine("El panel muestra las licencias que quedan con 5 días o menos. El aviso no modifica ninguna licencia.");
        text.AppendLine();
        text.AppendLine("5. SEGURIDAD");
        text.AppendLine("La clave privada debe permanecer exclusivamente en la computadora administrativa y nunca debe entregarse al cliente.");
        var box = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Surface, ForeColor = TextColor, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 11), Text = text.ToString() };
        panel.Controls.Add(box);
        return panel;
    }

    private Panel Header(string title, string subtitle)
    {
        var header = new Panel { Dock = DockStyle.Fill, BackColor = Surface };
        header.Paint += (_, e) => { using var p = new Pen(Orange, 2); e.Graphics.DrawLine(p, 0, header.Height - 1, header.Width, header.Height - 1); };
        var logo = new PictureBox { Location = new Point(14, 12), Size = new Size(42, 42), SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Transparent };
        try { var p = Path.Combine(AppContext.BaseDirectory, "FerrariPOS_logo.png"); if (File.Exists(p)) using (var img = Image.FromFile(p)) logo.Image = new Bitmap(img); } catch { }
        header.Controls.Add(logo);
        header.Controls.Add(new Label { Text = "FerrariPOS", ForeColor = TextColor, Font = new Font("Segoe UI Semibold", 18, FontStyle.Bold), Location = new Point(64, 9), AutoSize = true });
        header.Controls.Add(new Label { Text = title, ForeColor = Orange, Font = new Font("Segoe UI Semibold", 10, FontStyle.Bold), Location = new Point(20, 43), AutoSize = true });
        header.Controls.Add(new Label { Text = subtitle, ForeColor = Muted, Location = new Point(245, 44), AutoSize = true });
        keyStatus = keyStatus ?? new Label();
        if (!header.Controls.Contains(keyStatus))
        {
            keyStatus = new Label { Text = "Comprobando clave...", ForeColor = Muted, AutoSize = true, Anchor = AnchorStyles.Top | AnchorStyles.Right };
            header.Controls.Add(keyStatus);
            header.Resize += (_, _) => keyStatus.Location = new Point(Math.Max(700, header.ClientSize.Width - keyStatus.PreferredWidth - 18), 18);
        }
        return header;
    }

    private Panel StatCard(string title, string value, Color accent, out Label valueLabel)
    {
        var p = new Panel { Dock = DockStyle.Fill, BackColor = Surface, Margin = new Padding(5), Padding = new Padding(16) };
        p.Paint += (_, e) => { using var b = new SolidBrush(accent); e.Graphics.FillRectangle(b, 0, 0, 5, p.Height); };
        p.Controls.Add(new Label { Text = title, ForeColor = Muted, Font = new Font("Segoe UI Semibold", 8.5f, FontStyle.Bold), Location = new Point(16, 12), AutoSize = true });
        valueLabel = new Label { Text = value, ForeColor = accent, Font = new Font("Segoe UI Semibold", 22, FontStyle.Bold), Location = new Point(16, 34), AutoSize = true };
        p.Controls.Add(valueLabel);
        return p;
    }

    private void UpdateKeyStatus()
    {
        if (File.Exists(privateKeyPath))
        {
            try
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(File.ReadAllText(privateKeyPath));
                keyStatus.Text = $"CLAVE OK · RSA-{rsa.KeySize} · {Prefix}";
                keyStatus.ForeColor = Green;
            }
            catch { keyStatus.Text = "CLAVE INVÁLIDA"; keyStatus.ForeColor = Red; }
        }
        else { keyStatus.Text = "FALTA private_key.pem"; keyStatus.ForeColor = Red; }
    }

    private void UpdateGeneratorState()
    {
        var permanent = type?.Text == "PERMANENT";
        var deactivate = action?.Text == "DEACTIVATE";
        if (days != null) days.Enabled = !permanent && !deactivate;
        if (startUtc != null) startUtc.Enabled = !permanent && !deactivate;
        if (preview != null)
        {
            if (deactivate) preview.Text = "Vista previa: DESACTIVACIÓN · no se envía duración ni vencimiento.";
            else if (permanent) preview.Text = "Vista previa: PERMANENTE · ExpiresAtUtc = DateTime.MaxValue.";
            else preview.Text = $"Vista previa: {days.Value} días exactos desde {startUtc.Value:dd/MM/yyyy HH:mm:ss} local · se firmará una fecha UTC.";
        }
    }

    private void Generate()
    {
        if (!File.Exists(privateKeyPath)) { SetStatus("ERROR", "No se encontró private_key.pem.", Red); return; }
        var machine = Normalize(machineId.Text);
        if (machine.Length < 16) { SetStatus("ERROR", "Machine ID no válido. Pegalo completo.", Red); return; }
        var actionValue = action.Text.Trim().ToUpperInvariant();
        var typeValue = type.Text.Trim().ToUpperInvariant();
        if ((actionValue == "RENEW" || actionValue == "DEACTIVATE") && string.IsNullOrWhiteSpace(licenseId.Text)) { SetStatus("ERROR", "Para esta acción necesitás el ID de licencia actual.", Red); return; }
        var id = string.IsNullOrWhiteSpace(licenseId.Text) ? "FPOS-" + Guid.NewGuid().ToString("N")[..16].ToUpperInvariant() : licenseId.Text.Trim().ToUpperInvariant();
        int? duration = null;
        DateTime? expires = null;
        if (actionValue != "DEACTIVATE" && typeValue != "PERMANENT")
        {
            duration = null;
            var local = DateTime.SpecifyKind(startUtc.Value, DateTimeKind.Local);
            expires = local.ToUniversalTime().AddDays((double)days.Value);
            if (expires.Value <= DateTime.UtcNow) { SetStatus("ERROR", "La fecha de vencimiento calculada ya pasó.", Red); return; }
        }
        else if (typeValue == "PERMANENT" && actionValue != "DEACTIVATE") expires = DateTime.MaxValue;

        var payload = new Payload { Action = actionValue, LicenseId = id, MachineId = machine, Type = typeValue, IssuedAtUtc = DateTime.UtcNow, ExpiresAtUtc = expires, DurationDays = duration, CustomerName = customer.Text.Trim() };
        var json = JsonSerializer.Serialize(payload);
        var bytes = Encoding.UTF8.GetBytes(json);
        byte[] signature;
        try { using var rsa = RSA.Create(); rsa.ImportFromPem(File.ReadAllText(privateKeyPath)); signature = rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss); }
        catch (Exception ex) { SetStatus("ERROR", "No se pudo firmar: " + ex.Message, Red); return; }
        var token = Prefix + "." + B64(bytes) + "." + B64(signature);
        if (!VerifySignature(token, out var error)) { SetStatus("ERROR", "Autoverificación fallida: " + error, Red); return; }
        tokenBox.Text = token;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        var expiryText = expires.HasValue ? expires.Value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'") : "—";
        records.Insert(0, new LicenseRecord { Customer = string.IsNullOrWhiteSpace(payload.CustomerName) ? "(sin nombre)" : payload.CustomerName, LicenseId = id, Action = actionValue, Type = typeValue, DurationDays = actionValue == "DEACTIVATE" || typeValue == "PERMANENT" ? null : (int?)days.Value, ExpiresUtc = expiryText, Status = actionValue == "DEACTIVATE" ? "DESACTIVADA" : typeValue == "PERMANENT" ? "PERMANENTE" : "ACTIVA", TokenHashShort = hash[..12], GeneratedLocal = DateTime.Now.ToString("dd/MM/yyyy HH:mm") });
        SaveHistory(); RefreshAllViews();
        var output = Path.Combine(Environment.CurrentDirectory, id + ".txt"); File.WriteAllText(output, token + Environment.NewLine, Encoding.UTF8);
        if (typeValue == "PERMANENT") SetStatus("GENERADA Y VERIFICADA", $"Permanente · guardada en {output}", Green);
        else if (actionValue == "DEACTIVATE") SetStatus("GENERADA Y VERIFICADA", $"Desactivación · guardada en {output}", Green);
        else SetStatus("GENERADA Y VERIFICADA", $"Vence exactamente el {expires!.Value:dd/MM/yyyy HH:mm:ss} UTC · DurationDays NO enviado.", Green);
    }

    private void VerifyToken()
    {
        var raw = NormalizePastedToken(verifyBox.Text);
        if (string.IsNullOrWhiteSpace(raw)) { SetVerify("ERROR", "Pegá un token antes de verificar.", Red); return; }
        try
        {
            var parts = raw.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3 || parts[0] != Prefix) throw new InvalidOperationException("Formato incorrecto. Se espera FPOS-LIC-3.<payload>.<firma>.");
            var payloadBytes = Base64UrlDecode(parts[1]); var signature = Base64UrlDecode(parts[2]);
            var payload = JsonSerializer.Deserialize<Payload>(Encoding.UTF8.GetString(payloadBytes)) ?? throw new InvalidOperationException("Payload vacío o ilegible.");
            using var rsa = RSA.Create(); rsa.ImportFromPem(File.ReadAllText(privateKeyPath));
            if (!rsa.VerifyData(payloadBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss)) throw new InvalidOperationException("FIRMA RSA INVÁLIDA.");
            var lines = new List<string> { "========== DIAGNÓSTICO DE LICENCIA ==========", "Firma RSA:                 VÁLIDA", $"Formato:                   {parts[0]}", $"Acción:                    {payload.Action}", $"Tipo:                      {payload.Type}", $"License ID:                {payload.LicenseId}", $"Machine ID:                {payload.MachineId}", $"Cliente:                   {payload.CustomerName}", $"IssuedAtUtc:               {payload.IssuedAtUtc:O}", $"DurationDays en token:     {(payload.DurationDays.HasValue ? payload.DurationDays.Value.ToString() : "NULL (CORRECTO)")}", $"ExpiresAtUtc en token:     {(payload.ExpiresAtUtc.HasValue ? payload.ExpiresAtUtc.Value.ToString("O") : "NULL")}" };
            if (payload.DurationDays.HasValue && payload.ExpiresAtUtc.HasValue) lines.Add("ADVERTENCIA: contiene DurationDays Y ExpiresAtUtc. El cliente existente prioriza DurationDays.");
            else if (payload.Action is "ACTIVATE" or "RENEW" && payload.Type != "PERMANENT" && !payload.DurationDays.HasValue && payload.ExpiresAtUtc.HasValue) lines.Add("MODO DE VENCIMIENTO: EXACTO · compatible sin descontar la antigüedad de instalación.");
            if (payload.ExpiresAtUtc.HasValue && payload.ExpiresAtUtc.Value != DateTime.MaxValue)
            {
                var daysLeft = Math.Ceiling((payload.ExpiresAtUtc.Value.ToUniversalTime() - DateTime.UtcNow).TotalDays);
                lines.Add($"Días restantes desde AHORA: {daysLeft:0}");
                lines.Add($"Vencimiento local:          {payload.ExpiresAtUtc.Value.ToLocalTime():dd/MM/yyyy HH:mm:ss}");
            }
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
            lines.Add($"SHA-256 token:              {hash}"); lines.Add("==============================================");
            diagnosticOutput.Text = string.Join(Environment.NewLine, lines);
            SetVerify("TOKEN VÁLIDO", "Firma y contenido correctos.", Green);
        }
        catch (Exception ex) { diagnosticOutput.Text = "========== ERROR ==========\r\n" + ex.Message + "\r\n============================"; SetVerify("TOKEN INVÁLIDO", ex.Message, Red); }
    }

    private void CopyToken()
    {
        if (string.IsNullOrWhiteSpace(tokenBox.Text)) { SetStatus("SIN TOKEN", "Generá una licencia primero.", Yellow); return; }
        try { Clipboard.SetText(tokenBox.Text.Trim()); SetStatus("TOKEN COPIADO", "El token está ahora en el portapapeles.", Green); } catch (Exception ex) { SetStatus("ERROR", "No se pudo copiar: " + ex.Message, Red); }
    }

    private void SaveToken()
    {
        if (string.IsNullOrWhiteSpace(tokenBox.Text)) { SetStatus("SIN TOKEN", "Generá una licencia primero.", Yellow); return; }
        using var dialog = new SaveFileDialog { Title = "Guardar token de licencia", Filter = "Token de licencia (*.txt)|*.txt|Todos los archivos (*.*)|*.*", DefaultExt = "txt", AddExtension = true, FileName = string.IsNullOrWhiteSpace(licenseId.Text) ? "FerrariPOS_Token.txt" : $"FerrariPOS_{licenseId.Text.Trim()}_Token.txt", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllText(dialog.FileName, tokenBox.Text.Trim(), new UTF8Encoding(false)); SetStatus("TOKEN GUARDADO", "Licencia guardada correctamente.", Green); } catch (Exception ex) { SetStatus("ERROR", "No se pudo guardar: " + ex.Message, Red); }
    }

    private void CheckExpiringLicenses(bool showPopup = false)
    {
        var expiring = new List<string>();
        foreach (var r in records)
        {
            if (r.Status != "ACTIVA" || !DateTime.TryParse(r.ExpiresUtc, out var expiry)) continue;
            var left = Math.Ceiling((expiry.ToUniversalTime() - DateTime.UtcNow).TotalDays);
            if (left <= 5 && left >= 0) expiring.Add($"{r.Customer} · {r.LicenseId} · {left:0} día(s)");
        }
        if (expiryAlert != null)
        {
            expiryAlert.Visible = expiring.Count > 0;
            expiryAlert.Text = expiring.Count > 0 ? "⚠  ATENCIÓN: licencias por vencer en 5 días o menos: " + string.Join("   |   ", expiring) : "";
        }
        if (showPopup && expiring.Count > 0)
            MessageBox.Show(this, "Hay licencias que vencen en 5 días o menos:\n\n" + string.Join("\n", expiring), "Aviso de vencimiento", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void RefreshAllViews()
    {
        if (historyGrid != null) { historyGrid.DataSource = null; historyGrid.DataSource = records; }
        totalCount.Text = records.Count.ToString();
        activeCount.Text = records.Count(r => r.Status == "ACTIVA").ToString();
        permanentCount.Text = records.Count(r => r.Status == "PERMANENTE").ToString();
        expiringCount.Text = records.Count(HasFiveDaysOrLess).ToString();
        CheckExpiringLicenses();
        foreach (Control c in Controls) RefreshDashboardGrid(c);
    }

    private void RefreshDashboardGrid(Control root)
    {
        if (root is DataGridView grid && Equals(grid.Tag, "dashboardRecent"))
        {
            grid.DataSource = null; grid.DataSource = records.Take(12).ToList();
        }
        foreach (Control child in root.Controls) RefreshDashboardGrid(child);
    }

    private bool HasFiveDaysOrLess(LicenseRecord r)
    {
        if (r.Status != "ACTIVA" || !DateTime.TryParse(r.ExpiresUtc, out var expiry)) return false;
        var left = Math.Ceiling((expiry.ToUniversalTime() - DateTime.UtcNow).TotalDays);
        return left <= 5 && left >= 0;
    }

    private void LoadHistory()
    {
        try
        {
            if (!File.Exists(historyPath)) { RefreshAllViews(); return; }
            var loaded = JsonSerializer.Deserialize<List<LicenseRecord>>(File.ReadAllText(historyPath));
            if (loaded != null) { records.Clear(); records.AddRange(loaded); }
        }
        catch { records.Clear(); }
        RefreshAllViews();
    }

    private void SaveHistory()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(historyPath)!); File.WriteAllText(historyPath, JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true })); } catch { }
    }

    private void ShowSelectedLicense()
    {
        if (historyGrid?.CurrentRow?.DataBoundItem is not LicenseRecord r) { MessageBox.Show(this, "Seleccioná una licencia primero.", "Centro de licencias", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var f = new Form { Text = "Detalle de licencia · " + r.LicenseId, Width = 720, Height = 520, StartPosition = FormStartPosition.CenterParent, BackColor = Surface, ForeColor = TextColor, MinimizeBox = false, MaximizeBox = false };
        var box = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Input, ForeColor = TextColor, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 10.5f), Padding = new Padding(18), Text = BuildRecordDetails(r) };
        f.Controls.Add(box); f.ShowDialog(this);
    }

    private string BuildRecordDetails(LicenseRecord r)
    {
        return $"FERRARI'SPOS · DETALLE DE LICENCIA\r\n\r\nCliente:              {r.Customer}\r\nLicense ID:           {r.LicenseId}\r\nAcción:               {r.Action}\r\nTipo:                 {r.Type}\r\nDías nominales:       {(r.DurationDays.HasValue ? r.DurationDays.Value.ToString() : "—")}\r\nVencimiento UTC:      {r.ExpiresUtc}\r\nEstado:               {r.Status}\r\nHuella SHA-256:       {r.TokenHashShort}\r\nGenerada:             {r.GeneratedLocal}\r\n\r\nLa huella permite identificar el token generado sin mostrar la clave privada.";
    }

    private void CopySelectedHash()
    {
        if (historyGrid?.CurrentRow?.DataBoundItem is not LicenseRecord r) { MessageBox.Show(this, "Seleccioná una licencia primero.", "Centro de licencias", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        Clipboard.SetText(r.TokenHashShort); MessageBox.Show(this, "Huella copiada al portapapeles.", "Centro de licencias", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void SetStatus(string title, string detail, Color color) { status.Text = title; status.ForeColor = color; statusDetail.Text = detail; }
    private void SetVerify(string title, string detail, Color color) { verifyStatus.Text = title + " · " + detail; verifyStatus.ForeColor = color; }

    private void ConfigureGrid(DataGridView grid)
    {
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = Surface2, ForeColor = Muted, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold), Padding = new Padding(5) };
        grid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = Surface, ForeColor = TextColor, SelectionBackColor = Color.FromArgb(58, 65, 74), SelectionForeColor = TextColor, Padding = new Padding(3) };
        grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(32, 35, 39) };
        grid.RowTemplate.Height = 30;
    }

    private void HistoryGrid_CellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (sender is not DataGridView grid || e.RowIndex < 0 || e.ColumnIndex < 0) return;
        if (grid.Columns[e.ColumnIndex].DataPropertyName != nameof(LicenseRecord.Status)) return;
        var value = e.Value?.ToString() ?? "";
        e.CellStyle.ForeColor = value switch { "ACTIVA" => Green, "PERMANENTE" => Green, "DESACTIVADA" => Red, _ => Yellow };
        e.CellStyle.SelectionForeColor = e.CellStyle.ForeColor;
        e.CellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
    }

    private static DataGridViewTextBoxColumn TextColumn(string header, string prop, int width) => new() { HeaderText = header, DataPropertyName = prop, Width = width, SortMode = DataGridViewColumnSortMode.NotSortable };
    private TextBox Field(Control parent, string caption, int x, int y, int width, string placeholder) { AddLabel(parent, caption, x, y); var box = new TextBox { Location = new Point(x, y + 21), Width = width, BackColor = Input, ForeColor = TextColor, BorderStyle = BorderStyle.FixedSingle, PlaceholderText = placeholder }; parent.Controls.Add(box); return box; }
    private void AddLabel(Control parent, string text, int x, int y) => parent.Controls.Add(new Label { Text = text, ForeColor = Muted, Location = new Point(x, y), AutoSize = true });
    private ComboBox Combo(Control parent, string caption, int x, int y, int width, string[] values) { AddLabel(parent, caption, x, y); var box = new ComboBox { Location = new Point(x, y + 21), Width = width, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Input, ForeColor = TextColor, FlatStyle = FlatStyle.Flat }; box.Items.AddRange(values); box.SelectedIndex = 0; parent.Controls.Add(box); return box; }
    private Button Button(string text, Color color, int x, int y, int width, int height) => new RoundedButton { Text = text, Location = new Point(x, y), Size = new Size(width, height), BackColor = color, ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI Semibold", 9, FontStyle.Bold), Cursor = Cursors.Hand };
    private static string Normalize(string value) => Regex.Replace(value ?? "", @"\s+", "").ToUpperInvariant();
    private static string NormalizePastedToken(string value) { var text = (value ?? "").Trim(); var match = Regex.Match(text, @"FPOS-LIC-3\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+"); return match.Success ? match.Value : Normalize(text); }
    private static string B64(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Base64UrlDecode(string value) { var s = value.Replace('-', '+').Replace('_', '/'); switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; } return Convert.FromBase64String(s); }
    private bool VerifySignature(string token, out string error)
    {
        error = "";
        try { var parts = NormalizePastedToken(token).Split('.', StringSplitOptions.RemoveEmptyEntries); if (parts.Length != 3 || parts[0] != Prefix) { error = "Formato FPOS-LIC-3 incorrecto."; return false; } using var rsa = RSA.Create(); rsa.ImportFromPem(File.ReadAllText(privateKeyPath)); return rsa.VerifyData(Base64UrlDecode(parts[1]), Base64UrlDecode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pss); }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    private sealed class Payload
    {
        public string Action { get; set; } = "ACTIVATE";
        public string LicenseId { get; set; } = "";
        public string MachineId { get; set; } = "";
        public string Type { get; set; } = "CUSTOM";
        public DateTime IssuedAtUtc { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
        public int? DurationDays { get; set; }
        public string CustomerName { get; set; } = "";
    }

    private sealed class LicenseRecord
    {
        public string Customer { get; set; } = "";
        public string LicenseId { get; set; } = "";
        public string Action { get; set; } = "";
        public string Type { get; set; } = "";
        public int? DurationDays { get; set; }
        public string ExpiresUtc { get; set; } = "";
        public string Status { get; set; } = "NUEVA";
        public string TokenHashShort { get; set; } = "";
        public string GeneratedLocal { get; set; } = "";
    }

    private sealed class RoundedButton : Button
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(BackColor);
            using var path = new GraphicsPath();
            var r = Math.Min(10, Math.Min(Width, Height) / 2);
            path.AddArc(0, 0, r * 2, r * 2, 180, 90); path.AddArc(Width - r * 2, 0, r * 2, r * 2, 270, 90); path.AddArc(Width - r * 2, Height - r * 2, r * 2, r * 2, 0, 90); path.AddArc(0, Height - r * 2, r * 2, r * 2, 90, 90); path.CloseFigure();
            e.Graphics.FillPath(brush, path);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }
}

internal static class ControlExtensions
{
    public static void DoubleBuffered(this Control control, bool enabled)
    {
        typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(control, enabled);
    }
}
