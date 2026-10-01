using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using FerrarisPOS.Models;
using FerrarisPOS.Data;
using FerrarisPOS.Services;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Text.Json;

namespace FerrarisPOS.Forms;

public class MainForm : Form
{
    private readonly TextBox barcode = new();
    private readonly DataGridView grid = new();
    private readonly FlickerFreeLabel totalLabel = new();
    private readonly Label itemCountLabel = new();
    private readonly Label licenseLabel = new();
    private readonly Label statusLabel = new();
    private readonly Label userLabel = new();
    private readonly Label dailyTicketCountLabel = new();
    private readonly BlackTicketTabControl ticketTabs = new();
    private readonly List<List<CartItem>> tickets = new() { new List<CartItem>() };
    private readonly List<string> ticketNames = new() { "VENTA 1" };
    // Identificador estable para exponer tickets locales de Windows al Manager Android.
    private readonly List<string> ticketRemoteIds = new() { Guid.NewGuid().ToString("N") };
    private int activeTicket;
    // Cantidad pendiente cuando el usuario escribe, por ejemplo, 5* y luego abre F10.
    private double pendingSearchQuantity = 1;
    private readonly System.Windows.Forms.Timer licenseTimer = new() { Interval = 1000 };
    private readonly System.Windows.Forms.Timer onlineLicenseTimer = new() { Interval = 30000 };
    private Panel windowBar = null!;
    private Point windowDragStart;
    private bool windowDragging;
    private int selectedCartRow = -1;
    private bool refreshingCartGrid;
    private bool refreshingTicketTabs;
    private readonly Dictionary<int, int> ticketTableMap = new();
    private readonly Dictionary<int, int> mobileTableTicketMap = new();
    private readonly Dictionary<long, int> mobilePendingTicketMap = new();
    private readonly Dictionary<long, int> mobilePendingCustomerMap = new();
    private readonly System.Windows.Forms.Timer mobileTicketsTimer = new() { Interval = 1500 };
    private int mobileSyncRunning;
    private long lastCompletedSaleId;
    private long lastSalonMobileRefreshTicks;
    private string lastMobileTableSignature = string.Empty;
    private TableLayoutPanel mainCenter = null!;
    private SalonForm? salonWindow;
    private Button salonButton = null!;
    private Button assignTableButton = null!;
    private Button providerShortcutButton = null!;
    private Button purchasesButton = null!;
    private int UserId => Session.UserId;
    private List<CartItem> Cart => tickets[activeTicket];
    private Image? watermarkImage;
    private Image? gridWatermarkImage;
    private double ticketGeneralDiscountAmount;
    private readonly Dictionary<CartItem, double> generalDiscountAllocations = new();

    public MainForm()
    {
        CurrentInstance=this;
        Text = "Ferrari'sPOS® - Punto de Venta";
        TrySetProgramIcon();
        Width = 1500; Height = 850; MinimumSize = new Size(1000, 650);
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = true;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = ThemeService.IsLightTheme ? Color.FromArgb(246, 248, 251) : Color.FromArgb(10, 14, 24);
        KeyPreview = true;
        KeyDown += MainKeyDown;
        Build();
        watermarkImage = null;
        gridWatermarkImage = null;
        EnableAntiFlicker();
        Resize += (_, _) => ApplyConfiguredTotalFont();
        LanguageService.Apply(this);
        ThemeService.Apply(this);
        grid.BackgroundImage = null;
        grid.BackgroundImageLayout = ImageLayout.None;
        DisplayService.Apply(this);
        EnablePermanentBarcodeFocus();
        Activated += (_, _) =>
        {
            // Si una ventana secundaria modal quedó visualmente detrás por un
            // cambio de resolución, tema o activación de la ventana principal,
            // devolver inmediatamente esa ventana al frente. La ventana principal
            // nunca debe quedar por encima de un diálogo que sigue abierto.
            var child = OwnedForms.FirstOrDefault(x => x != null && !x.IsDisposed && x.Visible);
            if (child != null)
            {
                BeginInvoke(new Action(() =>
                {
                    if (!child.IsDisposed && !child.Disposing && child.Visible)
                    {
                        child.BringToFront();
                        child.Activate();
                    }
                }));
                return;
            }

            RefreshSessionInfo();
            RefreshDailySales();
            UpdateSalonIntegration();
            salonWindow?.RefreshSalon();
            UpdateOptionalModulesVisibility();
            FocusBarcodeWhenReady();
        };
        RefreshLicense();
        RefreshSessionInfo();
        RefreshDailySales();
        licenseTimer.Tick += (_, _) =>
        {
            if (!LicenseService.CanRun())
            {
                licenseTimer.Stop();
                MessageBox.Show(
                    "El período de prueba ha finalizado. El programa se cerrará.",
                    "FerrariPOS - Licencia vencida",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Stop);
                Close();
            }
            else
            {
                RefreshLicense();
            }
        };
        licenseTimer.Start();
        FormClosed += (_, _) => { licenseTimer.Dispose(); onlineLicenseTimer.Dispose(); mobileTicketsTimer.Dispose(); watermarkImage?.Dispose(); gridWatermarkImage?.Dispose(); Session.Clear(); };
        RefreshTabs();
        RefreshGrid();
        RefreshSessionInfo();
        RefreshDailySales();
        RefreshLicense();
        UpdateSalonIntegration();
        UpdateOptionalModulesVisibility();
        LanguageService.Apply(this);
        Shown += async (_, _) => { EnsureCashIsOpen(); await ImportMobileOpenTicketsAsync(); };
        mobileTicketsTimer.Tick += async (_, _) => await ImportMobileOpenTicketsAsync();
        mobileTicketsTimer.Start();
    }

    private void EnsureCashIsOpen()
    {
        if (IsDisposed || Disposing || Session.UserId <= 0) return;
        if (CashService.IsOpen()) return;

        using var opening = new CashOpeningForm();
        ThemeService.Apply(opening);
        opening.StartPosition = FormStartPosition.CenterParent;
        var result = ShowOwnedDialog(opening);
        if (result != DialogResult.OK)
        {
            Close();
            return;
        }

        try
        {
            CashService.Open(opening.OpeningAmount, Session.UserId, opening.MercadoPagoEnabled, opening.MercadoPagoOpeningAmount);
            try
            {
                var sessionId = CashService.CurrentSessionId();
                if (sessionId.HasValue) _ = EmailReportService.SendPosLinkOnCashOpenAsync(sessionId.Value, opening.OpeningAmount, Session.UserId);
            }
            catch { }
            RefreshSessionInfo();
            MessageBox.Show($"Caja abierta correctamente con un fondo inicial de ${opening.OpeningAmount:N2}." + (opening.MercadoPagoEnabled ? $"\nSaldo inicial Mercado Pago: ${opening.MercadoPagoOpeningAmount:N2}." : ""),
                "Apertura de caja", MessageBoxButtons.OK, MessageBoxIcon.Information);
            FocusBarcodeWhenReady();
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo abrir la caja:\n\n" + ex.Message, "Apertura de caja", MessageBoxButtons.OK, MessageBoxIcon.Error);
            BeginInvoke(new Action(EnsureCashIsOpen));
        }
    }

    private void InitializeWatermark()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Resources", "pos_background.png");
            if (!File.Exists(path))
                return;

            using var source = Image.FromFile(path);

            // El fondo general se conserva al 100 %. La transparencia/difuminado
            // se aplica únicamente a los paneles y al carrito, nunca al fondo base.
            watermarkImage?.Dispose();
            watermarkImage = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(watermarkImage))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height));
            }

            // En el carrito usamos una copia difuminada de la misma marca de agua.
            // La grilla queda con un velo negro semitransparente para que la imagen
            // se perciba detrás de los productos sin competir con la lectura.
            gridWatermarkImage?.Dispose();
            using var cartOpacitySource = CreateOpacityCopy(watermarkImage, 0.49f);
            gridWatermarkImage = CreateBlurredWatermark(cartOpacitySource);
            grid.BackgroundImage = gridWatermarkImage;
            grid.BackgroundImageLayout = ImageLayout.Zoom;
        }
        catch
        {
            watermarkImage?.Dispose();
            watermarkImage = null;
        }
    }

    private static Bitmap CreateOpacityCopy(Image source, float opacity)
    {
        var copy = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(copy);
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(new ColorMatrix { Matrix33 = Math.Clamp(opacity, 0f, 1f) }, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
        g.Clear(Color.Transparent);
        g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        return copy;
    }

    private static Bitmap CreateBlurredWatermark(Image source)
    {
        // Reducir y volver a ampliar produce un desenfoque suave y barato,
        // suficiente para que el fondo funcione como marca de agua.
        const int factor = 6;
        int smallW = Math.Max(1, source.Width / factor);
        int smallH = Math.Max(1, source.Height / factor);

        var small = new Bitmap(smallW, smallH, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(small))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(source, new Rectangle(0, 0, smallW, smallH));
        }

        var blurred = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(blurred))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = SmoothingMode.HighQuality;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(small, new Rectangle(0, 0, blurred.Width, blurred.Height));
        }

        small.Dispose();
        return blurred;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);

        if (ThemeService.IsLightTheme)
        {
            using var lightBrush = new SolidBrush(Color.FromArgb(246, 248, 251));
            e.Graphics.FillRectangle(lightBrush, ClientRectangle);
            return;
        }

        if (watermarkImage == null || ClientSize.Width <= 0 || ClientSize.Height <= 0)
            return;

        var client = ClientRectangle;

        // Cover: la marca ocupa todo el fondo sin deformar la imagen.
        float scale = Math.Max(
            (float)client.Width / watermarkImage.Width,
            (float)client.Height / watermarkImage.Height);

        int width = Math.Max(1, (int)Math.Ceiling(watermarkImage.Width * scale));
        int height = Math.Max(1, (int)Math.Ceiling(watermarkImage.Height * scale));
        int x = (client.Width - width) / 2;
        int y = (client.Height - height) / 2;

        e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        e.Graphics.DrawImage(
            watermarkImage,
            new Rectangle(x, y, width, height),
            0,
            0,
            watermarkImage.Width,
            watermarkImage.Height,
            GraphicsUnit.Pixel);
    }

    private void EnablePermanentBarcodeFocus()
    {
        // En la pantalla principal el lector trabaja como teclado: cualquier
        // clic sobre una zona que no sea el campo de código devuelve el foco al
        // TextBox del lector. Las ventanas secundarias quedan fuera de este
        // comportamiento y, al cerrarse, Activated vuelve a enfocar el lector.
        HookBarcodeFocus(this);
        FocusBarcodeWhenReady();
    }

    private void HookBarcodeFocus(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            if (child != barcode)
            {
                child.MouseUp += (_, _) => FocusBarcodeWhenReady();
                if (child.HasChildren)
                    HookBarcodeFocus(child);
            }
        }
    }

    private void FocusBarcodeWhenReady()
    {
        if (IsDisposed || Disposing || !IsHandleCreated || !Visible || WindowState == FormWindowState.Minimized)
            return;

        try
        {
            BeginInvoke(new Action(() =>
            {
                if (IsDisposed || Disposing || !Visible || WindowState == FormWindowState.Minimized) return;
                if (Form.ActiveForm != null && Form.ActiveForm != this) return;
                barcode.Focus();
                barcode.SelectAll();
            }));
        }
        catch (InvalidOperationException) { }
    }

    private void TrySetProgramIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "FerrariPOS_icono.ico");
            if (File.Exists(path)) Icon = new Icon(path);
        }
        catch { }
    }

    private Button Btn(string text, EventHandler click, int width = 120)
    {
        var b = new RoundedButton
        {
            Text = text,
            Width = width,
            Height = 36,
            Margin = new Padding(3),
            Padding = new Padding(3, 0, 3, 0),
            TabStop = false,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderSize = 1;
        b.Click += click;
        return b;
    }

    private void BuildWindowBar()
    {
        windowBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 34,
            BackColor = Color.FromArgb(35, 39, 47),
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };

        var title = new Label
        {
            Text = "Ferrari'sPOS® - Punto de Venta",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Cursor = Cursors.SizeAll,
            BackColor = Color.Transparent
        };
        title.MouseDown += WindowBarMouseDown;
        title.MouseMove += WindowBarMouseMove;
        title.MouseUp += WindowBarMouseUp;
        title.DoubleClick += (_, _) => ToggleMaximize();
        windowBar.Controls.Add(title);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            Width = 126,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };

        var minimize = WindowButton("—", "Minimizar", (_, _) => WindowState = FormWindowState.Minimized);
        var maximize = WindowButton("□", "Maximizar / Restaurar", (_, _) => ToggleMaximize());
        var close = WindowButton("×", "Cerrar", (_, _) => Close());
        buttons.Controls.Add(minimize);
        buttons.Controls.Add(maximize);
        buttons.Controls.Add(close);
        windowBar.Controls.Add(buttons);

        void PositionCenteredTitle()
        {
            if (windowBar.IsDisposed || title.IsDisposed) return;
            title.Bounds = new Rectangle(0, 0, Math.Max(150, windowBar.ClientSize.Width - 126), windowBar.ClientSize.Height);
            title.BringToFront();
            buttons.BringToFront();
        }
        windowBar.Resize += (_, _) => PositionCenteredTitle();
        PositionCenteredTitle();

        // Línea de identidad visual: reemplaza el tapiz por un acento discreto
        // que nunca se superpone con textos, tablas ni botones.
        var accentLine = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 3,
            Margin = Padding.Empty,
            Tag = "ThemeAccent"
        };
        windowBar.Controls.Add(accentLine);
        accentLine.BringToFront();

        windowBar.MouseDown += WindowBarMouseDown;
        windowBar.MouseMove += WindowBarMouseMove;
        windowBar.MouseUp += WindowBarMouseUp;
        windowBar.DoubleClick += (_, _) => ToggleMaximize();

        Controls.Add(windowBar);
        windowBar.BringToFront();
    }

    private Button WindowButton(string text, string tooltip, EventHandler click)
    {
        var b = new Button
        {
            Text = text,
            Width = 32,
            Height = 30,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(35, 39, 47),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10f, FontStyle.Regular),
            TabStop = false,
            Cursor = Cursors.Hand,
            AccessibleName = tooltip
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(65, 72, 84);
        b.FlatAppearance.MouseDownBackColor = Color.FromArgb(85, 92, 105);
        b.FlatAppearance.BorderSize = 0;
        b.Click += click;
        return b;
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == FormWindowState.Maximized
            ? FormWindowState.Normal
            : FormWindowState.Maximized;
    }

    private void WindowBarMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        windowDragging = true;
        windowDragStart = e.Location;
    }

    private void WindowBarMouseMove(object? sender, MouseEventArgs e)
    {
        if (!windowDragging || e.Button != MouseButtons.Left || WindowState == FormWindowState.Maximized)
            return;

        var screen = PointToScreen(e.Location);
        Location = new Point(screen.X - windowDragStart.X, screen.Y - windowDragStart.Y);
    }

    private void WindowBarMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) windowDragging = false;
    }

    private void Build()
    {
        // Layout principal responsive. La barra F1-F12 ocupa un espacio propio
        // y queda SIEMPRE visible encima de las pestañas/tickets de venta.
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Tag = "WatermarkTransparent"
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 198));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));
        Controls.Add(root);
        BuildWindowBar();

        // ========================= CABECERA =========================
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(7, 4, 7, 3),
            Margin = Padding.Empty,
            Tag = "WatermarkTransparent"
        };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));  // datos
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));  // F1-F12
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));  // búsqueda
        root.Controls.Add(header, 0, 0);

        // ========================= DATOS EN TIEMPO REAL =========================
        var titlePanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        for (int i = 0; i < 4; i++)
            titlePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

        Panel InfoCard(string caption, Label value)
        {
            var card = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(3, 2, 3, 2),
                Padding = new Padding(8, 4, 8, 3),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White
            };

            var cap = new Label
            {
                Text = caption,
                Location = new Point(8, 4),
                Size = new Size(250, 16),
                Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                BackColor = Color.Transparent
            };

            value.Location = new Point(8, 22);
            value.Size = new Size(250, 28);
            value.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            value.TextAlign = ContentAlignment.MiddleLeft;
            value.AutoEllipsis = true;
            value.BackColor = Color.Transparent;
            value.ForeColor = Color.FromArgb(25, 65, 45);
            value.Margin = Padding.Empty;
            value.Padding = Padding.Empty;

            card.Controls.Add(cap);
            card.Controls.Add(value);
            return card;
        }

        titlePanel.Controls.Add(InfoCard("LE ATIENDE", userLabel), 0, 0);
        titlePanel.Controls.Add(InfoCard("LICENCIA", licenseLabel), 1, 0);
        titlePanel.Controls.Add(InfoCard("TICKETS HOY", dailyTicketCountLabel), 2, 0);
        titlePanel.Controls.Add(InfoCard("ESTADO", statusLabel), 3, 0);
        header.Controls.Add(titlePanel, 0, 0);

        // ========================= BARRA F1-F12 =========================
        // 12 botones iguales, visibles y seleccionables con mouse.
        var shortcuts = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 12,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = new Padding(0, 2, 0, 2)
        };

        string[] keyNames =
        {
            "F1","F2","F3","F4","F5","F6",
            "F7","F8","F9","F10","F11","F12"
        };
        string[] captions =
        {
            "VENTA","CLIENTES","PRODUCTOS","INVENTARIO","CAJA","REPORTES",
            "CONFIGURACIÓN","USUARIOS","PROVEEDORES","BUSCAR","INGRESOS / EGRESOS","COBRAR"
        };

        EventHandler[] handlers =
        {
            (_, _) => { if (Require("VENTAS")) NewSale(); },
            (_, _) => { if (Require("CLIENTES")) Open(new CustomersForm()); },
            (_, _) => { if (Require("PRODUCTOS")) Open(new ProductsForm()); },
            (_, _) => { if (Require("INVENTARIO")) Open(new InventoryForm()); },
            (_, _) => { if (Require("CAJA")) Open(new CashForm()); },
            (_, _) => { if (Require("REPORTES")) Open(new ReportsForm()); },
            (_, _) => { if (Require("CONFIGURACION")) Open(new SettingsForm()); },
            (_, _) =>
            {
                if (Session.IsAdmin) Open(new UsersForm());
                else MessageBox.Show("Solo ADMIN puede administrar usuarios.", "Permisos",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            },
            (_, _) => { if (RequireOptionalModule("PROVEEDORES", "providers_enabled")) Open(new SuppliersForm()); },
            (_, _) => OpenSearch(),
            (_, _) => { if (Require("CAJA")) Open(new IncomeExpenseForm()); },
            (_, _) => { if (Require("VENTAS")) Charge(); }
        };

        for (int i = 0; i < 12; i++)
        {
            shortcuts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 12f));

            var b = new RoundedButton
            {
                Text = $"{new[] { "💵", "👥", "📦", "📈", "💰", "⚖️", "🔧", "👤", "🚚", "🔍", "↕", "💲" }[i]} {keyNames[i]}\r\n{captions[i]}",
                Dock = DockStyle.Fill,
                Margin = new Padding(2, 2, 2, 2),
                Padding = new Padding(2, 0, 2, 0),
                Font = new Font("Segoe UI Semibold", 8.2f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.FromArgb(45, 52, 64),
                FlatStyle = FlatStyle.Flat,
                UseVisualStyleBackColor = false,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.BorderColor = Color.FromArgb(92, 105, 125);
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(68, 78, 94);
            b.FlatAppearance.MouseDownBackColor = Color.FromArgb(30, 36, 45);
            b.Click += handlers[i];
            if (i == 8) providerShortcutButton = b;
            shortcuts.Controls.Add(b, i, 0);
        }
        header.Controls.Add(shortcuts, 0, 1);

        // ========================= BÚSQUEDA =========================
        var searchPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        searchPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));

        barcode.Dock = DockStyle.Fill;
        barcode.Margin = new Padding(2, 2, 6, 2);
        barcode.Tag = "FixedMainBarcode";
        barcode.Font = new Font("Segoe UI", 20, FontStyle.Bold);
        barcode.PlaceholderText = "CÓDIGO DE BARRAS · Enter para agregar · 3*CODIGO = 3 unidades · 3*PRODUCTO = 3 unidades";
        barcode.KeyDown += BarcodeKeyDown;
        searchPanel.Controls.Add(barcode, 0, 0);

        var add = Btn("ENTER · AGREGAR", (_, _) => AddByBarcode(), 135);
        add.Dock = DockStyle.Fill;
        searchPanel.Controls.Add(add, 1, 0);

        var search = Btn("F10 · BUSCAR", (_, _) => { if (Require("VENTAS")) OpenSearch(); }, 120);
        search.Dock = DockStyle.Fill;
        searchPanel.Controls.Add(search, 2, 0);
        header.Controls.Add(searchPanel, 0, 2);

        // ========================= VENTA =========================
        mainCenter = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(7, 5, 7, 5),
            Margin = Padding.Empty,
            Tag = "WatermarkTransparent"
        };
        mainCenter.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        mainCenter.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(mainCenter, 0, 1);

        var saleTabsBar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Tag = "WatermarkTransparent"
        };
        saleTabsBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        ticketTabs.Dock = DockStyle.Fill;
        ticketTabs.Margin = Padding.Empty;
        ticketTabs.Padding = new Point(0, 0);
        ticketTabs.ItemSize = new Size(112, 28);
        ticketTabs.SizeMode = TabSizeMode.Fixed;
        ticketTabs.Multiline = false;

        // Pestañas de ventas: fondo negro fijo y texto dividido por color.
        // "VENTA" queda gris y el número queda celeste flúor.
        // Se dibuja manualmente para evitar que Windows/tema deje un fondo blanco.
        ticketTabs.DrawMode = TabDrawMode.OwnerDrawFixed;
        ticketTabs.Tag = "TicketTabs";
        ticketTabs.DrawItem -= DrawTicketTab;
        ticketTabs.DrawItem += DrawTicketTab;
        ticketTabs.BackColor = ThemeService.IsLightTheme ? Color.White : Color.Black;
        ticketTabs.ForeColor = ThemeService.IsLightTheme ? Color.FromArgb(35, 43, 52) : Color.Gainsboro;
        ticketTabs.Appearance = TabAppearance.Normal;
        ticketTabs.SetHeaderBlackMode(!ThemeService.IsLightTheme);

        ticketTabs.SelectedIndexChanged += (_, _) =>
        {
            if (!refreshingTicketTabs && ticketTabs.SelectedIndex >= 0 && ticketTabs.SelectedIndex < tickets.Count)
            {
                activeTicket = ticketTabs.SelectedIndex;
                RefreshGrid();
                barcode.Focus();
            }
        };
        ticketTabs.MouseUp += TicketTabsMouseUp;
        saleTabsBar.Controls.Add(ticketTabs, 0, 0);

        mainCenter.Controls.Add(saleTabsBar, 0, 0);

        grid.Dock = DockStyle.Fill;
        grid.BackgroundImageLayout = ImageLayout.Zoom;
        grid.BackgroundImage = gridWatermarkImage ?? watermarkImage;
        grid.Tag = "CartGridWatermark";
        grid.AutoGenerateColumns = false;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.ReadOnly = true;
        grid.Visible = true;
        grid.BorderStyle = BorderStyle.FixedSingle;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.SelectionChanged += (_, _) =>
        {
            // Durante RefreshGrid WinForms dispara SelectionChanged varias veces
            // mientras limpia y reconstruye las filas. Esas selecciones internas
            // NUNCA deben reemplazar la selección lógica del ticket.
            if (refreshingCartGrid) return;
            if (grid.CurrentRow?.Index >= 0 && grid.CurrentRow.Index < Cart.Count && grid.Rows[grid.CurrentRow.Index].Selected)
                selectedCartRow = grid.CurrentRow.Index;
            ApplySelectedProductHighlight();
        };
        grid.CellMouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && e.RowIndex >= 0 && e.RowIndex < Cart.Count)
            {
                // Capturamos la fila al momento exacto del clic, antes de que el
                // foco vuelva al lector de códigos. Esta es la fuente de verdad
                // para +, -, Supr y descuentos.
                selectedCartRow = e.RowIndex;
            }
        };
        // La navegación del ticket funciona también si en algún momento la grilla
        // recibe el foco directamente. En la pantalla principal el foco vuelve al
        // lector de códigos, por lo que MainKeyDown también procesa estas teclas.
        grid.KeyDown += (_, e) => HandleCartNavigationKey(e);
        grid.CellDoubleClick += (_, _) => ApplySelectedDiscount();
        grid.MouseUp += (_, _) => FocusBarcodeWhenReady();
        grid.MultiSelect = false;
        // DataGridView no soporta BackColor/BackgroundColor con alfa. La
        // transparencia visual de la marca de agua se conserva pintando la
        // marca sobre el fondo opaco de cada celda, antes del contenido.
        grid.CellPainting -= PaintCartGridWatermark;
        grid.CellPainting += PaintCartGridWatermark;
        grid.RowHeadersVisible = false;
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        grid.ColumnHeadersHeight = 36;
        grid.RowTemplate.Height = 34;

        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Barcode", HeaderText = "CÓDIGO", FillWeight = 13 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Description", HeaderText = "DESCRIPCIÓN", FillWeight = 27 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "UnitPrice", HeaderText = "PRECIO", FillWeight = 11, DefaultCellStyle = new DataGridViewCellStyle { Format = "N2", Alignment = DataGridViewContentAlignment.MiddleRight } });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Quantity", HeaderText = "CANTIDAD", FillWeight = 12, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Total", HeaderText = "IMPORTE", FillWeight = 11, DefaultCellStyle = new DataGridViewCellStyle { Format = "N2", Alignment = DataGridViewContentAlignment.MiddleRight } });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Stock", HeaderText = "STOCK", FillWeight = 9, DefaultCellStyle = new DataGridViewCellStyle { Format = "N2", Alignment = DataGridViewContentAlignment.MiddleRight } });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Discount", HeaderText = "DESCUENTO", FillWeight = 9, DefaultCellStyle = new DataGridViewCellStyle { Format = "N2", Alignment = DataGridViewContentAlignment.MiddleRight } });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Mode", HeaderText = "TIPO", FillWeight = 8 });

        var gridMenu = new ContextMenuStrip();
        gridMenu.Items.Add("EDITAR PRECIO", null, (_, _) => EditSelectedPrice());
        gridMenu.Items.Add("DESCUENTO", null, (_, _) => ApplySelectedDiscount());
        gridMenu.Items.Add("ELIMINAR LÍNEA", null, (_, _) => DeleteSelected());
        grid.ContextMenuStrip = gridMenu;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        mainCenter.Controls.Add(grid, 0, 1);

        // ========================= BARRA INFERIOR =========================
        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(10, 5, 10, 5),
            Margin = Padding.Empty,
            Tag = "WatermarkTransparent"
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
        bottom.RowCount = 3;
        // Recupera el espacio que quedaba vacío debajo de la venta.
        // Las columnas y el diseño de los botones no cambian.
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.Controls.Add(bottom, 0, 2);

        totalLabel.AutoSize = false;
        totalLabel.Dock = DockStyle.Fill;
        totalLabel.TextAlign = ContentAlignment.MiddleLeft;
        totalLabel.AutoEllipsis = false;
        // Tamaño moderadamente reducido, pero todavía muy visible.
        totalLabel.Tag = "FixedMainTotal";
        totalLabel.Font = new Font("Segoe UI", 40, FontStyle.Bold);
        totalLabel.Use3DText = true;
        totalLabel.Resize += (_, _) => ApplyConfiguredTotalFont();
        totalLabel.VisibleChanged += (_, _) => ApplyConfiguredTotalFont();
        totalLabel.Padding = new Padding(0, 0, 4, 0);
        totalLabel.Cursor = Cursors.Hand;
        totalLabel.Click += (_, _) => ApplyGeneralTicketDiscount();
        totalLabel.MouseEnter += (_, _) => totalLabel.ForeColor = Color.Gold;
        totalLabel.MouseLeave += (_, _) =>
        {
            var current = Cart.Sum(x => x.Total);
            totalLabel.ForeColor = current > 0 ? Color.FromArgb(80, 245, 255) : Color.FromArgb(155, 155, 155);
        };
        bottom.Controls.Add(totalLabel, 0, 0);

        var actionsTop = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = false,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };
        void AddAction(string text, EventHandler handler, int w = 118)
        {
            var b = Btn(text, handler, w);
            b.Height = 36;
            actionsTop.Controls.Add(b);
        }
        AddAction("−", (_, _) => ChangeSelectedQuantity(-1), 45);
        AddAction("+", (_, _) => ChangeSelectedQuantity(1), 45);
        AddAction("ELIMINAR", (_, _) => DeleteSelected(), 110);
        AddAction("NUEVA VENTA", (_, _) => NewSale(), 125);
        AddAction("FAVORITOS", (_, _) => OpenFavorites(), 110);
        AddAction("NOMBRE VENTA", (_, _) => RenameActiveTicket(), 125);

        // IMP-TICKET queda junto a NOMBRE VENTA, en la barra superior de acciones.
        // De esta manera no ocupa la barra inferior ni genera desplazamiento horizontal.
        var printTicketButton = Btn("IMP-TICKET", (_, _) =>
        {
            if (!Require("VENTAS")) return;
            // Un único selector: desde acá se puede elegir cualquier ticket y luego
            // decidir entre imprimirlo o prepararlo para WhatsApp.
            using var picker = new TicketPrintForm(lastCompletedSaleId);
            ThemeService.Apply(picker);
            ShowOwnedDialog(picker);
        }, 110);
        printTicketButton.Height = 36;
        printTicketButton.Margin = new Padding(3, 1, 3, 1);
        printTicketButton.Font = new Font("Segoe UI Semibold", 8.2f, FontStyle.Bold);
        printTicketButton.Padding = new Padding(2, 0, 2, 0);
        actionsTop.Controls.Add(printTicketButton);
        bottom.Controls.Add(actionsTop, 1, 0);

        itemCountLabel.AutoSize = false;
        itemCountLabel.Dock = DockStyle.Fill;
        itemCountLabel.TextAlign = ContentAlignment.BottomLeft;
        itemCountLabel.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        bottom.Controls.Add(itemCountLabel, 0, 2);

        var actionsBottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoScroll = false,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };
        AddActionTo(actionsBottom, "F12 · COBRAR", (_, _) => { if (Require("VENTAS")) Charge(); }, 130);
        AddActionTo(actionsBottom, "INGRESO / EGRESO", (_, _) => { if (Require("CAJA")) Open(new IncomeExpenseForm()); }, 150);
        AddActionTo(actionsBottom, "CTRL+P · COMÚN", (_, _) => AddCommonProduct(), 130);
        assignTableButton = (RoundedButton)Btn("÷ · MESA", (_, _) => AssignActiveTicketToTable(), 105);
        salonButton = (RoundedButton)Btn("SALÓN", (_, _) => OpenSalon(false), 95);
        // REGla permanente de FerrariPOS: el QR de vinculación y la URL pública
        // deben poder verse desde cualquier usuario/cajero. Mostrar el QR NO
        // modifica permisos administrativos ni habilita funciones de gestión;
        // solamente entrega los datos necesarios para conectar Manager.
        var mobileQrButton = (RoundedButton)Btn("VINCULAR QR", (_, _) =>
        {
            try
            {
                ShowOwnedDialog(new MobilePairingForm());
            }
            catch (Exception ex)
            {
                MessageBox.Show("No se pudo abrir el QR de vinculación.\n\n" + ex.Message,
                    "FerrariPOS Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }, 110);
        actionsBottom.Controls.Add(assignTableButton);
        actionsBottom.Controls.Add(salonButton);
        // RightToLeft: al agregarse tercero, queda visualmente a la izquierda de SALÓN.
        actionsBottom.Controls.Add(mobileQrButton);
        bottom.Controls.Add(actionsBottom, 1, 2);

        // Acceso a tickets/devoluciones: se mantiene en la barra secundaria inferior.
        // IMP-TICKET está arriba, junto a NOMBRE VENTA, para evitar cualquier barra de desplazamiento.
        var dailyTicketsButton = Btn("DEVOLUCIONES", (_, _) =>
        {
            if (Require("VENTAS"))
            {
                using var f = new DailyTicketsForm();
                ThemeService.Apply(f);
                ShowOwnedDialog(f);
                RefreshDailySales();
                RefreshLicense();
            }
        }, 130);
        dailyTicketsButton.Height = 36;
        dailyTicketsButton.Margin = new Padding(3, 1, 3, 1);
        dailyTicketsButton.Font = new Font("Segoe UI Semibold", 8.2f, FontStyle.Bold);
        dailyTicketsButton.Padding = new Padding(2, 0, 2, 0);

        var dailyTicketsBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoScroll = false,
            Padding = new Padding(0, 1, 0, 1),
            Margin = Padding.Empty
        };
        purchasesButton = Btn("COMPRAS", (_, _) => { if (RequireOptionalModule("ÓRDENES DE COMPRA", "purchase_orders_enabled")) Open(new PurchaseOrdersForm()); }, 90);
        purchasesButton.Height = 36;
        purchasesButton.Margin = new Padding(3, 1, 3, 1);
        var dashboardButton = Btn("PANEL", (_, _) => { if (Require("REPORTES")) Open(new DashboardForm()); }, 90);
        dashboardButton.Height = 36;
        dashboardButton.Margin = new Padding(3, 1, 3, 1);
        var reservationsButton = Btn("RESERVAS", (_, _) => Open(new ReservationsForm()), 95);
        reservationsButton.Height = 36; reservationsButton.Margin = new Padding(3, 1, 3, 1);
        var wasteButton = Btn("MERMA", (_, _) => Open(new WasteForm()), 85);
        wasteButton.Height = 36; wasteButton.Margin = new Padding(3, 1, 3, 1);
        var promotionsButton = Btn("PROMOCIONES", (_, _) => { if (Require("VENTAS")) OpenPromotions(); }, 125);
        promotionsButton.Height = 36; promotionsButton.Margin = new Padding(3, 1, 3, 1);
        dailyTicketsBar.Controls.Add(dailyTicketsButton);
        dailyTicketsBar.Controls.Add(purchasesButton);
        dailyTicketsBar.Controls.Add(dashboardButton);
        dailyTicketsBar.Controls.Add(reservationsButton);
        dailyTicketsBar.Controls.Add(wasteButton);
        dailyTicketsBar.Controls.Add(promotionsButton);
        bottom.Controls.Add(dailyTicketsBar, 1, 1);
    }

    private static void AddActionTo(FlowLayoutPanel panel, string text, EventHandler handler, int width)
    {
        var b = new RoundedButton
        {
            Text = text,
            Width = width,
            Height = 36,
            Margin = new Padding(3),
            Padding = new Padding(3),
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand,
            TabStop = false
        };
        b.FlatAppearance.BorderSize = 1;
        b.Click += handler;
        panel.Controls.Add(b);
    }

    private bool Require(string permission)
    {
        if (Session.HasPermission(permission)) return true;
        MessageBox.Show($"Tu usuario no tiene permiso para: {permission}.\nSolicitá al ADMIN que lo habilite desde F8 Usuarios.",
            "Permiso insuficiente", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    private bool RequireOptionalModule(string moduleName, string settingKey)
    {
        if (Database.GetSetting(settingKey, "1") != "1")
        {
            MessageBox.Show($"El módulo {moduleName} está desactivado desde F7 · Configuración.",
                "Módulo desactivado", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        return moduleName == "ÓRDENES DE COMPRA"
            ? Require("INVENTARIO")
            : Require("PROVEEDORES");
    }

    public void UpdateOptionalModulesVisibility()
    {
        if (providerShortcutButton != null)
            providerShortcutButton.Visible = Database.GetSetting("providers_enabled", "1") == "1";
        if (purchasesButton != null)
            purchasesButton.Visible = Database.GetSetting("purchase_orders_enabled", "1") == "1";
    }

    private DialogResult ShowOwnedDialog(Form f)
    {
        // Todas las ventanas auxiliares quedan vinculadas a MainForm y se
        // comportan como diálogos reales. Esto evita que una ventana modal
        // termine detrás de la pantalla principal y deje la aplicación
        // aparentemente bloqueada. No usamos TopMost para no dejar el POS
        // por encima de otras aplicaciones de Windows.
        f.StartPosition = FormStartPosition.CenterParent;
        f.ShowInTaskbar = false;

        void KeepAboveOwner(object? sender, EventArgs e)
        {
            if (f.IsDisposed || f.Disposing || !f.Visible) return;
            BeginInvoke(new Action(() =>
            {
                if (!f.IsDisposed && !f.Disposing && f.Visible)
                {
                    f.BringToFront();
                    f.Activate();
                }
            }));
        }

        f.Shown += KeepAboveOwner;
        try
        {
            return f.ShowDialog(this);
        }
        finally
        {
            f.Shown -= KeepAboveOwner;
        }
    }

    private void Open(Form f)
    {
        ThemeService.Apply(f);
        // Toda ventana secundaria se centra respecto de la ventana principal.
        // Así, con dos monitores, nunca salta al monitor secundario.
        f.StartPosition = FormStartPosition.CenterParent;
        ShowOwnedDialog(f);
    }

    private bool TablesEnabled => Database.GetSetting("tables_enabled", "0") == "1";
    private bool TablesInMain => Database.GetSetting("tables_in_main", "0") == "1";
    private bool TablesEditEnabled => Database.GetSetting("tables_edit_enabled", "1") == "1";

    private void UpdateSalonIntegration()
    {
        if (IsDisposed) return;
        var enabled = TablesEnabled;
        if (salonButton != null) salonButton.Visible = enabled;
        if (assignTableButton != null) assignTableButton.Visible = enabled;

        if (enabled && TablesInMain)
        {
            if (mainCenter.ColumnCount != 2 || salonWindow == null || salonWindow.IsDisposed)
            {
                mainCenter.ColumnCount = 2;
                mainCenter.ColumnStyles.Clear();
                mainCenter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 64));
                mainCenter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
                var panel = new SalonForm(this, TablesEditEnabled, true);
                panel.TopLevel = false; panel.FormBorderStyle = FormBorderStyle.None; panel.Dock = DockStyle.Fill;
                mainCenter.Controls.Add(panel, 1, 0); mainCenter.SetRowSpan(panel, 2); panel.Show();
                HookBarcodeFocus(panel);
                salonWindow = panel;
            }
        }
        else if (mainCenter.ColumnCount == 2)
        {
            var embedded = mainCenter.Controls.Cast<Control>().FirstOrDefault(c => c is SalonForm);
            embedded?.Dispose();
            mainCenter.ColumnCount = 1;
            mainCenter.ColumnStyles.Clear();
            mainCenter.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            salonWindow = null;
        }
    }

    private void OpenSalon(bool edit)
    {
        if (!TablesEnabled)
        {
            MessageBox.Show("El sistema de mesas está desactivado. Activá 'Habilitar sistema de mesas' desde F7 · Configuración.", "Mesas", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (mainCenter.ColumnCount == 2 && !edit) { salonWindow?.BringToFront(); return; }
        using var f = new SalonForm(this, edit && TablesEditEnabled, false);
        ThemeService.Apply(f); ShowOwnedDialog(f);
    }

    public IReadOnlyDictionary<int,int> GetOccupiedTables() => ticketTableMap.ToDictionary(x => x.Value, x => x.Key);

    public (string ticketName, double total)? GetTableState(int tableId)
    {
        var pair = ticketTableMap.FirstOrDefault(x => x.Value == tableId);
        if (pair.Equals(default(KeyValuePair<int,int>))) return null;
        if (pair.Key < 0 || pair.Key >= tickets.Count) return null;
        return (ticketNames[pair.Key], CartTotal(tickets[pair.Key]));
    }

    private static double CartTotal(IReadOnlyList<CartItem> items) => Math.Round(items.Sum(x => x.Total), 2);

    public void AssignActiveTicketToTable()
    {
        if (!TablesEnabled) return;
        if (Cart.Count == 0) { MessageBox.Show("Primero agregá al menos un producto al ticket.", "Asignar mesa", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (ticketTableMap.ContainsKey(activeTicket)) { MessageBox.Show($"El ticket ya está asignado a la mesa {TableService.GetTables().FirstOrDefault(t => t.Id == ticketTableMap[activeTicket])?.Name ?? "seleccionada"}.", "Asignar mesa", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var f = new AssignTableForm(GetOccupiedTables());
        if (ShowOwnedDialog(f) != DialogResult.OK) return;
        AssignActiveTicketToTable(f.SelectedTableId);
    }

    public void AssignActiveTicketToTable(int tableId)
    {
        if (!TablesEnabled) return;
        if (Cart.Count == 0) { MessageBox.Show("Primero agregá al menos un producto al ticket.", "Asignar mesa", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (ticketTableMap.ContainsKey(activeTicket))
        {
            MessageBox.Show($"El ticket ya está asignado a la mesa {TableService.GetTables().FirstOrDefault(t => t.Id == ticketTableMap[activeTicket])?.Name ?? "seleccionada"}.", "Asignar mesa", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (ticketTableMap.Any(x => x.Value == tableId))
        {
            MessageBox.Show("Esa mesa ya tiene un ticket abierto.", "Mesa ocupada", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var reservation = ReservationService.GetTableReservationWithinWindow(tableId, DateTime.Now, 120);
        if (reservation is not null)
        {
            MessageBox.Show($"La mesa está reservada para las {reservation.Hour}.\n\nEstá dentro del período de protección de 2 horas y no puede asignarse a otra venta.", "Mesa reservada", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var assignedTable = TableService.GetTables().FirstOrDefault(t => t.Id == tableId);
        if (assignedTable == null) return;
        ticketTableMap[activeTicket] = tableId;
        ticketNames[activeTicket] = assignedTable.Name;
        RefreshTabs(); RefreshGrid(); barcode.Focus(); salonWindow?.RefreshSalon();
    }

    public void OpenTable(int tableId)
    {
        var table = TableService.GetTables().FirstOrDefault(x => x.Id == tableId);
        if (table == null) return;

        var existingTicket = ticketTableMap.FirstOrDefault(x => x.Value == tableId);
        if (existingTicket.Equals(default(KeyValuePair<int, int>)))
        {
            var reservation = ReservationService.GetTableReservationWithinWindow(tableId, DateTime.Now, 120);
            if (reservation is not null)
            {
                MessageBox.Show($"La mesa {table.Name} está reservada para las {reservation.Hour}.\n\nEstá bloqueada para nuevas ventas durante las 2 horas previas a la reserva.", "Mesa reservada", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        var pair = existingTicket;
        if (pair.Equals(default(KeyValuePair<int,int>)))
        {
            // Una mesa libre se convierte directamente en una nueva venta:
            // tocar la mesa ya no obliga al usuario a buscar el ticket ni usar ÷.
            if (Cart.Count > 0)
                NewSale();

            activeTicket = Math.Max(0, activeTicket);
            ticketTableMap[activeTicket] = tableId;
            ticketNames[activeTicket] = table.Name;
            RefreshTabs();
            RefreshGrid();
            barcode.Focus();
            salonWindow?.RefreshSalon();
            return;
        }

        activeTicket = pair.Key;
        ticketNames[activeTicket] = table.Name;
        RefreshTabs();
        RefreshGrid();
        barcode.Focus();

        using var detail = new MesaDetalleForm(
            table.Name,
            Cart,
            () => Charge(),
            () => { activeTicket = pair.Key; RefreshTabs(); RefreshGrid(); barcode.Focus(); });
        ThemeService.Apply(detail);
        ShowOwnedDialog(detail);
        salonWindow?.RefreshSalon();
    }

    private void ReleaseTableForActiveTicket()
    {
        if (ticketTableMap.Remove(activeTicket, out _)) salonWindow?.RefreshSalon();
    }

    public static MainForm? CurrentInstance { get; private set; }

    public string? GetExternalTableTicketJson(int tableId)
    {
        if (InvokeRequired) return (string?)Invoke(new Func<string?>(() => GetExternalTableTicketJson(tableId)));
        var pair=ticketTableMap.FirstOrDefault(x=>x.Value==tableId);
        if (pair.Key<0 || pair.Value!=tableId || pair.Key>=tickets.Count || tickets[pair.Key].Count==0) return null;
        var items=tickets[pair.Key].Select(x=>new {productId=x.ProductId,quantity=x.Quantity,unitPrice=x.UnitPrice,discount=x.DiscountAmount,description=x.Description,isCommon=x.IsCommon}).ToList();
        var name=pair.Key<ticketNames.Count?ticketNames[pair.Key]:$"MESA {tableId}";
        return JsonSerializer.Serialize(new {tableId,tableName=name,customerId=1,customerName="Consumidor final",items,notes="",updatedAt=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")});
    }

    public void CompleteExternalTableCharge(int tableId, long saleTicket = 0, double saleTotal = 0)
    {
        if (InvokeRequired) { Invoke(new Action(()=>CompleteExternalTableCharge(tableId, saleTicket, saleTotal))); return; }
        var pair=ticketTableMap.FirstOrDefault(x=>x.Value==tableId);
        var tableName = TableService.GetTables().FirstOrDefault(t=>t.Id==tableId)?.Name ?? $"MESA {tableId}";
        if(pair.Key>=0 && pair.Key<tickets.Count){
            var localTotal = tickets[pair.Key].Count > 0 ? CartTotal(tickets[pair.Key]) : 0;
            if (saleTotal <= 0) saleTotal = localTotal;
            tickets[pair.Key].Clear();
            if(pair.Key<ticketNames.Count) ticketNames[pair.Key]=$"VENTA {pair.Key+1}";
            ticketTableMap.Remove(pair.Key);
            mobileTableTicketMap.Remove(tableId);
        }
        salonWindow?.RefreshSalon(); RefreshTabs(); RefreshGrid();
        // Misma notificación visual/sonora que el cobro de un ticket de Windows,
        // pero identificando claramente que el origen fue una MESA cobrada desde Android.
        ShowAndroidChargeNotification(tableName, saleTicket, saleTotal);
    }

    private int FindTicketIndexByRemoteId(string remoteId)
    {
        if (string.IsNullOrWhiteSpace(remoteId)) return -1;
        for (var i = 0; i < tickets.Count && i < ticketRemoteIds.Count; i++)
            if (string.Equals(ticketRemoteIds[i], remoteId, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    public List<object> GetWindowsOpenTicketsSnapshot()
    {
        if (InvokeRequired) return (List<object>)Invoke(new Func<List<object>>(GetWindowsOpenTicketsSnapshot));
        var result = new List<object>();
        for (var i = 0; i < tickets.Count; i++)
        {
            if (tickets[i].Count == 0) continue;
            // Android debe ver TODAS las ventas que siguen abiertas en Windows:
            // ventas normales, ventas asignadas a mesa y ventas que hayan llegado
            // desde otro puesto móvil y estén todavía abiertas.
            var remoteId = i < ticketRemoteIds.Count ? ticketRemoteIds[i] : $"ticket-{i}";
            var items = tickets[i].Select(x => new { productId=x.ProductId, quantity=x.Quantity, unitPrice=x.UnitPrice, discount=x.DiscountAmount, description=x.Description, isCommon=x.IsCommon }).ToList();
            var tableId = ticketTableMap.TryGetValue(i, out var mappedTableId) ? mappedTableId : 0;
            var tableName = tableId > 0
                ? (TableService.GetTables().FirstOrDefault(t => t.Id == tableId)?.Name ?? $"MESA {tableId}")
                : "";
            var customerId = mobilePendingTicketMap.FirstOrDefault(x => x.Value == i).Key;
            var customer = customerId > 0 && mobilePendingCustomerMap.TryGetValue(customerId, out var mappedCustomerId)
                ? mappedCustomerId : 1;
            result.Add(new {
                id = remoteId,
                name = string.IsNullOrWhiteSpace(ticketNames.ElementAtOrDefault(i)) ? $"VENTA {i+1}" : ticketNames[i],
                total = CartTotal(tickets[i]),
                items,
                tableId,
                tableName,
                customerId = customer
            });
        }
        return result;
    }

    public bool AppendProductsToWindowsTicket(string remoteId, IReadOnlyList<CartItem> items)
    {
        if (InvokeRequired) return (bool)Invoke(new Func<string, IReadOnlyList<CartItem>, bool>(AppendProductsToWindowsTicket), remoteId, items);
        var ticketIndex = FindTicketIndexByRemoteId(remoteId);
        if (ticketIndex < 0 || items == null || items.Count == 0 || tickets[ticketIndex].Count == 0) return false;
        // Agregamos directamente sobre la lista viva del ticket. No reconstruimos
        // las pestañas aquí: RefreshTabs() puede limpiar/remapear pestañas vacías
        // mientras el Manager Android todavía está trabajando con el índice remoto,
        // haciendo que el producto recién agregado parezca desaparecer.
        tickets[ticketIndex].AddRange(items);

        // Las ventas recibidas desde Android se refrescan periódicamente desde
        // SQLite. Persistimos también el agregado realizado sobre Windows para
        // que ese refresco no vuelva a cargar la versión anterior del ticket y
        // haga desaparecer el producto recién agregado.
        var pending = mobilePendingTicketMap.FirstOrDefault(x => x.Value == ticketIndex);
        if (pending.Key > 0)
            WebDashboardServer.UpdateMobilePendingTicketItems(pending.Key, tickets[ticketIndex]);

        if (activeTicket == ticketIndex) RefreshGrid();
        salonWindow?.RefreshSalon();
        return true;
    }

    public bool CompleteWindowsTicketFromAndroid(string remoteId, long saleTicket = 0, double saleTotal = 0)
    {
        if (InvokeRequired) return (bool)Invoke(new Func<string, long, double, bool>(CompleteWindowsTicketFromAndroid), remoteId, saleTicket, saleTotal);
        var ticketIndex = FindTicketIndexByRemoteId(remoteId);
        if (ticketIndex < 0 || ticketIndex >= tickets.Count || tickets[ticketIndex].Count == 0) return false;
        var notificationName = string.IsNullOrWhiteSpace(ticketNames.ElementAtOrDefault(ticketIndex)) ? $"VENTA {ticketIndex + 1}" : ticketNames[ticketIndex];
        var notificationTotal = saleTotal > 0 ? saleTotal : CartTotal(tickets[ticketIndex]);
        var tableId = ticketTableMap.TryGetValue(ticketIndex, out var mappedTableId) ? mappedTableId : 0;
        if (tableId > 0) ticketTableMap.Remove(ticketIndex);
        // Si el ticket que Android acaba de cobrar provenía de una venta móvil,
        // hay que eliminar también su registro persistente. De lo contrario el
        // refresco automático ImportMobileOpenTickets() lo vuelve a importar
        // desde SQLite y la venta reaparece en Windows después de cobrarla.
        var pending = mobilePendingTicketMap.FirstOrDefault(x => x.Value == ticketIndex);
        if (pending.Key > 0)
        {
            WebDashboardServer.RemoveMobilePendingTicket(pending.Key);
            mobilePendingTicketMap.Remove(pending.Key);
            mobilePendingCustomerMap.Remove(pending.Key);
        }
        var mobileTable = mobileTableTicketMap.FirstOrDefault(x => x.Value == ticketIndex);
        if (mobileTable.Key > 0)
        {
            WebDashboardServer.RemoveMobileOpenTicket(mobileTable.Key);
            mobileTableTicketMap.Remove(mobileTable.Key);
        }
        tickets[ticketIndex].Clear();
        ticketNames[ticketIndex] = $"VENTA {ticketIndex + 1}";
        if (tableId > 0) salonWindow?.RefreshSalon();
        RefreshTabs();
        if (activeTicket == ticketIndex) RefreshGrid();
        RefreshDailySales();
        ShowAndroidChargeNotification(notificationName, saleTicket, notificationTotal);
        return true;
    }

    private void ShowAndroidChargeNotification(string ticketName, long saleTicket, double total)
    {
        if (IsDisposed || Disposing) return;
        try
        {
            SoundService.PlayCashRegister();
            var toast = new Form
            {
                FormBorderStyle = FormBorderStyle.None,
                StartPosition = FormStartPosition.Manual,
                ShowInTaskbar = false,
                TopMost = true,
                BackColor = Color.FromArgb(8, 10, 14),
                Size = new Size(410, 92),
                Opacity = 0.96,
                Padding = new Padding(2)
            };
            var screen = Screen.FromControl(this).WorkingArea;
            toast.Location = new Point(screen.Right - toast.Width - 18, screen.Bottom - toast.Height - 18);
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(12, 15, 20) };
            panel.Paint += (_, e) =>
            {
                using var pen = new Pen(Color.FromArgb(55, 231, 255), 2f);
                e.Graphics.DrawRectangle(pen, 1, 1, panel.Width - 3, panel.Height - 3);
                using var glow = new Pen(Color.FromArgb(35, 55, 231, 255), 5f);
                e.Graphics.DrawRectangle(glow, 3, 3, panel.Width - 7, panel.Height - 7);
            };
            var title = new Label { AutoSize = false, Text = "✓  COBRO DESDE ANDROID MANAGER", ForeColor = Color.FromArgb(55, 231, 255), Font = new Font("Segoe UI", 10f, FontStyle.Bold), Dock = DockStyle.Top, Height = 30, Padding = new Padding(14, 7, 5, 0) };
            var detail = new Label { AutoSize = false, Text = saleTicket > 0 ? $"{ticketName}  ·  Ticket #{saleTicket}  ·  ${total:N2}" : $"{ticketName}  ·  ${total:N2}", ForeColor = Color.White, Font = new Font("Segoe UI", 11f, FontStyle.Bold), Dock = DockStyle.Fill, Padding = new Padding(14, 0, 5, 5) };
            panel.Controls.Add(detail);
            panel.Controls.Add(title);
            toast.Controls.Add(panel);
            var blink = new System.Windows.Forms.Timer { Interval = 150 };
            var step = 0;
            blink.Tick += (_, _) =>
            {
                step++;
                toast.Opacity = step % 2 == 0 ? 0.28 : 0.96;
                if (step >= 7)
                {
                    blink.Stop();
                    blink.Dispose();
                    toast.Close();
                    toast.Dispose();
                }
            };
            toast.Shown += (_, _) => blink.Start();
            toast.Show(this);
        }
        catch
        {
            // La notificación nunca debe afectar el cobro ni cerrar FerrariPOS.
        }
    }

    private void DrawTicketTab(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabs || e.Index < 0 || e.Index >= tabs.TabPages.Count)
            return;

        var bounds = e.Bounds;
        var selected = e.Index == tabs.SelectedIndex;

        bool light = ThemeService.IsLightTheme;
        using var backBrush = new SolidBrush(light ? Color.FromArgb(242, 245, 248) : Color.Black);
        e.Graphics.FillRectangle(backBrush, bounds);

        // Borde muy sutil solamente para separar la pestaña activa.
        if (selected)
        {
            using var activeBrush = new SolidBrush(light ? Color.FromArgb(220, 232, 244) : Color.Black);
            e.Graphics.FillRectangle(activeBrush, bounds);
            using var linePen = new Pen(light ? Color.FromArgb(0, 120, 190) : Color.FromArgb(57, 255, 255), 1f);
            e.Graphics.DrawLine(linePen, bounds.Left + 2, bounds.Bottom - 1, bounds.Right - 2, bounds.Bottom - 1);
        }

        var text = tabs.TabPages[e.Index].Text?.Trim() ?? $"VENTA {e.Index + 1}";
        var match = Regex.Match(text, @"^VENTA\s+(\d+)(.*)$", RegexOptions.IgnoreCase);

        using var ventaBrush = new SolidBrush(light ? Color.FromArgb(80, 92, 105) : Color.FromArgb(170, 170, 170));
        using var numberBrush = new SolidBrush(light ? Color.FromArgb(0, 120, 190) : Color.FromArgb(57, 255, 255));
        using var otherBrush = new SolidBrush(light ? Color.FromArgb(80, 92, 105) : Color.FromArgb(170, 170, 170));
        using var font = new Font("Segoe UI", 9f, FontStyle.Bold);

        var format = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };

        float x = bounds.Left + 9;
        float textTop = bounds.Top;
        float textHeight = bounds.Height;

        if (match.Success)
        {
            var ventaSize = e.Graphics.MeasureString("VENTA ", font);
            e.Graphics.DrawString("VENTA ", font, ventaBrush,
                new RectangleF(x, textTop, ventaSize.Width + 2, textHeight), format);
            x += ventaSize.Width;

            var number = match.Groups[1].Value;
            var numberSize = e.Graphics.MeasureString(number, font);
            e.Graphics.DrawString(number, font, numberBrush,
                new RectangleF(x, textTop, numberSize.Width + 2, textHeight), format);
            x += numberSize.Width;

            var suffix = match.Groups[2].Value;
            if (!string.IsNullOrEmpty(suffix))
                e.Graphics.DrawString(suffix, font, otherBrush,
                    new RectangleF(x, textTop, Math.Max(1, bounds.Right - x - 5), textHeight), format);
        }
        else
        {
            e.Graphics.DrawString(text, font, otherBrush, new RectangleF(bounds.Left + 9, bounds.Top, bounds.Width - 12, bounds.Height), format);
        }
    }

    private void RemoveEmptyStandaloneTickets()
    {
        // Mantener la limpieza limitada a pestañas locales realmente vacías.
        // No tocamos tickets vinculados a mesas ni tickets móviles: sus mapas y
        // estados tienen significado propio y deben conservarse intactos.
        if (tickets.Count <= 1) return;

        var emptyStandalone = new List<int>();
        var hasNonEmptyTicket = false;
        for (var i = 0; i < tickets.Count; i++)
        {
            if (tickets[i].Count > 0)
            {
                hasNonEmptyTicket = true;
                continue;
            }

            var hasTable = ticketTableMap.ContainsKey(i);
            var hasMobilePending = mobilePendingTicketMap.Values.Contains(i);
            var hasMobileTable = mobileTableTicketMap.Values.Contains(i);
            if (!hasTable && !hasMobilePending && !hasMobileTable)
                emptyStandalone.Add(i);
        }

        if (emptyStandalone.Count == 0) return;

        // Si existen ventas con productos, no dejamos pestañas vacías.
        // Si todas las ventas están vacías, dejamos exactamente una pestaña local.
        var keepIndex = hasNonEmptyTicket ? -1 : emptyStandalone[0];
        var remove = new HashSet<int>(emptyStandalone.Where(i => i != keepIndex));
        if (remove.Count == 0) return;

        var activeOld = activeTicket;
        var activeRemoved = remove.Contains(activeOld);
        var newActive = activeOld;
        if (activeRemoved)
        {
            var next = Enumerable.Range(0, tickets.Count)
                .FirstOrDefault(i => !remove.Contains(i) && tickets[i].Count > 0, -1);
            if (next < 0)
                next = Enumerable.Range(0, tickets.Count).First(i => !remove.Contains(i));
            newActive = next;
        }

        var newTickets = new List<List<CartItem>>(tickets.Count - remove.Count);
        var newNames = new List<string>(ticketNames.Count - remove.Count);
        var newRemoteIds = new List<string>(ticketRemoteIds.Count - remove.Count);
        var indexMap = new Dictionary<int, int>();

        for (var oldIndex = 0; oldIndex < tickets.Count; oldIndex++)
        {
            if (remove.Contains(oldIndex)) continue;
            var newIndex = newTickets.Count;
            indexMap[oldIndex] = newIndex;
            newTickets.Add(tickets[oldIndex]);
            newNames.Add(ticketNames.ElementAtOrDefault(oldIndex) ?? $"VENTA {newIndex + 1}");
            newRemoteIds.Add(ticketRemoteIds.ElementAtOrDefault(oldIndex) ?? Guid.NewGuid().ToString("N"));
        }

        tickets.Clear();
        tickets.AddRange(newTickets);
        ticketNames.Clear();
        ticketNames.AddRange(newNames);
        ticketRemoteIds.Clear();
        ticketRemoteIds.AddRange(newRemoteIds);

        var remappedTables = new Dictionary<int, int>();
        foreach (var pair in ticketTableMap)
            if (indexMap.TryGetValue(pair.Key, out var mappedIndex))
                remappedTables[mappedIndex] = pair.Value;
        ticketTableMap.Clear();
        foreach (var pair in remappedTables) ticketTableMap[pair.Key] = pair.Value;

        var remappedMobileTables = new Dictionary<int, int>();
        foreach (var pair in mobileTableTicketMap)
            if (indexMap.TryGetValue(pair.Value, out var mappedIndex))
                remappedMobileTables[pair.Key] = mappedIndex;
        mobileTableTicketMap.Clear();
        foreach (var pair in remappedMobileTables) mobileTableTicketMap[pair.Key] = pair.Value;

        var remappedPending = new Dictionary<long, int>();
        foreach (var pair in mobilePendingTicketMap)
            if (indexMap.TryGetValue(pair.Value, out var mappedIndex))
                remappedPending[pair.Key] = mappedIndex;
        mobilePendingTicketMap.Clear();
        foreach (var pair in remappedPending) mobilePendingTicketMap[pair.Key] = pair.Value;

        if (tickets.Count == 0)
        {
            tickets.Add(new List<CartItem>());
            ticketNames.Add("VENTA 1");
            ticketRemoteIds.Add(Guid.NewGuid().ToString("N"));
            activeTicket = 0;
        }
        else
        {
            activeTicket = indexMap.TryGetValue(newActive, out var mappedActive)
                ? mappedActive
                : Math.Clamp(newActive, 0, tickets.Count - 1);
        }

        selectedCartRow = -1;
    }

    private void RefreshTabs()
    {
        RemoveEmptyStandaloneTickets();
        ticketTabs.SuspendLayout();
        refreshingTicketTabs = true;
        try
        {
            // Nunca dejamos un TabControl sin páginas ni intentamos seleccionar
            // el índice 0 cuando la colección todavía está vacía.
            if (tickets.Count == 0)
            {
                tickets.Add(new List<CartItem>());
                ticketNames.Add("VENTA 1");
                ticketRemoteIds.Add(Guid.NewGuid().ToString("N"));
            }

            ticketTabs.TabPages.Clear();
            for (int i = 0; i < tickets.Count; i++)
                ticketTabs.TabPages.Add(new TabPage(string.IsNullOrWhiteSpace(ticketNames.ElementAtOrDefault(i)) ? $"VENTA {i + 1}" : ticketNames[i]));

            activeTicket = Math.Clamp(activeTicket, 0, Math.Max(0, tickets.Count - 1));
            if (ticketTabs.TabCount > 0)
                ticketTabs.SelectedIndex = activeTicket;
        }
        finally
        {
            refreshingTicketTabs = false;
            ticketTabs.ResumeLayout();
        }
    }

    private void OpenFavorites()
    {
        using var f = new FavoritesForm();
        if (ShowOwnedDialog(f) == DialogResult.OK && f.SelectedProduct is Product p) AddProduct(p, 1);
    }

    private void ApplyGeneralTicketDiscount()
    {
        if (Cart.Count == 0)
        {
            MessageBox.Show("Agregá al menos un producto antes de aplicar un descuento general.", "Descuento general", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Si algún renglón fue eliminado desde la última aplicación, descartamos
        // su parte del descuento general para no afectar descuentos de productos.
        foreach (var stale in generalDiscountAllocations.Keys.Where(x => !Cart.Contains(x)).ToList())
            generalDiscountAllocations.Remove(stale);
        ticketGeneralDiscountAmount = Math.Round(generalDiscountAllocations.Values.Sum(), 2);

        var currentNet = Math.Round(Cart.Sum(x => x.Total), 2);
        var baseTotal = Math.Round(currentNet + ticketGeneralDiscountAmount, 2);
        using var f = new TicketDiscountForm(baseTotal, ticketGeneralDiscountAmount);
        if (ShowOwnedDialog(f) != DialogResult.OK) return;

        var target = Math.Clamp(f.DiscountAmount, 0, baseTotal);
        var delta = Math.Round(target - ticketGeneralDiscountAmount, 2);

        if (delta > 0.005)
        {
            var remaining = delta;
            var eligible = Cart.Where(x => x.Total > 0).ToList();
            for (int i = 0; i < eligible.Count && remaining > 0.005; i++)
            {
                var item = eligible[i];
                var share = i == eligible.Count - 1
                    ? remaining
                    : Math.Round(delta * item.Total / currentNet, 2, MidpointRounding.AwayFromZero);
                share = Math.Min(share, Math.Min(item.Total, remaining));
                item.DiscountAmount = Math.Min(item.GrossTotal, item.DiscountAmount + share);
                generalDiscountAllocations[item] = generalDiscountAllocations.GetValueOrDefault(item) + share;
                remaining = Math.Round(remaining - share, 2);
            }
        }
        else if (delta < -0.005)
        {
            var toRemove = -delta;
            foreach (var item in generalDiscountAllocations.Keys.ToList())
            {
                if (toRemove <= 0.005) break;
                var allocated = generalDiscountAllocations[item];
                var remove = Math.Min(allocated, toRemove);
                item.DiscountAmount = Math.Max(0, item.DiscountAmount - remove);
                var left = Math.Round(allocated - remove, 2);
                if (left <= 0.005) generalDiscountAllocations.Remove(item);
                else generalDiscountAllocations[item] = left;
                toRemove = Math.Round(toRemove - remove, 2);
            }
        }

        ticketGeneralDiscountAmount = Math.Round(generalDiscountAllocations.Values.Sum(), 2);
        RefreshGrid();
    }

    private void EditSelectedPrice()
    {
        if (!Session.IsAdmin && !Session.HasPermission("EDITAR_PRECIOS")) { MessageBox.Show("No tenés permiso para modificar precios en la venta.", "Permisos", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        var row = SelectedIndex; if (row < 0 || row >= Cart.Count || Cart[row].IsCommon) return;
        var item = Cart[row];
        var oldPrice = item.UnitPrice;
        using var f = new InputBoxForm("Precio de venta", oldPrice.ToString("0.00"));
        if (ShowOwnedDialog(f) != DialogResult.OK) return;
        if (!double.TryParse(f.Value.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var price) || price < 0)
        {
            MessageBox.Show("Precio inválido.");
            return;
        }
        if (Math.Abs(price - oldPrice) < 0.005) return;

        using var reasonPrompt = new ReasonPromptForm(
            "MOTIVO · CAMBIO DE PRECIO",
            $"Producto: {item.Description}\nPrecio anterior: ${oldPrice:N2}\nNuevo precio: ${price:N2}\n\nEl motivo es obligatorio y quedará incluido en el resumen del cierre.");
        if (reasonPrompt.ShowDialog(this) != DialogResult.OK) return;

        item.UnitPrice = price;
        item.RetailPrice = price;
        item.IsWholesale = false;

        try
        {
            using var cn = Database.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "INSERT INTO price_change_log(product_id,product_barcode,product_name,old_price,new_price,reason,user_id) VALUES($p,$b,$n,$o,$v,$r,$u)";
            cmd.Parameters.AddWithValue("$p", item.ProductId);
            cmd.Parameters.AddWithValue("$b", item.Barcode ?? "");
            cmd.Parameters.AddWithValue("$n", item.Description ?? "");
            cmd.Parameters.AddWithValue("$o", oldPrice);
            cmd.Parameters.AddWithValue("$v", price);
            cmd.Parameters.AddWithValue("$r", reasonPrompt.Reason);
            cmd.Parameters.AddWithValue("$u", Session.UserId);
            cmd.ExecuteNonQuery();
            AuditService.Log(Session.UserId, "PRICE_CHANGE", "PRODUCTOS",
                $"Cambio de precio: {item.Description} · {oldPrice:N2} → {price:N2} · Motivo: {reasonPrompt.Reason}");
        }
        catch (Exception ex)
        {
            item.UnitPrice = oldPrice;
            item.RetailPrice = oldPrice;
            MessageBox.Show("No se pudo registrar el cambio de precio. El precio no fue modificado.\n\n" + ex.Message,
                "Cambio de precio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        RefreshGrid();
    }

    private void NewSale()
    {
        // NUEVA VENTA siempre crea un ticket nuevo, incluso si el anterior está vacío.
        // No llamamos a RefreshTabs aquí porque ese método limpia las pestañas locales
        // vacías y eliminaría inmediatamente la nueva venta recién creada.
        tickets.Add(new List<CartItem>());
        ticketNames.Add($"VENTA {tickets.Count}");
        ticketRemoteIds.Add(Guid.NewGuid().ToString("N"));
        ticketGeneralDiscountAmount = 0;
        generalDiscountAllocations.Clear();
        activeTicket = tickets.Count - 1;
        selectedCartRow = -1;

        ticketTabs.SuspendLayout();
        refreshingTicketTabs = true;
        try
        {
            ticketTabs.TabPages.Add(new TabPage(ticketNames[activeTicket]));
            ticketTabs.SelectedIndex = activeTicket;
        }
        finally
        {
            refreshingTicketTabs = false;
            ticketTabs.ResumeLayout();
        }

        RefreshGrid();
        RefreshSessionInfo();
        RefreshDailySales();
        RefreshLicense();
        barcode.Clear(); barcode.Focus();
    }

    private void TicketTabsMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;

        for (var i = 0; i < ticketTabs.TabCount; i++)
        {
            if (!ticketTabs.GetTabRect(i).Contains(e.Location)) continue;

            ticketTabs.SelectedIndex = i;
            var menu = new ContextMenuStrip();
            var delete = new ToolStripMenuItem($"ELIMINAR VENTA {i + 1}");
            delete.Click += (_, _) => DeleteTicket(i);
            menu.Items.Add(delete);
            var rename = new ToolStripMenuItem("RENOMBRAR VENTA");
            rename.Click += (_, _) => RenameActiveTicket();
            menu.Items.Add(rename);
            menu.Show(ticketTabs, e.Location);
            break;
        }
    }

    private void DeleteTicket(int index)
    {
        if (index < 0 || index >= tickets.Count) return;

        var name = string.IsNullOrWhiteSpace(ticketNames[index]) ? $"VENTA {index + 1}" : ticketNames[index];
        var itemCount = tickets[index].Count;

        // Un ticket vacío se elimina directamente, sin pedir confirmación.
        // Si tiene productos, se confirma antes de borrar.
        if (itemCount > 0)
        {
            var answer = MessageBox.Show(
                $"La venta '{name}' tiene {itemCount} artículo(s).\n\n¿Querés eliminarla definitivamente?",
                "Eliminar venta",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
        }

        // Las ventas móviles no son solamente pestañas locales: también tienen
        // un registro persistente en SQLite. Si no se elimina ese registro, el
        // refresco automático de ImportMobileOpenTickets vuelve a crear la pestaña
        // y parece que "Eliminar ticket" solamente refresca.
        var isMobilePending = mobilePendingTicketMap.FirstOrDefault(x => x.Value == index);
        var hasMobilePending = isMobilePending.Key != 0;
        if (hasMobilePending)
        {
            WebDashboardServer.RemoveMobilePendingTicket(isMobilePending.Key);
            mobilePendingTicketMap.Remove(isMobilePending.Key);
            mobilePendingCustomerMap.Remove(isMobilePending.Key);
        }

        var mappedTable = ticketTableMap.TryGetValue(index, out var tableId) ? tableId : (int?)null;
        var isMobileTable = mappedTable.HasValue && mobileTableTicketMap.ContainsKey(mappedTable.Value);
        if (isMobileTable)
        {
            // Borra el ticket persistido que llegó desde Android y libera la mesa.
            WebDashboardServer.RemoveMobileOpenTicket(mappedTable.Value);
            mobileTableTicketMap.Remove(mappedTable.Value);
        }

        // Quitar la asociación de mesa del ticket eliminado y remapear los
        // índices de las asociaciones que quedan.
        ticketTableMap.Remove(index);
        var remappedTables = new Dictionary<int, int>();
        foreach (var pair in ticketTableMap)
        {
            remappedTables[pair.Key > index ? pair.Key - 1 : pair.Key] = pair.Value;
        }
        ticketTableMap.Clear();
        foreach (var pair in remappedTables) ticketTableMap[pair.Key] = pair.Value;

        // Remapear también los índices de ventas móviles. Esto es importante
        // porque al quitar una pestaña, todas las pestañas posteriores bajan una
        // posición; si no se remapean, el próximo refresco puede apuntar a otra
        // venta y volver a mostrarla.
        var remappedMobileTables = new Dictionary<int, int>();
        foreach (var pair in mobileTableTicketMap)
        {
            var oldIndex = pair.Value;
            if (oldIndex == index) continue;
            remappedMobileTables[pair.Key] = oldIndex > index ? oldIndex - 1 : oldIndex;
        }
        mobileTableTicketMap.Clear();
        foreach (var pair in remappedMobileTables) mobileTableTicketMap[pair.Key] = pair.Value;

        var remappedMobilePending = new Dictionary<long, int>();
        foreach (var pair in mobilePendingTicketMap)
        {
            var oldIndex = pair.Value;
            if (oldIndex == index) continue;
            remappedMobilePending[pair.Key] = oldIndex > index ? oldIndex - 1 : oldIndex;
        }
        mobilePendingTicketMap.Clear();
        foreach (var pair in remappedMobilePending) mobilePendingTicketMap[pair.Key] = pair.Value;

        tickets.RemoveAt(index);
        ticketNames.RemoveAt(index);
        if (index < ticketRemoteIds.Count) ticketRemoteIds.RemoveAt(index);

        if (tickets.Count == 0)
        {
            tickets.Add(new List<CartItem>());
            ticketNames.Add("VENTA 1");
            ticketRemoteIds.Add(Guid.NewGuid().ToString("N"));
        }

        if (activeTicket > index) activeTicket--;
        else if (activeTicket == index) activeTicket = Math.Min(index, tickets.Count - 1);

        selectedCartRow = -1;
        RefreshTabs();
        RefreshGrid();
        RefreshSessionInfo();
        salonWindow?.RefreshSalon();
        barcode.Clear();
        barcode.Focus();
    }

    private void RenameActiveTicket()
    {
        if (activeTicket < 0 || activeTicket >= tickets.Count) return;
        using var f = new TicketNameForm(ticketNames[activeTicket], activeTicket + 1);
        if (ShowOwnedDialog(f) == DialogResult.OK)
        {
            ticketNames[activeTicket] = string.IsNullOrWhiteSpace(f.TicketName) ? $"VENTA {activeTicket + 1}" : f.TicketName.Trim();
            // Si el ticket pertenece a una mesa, mantener visible el nombre de la mesa
            // como título principal evita que se pierda el enlace visual.
            if (ticketTableMap.TryGetValue(activeTicket, out var tableId))
            {
                var table = TableService.GetTables().FirstOrDefault(t => t.Id == tableId);
                if (table != null && string.Equals(ticketNames[activeTicket], $"VENTA {activeTicket + 1}", StringComparison.OrdinalIgnoreCase))
                    ticketNames[activeTicket] = table.Name;
            }
            RefreshTabs();
            ticketTabs.SelectedIndex = activeTicket;
            barcode.Focus();
        }
    }

    private void OpenSearch()
    {
        // Permite el flujo de venta rápida: escribir "5*", pulsar F10,
        // buscar un producto y presionar Enter. La cantidad 5 se conserva
        // durante toda la búsqueda y se aplica al producto seleccionado.
        pendingSearchQuantity = ExtractPendingQuantityFromBarcode();

        using var f = new ProductsForm(true);
        if (ShowOwnedDialog(f) == DialogResult.OK && f.SelectedProduct is { } p)
        {
            var quantity = pendingSearchQuantity > 0 ? pendingSearchQuantity : 1;
            AddProduct(p, quantity);
        }

        pendingSearchQuantity = 1;
        barcode.Clear();
        barcode.Focus();
    }

    private double ExtractPendingQuantityFromBarcode()
    {
        var raw = barcode.Text.Trim();
        if (raw.Length == 0) return 1;

        // Solo captura el caso "cantidad*". Si hay algo después del *,
        // AddByBarcode sigue manejando normalmente "cantidad*producto/código".
        var m = Regex.Match(raw, @"^\s*(\d+(?:[.,]\d+)?)\s*\*\s*$");
        if (!m.Success) return 1;

        var quantity = ParseNumber(m.Groups[1].Value);
        if (quantity <= 0) return 1;

        barcode.Clear();
        return quantity;
    }

    private void MainKeyDown(object? sender, KeyEventArgs e)
    {
        // Mientras estamos en la venta, las flechas recorren los renglones ya
        // cargados del ticket y Supr elimina el renglón seleccionado. Esto se
        // mantiene activo aunque el foco esté en el lector de códigos.
        if (HandleCartNavigationKey(e)) return;

        if (e.KeyCode == Keys.Divide || (e.KeyCode == Keys.OemQuestion && e.Shift)) { if (TablesEnabled) AssignActiveTicketToTable(); }
        else if (e.Control && e.KeyCode == Keys.P) AddCommonProduct();
        else if (e.Control && e.KeyCode == Keys.O) { if (Require("PROMOCIONES")) OpenPromotions(); }
        else if (e.KeyCode == Keys.D && e.Control) ApplySelectedDiscount();
        else if (e.KeyCode == Keys.Insert) ToggleWholesaleSelected();
        else if (e.KeyCode == Keys.Delete) DeleteSelected();
        else if (e.KeyCode == Keys.Add || (e.KeyCode == Keys.Oemplus && e.Shift)) ChangeSelectedQuantity(1);
        else if (e.KeyCode == Keys.Subtract || e.KeyCode == Keys.OemMinus) ChangeSelectedQuantity(-1);
        else if (e.KeyCode == Keys.F1) { if (Require("VENTAS")) NewSale(); }
        else if (e.KeyCode == Keys.F2) { if (Require("CLIENTES")) Open(new CustomersForm()); }
        else if (e.KeyCode == Keys.F3) { if (Require("PRODUCTOS")) Open(new ProductsForm()); }
        else if (e.KeyCode == Keys.F4) { if (Require("INVENTARIO")) Open(new InventoryForm()); }
        else if (e.KeyCode == Keys.F5) { if (Require("CAJA")) Open(new CashForm()); }
        else if (e.KeyCode == Keys.F6) { if (Require("REPORTES")) Open(new ReportsForm()); }
        else if (e.KeyCode == Keys.F7) { if (Require("CONFIGURACION")) Open(new SettingsForm()); }
        else if (e.KeyCode == Keys.F8) { if (Session.IsAdmin) Open(new UsersForm()); else MessageBox.Show("Solo ADMIN puede administrar usuarios.", "Permisos", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        else if (e.KeyCode == Keys.F9) { if (Require("PROVEEDORES")) Open(new SuppliersForm()); }
        else if (e.KeyCode == Keys.F10) OpenSearch();
        else if (e.KeyCode == Keys.F11) { if (Require("CAJA")) Open(new IncomeExpenseForm()); }
        else if (e.KeyCode == Keys.F12) { if (Require("VENTAS")) Charge(); }
        else return;
        e.Handled = true; e.SuppressKeyPress = true;
    }

    private bool HandleCartNavigationKey(KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Up && e.KeyCode != Keys.Down && e.KeyCode != Keys.Delete)
            return false;

        if (Cart.Count == 0)
            return false;

        if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
        {
            int current = selectedCartRow;
            if (current < 0 || current >= Cart.Count) current = 0;

            int next = e.KeyCode == Keys.Up
                ? Math.Max(0, current - 1)
                : Math.Min(Cart.Count - 1, current + 1);

            selectedCartRow = next;
            SelectCartRow(next);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return true;
        }

        // Supr elimina exactamente el producto/renglón actualmente seleccionado.
        DeleteSelected();
        e.Handled = true;
        e.SuppressKeyPress = true;
        return true;
    }

    private void SelectCartRow(int row)
    {
        if (row < 0 || row >= Cart.Count || row >= grid.Rows.Count) return;

        selectedCartRow = row;
        grid.ClearSelection();
        grid.Rows[row].Selected = true;
        grid.CurrentCell = grid.Rows[row].Cells["Description"];
        grid.FirstDisplayedScrollingRowIndex = Math.Max(0, Math.Min(row, grid.RowCount - 1));
        grid.Refresh();
    }

    private void BarcodeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter) { AddByBarcode(); e.SuppressKeyPress = true; }
    }

    private void EnableAntiFlicker()
    {
        // Repintado compuesto + doble buffer en toda la jerarquía. Esto evita
        // el titileo visible de VENTA 1/2/3 y de los paneles al sincronizar
        // tickets móviles o actualizar la grilla.
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        UpdateStyles();
        EnableDoubleBuffer(this);
        EnableDoubleBuffer(grid);
        EnableDoubleBuffer(ticketTabs);
        EnableDoubleBufferRecursive(this);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
            return cp;
        }
    }

    private static void EnableDoubleBufferRecursive(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            EnableDoubleBuffer(child);
            if (child.HasChildren) EnableDoubleBufferRecursive(child);
        }
    }

    private static void EnableDoubleBuffer(Control control)
    {
        try
        {
            var prop = typeof(Control).GetProperty("DoubleBuffered",
                BindingFlags.Instance | BindingFlags.NonPublic);
            prop?.SetValue(control, true, null);
        }
        catch { }
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_ERASEBKGND = 0x0014;
        if (m.Msg == WM_ERASEBKGND)
        {
            m.Result = (IntPtr)1;
            return;
        }
        base.WndProc(ref m);
    }

    private bool EnsureLicense()
    {
        if (!LicenseService.IsExpired) return true;
        using var f = new SettingsForm(); f.StartPosition = FormStartPosition.CenterParent; ShowOwnedDialog(f);
        return !LicenseService.IsExpired;
    }

    private void AddByBarcode()
    {
        if (!EnsureLicense()) return;
        var raw = barcode.Text.Trim();
        if (raw.Length == 0) return;

        double quantity = 1;
        string code = raw;
        var m = Regex.Match(raw, @"^\s*(\d+(?:[.,]\d+)?)\s*\*\s*(.+?)\s*$");
        if (m.Success)
        {
            quantity = ParseNumber(m.Groups[1].Value);
            code = m.Groups[2].Value.Trim();
            if (quantity <= 0) { MessageBox.Show("La cantidad antes de * debe ser mayor a cero."); return; }
        }

        // Primero se busca por código de barras. Si el texto después de * no es
        // un código, también se admite el nombre exacto del producto: 3*Producto.
        var p = ProductService.FindByBarcode(code);
        if (p is null && m.Success)
        {
            var matches = ProductService.Search(code)
                .Where(x => string.Equals(x.Description.Trim(), code.Trim(), StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matches.Count == 1) p = matches[0];
        }

        if (p is null)
        {
            MessageBox.Show("No se encontró el producto.\n\nFormatos válidos:\n• 3*CODIGO → agrega 3 unidades.\n• 3*PRODUCTO → agrega 3 unidades del producto con ese nombre exacto.", "FerrarisPOS", MessageBoxButtons.OK, MessageBoxIcon.Information);
            barcode.SelectAll(); barcode.Focus(); return;
        }
        AddProduct(p, quantity);
        barcode.Clear(); barcode.Focus();
    }

    private static double ParseNumber(string text) =>
        double.TryParse(text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private void PersistActiveMobileTicket()
    {
        if (activeTicket < 0 || activeTicket >= tickets.Count || tickets[activeTicket].Count == 0) return;

        // Los tickets que llegaron desde Android siguen teniendo su copia de origen
        // en SQLite. Si Windows agrega/modifica algo localmente y no actualiza esa
        // copia, el timer de ImportMobileOpenTickets vuelve a cargar la versión vieja
        // y parece que el producto recién agregado desapareció.
        var pending = mobilePendingTicketMap.FirstOrDefault(x => x.Value == activeTicket);
        if (pending.Key > 0)
        {
            WebDashboardServer.UpdateMobilePendingTicketItems(pending.Key, tickets[activeTicket]);
            return;
        }

        var mobileTable = mobileTableTicketMap.FirstOrDefault(x => x.Value == activeTicket);
        if (mobileTable.Key > 0)
            WebDashboardServer.UpdateMobileOpenTicketItems(mobileTable.Key, tickets[activeTicket]);
    }

    private void AddProduct(Product p, double quantity)
    {
        if (p.IsBulk)
        {
            using var weightForm = new BulkWeightForm(p.Description, p.SalePrice, p.Stock);
            if (ShowOwnedDialog(weightForm) != DialogResult.OK) return;
            quantity = weightForm.Kilos;
        }

        if (quantity <= 0) return;
        if (p.UsesInventory && p.Stock < quantity)
        {
            MessageBox.Show($"Stock insuficiente para {p.Description}. Disponible: {p.Stock:N3} kg.");
            return;
        }

        string modifierSummary = "";
        double modifierDelta = 0;
        var modifiers = ProductModifierService.Get(p.Id);
        if (modifiers.Count > 0)
        {
            using var picker = new ModifierPickerForm(p.Id, p.Description);
            if (ShowOwnedDialog(picker) != DialogResult.OK) return;
            modifierSummary = picker.Summary;
            modifierDelta = picker.TotalDelta;
        }

        var existing = modifierSummary.Length == 0 ? Cart.FirstOrDefault(x => x.ProductId == p.Id && !x.IsCommon && string.IsNullOrWhiteSpace(x.Modifiers)) : null;
        if (existing is null)
        {
            Cart.Add(new CartItem
            {
                ProductId = p.Id, Barcode = p.Barcode, Description = string.IsNullOrWhiteSpace(modifierSummary) ? p.Description : $"{p.Description} · {modifierSummary}",
                UnitPrice = p.SalePrice + modifierDelta, RetailPrice = p.SalePrice + modifierDelta, WholesalePrice = p.WholesalePrice + modifierDelta,
                Quantity = quantity, Stock = p.Stock, IsCommon = false, IsWholesale = false, IsBulk = p.IsBulk, UsesInventory = p.UsesInventory, Modifiers = modifierSummary
            });
            selectedCartRow = Cart.Count - 1;
        }
        else
        {
            if (p.UsesInventory && existing.Quantity + quantity > p.Stock)
            {
                MessageBox.Show($"Stock insuficiente. Disponible: {p.Stock:N2}.");
                return;
            }
            existing.Quantity += quantity;
            selectedCartRow = Cart.IndexOf(existing);
        }
        PersistActiveMobileTicket();
        RefreshGrid();
    }

    private void OpenPromotions()
    {
        if (!EnsureLicense()) return;
        using var f = new PromotionsForm();
        ThemeService.Apply(f);
        if (ShowOwnedDialog(f) != DialogResult.OK || f.SelectedItems.Count == 0) return;
        Cart.AddRange(f.SelectedItems);
        selectedCartRow = Cart.Count - 1;
        PersistActiveMobileTicket();
        RefreshGrid();
        barcode.Focus();
    }

    private void AddCommonProduct()
    {
        if (!EnsureLicense()) return;
        using var f = new CommonProductForm();
        if (ShowOwnedDialog(f) != DialogResult.OK) { barcode.Focus(); return; }
        var common = ProductService.CommonProduct();
        Cart.Add(new CartItem
        {
            ProductId = common.Id, Barcode = "COMÚN",
            Description = string.IsNullOrWhiteSpace(f.ProductDescription) ? "PRODUCTO COMÚN / REDONDEO" : f.ProductDescription,
            UnitPrice = f.Amount, RetailPrice = f.Amount, WholesalePrice = f.Amount,
            Quantity = 1, Stock = 0, IsCommon = true, UsesInventory = false
        });
        selectedCartRow = Cart.Count - 1;
        PersistActiveMobileTicket();
        RefreshGrid(); barcode.Focus();
    }

    private int SelectedIndex
    {
        get
        {
            // La selección lógica del ticket es independiente del foco de Windows.
            // El lector de códigos puede recuperar el foco y DataGridView puede
            // cambiar CurrentRow internamente; por eso +/−, Supr y demás acciones
            // deben usar SIEMPRE selectedCartRow como fuente de verdad.
            if (selectedCartRow >= 0 && selectedCartRow < Cart.Count)
                return selectedCartRow;

            return -1;
        }
    }

    private void ChangeSelectedQuantity(int delta)
    {
        var row = SelectedIndex;
        if (row < 0 || row >= Cart.Count) return;

        var item = Cart[row];
        var newQty = item.Quantity + delta;

        if (newQty <= 0)
        {
            Cart.RemoveAt(row);
            selectedCartRow = Math.Min(row, Cart.Count - 1);
            RefreshGrid();
            return;
        }

        if (!item.IsCommon && item.UsesInventory && newQty > item.Stock)
        {
            MessageBox.Show($"No podés superar el stock disponible: {item.Stock:N2}.");
            return;
        }

        item.Quantity = newQty;
        selectedCartRow = row;
        RefreshGrid();
    }

    private void DeleteSelected()
    {
        // La eliminación debe funcionar aunque el foco esté en el lector,
        // en la grilla o en otro control de la pantalla de venta.
        var row = SelectedIndex;

        if (row < 0 || row >= Cart.Count)
        {
            if (grid.CurrentRow != null && grid.CurrentRow.Index >= 0 && grid.CurrentRow.Index < Cart.Count)
                row = grid.CurrentRow.Index;
            else if (grid.SelectedRows.Count > 0 && grid.SelectedRows[0].Index >= 0 && grid.SelectedRows[0].Index < Cart.Count)
                row = grid.SelectedRows[0].Index;
        }

        if (row < 0 || row >= Cart.Count) return;

        Cart.RemoveAt(row);
        selectedCartRow = Math.Min(row, Cart.Count - 1);

        // Si esta venta llegó desde Android, la lista visible de Windows también
        // tiene una copia persistente en SQLite. Guardamos inmediatamente la
        // eliminación para que el refresco automático no vuelva a traer la línea.
        // Si era la última línea, eliminamos la venta móvil persistida: de lo
        // contrario ImportMobileOpenTickets la reconstruiría en el siguiente tick.
        var pendingMobile = mobilePendingTicketMap.FirstOrDefault(x => x.Value == activeTicket);
        if (pendingMobile.Key > 0)
        {
            if (Cart.Count == 0)
            {
                WebDashboardServer.RemoveMobilePendingTicket(pendingMobile.Key);
                mobilePendingTicketMap.Remove(pendingMobile.Key);
                mobilePendingCustomerMap.Remove(pendingMobile.Key);
            }
            else
            {
                PersistActiveMobileTicket();
            }
        }
        else
        {
            var mobileTable = mobileTableTicketMap.FirstOrDefault(x => x.Value == activeTicket);
            if (mobileTable.Key > 0)
            {
                if (Cart.Count == 0)
                {
                    WebDashboardServer.RemoveMobileOpenTicket(mobileTable.Key);
                    mobileTableTicketMap.Remove(mobileTable.Key);
                    ticketTableMap.Remove(activeTicket);
                }
                else
                {
                    PersistActiveMobileTicket();
                }
            }
        }

        SoundService.PlayTrash();
        RefreshGrid();
    }

    private void ToggleWholesaleSelected()
    {
        var row = SelectedIndex;
        if (row < 0 || row >= Cart.Count) return;
        var item = Cart[row];
        if (item.IsCommon) return;
        if (item.WholesalePrice <= 0)
        {
            MessageBox.Show("Este producto no tiene precio de mayoreo configurado.");
            return;
        }
        item.IsWholesale = !item.IsWholesale;
        item.UnitPrice = item.IsWholesale ? item.WholesalePrice : item.RetailPrice;
        RefreshGrid();
        if (row < grid.Rows.Count) grid.Rows[row].Selected = true;
    }

    private void ApplySelectedProductHighlight()
    {
        if (grid.Columns.Count == 0) return;
        bool neonTheme = ThemeService.CurrentTheme is "Graphite" or "Grafito" or "Grafito Premium" or "Oscuro";
        var cyan = Color.FromArgb(57, 255, 255);
        var green = Color.FromArgb(57, 255, 20);
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.IsNewRow) continue;
            row.Cells["Barcode"].Style.SelectionForeColor = neonTheme ? cyan : Color.Empty;
            row.Cells["Description"].Style.SelectionForeColor = neonTheme ? green : Color.Empty;
            row.Cells["UnitPrice"].Style.SelectionForeColor = neonTheme ? green : Color.Empty;
            row.Cells["Total"].Style.SelectionForeColor = neonTheme ? green : Color.Empty;
            row.Cells["Quantity"].Style.SelectionForeColor = neonTheme ? green : Color.Empty;
            // El STOCK conserva celeste flúor incluso cuando toda la fila está seleccionada.
            row.Cells["Stock"].Style.SelectionForeColor = neonTheme ? cyan : Color.Empty;
            row.Cells["Discount"].Style.SelectionForeColor = neonTheme ? green : Color.Empty;
            row.Cells["Mode"].Style.SelectionForeColor = neonTheme ? green : Color.Empty;
        }
    }

    private void ApplySelectedDiscount()
    {
        var row = SelectedIndex;
        if (row < 0 || row >= Cart.Count) return;
        var item = Cart[row];
        using var f = new DiscountForm(item.Description, item.GrossTotal, item.DiscountAmount);
        if (ShowOwnedDialog(f) != DialogResult.OK) return;
        item.DiscountAmount = Math.Min(item.GrossTotal, f.DiscountAmount);
        selectedCartRow = row;
        RefreshGrid();
        if (row < grid.Rows.Count) grid.Rows[row].Selected = true;
    }

    private void PaintCartGridWatermark(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (ThemeService.IsLightTheme) return;

        if (sender is not DataGridView dgv ||
            !string.Equals(dgv.Tag?.ToString(), "CartGridWatermark", StringComparison.Ordinal) ||
            e.RowIndex < 0 || e.ColumnIndex < 0 ||
            gridWatermarkImage == null ||
            e.CellBounds.Width <= 0 || e.CellBounds.Height <= 0)
            return;

        // Pintamos nosotros el fondo para poder simular el alfa que WinForms
        // no permite directamente en DataGridViewCellStyle. La marca de agua
        // original ya tiene 49% de opacidad; aquí se aplica aproximadamente
        // la misma segunda capa que tenía el diseño anterior.
        var style = dgv.Rows[e.RowIndex].Cells[e.ColumnIndex].InheritedStyle;
        Color background = style.BackColor.IsEmpty ? dgv.DefaultCellStyle.BackColor : style.BackColor;

        if (e.State.HasFlag(DataGridViewElementStates.Selected))
            background = BlendWithAlpha(background, style.SelectionBackColor, 150);

        using (var backgroundBrush = new SolidBrush(background))
            e.Graphics.FillRectangle(backgroundBrush, e.CellBounds);

        DrawWatermarkPortion(e.Graphics, dgv, e.CellBounds, gridWatermarkImage, 0.51f);

        // Contenido y borde normales de DataGridView: colores, fuentes,
        // alineación, selección y formatos de cada columna permanecen intactos.
        e.PaintContent(e.CellBounds);
        e.Paint(e.CellBounds, DataGridViewPaintParts.Border);
        e.Handled = true;
    }

    private static Color BlendWithAlpha(Color baseColor, Color overlay, int alpha)
    {
        alpha = Math.Clamp(alpha, 0, 255);
        float a = alpha / 255f;
        return Color.FromArgb(
            (int)Math.Round(baseColor.R + (overlay.R - baseColor.R) * a),
            (int)Math.Round(baseColor.G + (overlay.G - baseColor.G) * a),
            (int)Math.Round(baseColor.B + (overlay.B - baseColor.B) * a));
    }

    private static void DrawWatermarkPortion(Graphics g, DataGridView dgv, Rectangle cellBounds, Image image, float opacity)
    {
        if (dgv.ClientSize.Width <= 0 || dgv.ClientSize.Height <= 0 || image.Width <= 0 || image.Height <= 0)
            return;

        float scale = Math.Max(
            (float)dgv.ClientSize.Width / image.Width,
            (float)dgv.ClientSize.Height / image.Height);
        int drawWidth = Math.Max(1, (int)Math.Ceiling(image.Width * scale));
        int drawHeight = Math.Max(1, (int)Math.Ceiling(image.Height * scale));
        int drawX = (dgv.ClientSize.Width - drawWidth) / 2;
        int drawY = (dgv.ClientSize.Height - drawHeight) / 2;

        // Convertimos el rectángulo de la celda a la zona correspondiente de
        // la imagen escalada para no deformar la marca de agua.
        var intersection = Rectangle.Intersect(cellBounds, dgv.ClientRectangle);
        if (intersection.Width <= 0 || intersection.Height <= 0) return;

        float srcX = (intersection.Left - drawX) / (float)drawWidth * image.Width;
        float srcY = (intersection.Top - drawY) / (float)drawHeight * image.Height;
        float srcW = intersection.Width / (float)drawWidth * image.Width;
        float srcH = intersection.Height / (float)drawHeight * image.Height;

        using var attributes = new ImageAttributes();
        var matrix = new ColorMatrix { Matrix33 = Math.Clamp(opacity, 0f, 1f) };
        attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
        g.DrawImage(
            image,
            intersection,
            srcX, srcY, srcW, srcH,
            GraphicsUnit.Pixel,
            attributes);
    }

    private void RefreshGrid()
    {
        if (grid.Columns.Count == 0) return;
        refreshingCartGrid = true;
        grid.SuspendLayout(); grid.Rows.Clear();

        if (Cart.Count == 0)
        {
            int emptyRow = grid.Rows.Add();
            grid.Rows[emptyRow].Cells["Description"].Value = "NO HAY PRODUCTOS EN LA VENTA";
            grid.Rows[emptyRow].Cells["Description"].Style.ForeColor = Color.Gray;
            grid.Rows[emptyRow].Cells["Description"].Style.Font = new Font("Segoe UI", 10, FontStyle.Italic);
        }
        else
        {
            foreach (var item in Cart)
            {
                string quantityText = item.IsBulk
                    ? item.Quantity < 1
                        ? $"{item.Quantity * 1000:N0} g"
                        : $"{item.Quantity:N3} kg"
                    : item.Quantity.ToString("N0");
                var currency = Database.GetSetting("currency", "$");
                int rowIndex = grid.Rows.Add(item.Barcode, item.Description, $"{currency}{item.UnitPrice:N2}", quantityText, $"{currency}{item.Total:N2}",
                    (!item.UsesInventory || item.IsCommon) ? "NO APLICA" : item.Stock, $"{currency}{item.DiscountAmount:N2}", item.IsCommon ? "COMÚN" : (!item.UsesInventory ? "SIN INVENTARIO" : (item.IsBulk ? "GRANEL" : (item.IsWholesale ? "MAYOREO" : "VENTA"))));
                if (item.IsWholesale) grid.Rows[rowIndex].DefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            }
        }

        grid.ClearSelection();
        if (grid.Rows.Count > 0 && Cart.Count > 0)
        {
            selectedCartRow = Math.Max(0, Math.Min(selectedCartRow, Cart.Count - 1));
            grid.ClearSelection();
            grid.Rows[selectedCartRow].Selected = true;
            grid.CurrentCell = grid.Rows[selectedCartRow].Cells["Description"];
            grid.FirstDisplayedScrollingRowIndex = Math.Max(0, Math.Min(selectedCartRow, grid.RowCount - 1));
        }
        else
        {
            selectedCartRow = -1;
            grid.CurrentCell = null;
        }
        grid.Visible = true;
        grid.ResumeLayout();
        refreshingCartGrid = false;
        ApplySelectedProductHighlight();

        var currencySymbol = Database.GetSetting("currency", "$");
        var total = Cart.Sum(x => x.Total);
        totalLabel.Text = $"TOTAL {currencySymbol}{total:N2}";
        ApplyConfiguredTotalFont();
        totalLabel.ForeColor = total > 0
            ? Color.FromArgb(80, 245, 255)
            : Color.FromArgb(155, 155, 155);
        var units = Cart.Sum(x => x.Quantity);
        itemCountLabel.Text = LanguageService.CurrentCode switch
        {
            LanguageService.English => $"ITEMS IN SALE: {units:N0}    ·    LINES: {Cart.Count:N0}    ·    ACTIVE SALE: {activeTicket + 1}",
            LanguageService.Portuguese => $"ITENS NA VENDA: {units:N0}    ·    LINHAS: {Cart.Count:N0}    ·    VENDA ATIVA: {activeTicket + 1}",
            LanguageService.French => $"ARTICLES DE LA VENTE : {units:N0}    ·    LIGNES : {Cart.Count:N0}    ·    VENTE ACTIVE : {activeTicket + 1}",
            _ => $"ARTÍCULOS EN LA VENTA: {units:N0}    ·    RENGLONES: {Cart.Count:N0}    ·    VENTA ACTIVA: {activeTicket + 1}"
        };

        // Si la venta está vinculada a una mesa, el salón debe reflejar
        // inmediatamente el nuevo importe. Antes solo se actualizaba al
        // pulsar ÷/MESA o al volver a activar la ventana.
        if (TablesEnabled && ticketTableMap.ContainsKey(activeTicket))
            salonWindow?.RefreshSalon();
    }

    // Mantiene TOTAL en 40 pt normalmente. Si el importe se estira, reduce
    // progresivamente la tipografía hasta que entre completa, sin cambiar el
    // tamaño del control ni generar barras de desplazamiento.
    public void ApplyConfiguredTotalFont()
    {
        if (totalLabel.IsDisposed) return;

        // El precio principal debe verse grande. La prioridad es mantener 40 pt
        // hasta 6 dígitos; recién después se reduce progresivamente. El Label
        // no cambia de tamaño ni de posición.
        const float maxSize = 40f;
        const float minSize = 14f;
        const float depth = 3.2f;
        const float safety = 6f;
        const float maxHorizontalCompression = 0.58f;

        var availableWidth = Math.Max(1, totalLabel.ClientSize.Width - totalLabel.Padding.Horizontal - 4);
        var availableHeight = Math.Max(1, totalLabel.ClientSize.Height - totalLabel.Padding.Vertical - 4);
        var text = totalLabel.Text ?? string.Empty;
        var digitCount = text.Count(char.IsDigit);

        // Escalones suaves: 6 dígitos todavía se intenta mostrar a tamaño máximo;
        // 7/8 dígitos empiezan a reducirse sin convertir el TOTAL en texto pequeño.
        var preferredSize = digitCount <= 6 ? maxSize
            : digitCount == 7 ? 37f
            : digitCount == 8 ? 34f
            : digitCount == 9 ? 31f
            : 28f;

        using var probeBitmap = new Bitmap(1, 1);
        using var g = Graphics.FromImage(probeBitmap);
        g.PageUnit = GraphicsUnit.Pixel;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        var chosen = Math.Min(maxSize, Math.Max(minSize, preferredSize));
        var canFitAtPreferredSize = false;

        // Primero buscamos el mayor tamaño que entre con una compresión horizontal
        // moderada. Esto conserva altura/legibilidad para importes de 6 dígitos.
        for (var size = chosen; size >= minSize; size -= 0.5f)
        {
            using var path = new GraphicsPath();
            var emSize = size * g.DpiY / 72f;
            path.AddString(text, totalLabel.Font.FontFamily, (int)totalLabel.Font.Style,
                emSize, new PointF(0, 0), StringFormat.GenericTypographic);
            var b = path.GetBounds();
            if (b.Width <= 0 || b.Height <= 0) continue;

            var widthWith3D = b.Width + depth + safety;
            var heightWith3D = b.Height + depth + safety;
            if (heightWith3D <= availableHeight && widthWith3D <= availableWidth / maxHorizontalCompression)
            {
                chosen = size;
                canFitAtPreferredSize = size == preferredSize;
                break;
            }
        }

        // Si la altura disponible lo permite, no bajamos de tamaño solamente porque
        // el texto sea ancho: FlickerFreeLabel hará una compresión vectorial controlada.
        // Solo reducimos por debajo del tamaño elegido cuando ni siquiera la compresión
        // máxima puede hacer entrar la geometría 3D.
        if (!canFitAtPreferredSize)
        {
            for (var size = chosen; size >= minSize; size -= 0.5f)
            {
                using var path = new GraphicsPath();
                var emSize = size * g.DpiY / 72f;
                path.AddString(text, totalLabel.Font.FontFamily, (int)totalLabel.Font.Style,
                    emSize, new PointF(0, 0), StringFormat.GenericTypographic);
                var b = path.GetBounds();
                if (b.Width <= 0 || b.Height <= 0) continue;

                if (b.Height + depth + safety <= availableHeight &&
                    b.Width + depth + safety <= availableWidth / maxHorizontalCompression)
                {
                    chosen = size;
                    break;
                }
            }
        }

        // Seguridad final para resoluciones/DPI muy pequeños.
        if (chosen < minSize) chosen = minSize;

        if (Math.Abs(totalLabel.Font.SizeInPoints - chosen) > 0.05f)
            totalLabel.Font = new Font(totalLabel.Font.FontFamily, chosen, totalLabel.Font.Style);

        totalLabel.AutoEllipsis = false;
        totalLabel.TextAlign = ContentAlignment.MiddleCenter;
        totalLabel.Padding = new Padding(0, 0, 4, 0);
        totalLabel.Invalidate();
    }

    private void RefreshSessionInfo()
    {
        var fullName = (Session.FullName ?? "").Trim();
        var username = (Session.Username ?? "").Trim();

        string displayName;
        if (string.IsNullOrWhiteSpace(fullName) && string.IsNullOrWhiteSpace(username))
            displayName = "Sin usuario";
        else if (string.IsNullOrWhiteSpace(fullName))
            displayName = username;
        else if (string.Equals(fullName, username, StringComparison.OrdinalIgnoreCase))
            displayName = fullName;
        else
            displayName = $"{fullName} · {username}";

        userLabel.Text = displayName;
        userLabel.ForeColor = ThemeService.IsLightTheme ? Color.FromArgb(35, 70, 105) : Color.FromArgb(25, 65, 45);
        userLabel.AccessibleName = $"Usuario activo: {displayName}";

        statusLabel.Text = "Sistema listo";
        statusLabel.ForeColor = ThemeService.IsLightTheme ? Color.FromArgb(35, 105, 75) : Color.FromArgb(25, 105, 65);
        statusLabel.AccessibleName = "Estado: Sistema listo";
    }

    private void RefreshDailySales()
    {
        try
        {
            var ticketsToday = SaleService.TodayTicketCount();
            dailyTicketCountLabel.Text = ticketsToday == 1
                ? "1 ticket realizado"
                : $"{ticketsToday:N0} tickets realizados";
            dailyTicketCountLabel.AccessibleName = $"Tickets realizados hoy: {ticketsToday}";
        }
        catch
        {
            dailyTicketCountLabel.Text = "0 tickets";
            dailyTicketCountLabel.AccessibleName = "Tickets realizados hoy: 0";
        }
    }

    private async Task ImportMobileOpenTicketsAsync()
    {
        if (IsDisposed || Disposing) return;
        if (Interlocked.Exchange(ref mobileSyncRunning, 1) != 0) return;

        try
        {
            // Capturamos solamente el estado local que vive en los tickets de Windows.
            // La lectura pesada de SQLite se hace fuera del hilo de UI para que la
            // sincronización Android no congele mesas, tickets ni cobro.
            var localExternalTables = new Dictionary<int, string>();
            foreach (var tableId in ticketTableMap.Values.Distinct())
            {
                try
                {
                    var json = GetExternalTableTicketJson(tableId);
                    if (!string.IsNullOrWhiteSpace(json)) localExternalTables[tableId] = json!;
                }
                catch { }
            }
            // El servidor HTTP lee este snapshot sin invocar el hilo de UI. Así,
            // las consultas Android de mesas/tickets no pueden detener Windows
            // esperando a que termine una operación visual.
            WebDashboardServer.UpdateWindowsExternalTableSnapshot(localExternalTables);

            var snapshot = await Task.Run(() => LoadMobileSyncSnapshot(localExternalTables));
            if (IsDisposed || Disposing) return;

            var tabsBefore = ticketNames.ToArray();
            var activeCartBefore = Cart.Select(x => $"{x.ProductId}|{x.Quantity:R}|{x.UnitPrice:R}|{x.DiscountAmount:R}|{x.Description}|{x.IsCommon}").ToArray();
            var tableSignature = string.Join("|", snapshot.Tables
                .OrderBy(x => x.TableId)
                .Select(x => $"{x.TableId}:{x.TableName}:{string.Join(",", x.Items.Select(i => $"{i.ProductId}:{i.Quantity:R}:{i.UnitPrice:R}:{i.DiscountAmount:R}:{i.IsCommon}"))}"));
            var tableStateChanged = !string.Equals(tableSignature, lastMobileTableSignature, StringComparison.Ordinal);
            lastMobileTableSignature = tableSignature;

            var seen = new HashSet<int>();
            foreach (var row in snapshot.Tables)
            {
                if (row.Items.Count == 0) continue;
                seen.Add(row.TableId);
                if (!mobileTableTicketMap.TryGetValue(row.TableId, out var ticketIndex) || ticketIndex < 0 || ticketIndex >= tickets.Count)
                {
                    if (tickets.Count == 1 && tickets[0].Count == 0 && ticketTableMap.Count == 0)
                        ticketIndex = 0;
                    else
                    {
                        tickets.Add(new List<CartItem>());
                        ticketNames.Add($"VENTA {tickets.Count}");
                        ticketRemoteIds.Add(Guid.NewGuid().ToString("N"));
                        ticketIndex = tickets.Count - 1;
                    }
                    mobileTableTicketMap[row.TableId] = ticketIndex;
                }

                tickets[ticketIndex].Clear();
                tickets[ticketIndex].AddRange(row.Items);
                ticketNames[ticketIndex] = string.IsNullOrWhiteSpace(row.TableName) ? $"MESA {row.TableId}" : row.TableName;
                ticketTableMap[ticketIndex] = row.TableId;
            }

            foreach (var tableId in snapshot.EmptyTableIds)
                WebDashboardServer.RemoveMobileOpenTicket(tableId);

            var seenPending = new HashSet<long>();
            foreach (var row in snapshot.Pending)
            {
                if (row.Items.Count == 0) continue;
                seenPending.Add(row.Id);
                mobilePendingCustomerMap[row.Id] = row.CustomerId;

                if (!mobilePendingTicketMap.TryGetValue(row.Id, out var ticketIndex) || ticketIndex < 0 || ticketIndex >= tickets.Count)
                {
                    tickets.Add(new List<CartItem>());
                    ticketNames.Add(string.IsNullOrWhiteSpace(row.CustomerName) ? $"VENTA MÓVIL #{row.Id}" : $"VENTA MÓVIL #{row.Id} · {row.CustomerName}");
                    ticketRemoteIds.Add(Guid.NewGuid().ToString("N"));
                    ticketIndex = tickets.Count - 1;
                    mobilePendingTicketMap[row.Id] = ticketIndex;
                }

                tickets[ticketIndex].Clear();
                tickets[ticketIndex].AddRange(row.Items);
                ticketNames[ticketIndex] = string.IsNullOrWhiteSpace(row.CustomerName) ? $"VENTA MÓVIL #{row.Id}" : $"VENTA MÓVIL #{row.Id} · {row.CustomerName}";
                ticketTableMap.Remove(ticketIndex);
            }

            foreach (var pendingId in snapshot.EmptyPendingIds)
                WebDashboardServer.RemoveMobilePendingTicket(pendingId);

            var stalePending = mobilePendingTicketMap.Keys.Where(id => !seenPending.Contains(id)).ToList();
            foreach (var pendingId in stalePending)
            {
                if (mobilePendingTicketMap.TryGetValue(pendingId, out var idx) && idx >= 0 && idx < tickets.Count && !ticketTableMap.ContainsKey(idx))
                {
                    tickets[idx].Clear();
                    ticketNames[idx] = $"VENTA {idx + 1}";
                }
                mobilePendingTicketMap.Remove(pendingId);
                mobilePendingCustomerMap.Remove(pendingId);
            }

            var stale = mobileTableTicketMap.Keys.Where(id => !seen.Contains(id)).ToList();
            foreach (var tableId in stale)
            {
                if (mobileTableTicketMap.TryGetValue(tableId, out var idx) && idx >= 0 && idx < tickets.Count && ticketTableMap.TryGetValue(idx, out var mapped) && mapped == tableId)
                {
                    ticketTableMap.Remove(idx);
                    tickets[idx].Clear();
                    ticketNames[idx] = $"VENTA {idx + 1}";
                }
                mobileTableTicketMap.Remove(tableId);
            }

            var tabsChanged = tabsBefore.Length != ticketNames.Count || tabsBefore.Where((name, i) => i < ticketNames.Count && !string.Equals(name, ticketNames[i], StringComparison.Ordinal)).Any();
            if (tabsChanged) RefreshTabs();
            var activeCartAfter = Cart.Select(x => $"{x.ProductId}|{x.Quantity:R}|{x.UnitPrice:R}|{x.DiscountAmount:R}|{x.Description}|{x.IsCommon}").ToArray();
            var gridChanged = !activeCartBefore.SequenceEqual(activeCartAfter, StringComparer.Ordinal);
            if (gridChanged) RefreshGrid();
            if (tableStateChanged && salonWindow != null)
            {
                var nowTicks = Environment.TickCount64;
                if (nowTicks - lastSalonMobileRefreshTicks >= 300)
                {
                    lastSalonMobileRefreshTicks = nowTicks;
                    salonWindow.RefreshSalon();
                }
            }
        }
        catch (Exception ex)
        {
            // La sincronización es de fondo: un fallo de red/SQLite nunca debe
            // interrumpir ni bloquear la operatoria normal de Windows. Se registra
            // para poder detectar exactamente qué estaba ocurriendo si el cliente
            // vuelve a reportar una demora o una mesa que no se actualiza.
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "FerrarisPOS_error.log");
                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] SYNC MESAS/MANAGER\r\n{ex}\r\n\r\n");
            }
            catch { }
        }
        finally
        {
            Interlocked.Exchange(ref mobileSyncRunning, 0);
        }
    }

    private sealed class MobileExternalTicketDto
    {
        public int TableId { get; set; }
        public string? TableName { get; set; }
        public List<MobileOpenLine>? Items { get; set; }
    }

    private sealed class MobileSyncTableSnapshot
    {
        public int TableId { get; init; }
        public string TableName { get; init; } = "";
        public List<CartItem> Items { get; init; } = new();
    }

    private sealed class MobileSyncPendingSnapshot
    {
        public long Id { get; init; }
        public int CustomerId { get; init; }
        public string CustomerName { get; init; } = "";
        public List<CartItem> Items { get; init; } = new();
    }

    private sealed class MobileSyncSnapshot
    {
        public List<MobileSyncTableSnapshot> Tables { get; init; } = new();
        public List<MobileSyncPendingSnapshot> Pending { get; init; } = new();
        public List<int> EmptyTableIds { get; init; } = new();
        public List<long> EmptyPendingIds { get; init; } = new();
    }

    private MobileSyncSnapshot LoadMobileSyncSnapshot(Dictionary<int, string> localExternalTables)
    {
        var result = new MobileSyncSnapshot();
        var productCache = new Dictionary<int, (string Barcode, string Description, double Sale, double Wholesale, double Cost, double Stock, bool Bulk, bool UsesInventory)>();
        var common = (Id: 0, Description: "");

        using var cn = Database.Open();
        using (var pc = cn.CreateCommand())
        {
            pc.CommandText = "SELECT id,barcode,description,sale_price,wholesale_price,cost_price,stock,is_bulk,COALESCE(uses_inventory,1) FROM products";
            using var pr = pc.ExecuteReader();
            while (pr.Read())
            {
                var id = pr.GetInt32(0);
                productCache[id] = (pr.GetString(1), pr.GetString(2), pr.GetDouble(3), pr.GetDouble(4), pr.GetDouble(5), pr.GetDouble(6), pr.GetInt32(7) != 0, pr.GetInt32(8) != 0);
                if (string.Equals(pr.GetString(1), "__COMUN__", StringComparison.Ordinal)) common = (id, pr.GetString(2));
            }
        }

        static List<MobileOpenLine> ParseLines(string json) =>
            JsonSerializer.Deserialize<List<MobileOpenLine>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

        List<CartItem> ConvertLines(List<MobileOpenLine> lines)
        {
            var cart = new List<CartItem>();
            foreach (var line in lines)
            {
                if (line.IsCommon)
                {
                    if (common.Id <= 0) continue;
                    cart.Add(new CartItem
                    {
                        ProductId = common.Id, Barcode = "COMÚN",
                        Description = string.IsNullOrWhiteSpace(line.Description) ? common.Description : line.Description!,
                        UnitPrice = line.UnitPrice, RetailPrice = line.UnitPrice, WholesalePrice = line.UnitPrice,
                        CostPrice = 0, Stock = 0, IsBulk = false, UsesInventory = false,
                        Quantity = line.Quantity, DiscountAmount = line.Discount, IsCommon = true
                    });
                    continue;
                }

                if (!productCache.TryGetValue(line.ProductId, out var p)) continue;
                cart.Add(new CartItem
                {
                    ProductId = line.ProductId, Barcode = p.Barcode,
                    Description = string.IsNullOrWhiteSpace(line.Description) ? p.Description : line.Description!,
                    UnitPrice = line.UnitPrice, RetailPrice = p.Sale, WholesalePrice = p.Wholesale,
                    CostPrice = p.Cost, Stock = p.Stock, IsBulk = p.Bulk, UsesInventory = p.UsesInventory,
                    Quantity = line.Quantity, DiscountAmount = line.Discount, IsCommon = false
                });
            }
            return cart;
        }

        WebDashboardServer.EnsureMobileOpenTicketsForMainForm();
        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = "SELECT table_id,table_name,items_json FROM mobile_open_tickets ORDER BY table_id";
            using var r = cmd.ExecuteReader();
            var seen = new HashSet<int>();
            while (r.Read())
            {
                var tableId = r.GetInt32(0);
                var cart = ConvertLines(ParseLines(r.GetString(2)));
                if (cart.Count == 0) { result.EmptyTableIds.Add(tableId); continue; }
                seen.Add(tableId);
                result.Tables.Add(new MobileSyncTableSnapshot { TableId = tableId, TableName = r.GetString(1), Items = cart });
            }

            // Si una mesa está abierta directamente en Windows, la conservamos como
            // fuente de verdad cuando todavía no existe un registro móvil para ella.
            foreach (var pair in localExternalTables)
            {
                if (seen.Contains(pair.Key)) continue;
                try
                {
                    var dto = JsonSerializer.Deserialize<MobileExternalTicketDto>(pair.Value, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (dto?.Items == null || dto.Items.Count == 0) continue;
                    result.Tables.Add(new MobileSyncTableSnapshot
                    {
                        TableId = dto.TableId,
                        TableName = dto.TableName ?? $"MESA {dto.TableId}",
                        Items = ConvertLines(dto.Items)
                    });
                }
                catch { }
            }
        }

        using (var cmd = cn.CreateCommand())
        {
            cmd.CommandText = "SELECT id,customer_id,customer_name,items_json FROM mobile_pending_tickets ORDER BY id";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var id = r.GetInt64(0);
                var cart = ConvertLines(ParseLines(r.GetString(3)));
                if (cart.Count == 0) { result.EmptyPendingIds.Add(id); continue; }
                result.Pending.Add(new MobileSyncPendingSnapshot
                {
                    Id = id,
                    CustomerId = r.IsDBNull(1) ? 1 : r.GetInt32(1),
                    CustomerName = r.IsDBNull(2) ? "" : r.GetString(2),
                    Items = cart
                });
            }
        }

        return result;
    }

    private sealed class MobileOpenLine
    {
        public int ProductId { get; set; }
        public double Quantity { get; set; }
        public double UnitPrice { get; set; }
        public double Discount { get; set; }
        public string? Description { get; set; }
        public bool IsCommon { get; set; }
    }

    private void Charge()
    {
        if (!EnsureLicense()) return;
        if (Cart.Count == 0) { MessageBox.Show("Agregá al menos un producto."); return; }

        var pendingEntryForCustomer = mobilePendingTicketMap.FirstOrDefault(x => x.Value == activeTicket);
        var pendingCustomerId = pendingEntryForCustomer.Key != 0 && mobilePendingCustomerMap.TryGetValue(pendingEntryForCustomer.Key, out var cid) ? cid : 1;
        using var f = new PaymentForm(Cart, pendingCustomerId);
        ThemeService.Apply(f);
        if (ShowOwnedDialog(f) != DialogResult.OK) return;
        try
        {
            var tableId = ticketTableMap.TryGetValue(activeTicket, out var assignedTableId) ? assignedTableId : (int?)null;
            var saleId = SaleService.Complete(UserId, f.CustomerId, Cart, f.Payments, f.Received, f.Change, tableId, f.SaleChannel, f.SaleNotes, f.DeliveryAddress, f.DeliveryStatus, f.DiscountReason);
            lastCompletedSaleId = saleId;
            if (f.PrintRequested) TicketService.Print(saleId);
            SoundService.PlayCashRegister();
            if (tableId.HasValue)
            {
                WebDashboardServer.RemoveMobileOpenTicket(tableId.Value);
                mobileTableTicketMap.Remove(tableId.Value);
            }
            var pendingEntry = mobilePendingTicketMap.FirstOrDefault(x => x.Value == activeTicket);
            if (pendingEntry.Key != 0)
            {
                WebDashboardServer.RemoveMobilePendingTicket(pendingEntry.Key);
                mobilePendingTicketMap.Remove(pendingEntry.Key);
                mobilePendingCustomerMap.Remove(pendingEntry.Key);
            }
            ReleaseTableForActiveTicket();
            Cart.Clear();
            ticketGeneralDiscountAmount = 0;
            generalDiscountAllocations.Clear();
            ticketNames[activeTicket] = $"VENTA {activeTicket + 1}";
            selectedCartRow = -1;
            RefreshTabs();
            RefreshGrid();
            RefreshDailySales();
            salonWindow?.RefreshSalon();
            barcode.Focus();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "No se pudo registrar la venta", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void Backup()
    {
        try { var file = BackupService.CreateBackup(); MessageBox.Show($"Respaldo creado:\n{file}", "FerrarisPOS"); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Respaldo", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void RefreshLicense()
    {
        var d = LicenseService.DaysRemaining;

        if (LicenseService.IsPermanent || d < 0)
        {
            licenseLabel.Text = "ACTIVADA · SIN VENCIMIENTO";
            licenseLabel.AccessibleName = "Licencia activada permanentemente";
        }
        else if (d == 1)
        {
            licenseLabel.Text = "1 día restante";
            licenseLabel.AccessibleName = "Licencia de prueba: 1 día restante";
        }
        else if (d > 0)
        {
            licenseLabel.Text = $"{d:N0} días restantes";
            licenseLabel.AccessibleName = $"Licencia de prueba: {d} días restantes";
        }
        else
        {
            licenseLabel.Text = "VENCIDA · ACTIVAR EN F7";
            licenseLabel.AccessibleName = "Licencia de prueba vencida";
        }

        licenseLabel.ForeColor =
            d < 0 ? Color.DarkGreen :
            d <= 7 ? Color.DarkRed :
            d <= 30 ? Color.DarkOrange :
            Color.DarkGreen;

        statusLabel.Text = d == 0
            ? "Bloqueado · activar licencia"
            : "Sistema listo";
        statusLabel.ForeColor = d == 0 ? Color.DarkRed : Color.DarkGreen;
    }
}


internal sealed class TicketNameForm : Form
{
    private readonly TextBox name = new();
    public string TicketName => name.Text.Trim();

    public TicketNameForm(string current, int number)
    {
        Text = "Nombrar venta";
        Width = 500; Height = 210;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        Controls.Add(new Label { Text = $"NOMBRE DE LA VENTA {number}", Location = new Point(20, 20), AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold) });
        name.Location = new Point(20, 55); name.Width = 440; name.Text = current.StartsWith("VENTA ", StringComparison.OrdinalIgnoreCase) ? "" : current;
        name.PlaceholderText = "Ej.: Pedido Javier / Cliente Betsa­be";
        Controls.Add(name);
        var ok = new Button { Text = "GUARDAR", Location = new Point(20, 105), Width = 200, Height = 40, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "CANCELAR", Location = new Point(240, 105), Width = 200, Height = 40, DialogResult = DialogResult.Cancel };
        Controls.Add(ok); Controls.Add(cancel);
        AcceptButton = ok; CancelButton = cancel;
    }
}


internal sealed class CashOpeningForm : Form
{
    private readonly TextBox amount = new();
    private readonly CheckBox enableMercadoPago = new();
    private readonly TextBox mercadoPagoAmount = new();
    public double OpeningAmount { get; private set; }
    public bool MercadoPagoEnabled => enableMercadoPago.Checked;
    public double MercadoPagoOpeningAmount { get; private set; }

    public CashOpeningForm()
    {
        Text = "FerrarisPOS · Apertura de caja";
        Width = 560;
        Height = 430;
        MinimumSize = new Size(560, 430);
        MaximumSize = new Size(560, 430);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;

        var title = new Label
        {
            Text = "APERTURA DE CAJA",
            Location = new Point(28, 24),
            Size = new Size(500, 35),
            Font = new Font("Segoe UI", 17, FontStyle.Bold)
        };
        Controls.Add(title);

        var info = new Label
        {
            Text = "Ingresá el efectivo con el que comienza la jornada.\nMercado Pago es opcional y funciona como una caja virtual separada.",
            Location = new Point(30, 67),
            Size = new Size(500, 48),
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.DimGray
        };
        Controls.Add(info);

        Controls.Add(new Label { Text = "DINERO INICIAL EN EFECTIVO", Location = new Point(30, 130), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        amount.Location = new Point(30, 155);
        amount.Width = 500;
        amount.Height = 40;
        amount.Font = new Font("Segoe UI", 16, FontStyle.Bold);
        amount.TextAlign = HorizontalAlignment.Right;
        amount.PlaceholderText = "0,00";
        amount.Text = "0,00";
        Controls.Add(amount);

        enableMercadoPago.Text = "HABILITAR CAJA PARALELA DE MERCADO PAGO";
        enableMercadoPago.Location = new Point(30, 215);
        enableMercadoPago.AutoSize = true;
        enableMercadoPago.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        enableMercadoPago.CheckedChanged += (_, _) =>
        {
            mercadoPagoAmount.Enabled = enableMercadoPago.Checked;
            if (!enableMercadoPago.Checked) mercadoPagoAmount.Text = "0,00";
            else { mercadoPagoAmount.Focus(); mercadoPagoAmount.SelectAll(); }
        };
        Controls.Add(enableMercadoPago);

        Controls.Add(new Label { Text = "SALDO INICIAL MERCADO PAGO", Location = new Point(30, 250), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        mercadoPagoAmount.Location = new Point(30, 275);
        mercadoPagoAmount.Width = 500;
        mercadoPagoAmount.Height = 36;
        mercadoPagoAmount.Font = new Font("Segoe UI", 14, FontStyle.Bold);
        mercadoPagoAmount.TextAlign = HorizontalAlignment.Right;
        mercadoPagoAmount.Text = "0,00";
        mercadoPagoAmount.Enabled = false;
        Controls.Add(mercadoPagoAmount);

        var open = new Button
        {
            Text = "ABRIR CAJA",
            Location = new Point(300, 330),
            Width = 230,
            Height = 45,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        open.Click += (_, _) => OpenCash();
        Controls.Add(open);

        var cancel = new Button
        {
            Text = "CANCELAR",
            Location = new Point(30, 330),
            Width = 250,
            Height = 45,
            DialogResult = DialogResult.Cancel,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        Controls.Add(cancel);

        AcceptButton = open;
        CancelButton = cancel;
        Shown += (_, _) => { amount.Focus(); amount.SelectAll(); };
    }

    private static bool TryAmount(string text, out double value)
    {
        var raw = text.Trim();
        if (raw.Contains(",")) raw = raw.Replace(".", "").Replace(",", ".");
        return double.TryParse(raw, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out value);
    }

    private void OpenCash()
    {
        if (!TryAmount(amount.Text, out var cash) || cash < 0)
        {
            MessageBox.Show("Ingresá un importe de efectivo válido mayor o igual a cero.", "Apertura de caja", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            amount.Focus(); amount.SelectAll(); return;
        }

        var mp = 0d;
        if (MercadoPagoEnabled)
        {
            if (!TryAmount(mercadoPagoAmount.Text, out mp) || mp <= 0)
            {
                MessageBox.Show("Para habilitar Mercado Pago ingresá un saldo inicial mayor a cero.", "Apertura de caja", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                mercadoPagoAmount.Focus(); mercadoPagoAmount.SelectAll(); return;
            }
        }

        OpeningAmount = cash;
        MercadoPagoOpeningAmount = mp;
        DialogResult = DialogResult.OK;
        Close();
    }
}
