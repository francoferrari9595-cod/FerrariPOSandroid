using System.Drawing.Printing;
using System.Drawing;
using System.Globalization;
using FerrarisPOS.Data;
using FerrarisPOS.Services;

namespace FerrarisPOS.Forms;

public class SettingsForm : Form
{
    private readonly Label license = new(), licenseDetails = new();
    private readonly TextBox key = new(), rate = new(), business = new(), currency = new();
    private readonly SafeComboBox businessType = new();
    private readonly SafeComboBox theme = new(), resolution = new();
    private readonly NumericUpDown fontSize = new();
    private readonly TrackBar brightness = new();
    private readonly CheckBox fullscreen = new();
    private readonly CheckBox emailEnabled = new();
    private readonly CheckBox posLinkEmail = new();
    private readonly CheckBox cashSoundEnabled = new();
    private readonly TextBox emailTo = new(), smtpHost = new(), smtpPort = new(), smtpUser = new(), smtpPassword = new();
    private readonly CheckBox smtpSsl = new();
    private readonly CheckBox email2Enabled = new();
    private readonly TextBox emailTo2 = new(), smtpHost2 = new(), smtpPort2 = new(), smtpUser2 = new(), smtpPassword2 = new();
    private readonly CheckBox smtpSsl2 = new();
    private readonly CheckBox whatsappEnabled = new();
    private readonly CheckBox tablesEnabled = new();
    private readonly CheckBox tablesInMain = new();
    private readonly CheckBox allowAndroidCharge = new();
    private readonly CheckBox tablesEditEnabled = new();
    private readonly CheckBox tableDecorationsEnabled = new();
    private readonly CheckBox providersEnabled = new();
    private readonly CheckBox purchaseOrdersEnabled = new();
    private readonly CheckBox inventoryGlobalEnabled = new();
    private Image? settingsBackgroundImage;
    private readonly TextBox whatsappCountry = new(), whatsappNumber = new();
    private readonly SafeComboBox printPrinter = new();
    private readonly CheckBox printPreview = new();
    private GroupBox? webLinkGroup;
    private readonly TextBox webLink = new();
    private readonly Label webLinkStatus = new();
    private System.Windows.Forms.Timer? webLinkTimer;

    public SettingsForm()
    {
        Text = "FerrarisPOS - Configuración del programa";
        Width = 1100; Height = 1120;
        MinimumSize = new Size(1000, 980);
        AutoScroll = true;
        AutoScrollMinSize = new Size(0, 3250);
        BackColor = Color.FromArgb(10, 14, 24);
        Build(); InitializeSettingsBackground(); ThemeService.Apply(this); UpdateView(); LanguageService.Apply(this);
    }

    private void InitializeSettingsBackground()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Resources", "pos_background.png");
            if (File.Exists(path))
            {
                using var source = Image.FromFile(path);
                // Fondo compuesto previamente: 49 % de la imagen + base oscura
                // + desenfoque. Evita que Windows 10/11 pinte las superficies
                // secundarias como blanco y mantiene el mismo aspecto que Win8.
                settingsBackgroundImage = ThemeService.CreateSecondaryBackground(source, ClientSize);
            }
        }
        catch { settingsBackgroundImage = null; }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        if (!string.Equals(ThemeService.CurrentTheme, "Oscuro", StringComparison.OrdinalIgnoreCase) ||
            settingsBackgroundImage == null || ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        float scale = Math.Max((float)ClientSize.Width / settingsBackgroundImage.Width, (float)ClientSize.Height / settingsBackgroundImage.Height);
        int width = Math.Max(1, (int)Math.Ceiling(settingsBackgroundImage.Width * scale));
        int height = Math.Max(1, (int)Math.Ceiling(settingsBackgroundImage.Height * scale));
        int x = (ClientSize.Width - width) / 2;
        int y = (ClientSize.Height - height) / 2;
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        e.Graphics.DrawImage(settingsBackgroundImage, new Rectangle(x, y, width, height));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            webLinkTimer?.Stop();
            webLinkTimer?.Dispose();
            settingsBackgroundImage?.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed record LanguageOption(string Code, string Name)
    {
        public override string ToString() => Name;
    }

    private void Build()
    {
        Controls.Add(new Label { Text = "CONFIGURACIÓN DEL PROGRAMA", Location = new Point(25, 20), AutoSize = true, Font = new Font("Segoe UI", 19, FontStyle.Bold) });
        license.Location = new Point(25, 65); license.AutoSize = true; license.Font = new Font("Segoe UI", 14, FontStyle.Bold); Controls.Add(license);

        licenseDetails.Location = new Point(25, 92);
        licenseDetails.Size = new Size(900, 22);
        licenseDetails.AutoEllipsis = true;
        licenseDetails.Font = new Font("Segoe UI", 9f);
        Controls.Add(licenseDetails);

        Controls.Add(new Label { Text = "ACTIVACIÓN / RENOVACIÓN / DESACTIVACIÓN", Location = new Point(25, 125), AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold) });
        key.Location = new Point(25, 150); key.Width = 300; key.PlaceholderText = "Pegá el código de licencia"; key.UseSystemPasswordChar = true; Controls.Add(key);
        var act = new Button { Text = "APLICAR LICENCIA", Location = new Point(755, 130), Width = 145, Height = 40, Font = new Font("Segoe UI", 8.5f, FontStyle.Bold) };
        act.Click += (_, _) => Activate(); Controls.Add(act);

        var machineIdLabel = new Label
        {
            Text = $"ID DE ESTE EQUIPO: {LicenseService.MachineId}",
            Location = new Point(25, 185),
            Size = new Size(900, 22),
            AutoEllipsis = true,
            Font = new Font("Consolas", 8.5f, FontStyle.Bold)
        };
        Controls.Add(machineIdLabel);
        var copyMachineId = new Button
        {
            Text = "COPIAR ID",
            Location = new Point(600, 130),
            Width = 145,
            Height = 40
        };
        copyMachineId.Click += (_, _) =>
        {
            Clipboard.SetText(LicenseService.MachineId);
            MessageBox.Show("ID del equipo copiado. Podés enviárselo al administrador para generar una licencia vinculada a esta PC.", "Licencia", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        Controls.Add(copyMachineId);

        var licenseContact = new Button
        {
            Text = "CONSEGUÍ TU LICENCIA AQUÍ",
            Location = new Point(330, 130),
            Width = 255,
            Height = 40,
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        licenseContact.Click += (_, _) =>
        {
            MessageBox.Show(
                "¿Querés adquirir una licencia de FerrarisPOS?\n\n" +
                "WhatsApp: +54 261 540 7856\n" +
                "Correo electrónico: francoferrari95@gmail.com\n\n" +
                "Podés comunicarte por cualquiera de estos medios.",
                "Conseguí tu licencia aquí",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        };
        Controls.Add(licenseContact);

        // LINK POS FERRARI: solo ADMINISTRADOR. El programa inicia Cloudflare
        // automáticamente; el cliente final no instala ni configura cloudflared.
        if (Session.IsAdmin)
        {
            webLinkGroup = new GroupBox
            {
                Text = "LINK POS FERRARI · ADMINISTRADOR",
                Location = new Point(20, 215),
                Size = new Size(995, 215),
                Font = new Font("Segoe UI", 9, FontStyle.Bold)
            };
            webLinkGroup.Controls.Add(new Label
            {
                Text = "Link público del panel POS",
                Location = new Point(20, 28),
                AutoSize = true,
                Font = new Font("Segoe UI", 10, FontStyle.Bold)
            });
            webLinkGroup.Controls.Add(new Label
            {
                Text = "Se genera automáticamente al iniciar FerrariPOS. El cliente solo abre el enlace en su navegador.",
                Location = new Point(20, 51),
                Size = new Size(930, 22),
                ForeColor = Color.DimGray
            });
            webLink.Location = new Point(20, 78);
            webLink.Width = 620;
            webLink.Height = 32;
            webLink.ReadOnly = true;
            webLink.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            webLink.BackColor = Color.FromArgb(28, 34, 42);
             webLink.ForeColor = Color.White;
            webLink.Text = WebDashboardServer.Current?.AccessUrl ?? "Conectando automáticamente...";
            webLinkGroup.Controls.Add(webLink);

            var copyLink = new Button { Text = "COPIAR LINK", Location = new Point(650, 77), Width = 150, Height = 34 };
            copyLink.Click += (_, _) =>
            {
                if (!string.IsNullOrWhiteSpace(webLink.Text) && webLink.Text.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    Clipboard.SetText(webLink.Text);
                    MessageBox.Show("Link POS Ferrari copiado al portapapeles.", "Link POS Ferrari", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            };
            webLinkGroup.Controls.Add(copyLink);

            var openLink = new Button { Text = "ABRIR PANEL", Location = new Point(810, 77), Width = 145, Height = 34 };
            openLink.Click += (_, _) =>
            {
                var url = webLink.Text.Trim();
                if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true }); } catch { }
            };
            webLinkGroup.Controls.Add(openLink);

            webLinkStatus.Location = new Point(20, 112);
            webLinkStatus.Size = new Size(930, 22);
            webLinkStatus.ForeColor = Color.DarkOrange;
            webLinkStatus.Text = "● Iniciando enlace público automáticamente...";
            webLinkGroup.Controls.Add(webLinkStatus);

            var mobilePair = new Button
            {
                Text = "VINCULAR MANAGER · QR / CÓDIGO",
                Location = new Point(650, 137),
                Width = 305,
                Height = 32,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            mobilePair.Click += (_, _) =>
            {
                using var dlg = new MobilePairingForm();
                dlg.ShowDialog(this);
            };
            webLinkGroup.Controls.Add(mobilePair);

            var mobileCodeLabel = new Label
            {
                Text = "CÓDIGO DE VINCULACIÓN:",
                Location = new Point(20, 139),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
            };
            webLinkGroup.Controls.Add(mobileCodeLabel);
            var mobileCode = new TextBox
            {
                Location = new Point(20, 160), Width = 790, Height = 27,
                ReadOnly = true, Text = WebDashboardServer.GetMobilePairingCode(),
                BackColor = Color.FromArgb(30, 36, 45), ForeColor = Color.White
            };
            webLinkGroup.Controls.Add(mobileCode);
            var copyMobileCode = new Button { Text = "COPIAR CÓDIGO", Location = new Point(820, 158), Width = 135, Height = 32 };
            copyMobileCode.Click += (_, _) => { Clipboard.SetText(mobileCode.Text); MessageBox.Show("Código de vinculación copiado.", "FerrariPOS Manager", MessageBoxButtons.OK, MessageBoxIcon.Information); };
            webLinkGroup.Controls.Add(copyMobileCode);

            // Soporte de conexión: no reemplaza ni altera el funcionamiento normal
            // de Cloudflare. Solo permite reparar y generar un diagnóstico completo
            // cuando el cliente tiene un problema. El informe queda en el Escritorio.
            var repairConnection = new Button
            {
                Text = "REPARAR CONEXIÓN",
                Location = new Point(20, 190),
                Width = 250,
                Height = 22,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            repairConnection.Click += async (_, _) =>
            {
                repairConnection.Enabled = false;
                try
                {
                    Cursor = Cursors.WaitCursor;
                    var report = await NetworkDiagnosticService.RepairAndDiagnoseAsync();
                    Cursor = Cursors.Default;
                    MessageBox.Show(
                        "FerrariPOS terminó la comprobación y la reparación automática.\n\n" +
                        "El diagnóstico fue guardado en el Escritorio.\n\n" +
                        "Si la conexión no quedó funcionando, enviá ese archivo al soporte.",
                        "Conexión FerrariPOS", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "explorer.exe", Arguments = "/select,\"" + report + "\"", UseShellExecute = true }); } catch { }
                }
                catch (Exception ex)
                {
                    Cursor = Cursors.Default;
                    MessageBox.Show("No se pudo completar el diagnóstico automático.\n\n" + ex.Message, "Conexión FerrariPOS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                finally { repairConnection.Enabled = true; }
            };
            webLinkGroup.Controls.Add(repairConnection);

            var diagnoseConnection = new Button
            {
                Text = "GENERAR DIAGNÓSTICO",
                Location = new Point(280, 190),
                Width = 250,
                Height = 22,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            diagnoseConnection.Click += async (_, _) =>
            {
                diagnoseConnection.Enabled = false;
                try
                {
                    Cursor = Cursors.WaitCursor;
                    var report = await NetworkDiagnosticService.RepairAndDiagnoseAsync(default, repair: false);
                    Cursor = Cursors.Default;
                    MessageBox.Show(
                        "Diagnóstico generado correctamente.\n\n" + report,
                        "Diagnóstico FerrariPOS", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = "explorer.exe", Arguments = "/select,\"" + report + "\"", UseShellExecute = true }); } catch { }
                }
                catch (Exception ex)
                {
                    Cursor = Cursors.Default;
                    MessageBox.Show("No se pudo generar el diagnóstico.\n\n" + ex.Message, "Diagnóstico FerrariPOS", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                finally { diagnoseConnection.Enabled = true; }
            };
            webLinkGroup.Controls.Add(diagnoseConnection);

            Controls.Add(webLinkGroup);

            webLinkTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            webLinkTimer.Tick += (_, _) => UpdateWebLink();
            webLinkTimer.Start();
            UpdateWebLink();
        }

        var commercialGroup = new GroupBox
        {
            Text = "IDENTIDAD DEL COMERCIO Y APARIENCIA",
            Location = new Point(20, 445),
            Size = new Size(995, 285),
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };

        commercialGroup.Controls.Add(new Label { Text = "NOMBRE DEL COMERCIO", Location = new Point(20, 28), AutoSize = true });
        business.Location = new Point(20, 53);
        business.Width = 330;
        business.Text = Database.GetSetting("business_name", "Ferrari's Punto de Venta");
        commercialGroup.Controls.Add(business);
        var bs = new Button { Text = "GUARDAR NOMBRE", Location = new Point(20, 93), Width = 145, Height = 34 };
        bs.Click += (_, _) =>
        {
            Database.SetSetting("business_name", business.Text.Trim());
            MessageBox.Show("Configuración guardada.");
        };
        commercialGroup.Controls.Add(bs);
        commercialGroup.Controls.Add(new Label { Text = "RUBRO DEL COMERCIO", Location = new Point(20, 138), AutoSize = true });
        businessType.Location = new Point(20, 163); businessType.Width = 330; businessType.DropDownStyle = ComboBoxStyle.DropDownList;
        businessType.Items.AddRange(new object[] { "Abarrotes", "Almacén", "Carnicería", "Despensa", "Kiosco", "Supermercado", "Panadería", "Pastelería", "Verdulería", "Frutería", "Fiambrería", "Rotisería", "Restaurante", "Bar / Cafetería", "Heladería", "Pizzería", "Farmacia", "Perfumería", "Ferretería", "Electrodomésticos", "Ropa / Indumentaria", "Calzado", "Librería", "Juguetería", "Veterinaria", "Mayorista", "Distribuidora", "Servicios", "Otro" });
        var savedType = Database.GetSetting("business_type", "Punto de Venta");
        var typeIndex = businessType.Items.IndexOf(savedType); businessType.SelectedIndex = typeIndex >= 0 ? typeIndex : 0;
        commercialGroup.Controls.Add(businessType);
        var bt = new Button { Text = "GUARDAR RUBRO", Location = new Point(20, 202), Width = 145, Height = 34 };
        bt.Click += (_, _) => { Database.SetSetting("business_type", businessType.SelectedItem?.ToString() ?? "Punto de Venta"); MessageBox.Show("Rubro del comercio guardado. El título del panel web se actualizará automáticamente."); };
        commercialGroup.Controls.Add(bt);

        commercialGroup.Controls.Add(new Label { Text = "TIPO DE CAMBIO USD", Location = new Point(375, 28), AutoSize = true });
        rate.Location = new Point(375, 53);
        rate.Width = 150;
        rate.Text = Database.GetSetting("exchange_rate", "1");
        commercialGroup.Controls.Add(rate);
        var rs = new Button { Text = "GUARDAR", Location = new Point(375, 93), Width = 110, Height = 34 };
        rs.Click += (_, _) =>
        {
            Database.SetSetting("exchange_rate", rate.Text.Replace(',', '.'));
            MessageBox.Show("Tipo de cambio guardado.");
        };
        commercialGroup.Controls.Add(rs);

        commercialGroup.Controls.Add(new Label { Text = "SÍMBOLO DE MONEDA", Location = new Point(555, 28), AutoSize = true });
        currency.Location = new Point(555, 53);
        currency.Width = 90;
        currency.MaxLength = 5;
        currency.Text = Database.GetSetting("currency", "$");
        commercialGroup.Controls.Add(currency);
        var cs = new Button { Text = "GUARDAR MONEDA", Location = new Point(655, 50), Width = 145, Height = 35 };
        cs.Click += (_, _) =>
        {
            var symbol = currency.Text.Trim();
            if (string.IsNullOrWhiteSpace(symbol)) symbol = "$";
            Database.SetSetting("currency", symbol);
            currency.Text = symbol;
            MessageBox.Show($"Símbolo de moneda guardado: {symbol}");
        };
        commercialGroup.Controls.Add(cs);

        commercialGroup.Controls.Add(new Label
        {
            Text = "TEMA DE LA INTERFAZ",
            Location = new Point(375, 140),
            AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        });
        // El tema ocupa una fila independiente y ya no comparte coordenadas con
        // moneda/tipo de cambio. Esto evita superposición en resoluciones menores.
        theme.Location = new Point(375, 165);
        theme.Width = 285;
        theme.DropDownStyle = ComboBoxStyle.DropDownList;
        theme.Items.AddRange(ThemeService.AvailableThemes);
        var savedTheme = ThemeService.CurrentTheme;
        var themeIndex = theme.Items.IndexOf(savedTheme);
        theme.SelectedIndex = themeIndex >= 0 && themeIndex < theme.Items.Count ? themeIndex : (theme.Items.Count > 0 ? 0 : -1);
        commercialGroup.Controls.Add(theme);

        var saveTheme = new Button
        {
            Text = "APLICAR TEMA",
            Location = new Point(680, 164),
            Width = 200,
            Height = 35,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        saveTheme.Click += (_, _) =>
        {
            ThemeService.SetTheme(theme.SelectedItem?.ToString() ?? "Oscuro");
            MessageBox.Show("Tema aplicado. La interfaz se actualizó.");
        };
        commercialGroup.Controls.Add(saveTheme);

        // CUENTA DEL PANEL CENTRAL: se crea automáticamente para cada instalación.
        // No depende del usuario de Windows ni del cajero y permite entrar al panel
        // central desde cualquier lugar.
        commercialGroup.Controls.Add(new Label { Text = "CUENTA DEL PANEL CENTRAL", Location = new Point(500, 205), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        var centralUser = new TextBox { Location = new Point(500, 228), Width = 185, Height = 28, ReadOnly = true, BackColor = Color.FromArgb(28,34,42), ForeColor = Color.White };
        var centralPass = new TextBox { Location = new Point(695, 228), Width = 185, Height = 28, ReadOnly = true, UseSystemPasswordChar = false, BackColor = Color.FromArgb(28,34,42), ForeColor = Color.White };
        centralUser.Text = CentralApiClient.WebUsername;
        centralPass.Text = CentralApiClient.WebPassword;
        commercialGroup.Controls.Add(centralUser);
        commercialGroup.Controls.Add(centralPass);
        var openCentral = new Button { Text = "ABRIR PANEL", Location = new Point(500, 258), Width = 140, Height = 24 };
        openCentral.Click += (_, _) => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = CentralApiClient.WebPanelUrl, UseShellExecute = true }); } catch { } };
        commercialGroup.Controls.Add(openCentral);
        var copyCentral = new Button { Text = "COPIAR DATOS", Location = new Point(648, 258), Width = 140, Height = 24 };
        copyCentral.Click += (_, _) => { Clipboard.SetText($"Usuario: {centralUser.Text}\r\nContraseña: {centralPass.Text}\r\nPanel: {CentralApiClient.WebPanelUrl}"); MessageBox.Show("Datos del Panel Central copiados.", "FerrariPOS Central", MessageBoxButtons.OK, MessageBoxIcon.Information); };
        commercialGroup.Controls.Add(copyCentral);
        var resetCentral = new Button { Text = "NUEVA CONTRASEÑA", Location = new Point(796, 258), Width = 150, Height = 24 };
        resetCentral.Click += async (_, _) =>
        {
            resetCentral.Enabled = false;
            try
            {
                var password = await CentralApiClient.ResetWebPasswordAsync();
                if (!string.IsNullOrWhiteSpace(password)) { centralPass.Text = password; MessageBox.Show("Contraseña del Panel Central renovada correctamente.", "FerrariPOS Central", MessageBoxButtons.OK, MessageBoxIcon.Information); }
                else MessageBox.Show("No se pudo renovar la contraseña. Comprobá la conexión a Internet.", "FerrariPOS Central", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { resetCentral.Enabled = true; }
        };
        commercialGroup.Controls.Add(resetCentral);
        _ = Task.Run(async () =>
        {
            try
            {
                await CentralApiClient.EnsureWebAccountAsync();
                if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(() => { centralUser.Text = CentralApiClient.WebUsername; centralPass.Text = CentralApiClient.WebPassword; }));
            } catch { }
        });

        // Los campos anteriores siguen funcionando, pero ahora están dentro de un
        // único bloque visual. El grupo queda antes de idioma, tipografía y sonido.
        Controls.Add(commercialGroup);

        // Idioma: tarjeta independiente en F7 para no superponer controles de identidad,
        // tema o tipografía. El idioma seleccionado durante la instalación aparece aquí
        // como valor inicial y el usuario puede cambiarlo posteriormente.
        var languageGroup = new GroupBox
        {
            Text = "IDIOMA DEL PROGRAMA",
            Location = new Point(25, 750),
            Size = new Size(450, 150),
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        var languageLabel = new Label { Text = "IDIOMA", Location = new Point(20, 30), AutoSize = true };
        var languageCombo = new SafeComboBox
        {
            Location = new Point(20, 58), Width = 245,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        foreach (var option in LanguageService.Options) languageCombo.Items.Add(new LanguageOption(option.Code, option.Name));
        var currentLanguage = LanguageService.CurrentCode;
        var languageIndex = -1;
        for (var i = 0; i < languageCombo.Items.Count; i++)
            if (languageCombo.Items[i] is LanguageOption o && o.Code == currentLanguage) { languageIndex = i; break; }
        languageCombo.SelectedIndex = languageIndex >= 0 ? languageIndex : 0;
        languageGroup.Controls.Add(languageLabel);
        languageGroup.Controls.Add(languageCombo);
        var saveLanguage = new Button { Text = "GUARDAR IDIOMA", Location = new Point(280, 55), Width = 145, Height = 40 };
        saveLanguage.Click += (_, _) =>
        {
            if (languageCombo.SelectedItem is not LanguageOption selected) return;
            LanguageService.SetLanguage(selected.Code);
            LanguageService.Apply(this);
            ThemeService.Apply(this);
            MessageBox.Show($"Idioma guardado: {selected.Name}. Los textos abiertos se actualizaron.", "Idioma", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        languageGroup.Controls.Add(saveLanguage);
        languageGroup.Controls.Add(new Label
        {
            Text = "El idioma elegido en el instalador queda como idioma inicial.\nPodés cambiarlo después desde F7.",
            Location = new Point(20, 102), Size = new Size(405, 38), ForeColor = Color.DimGray
        });
        Controls.Add(languageGroup);

        // Identidad del ticket: configuración independiente para no tocar red, Cloudflare ni setup.
        var ticketIdentity = new GroupBox
        {
            Text = "TICKET · IDENTIDAD Y MENSAJE",
            Location = new Point(25, 910),
            Size = new Size(450, 150),
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        var ticketAddress = new TextBox { Location = new Point(20, 48), Width = 190, Text = Database.GetSetting("ticket_business_address", ""), PlaceholderText = "Dirección" };
        var ticketPhone = new TextBox { Location = new Point(220, 48), Width = 215, Text = Database.GetSetting("ticket_business_phone", ""), PlaceholderText = "Teléfono" };
        var ticketFooter = new TextBox { Location = new Point(20, 88), Width = 300, Text = Database.GetSetting("ticket_footer", "Gracias por su compra"), PlaceholderText = "Mensaje final del ticket" };
        ticketIdentity.Controls.Add(new Label { Text = "DIRECCIÓN / TELÉFONO", Location = new Point(20, 25), AutoSize = true });
        ticketIdentity.Controls.Add(ticketAddress); ticketIdentity.Controls.Add(ticketPhone); ticketIdentity.Controls.Add(ticketFooter);
        var saveTicketIdentity = new Button { Text = "GUARDAR TICKET", Location = new Point(325, 86), Width = 110, Height = 34 };
        saveTicketIdentity.Click += (_, _) =>
        {
            Database.SetSetting("ticket_business_address", ticketAddress.Text.Trim());
            Database.SetSetting("ticket_business_phone", ticketPhone.Text.Trim());
            Database.SetSetting("ticket_footer", string.IsNullOrWhiteSpace(ticketFooter.Text) ? "Gracias por su compra" : ticketFooter.Text.Trim());
            MessageBox.Show("Identidad del ticket guardada. Los próximos tickets usarán estos datos.", "Ticket", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        ticketIdentity.Controls.Add(saveTicketIdentity);
        Controls.Add(ticketIdentity);

        // Tarjeta independiente de impresión: queda a la derecha de Idioma y no
        // modifica ni desplaza los bloques existentes de F7. La impresora elegida
        // también se utiliza al imprimir estados de deuda y demás documentos que
        // respeten la configuración general de impresión.
        var printGroup = new GroupBox
        {
            Text = "IMPRESIÓN / PDF",
            Location = new Point(500, 750),
            Size = new Size(490, 150),
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };

        printGroup.Controls.Add(new Label
        {
            Text = "IMPRESORA DE SALIDA",
            Location = new Point(20, 28),
            AutoSize = true
        });

        printPrinter.Location = new Point(20, 52);
        printPrinter.Width = 285;
        printPrinter.DropDownStyle = ComboBoxStyle.DropDownList;
        LoadPrintPrinters();
        printGroup.Controls.Add(printPrinter);

        var refreshPrinters = new Button
        {
            Text = "ACTUALIZAR",
            Location = new Point(315, 50),
            Width = 145,
            Height = 34
        };
        refreshPrinters.Click += (_, _) => LoadPrintPrinters();
        printGroup.Controls.Add(refreshPrinters);

        printPreview.Text = "MOSTRAR VISTA PREVIA ANTES DE IMPRIMIR";
        printPreview.Location = new Point(20, 91);
        printPreview.AutoSize = true;
        printPreview.Checked = PrintConfigurationService.PreviewEnabled;
        printGroup.Controls.Add(printPreview);

        var pdf = new Button
        {
            Text = "SELECCIONAR PDF",
            Location = new Point(315, 88),
            Width = 145,
            Height = 34
        };
        pdf.Click += (_, _) => SelectPdfPrinter();
        printGroup.Controls.Add(pdf);

        var configurePrinter = new Button
        {
            Text = "CONFIGURAR IMPRESORA",
            Location = new Point(20, 88),
            Width = 180,
            Height = 26,
            Font = new Font("Segoe UI", 8f, FontStyle.Bold)
        };
        configurePrinter.Click += (_, _) => ConfigurePrinter();
        printGroup.Controls.Add(configurePrinter);

        var savePrint = new Button
        {
            Text = "GUARDAR IMPRESIÓN",
            Location = new Point(210, 88),
            Width = 175,
            Height = 26,
            Font = new Font("Segoe UI", 8f, FontStyle.Bold)
        };
        savePrint.Click += (_, _) => SavePrintConfiguration();
        printGroup.Controls.Add(savePrint);

        printGroup.Controls.Add(new Label
        {
            Text = "Para PDF elegí 'Microsoft Print to PDF'. Windows pedirá el nombre y ubicación del archivo al imprimir.",
            Location = new Point(390, 118),
            Size = new Size(1, 1),
            Visible = false
        });

        Controls.Add(printGroup);

        Controls.Add(new Label { Text = "APARIENCIA Y TIPOGRAFÍA DEL PROGRAMA", Location = new Point(500, 920), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        Controls.Add(new Label { Text = "FUENTE PREDETERMINADA", Location = new Point(500, 950), AutoSize = true });
        var fixedFont = new TextBox { Location = new Point(500, 975), Width = 250, ReadOnly = true, Text = "Segoe UI", TabStop = false };
        Controls.Add(fixedFont);
        Controls.Add(new Label { Text = "TAMAÑO", Location = new Point(765, 950), AutoSize = true, Font = new Font("Segoe UI", 8, FontStyle.Bold) });
        fontSize.Location = new Point(765, 975); fontSize.Width = 90; fontSize.Minimum = 7; fontSize.Maximum = 20; fontSize.DecimalPlaces = 1; fontSize.Increment = 0.5m;
        fontSize.Value = Math.Clamp(Convert.ToDecimal(ThemeService.CurrentFontSize), fontSize.Minimum, fontSize.Maximum);
        Controls.Add(fontSize);
        var saveFont = new Button { Text = "GUARDAR TAMAÑO", Location = new Point(865, 970), Width = 150, Height = 40 };
        saveFont.Click += (_, _) =>
        {
            var selected = (float)fontSize.Value;
            if (Math.Abs(selected - ThemeService.CurrentFontSize) < 0.01f)
            {
                MessageBox.Show("El tamaño actual ya está seleccionado. La fuente del programa es fija en Segoe UI.", "Tipografía", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            var answer = MessageBox.Show(
                $"La fuente del programa es fija en Segoe UI.\n\n¿Querés cambiar el tamaño general de {ThemeService.CurrentFontSize:0.0} a {selected:0.0}?",
                "Modificar tamaño de letra",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;
            ThemeService.SetFontSize(selected);
            MessageBox.Show($"Tamaño de letra cambiado a {selected:0.0}.", "Tipografía", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        Controls.Add(saveFont);

        Controls.Add(new Label
        {
            Text = "SONIDO DE CAJA REGISTRADORA",
            Location = new Point(25, 1080),
            AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        });
        cashSoundEnabled.Text = "Reproducir sonido al cobrar (F1 / F2)";
        cashSoundEnabled.Location = new Point(25, 1110);
        cashSoundEnabled.AutoSize = true;
        cashSoundEnabled.Checked = Database.GetSetting("cash_register_sound_enabled", "1") == "1";
        Controls.Add(cashSoundEnabled);

        var saveSound = new Button
        {
            Text = "GUARDAR SONIDO",
            Location = new Point(25, 1145),
            Width = 170,
            Height = 40
        };
        saveSound.Click += (_, _) =>
        {
            Database.SetSetting("cash_register_sound_enabled", cashSoundEnabled.Checked ? "1" : "0");
            MessageBox.Show(cashSoundEnabled.Checked
                ? "Sonido de caja activado."
                : "Sonido de caja desactivado.");
        };
        Controls.Add(saveSound);

        var group = new GroupBox { Text = "CORREO AUTOMÁTICO · ARQUEO Y LINK POS", Location = new Point(25, 1200), Size = new Size(960, 275) };
        emailEnabled.Text = "ENVIAR REPORTE AUTOMÁTICAMENTE AL CERRAR CAJA";
        emailEnabled.Location = new Point(20, 30); emailEnabled.AutoSize = true;
        emailEnabled.Checked = Database.GetSetting("report_email_enabled", "0") == "1"; group.Controls.Add(emailEnabled);
        posLinkEmail.Text = "ENVIAR LINK PÚBLICO AL INGRESAR AL PROGRAMA"; posLinkEmail.Location = new Point(300, 30); posLinkEmail.AutoSize = true; posLinkEmail.Checked = Database.GetSetting("pos_link_email_enabled", "1") == "1";
        group.Controls.Add(posLinkEmail);

        AddField(group, "CORREO DESTINO", emailTo, 65, Database.GetSetting("report_email_to"));
        AddField(group, "SERVIDOR SMTP", smtpHost, 105, Database.GetSetting("smtp_host"));
        AddField(group, "PUERTO", smtpPort, 145, Database.GetSetting("smtp_port", "587"));
        AddField(group, "USUARIO SMTP", smtpUser, 185, Database.GetSetting("smtp_user"));
        AddField(group, "CONTRASEÑA / APP PASSWORD", smtpPassword, 225, Database.GetSetting("smtp_password"));
        smtpPassword.UseSystemPasswordChar = true;
        smtpSsl.Text = "Usar SSL/TLS"; smtpSsl.Location = new Point(610, 105); smtpSsl.AutoSize = true;
        smtpSsl.Checked = Database.GetSetting("smtp_ssl", "1") == "1"; group.Controls.Add(smtpSsl);

        var autoSmtp = new Button { Text = "DETECTAR SMTP", Location = new Point(610, 145), Width = 150, Height = 42 };
        autoSmtp.Click += (_, _) => DetectSmtp(); group.Controls.Add(autoSmtp);

        var saveEmail = new Button { Text = "GUARDAR CORREO", Location = new Point(770, 145), Width = 160, Height = 42 };
        saveEmail.Click += (_, _) => SaveEmail(); group.Controls.Add(saveEmail);

        var testEmail = new Button { Text = "PROBAR ENVÍO", Location = new Point(610, 195), Width = 150, Height = 42 };
        testEmail.Click += (_, _) => TestEmail(); group.Controls.Add(testEmail);

        var emailHelp = new Label
        {
            Text = "Para Gmail/Outlook normalmente necesitás una contraseña de aplicación.\nEl link POS se envía al ingresar y el arqueo al cerrar caja.",
            Location = new Point(770, 195), Size = new Size(175, 55),
            Font = new Font("Segoe UI", 8), ForeColor = Color.DimGray
        };
        group.Controls.Add(emailHelp);
        group.Enabled = Session.IsAdmin;
        if (!Session.IsAdmin)
        {
            group.Controls.Add(new Label { Text = "Solo ADMIN puede configurar el envío automático.", Location = new Point(610, 255), AutoSize = true, ForeColor = Color.DarkRed });
        }
        Controls.Add(group);

        // Segundo destinatario SMTP: se agrega como bloque independiente para no alterar
        // la configuración existente del primer correo. Ambos pueden enviar el mismo arqueo.
        var group2 = new GroupBox { Text = "SEGUNDO CORREO · ENVÍO DEL ARQUEO", Location = new Point(25, 1490), Size = new Size(960, 275) };
        email2Enabled.Text = "ENVIAR TAMBIÉN EL REPORTE AL SEGUNDO CORREO";
        email2Enabled.Location = new Point(20, 30); email2Enabled.AutoSize = true;
        email2Enabled.Checked = Database.GetSetting("report_email2_enabled", "0") == "1";
        group2.Controls.Add(email2Enabled);

        AddField(group2, "CORREO DESTINO 2", emailTo2, 65, Database.GetSetting("report_email_to2"));
        AddField(group2, "SERVIDOR SMTP 2", smtpHost2, 105, Database.GetSetting("smtp_host2"));
        AddField(group2, "PUERTO 2", smtpPort2, 145, Database.GetSetting("smtp_port2", "587"));
        AddField(group2, "USUARIO SMTP 2", smtpUser2, 185, Database.GetSetting("smtp_user2"));
        AddField(group2, "CONTRASEÑA / APP PASSWORD 2", smtpPassword2, 225, Database.GetSetting("smtp_password2"));
        smtpPassword2.UseSystemPasswordChar = true;
        smtpSsl2.Text = "Usar SSL/TLS 2"; smtpSsl2.Location = new Point(610, 105); smtpSsl2.AutoSize = true;
        smtpSsl2.Checked = Database.GetSetting("smtp_ssl2", "1") == "1"; group2.Controls.Add(smtpSsl2);

        var autoSmtp2 = new Button { Text = "DETECTAR SMTP 2", Location = new Point(610, 145), Width = 150, Height = 42 };
        autoSmtp2.Click += (_, _) => DetectSmtp2(); group2.Controls.Add(autoSmtp2);

        var saveEmail2 = new Button { Text = "GUARDAR CORREO 2", Location = new Point(770, 145), Width = 160, Height = 42 };
        saveEmail2.Click += (_, _) => SaveEmail2(); group2.Controls.Add(saveEmail2);

        var testEmail2 = new Button { Text = "PROBAR ENVÍO 2", Location = new Point(610, 195), Width = 150, Height = 42 };
        testEmail2.Click += (_, _) => TestEmail2(); group2.Controls.Add(testEmail2);

        group2.Controls.Add(new Label
        {
            Text = "El segundo correo usa su propia cuenta SMTP, puerto, contraseña y SSL/TLS.\nEl mismo informe de arqueo se envía a ambos destinatarios cuando están habilitados y correctamente configurados.",
            Location = new Point(770, 195), Size = new Size(175, 65),
            Font = new Font("Segoe UI", 8), ForeColor = Color.DimGray
        });
        group2.Enabled = Session.IsAdmin;
        if (!Session.IsAdmin)
            group2.Controls.Add(new Label { Text = "Solo ADMIN puede configurar el segundo correo.", Location = new Point(610, 255), AutoSize = true, ForeColor = Color.DarkRed });
        Controls.Add(group2);

        var displayGroup = new GroupBox
        {
            Text = "PANTALLA · RESOLUCIÓN",
            Location = new Point(25, 1790),
            Size = new Size(960, 150)
        };
        displayGroup.Controls.Add(new Label { Text = "RESOLUCIÓN", Location = new Point(20, 30), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        resolution.Location = new Point(20, 55); resolution.Width = 300; resolution.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (var r in DisplayService.Resolutions) resolution.Items.Add(r.Name);
        var currentResolution = Database.GetSetting("resolution", "auto");
        var currentIndex = Array.FindIndex(DisplayService.Resolutions, r => $"{r.Width}x{r.Height}" == currentResolution);
        if (resolution.Items.Count > 0) resolution.SelectedIndex = currentIndex >= 0 && currentIndex < resolution.Items.Count ? currentIndex : 0;
        displayGroup.Controls.Add(resolution);

        fullscreen.Text = "PANTALLA COMPLETA";
        fullscreen.AutoSize = true;
        fullscreen.Location = new Point(340, 57);
        fullscreen.Checked = Database.GetSetting("fullscreen", "0") == "1";
        displayGroup.Controls.Add(fullscreen);

        var saveDisplay = new Button { Text = "APLICAR PANTALLA", Location = new Point(340, 90), Width = 160, Height = 40 };
        saveDisplay.Click += (_, _) => ApplyDisplay();
        displayGroup.Controls.Add(saveDisplay);

        displayGroup.Controls.Add(new Label
        {
            Text = "AUTO detecta el monitor al iniciar.\n1024×768 · 1280×720 · 1280×1024 · 1366×768 · 1440×900 · 1600×900 · 1680×1050 · 1920×1080 · 1920×1200",
            Location = new Point(540, 35), Size = new Size(390, 65),
            Font = new Font("Segoe UI", 9), ForeColor = Color.DimGray
        });
        Controls.Add(displayGroup);

        var whatsappGroup = new GroupBox
        {
            Text = "WHATSAPP / SMS · INFORME DE CIERRE",
            Location = new Point(25, 1955), Size = new Size(960, 145)
        };
        whatsappEnabled.Text = "PREPARAR WHATSAPP AL CERRAR CAJA";
        whatsappEnabled.Location = new Point(20, 30); whatsappEnabled.AutoSize = true;
        whatsappEnabled.Checked = Database.GetSetting("whatsapp_enabled", "0") == "1";
        whatsappGroup.Controls.Add(whatsappEnabled);
        AddField(whatsappGroup, "CÓDIGO DE PAÍS", whatsappCountry, 60, Database.GetSetting("whatsapp_country", "54"));
        AddField(whatsappGroup, "NÚMERO", whatsappNumber, 95, Database.GetSetting("whatsapp_number", ""));
        var saveWhatsApp = new Button { Text = "GUARDAR WHATSAPP", Location = new Point(610, 55), Width = 170, Height = 40 };
        saveWhatsApp.Click += (_, _) =>
        {
            Database.SetSetting("whatsapp_enabled", whatsappEnabled.Checked ? "1" : "0");
            Database.SetSetting("whatsapp_country", new string(whatsappCountry.Text.Where(char.IsDigit).ToArray()));
            Database.SetSetting("whatsapp_number", new string(whatsappNumber.Text.Where(char.IsDigit).ToArray()));
            MessageBox.Show("Configuración de WhatsApp guardada. Al cerrar caja se podrá preparar el informe detallado.", "WhatsApp", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        whatsappGroup.Controls.Add(saveWhatsApp);
        whatsappGroup.Controls.Add(new Label { Text = "Se abrirá WhatsApp con el informe listo para enviar.\nEl Excel queda guardado en la PC para adjuntarlo al chat.", Location = new Point(610, 100), Size = new Size(320, 35), ForeColor = Color.DimGray });
        Controls.Add(whatsappGroup);

        var tablesGroup = new GroupBox { Text = "MESAS Y SALÓN / TICKETS WINDOWS EN ANDROID", Location = new Point(25, 2115), Size = new Size(960, 235) };
        tablesEnabled.Text = "HABILITAR SISTEMA DE MESAS"; tablesEnabled.Location = new Point(20, 30); tablesEnabled.AutoSize = true; tablesEnabled.Checked = Database.GetSetting("tables_enabled", "0") == "1"; tablesGroup.Controls.Add(tablesEnabled);
        tablesInMain.Text = "MOSTRAR SALÓN DENTRO DE LA PANTALLA PRINCIPAL"; tablesInMain.Location = new Point(20, 65); tablesInMain.AutoSize = true; tablesInMain.Checked = Database.GetSetting("tables_in_main", "0") == "1"; tablesGroup.Controls.Add(tablesInMain);
        allowAndroidCharge.Text = "PERMITIR VER / AGREGAR PRODUCTOS / COBRAR TICKETS DE WINDOWS DESDE ANDROID"; allowAndroidCharge.Location = new Point(20, 100); allowAndroidCharge.AutoSize = true; allowAndroidCharge.Checked = Database.GetSetting("allow_android_charge", "0") == "1"; allowAndroidCharge.Enabled = Session.IsAdmin; tablesGroup.Controls.Add(allowAndroidCharge); if (!Session.IsAdmin) tablesGroup.Controls.Add(new Label { Text = "Solo ADMINISTRADOR puede habilitar esta función.", Location = new Point(20, 190), AutoSize = true, ForeColor = Color.DarkRed });
        tablesEditEnabled.Text = "PERMITIR EDITAR Y ACOMODAR EL SALÓN"; tablesEditEnabled.Location = new Point(20, 135); tablesEditEnabled.AutoSize = true; tablesEditEnabled.Checked = Database.GetSetting("tables_edit_enabled", "1") == "1"; tablesGroup.Controls.Add(tablesEditEnabled);
        tableDecorationsEnabled.Text = "MOSTRAR ELEMENTOS DECORATIVOS (CÉSPED, PLANTAS, ETC.)"; tableDecorationsEnabled.Location = new Point(20, 170); tableDecorationsEnabled.AutoSize = true; tableDecorationsEnabled.Checked = Database.GetSetting("table_decorations_enabled", "1") == "1"; tablesGroup.Controls.Add(tableDecorationsEnabled);
        var saveTables = new Button { Text = "GUARDAR MESAS", Location = new Point(650, 45), Width = 250, Height = 50 };
        saveTables.Click += (_, _) => { Database.SetSetting("tables_enabled", tablesEnabled.Checked ? "1" : "0"); Database.SetSetting("tables_in_main", tablesInMain.Checked ? "1" : "0"); if (Session.IsAdmin) Database.SetSetting("allow_android_charge", allowAndroidCharge.Checked ? "1" : "0"); Database.SetSetting("tables_edit_enabled", tablesEditEnabled.Checked ? "1" : "0"); Database.SetSetting("table_decorations_enabled", tableDecorationsEnabled.Checked ? "1" : "0"); TableService.EnsureTables(); MessageBox.Show("Configuración guardada. La opción de tickets Windows en Android solo puede cambiarla ADMINISTRADOR.", "Mesas y tickets", MessageBoxButtons.OK, MessageBoxIcon.Information); };
        tablesGroup.Controls.Add(saveTables);
        var openSalon = new Button { Text = "ABRIR / EDITAR SALÓN", Location = new Point(650, 105), Width = 250, Height = 45 };
        openSalon.Click += (_, _) => { if (Owner is MainForm mf && tablesEnabled.Checked) { using var f = new SalonForm(mf, tablesEditEnabled.Checked, false); ThemeService.Apply(f); f.StartPosition = FormStartPosition.CenterParent; f.ShowDialog(this); } else MessageBox.Show("Activá primero el sistema de mesas.", "Mesas y salón", MessageBoxButtons.OK, MessageBoxIcon.Information); };
        tablesGroup.Controls.Add(openSalon);
        Controls.Add(tablesGroup);

        var inventoryGroup = new GroupBox
        {
            Text = "CONTROL GLOBAL · INVENTARIO",
            Location = new Point(25, 2365),
            Size = new Size(960, 175),
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Tag = "InventoryControlCard"
        };

        inventoryGlobalEnabled.Text = "USAR INVENTARIO GLOBALMENTE";
        inventoryGlobalEnabled.Location = new Point(20, 32);
        inventoryGlobalEnabled.AutoSize = true;
        inventoryGlobalEnabled.Font = new Font("Segoe UI Semibold", 10, FontStyle.Bold);
        inventoryGlobalEnabled.Checked = InventoryControlService.IsGlobalEnabled;
        inventoryGlobalEnabled.Enabled = Session.IsAdmin;
        inventoryGroup.Controls.Add(inventoryGlobalEnabled);

        var inventoryStatus = new Label
        {
            Location = new Point(20, 65),
            Size = new Size(585, 62),
            Text = InventoryControlService.IsGlobalEnabled
                ? "ACTIVO · Los productos nuevos usarán inventario por defecto. Cada producto puede conservar su configuración individual."
                : "DESACTIVADO · Los stocks existentes quedan congelados. No se descuentan ventas, no se suman compras y se puede vender sin stock.",
            Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
            ForeColor = InventoryControlService.IsGlobalEnabled ? Color.DarkGreen : Color.DarkOrange,
            BackColor = Color.Transparent
        };
        inventoryGroup.Controls.Add(inventoryStatus);

        var saveInventory = new Button
        {
            Text = "GUARDAR CONTROL DE INVENTARIO",
            Location = new Point(635, 38),
            Width = 285,
            Height = 48,
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
            Enabled = Session.IsAdmin
        };
        saveInventory.Click += (_, _) =>
        {
            if (!Session.IsAdmin)
            {
                MessageBox.Show("Solo ADMINISTRADOR puede modificar el uso global de inventario.", "Permiso restringido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                var old = InventoryControlService.IsGlobalEnabled;
                var next = inventoryGlobalEnabled.Checked;
                InventoryControlService.SetGlobalEnabled(next);
                inventoryStatus.Text = next
                    ? "ACTIVO · Los productos nuevos usarán inventario por defecto. Cada producto puede conservar su configuración individual."
                    : "DESACTIVADO · Los stocks existentes quedan congelados. No se descuentan ventas, no se suman compras y se puede vender sin stock.";
                inventoryStatus.ForeColor = next ? Color.DarkGreen : Color.DarkOrange;
                MessageBox.Show(
                    next
                        ? "Inventario global habilitado. Los productos nuevos quedarán configurados para usar inventario por defecto."
                        : "Inventario global deshabilitado. Los contadores actuales quedaron congelados y las ventas podrán realizarse sin stock.",
                    "Control global de inventario", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                inventoryGlobalEnabled.Checked = InventoryControlService.IsGlobalEnabled;
                MessageBox.Show(ex.Message, "Control global de inventario", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        inventoryGroup.Controls.Add(saveInventory);

        inventoryGroup.Controls.Add(new Label
        {
            Text = Session.IsAdmin ? "Solo ADMINISTRADOR puede cambiar este control." : "Solo ADMINISTRADOR puede cambiar este control.",
            Location = new Point(635, 96),
            Size = new Size(285, 38),
            ForeColor = Color.DimGray,
            BackColor = Color.Transparent
        });
        Controls.Add(inventoryGroup);

        var modulesGroup = new GroupBox
        {
            Text = "MÓDULOS OPCIONALES · PROVEEDORES Y COMPRAS",
            Location = new Point(25, 2555),
            Size = new Size(960, 175)
        };
        providersEnabled.Text = "HABILITAR PROVEEDORES";
        providersEnabled.Location = new Point(20, 35);
        providersEnabled.AutoSize = true;
        providersEnabled.Checked = Database.GetSetting("providers_enabled", "1") == "1";
        modulesGroup.Controls.Add(providersEnabled);

        purchaseOrdersEnabled.Text = "HABILITAR ÓRDENES DE COMPRA / RECEPCIÓN";
        purchaseOrdersEnabled.Location = new Point(20, 75);
        purchaseOrdersEnabled.AutoSize = true;
        purchaseOrdersEnabled.Checked = Database.GetSetting("purchase_orders_enabled", "1") == "1";
        modulesGroup.Controls.Add(purchaseOrdersEnabled);

        modulesGroup.Controls.Add(new Label
        {
            Text = "Podés trabajar con el POS sin estos módulos. Si se desactivan, sus botones desaparecen de la pantalla principal.",
            Location = new Point(20, 108),
            Size = new Size(600, 35),
            ForeColor = Color.DimGray
        });

        var saveModules = new Button
        {
            Text = "GUARDAR MÓDULOS",
            Location = new Point(650, 48),
            Width = 250,
            Height = 50
        };
        saveModules.Click += (_, _) =>
        {
            Database.SetSetting("providers_enabled", providersEnabled.Checked ? "1" : "0");
            Database.SetSetting("purchase_orders_enabled", purchaseOrdersEnabled.Checked ? "1" : "0");
            if (Owner is MainForm main) main.UpdateOptionalModulesVisibility();
            MessageBox.Show("Configuración de proveedores y órdenes de compra guardada.", "Módulos", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        modulesGroup.Controls.Add(saveModules);
        Controls.Add(modulesGroup);

        var backup = new Button { Text = "CREAR RESPALDO DE BASE DE DATOS", Location = new Point(25, 2835), Width = 260, Height = 42 };
        backup.Click += (_, _) => { try { MessageBox.Show($"Respaldo creado en:\n{BackupService.CreateBackup()}"); } catch (Exception ex) { MessageBox.Show(ex.Message); } }; Controls.Add(backup);

        var customerBackup = new Button { Text = "RESPALDO CLIENTES + DEUDAS", Location = new Point(300, 2580), Width = 250, Height = 42 };
        customerBackup.Click += (_, _) =>
        {
            try
            {
                var path = CustomerBackupService.Sync();
                MessageBox.Show($"Respaldo oculto de clientes y cuentas corrientes actualizado.\n\nArchivo:\n{path}", "FerrariPOS · Respaldo clientes", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo actualizar el respaldo de clientes y deudas:\n\n" + ex.Message, "Respaldo clientes", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        Controls.Add(customerBackup);

        var customerBackupStatus = new Button { Text = "ESTADO DEL RESPALDO CLIENTES", Location = new Point(565, 2580), Width = 250, Height = 42 };
        customerBackupStatus.Click += (_, _) => MessageBox.Show(CustomerBackupService.Status(), "FerrariPOS · Respaldo clientes", MessageBoxButtons.OK, MessageBoxIcon.Information);
        Controls.Add(customerBackupStatus);
        var about = new Button { Text = "SOBRE EL PROGRAMA", Location = new Point(310, 2520), Width = 180, Height = 42 };
        about.Click += (_, _) => MessageBox.Show("FerrarisPOS\nPunto de Venta\nC# / .NET 8 + SQLite\nCaja, inventario, clientes, crédito y reportes.", "Sobre FerrarisPOS"); Controls.Add(about);
        var folder = new Button { Text = "ABRIR CARPETA DE DATOS", Location = new Point(510, 2520), Width = 200, Height = 42 };
        folder.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", DataDirectory()) { UseShellExecute = true }); Controls.Add(folder);

        // Brillo global de la interfaz. Se guarda en SQLite para que el mismo
        // ajuste visual se aplique a todos los empleados que utilizan esta base.
        var brightnessGroup = new GroupBox
        {
            Text = "PANTALLA · BRILLO GENERAL DEL PROGRAMA",
            Location = new Point(25, 2895),
            Size = new Size(960, 205)
        };
        brightnessGroup.Controls.Add(new Label
        {
            Text = "Ajustá la claridad de fondos, botones, campos y superficies para mejorar la lectura.",
            Location = new Point(20, 30),
            Size = new Size(650, 24),
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        });

        brightness.Minimum = 70;
        brightness.Maximum = 140;
        brightness.TickFrequency = 10;
        brightness.SmallChange = 5;
        brightness.LargeChange = 10;
        brightness.Value = Math.Clamp(ThemeService.BrightnessLevel, brightness.Minimum, brightness.Maximum);
        brightness.Location = new Point(20, 62);
        brightness.Width = 650;
        brightness.Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        brightnessGroup.Controls.Add(brightness);

        var brightnessValue = new Label
        {
            Text = $"{brightness.Value}%",
            Location = new Point(690, 60),
            Size = new Size(80, 30),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 11, FontStyle.Bold)
        };
        brightnessGroup.Controls.Add(brightnessValue);

        brightnessGroup.Controls.Add(new Label { Text = "70%  ·  Más oscuro", Location = new Point(20, 92), AutoSize = true, ForeColor = Color.DimGray });
        brightnessGroup.Controls.Add(new Label { Text = "100%  ·  Normal", Location = new Point(300, 92), AutoSize = true, ForeColor = Color.DimGray });
        brightnessGroup.Controls.Add(new Label { Text = "140%  ·  Más claro", Location = new Point(555, 92), AutoSize = true, ForeColor = Color.DimGray });

        var saveBrightness = new Button
        {
            Text = "GUARDAR BRILLO",
            Location = new Point(790, 58),
            Width = 140,
            Height = 45
        };
        saveBrightness.Click += (_, _) =>
        {
            ThemeService.SetBrightness(brightness.Value);
            brightnessValue.Text = $"{brightness.Value}%";
            MessageBox.Show($"Brillo global guardado en {brightness.Value}%.\n\nEste ajuste queda guardado para todos los empleados que utilizan esta base de datos.", "Brillo", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        brightnessGroup.Controls.Add(saveBrightness);

        brightness.Scroll += (_, _) => brightnessValue.Text = $"{brightness.Value}%";
        brightnessGroup.Controls.Add(new Label
        {
            Text = Session.IsAdmin ? "Solo ADMINISTRADOR puede cambiar este ajuste global." : "El brillo es un ajuste global. Solo ADMINISTRADOR puede modificarlo.",
            Location = new Point(20, 135),
            Size = new Size(650, 32),
            ForeColor = Color.DimGray
        });
        brightnessGroup.Enabled = Session.IsAdmin;
        Controls.Add(brightnessGroup);
    }

    private static void AddField(Control parent, string label, TextBox box, int y, string value)
    {
        parent.Controls.Add(new Label { Text = label, Location = new Point(20, y + 5), AutoSize = true, Font = new Font("Segoe UI", 8, FontStyle.Bold) });
        box.Location = new Point(155, y); box.Width = 400; box.Text = value; parent.Controls.Add(box);
    }

    private void LoadPrintPrinters()
    {
        var saved = PrintConfigurationService.SavedPrinter;
        printPrinter.Items.Clear();

        foreach (var printer in PrintConfigurationService.InstalledPrinters())
            printPrinter.Items.Add(printer);

        if (printPrinter.Items.Count == 0)
        {
            printPrinter.Text = "No hay impresoras instaladas";
            return;
        }

        var index = -1;
        if (!string.IsNullOrWhiteSpace(saved))
        {
            for (var i = 0; i < printPrinter.Items.Count; i++)
            {
                if (string.Equals(printPrinter.Items[i]?.ToString(), saved, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }
        }

        if (index < 0)
        {
            var defaultPrinter = new PrinterSettings().PrinterName;
            for (var i = 0; i < printPrinter.Items.Count; i++)
            {
                if (string.Equals(printPrinter.Items[i]?.ToString(), defaultPrinter, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }
        }

        printPrinter.SelectedIndex = index >= 0 ? index : 0;
    }

    private void SelectPdfPrinter()
    {
        var pdfPrinter = PrintConfigurationService.FindPdfPrinter();
        if (string.IsNullOrWhiteSpace(pdfPrinter))
        {
            MessageBox.Show(
                "No se encontró 'Microsoft Print to PDF' en Windows. Instalalo desde las características opcionales de Windows y luego presioná ACTUALIZAR.",
                "PDF", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        for (var i = 0; i < printPrinter.Items.Count; i++)
        {
            if (string.Equals(printPrinter.Items[i]?.ToString(), pdfPrinter, StringComparison.OrdinalIgnoreCase))
            {
                printPrinter.SelectedIndex = i;
                return;
            }
        }
    }

    private void ConfigurePrinter()
    {
        if (printPrinter.SelectedItem is not string selected || string.IsNullOrWhiteSpace(selected))
        {
            MessageBox.Show("Seleccioná una impresora primero.", "Impresión", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var document = new PrintDocument();
        document.PrinterSettings.PrinterName = selected;
        using var dialog = new PrintDialog { Document = document, UseEXDialog = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            var chosen = document.PrinterSettings.PrinterName;
            for (var i = 0; i < printPrinter.Items.Count; i++)
            {
                if (string.Equals(printPrinter.Items[i]?.ToString(), chosen, StringComparison.OrdinalIgnoreCase))
                {
                    printPrinter.SelectedIndex = i;
                    break;
                }
            }
            SavePrintConfiguration(true);
        }
    }

    private void SavePrintConfiguration(bool silent = false)
    {
        var printer = printPrinter.SelectedItem?.ToString() ?? "";
        if (string.IsNullOrWhiteSpace(printer))
        {
            MessageBox.Show("Seleccioná una impresora de salida.", "Impresión", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        PrintConfigurationService.Save(printer, printPreview.Checked);
        if (!silent)
        {
            MessageBox.Show(
                $"Configuración de impresión guardada.\n\nImpresora: {printer}\nVista previa: {(printPreview.Checked ? "ACTIVADA" : "DESACTIVADA")}\n\nPara generar PDF, seleccioná 'Microsoft Print to PDF'.",
                "Impresión / PDF", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void DetectSmtp()
    {
        var account = string.IsNullOrWhiteSpace(smtpUser.Text) ? emailTo.Text.Trim() : smtpUser.Text.Trim();
        var host = EmailReportService.SuggestHost(account);
        if (string.IsNullOrWhiteSpace(smtpUser.Text) && account.Contains('@'))
            smtpUser.Text = account;

        if (!string.IsNullOrWhiteSpace(host))
        {
            smtpHost.Text = host;
            smtpPort.Text = "587";
            smtpSsl.Checked = true;
            MessageBox.Show($"SMTP detectado: {host}:587 con TLS.\n\nSolo falta ingresar la contraseña o contraseña de aplicación.", "Correo", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show("No pude identificar automáticamente el proveedor. Ingresá manualmente el servidor SMTP.", "Correo", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void SaveEmail(bool silent = false)
    {
        if (emailEnabled.Checked)
        {
            if (string.IsNullOrWhiteSpace(emailTo.Text))
            {
                MessageBox.Show("Indicá el correo destinatario."); return;
            }
            if (string.IsNullOrWhiteSpace(smtpUser.Text))
                smtpUser.Text = emailTo.Text.Trim();
            if (string.IsNullOrWhiteSpace(smtpHost.Text))
                smtpHost.Text = EmailReportService.SuggestHost(smtpUser.Text);
            if (string.IsNullOrWhiteSpace(smtpPort.Text))
                smtpPort.Text = "587";
            if (string.IsNullOrWhiteSpace(smtpHost.Text) || string.IsNullOrWhiteSpace(smtpUser.Text) || string.IsNullOrWhiteSpace(smtpPassword.Text))
            {
                MessageBox.Show("Para enviar el Excel automáticamente necesitás completar servidor SMTP, usuario y contraseña/contraseña de aplicación.", "Configuración de correo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        Database.SetSetting("report_email_enabled", emailEnabled.Checked ? "1" : "0");
        Database.SetSetting("pos_link_email_enabled", posLinkEmail.Checked ? "1" : "0");
        Database.SetSetting("report_email_to", emailTo.Text.Trim());
        Database.SetSetting("smtp_host", smtpHost.Text.Trim());
        Database.SetSetting("smtp_port", string.IsNullOrWhiteSpace(smtpPort.Text) ? "587" : smtpPort.Text.Trim());
        Database.SetSetting("smtp_user", smtpUser.Text.Trim());
        Database.SetSetting("smtp_password", smtpPassword.Text);
        Database.SetSetting("smtp_ssl", smtpSsl.Checked ? "1" : "0");
        if (!silent)
            MessageBox.Show("Configuración de correo guardada.\n\nEl link público se enviará automáticamente cada vez que se ingrese al programa, siempre que el envío esté habilitado y el SMTP esté configurado.", "Correo", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void DetectSmtp2()
    {
        var account = string.IsNullOrWhiteSpace(smtpUser2.Text) ? emailTo2.Text.Trim() : smtpUser2.Text.Trim();
        var host = EmailReportService.SuggestHost(account);
        if (string.IsNullOrWhiteSpace(smtpUser2.Text) && account.Contains('@'))
            smtpUser2.Text = account;

        if (!string.IsNullOrWhiteSpace(host))
        {
            smtpHost2.Text = host;
            smtpPort2.Text = "587";
            smtpSsl2.Checked = true;
            MessageBox.Show($"SMTP detectado para el segundo correo: {host}:587 con TLS.\n\nSolo falta ingresar la contraseña o contraseña de aplicación.", "Segundo correo", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show("No pude identificar automáticamente el proveedor del segundo correo. Ingresá manualmente el servidor SMTP.", "Segundo correo", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void SaveEmail2(bool silent = false)
    {
        if (email2Enabled.Checked)
        {
            if (string.IsNullOrWhiteSpace(emailTo2.Text))
            { MessageBox.Show("Indicá el segundo correo destinatario."); return; }
            if (string.IsNullOrWhiteSpace(smtpUser2.Text))
                smtpUser2.Text = emailTo2.Text.Trim();
            if (string.IsNullOrWhiteSpace(smtpHost2.Text))
                smtpHost2.Text = EmailReportService.SuggestHost(smtpUser2.Text);
            if (string.IsNullOrWhiteSpace(smtpPort2.Text))
                smtpPort2.Text = "587";
            if (string.IsNullOrWhiteSpace(smtpHost2.Text) || string.IsNullOrWhiteSpace(smtpUser2.Text) || string.IsNullOrWhiteSpace(smtpPassword2.Text))
            {
                MessageBox.Show("Para el segundo correo necesitás completar servidor SMTP, usuario y contraseña/contraseña de aplicación.", "Segundo correo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        Database.SetSetting("report_email2_enabled", email2Enabled.Checked ? "1" : "0");
        Database.SetSetting("report_email_to2", emailTo2.Text.Trim());
        Database.SetSetting("smtp_host2", smtpHost2.Text.Trim());
        Database.SetSetting("smtp_port2", string.IsNullOrWhiteSpace(smtpPort2.Text) ? "587" : smtpPort2.Text.Trim());
        Database.SetSetting("smtp_user2", smtpUser2.Text.Trim());
        Database.SetSetting("smtp_password2", smtpPassword2.Text);
        Database.SetSetting("smtp_ssl2", smtpSsl2.Checked ? "1" : "0");
        if (!silent)
            MessageBox.Show("Configuración del segundo correo guardada.\n\nEl mismo informe de arqueo se enviará al segundo destinatario al cerrar caja.", "Segundo correo", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void TestEmail2()
    {
        try
        {
            SaveEmail2(true);
            if (!email2Enabled.Checked || string.IsNullOrWhiteSpace(emailTo2.Text)) return;
            // Enviamos una prueba solo al segundo perfil, sin alterar ni depender del primero.
            Database.SetSetting("report_email2_enabled", "1");
            EmailReportService.SendToSecond("FerrarisPOS · Prueba de segundo correo", "Este es un correo de prueba de FerrarisPOS para el segundo destinatario.\n\nLa configuración SMTP 2 funciona correctamente.");
            MessageBox.Show("Correo de prueba del segundo destinatario enviado correctamente.", "Segundo correo", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo enviar la prueba del segundo correo:\n\n" + ex.Message, "Segundo correo", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void TestEmail()
    {
        try
        {
            SaveEmail(true);
            if (!EmailReportService.IsConfigured)
                return;
            EmailReportService.Send("FerrarisPOS · Prueba de correo", "Este es un correo de prueba de FerrarisPOS.\n\nLa configuración SMTP funciona correctamente.");
            MessageBox.Show("Correo de prueba enviado correctamente.", "Correo", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo enviar el correo de prueba:\n\n" + ex.Message, "Correo", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ApplyDisplay()
    {
        var index = Math.Max(0, resolution.SelectedIndex);
        var selected = DisplayService.Resolutions[index];
        var resolutionValue = selected.Width == 0 || selected.Height == 0
            ? "auto"
            : $"{selected.Width}x{selected.Height}";
        Database.SetSetting("resolution", resolutionValue);
        Database.SetSetting("fullscreen", fullscreen.Checked ? "1" : "0");
        if (Owner != null)
        {
            // La configuración se abre como diálogo modal. Nunca traer el Owner
            // al frente aquí: hacerlo puede dejar esta ventana detrás de la
            // pantalla principal, aunque siga modal, obligando a recuperarla
            // con TAB. Aplicamos el cambio al Owner y mantenemos Configuración
            // por encima.
            DisplayService.Apply(Owner);
            BeginInvoke(new Action(() =>
            {
                if (!IsDisposed && !Disposing)
                {
                    BringToFront();
                    Activate();
                }
            }));
        }
        MessageBox.Show($"Pantalla aplicada: {selected.Name}{(fullscreen.Checked ? "\nModo pantalla completa activado." : "")}", "Pantalla", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static string DataDirectory() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FerrarisPOS");
    private void UpdateWebLink()
    {
        if (webLinkGroup == null) return;
        var url = WebDashboardServer.Current?.AccessUrl ?? "";
        if (string.IsNullOrWhiteSpace(url))
        {
            webLink.Text = "Conectando automáticamente...";
            webLinkStatus.Text = "● Conectando FerrariPOS con el acceso público...";
            webLinkStatus.ForeColor = Color.DarkOrange;
            return;
        }

        webLink.Text = url;
        if (url.Contains("trycloudflare.com", StringComparison.OrdinalIgnoreCase))
        {
            webLinkStatus.Text = "● ONLINE · Link público activo · El cliente no necesita instalar Cloudflare.";
            webLinkStatus.ForeColor = Color.DarkGreen;
        }
        else
        {
            webLinkStatus.Text = "● Servidor local activo · esperando enlace público...";
            webLinkStatus.ForeColor = Color.DarkOrange;
        }
    }

    private void UpdateView()
    {
        var d = LicenseService.DaysRemaining;
        license.Text = LicenseService.GetStatusText();
        license.ForeColor = d >= 0 && d <= 30 ? Color.DarkRed : Color.DarkGreen;

        var expiry = LicenseService.IsPermanent
            ? "Vencimiento: PERMANENTE"
            : $"Vencimiento: {LicenseService.ExpiresAtUtc.ToLocalTime():dd/MM/yyyy HH:mm}";

        licenseDetails.Text =
            $"ID LICENCIA: {(string.IsNullOrWhiteSpace(LicenseService.LicenseId) ? "SIN ACTIVAR" : LicenseService.LicenseId)} · {expiry}";
    }

    private new void Activate()
    {
        var token = key.Text.Trim();

        if (string.IsNullOrWhiteSpace(token))
        {
            MessageBox.Show("Pegá el código de licencia antes de continuar.", "Licencia", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (LicenseService.ApplyLicenseToken(token, out var message))
        {
            MessageBox.Show(message, "FerrarisPOS · Licencia", MessageBoxButtons.OK, MessageBoxIcon.Information);
            UpdateView();
            key.Clear();
        }
        else
        {
            MessageBox.Show(message, "FerrarisPOS · Licencia", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
