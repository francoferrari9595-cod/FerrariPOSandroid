using FerrarisPOS.Data;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Globalization;

namespace FerrarisPOS.Services;

public static class ThemeService
{
    public static bool IsLightTheme => string.Equals(CurrentTheme, "Claro", StringComparison.OrdinalIgnoreCase);

    // V33: paleta de Windows alineada con los temas visuales de FerrariPOS Manager Android.
    // Se conserva el tema Claro y se reemplazan las paletas Windows anteriores.
    public static readonly string[] AvailableThemes =
    {
        "Oscuro", "Claro", "Ejecutivo Azul", "Esmeralda", "Rojo Ferrari", "Titanium",
        "Black", "Celeste", "Cyber Neon", "Aurora", "Graphite", "Quantum",
        "Negro Neón", "Blanco Neón", "Plateado"
    };

    public static Color CurrentWindowColor => Palette(CurrentTheme).Window;
    public static Color CurrentSurfaceColor => Palette(CurrentTheme).Surface;
    public static Color CurrentAccentColor => Palette(CurrentTheme).Accent;

    public static string CurrentTheme
    {
        get
        {
            var saved = Database.GetSetting("theme", "Graphite");
            if (!AvailableThemes.Contains(saved, StringComparer.OrdinalIgnoreCase))
            {
                // Compatibilidad con instalaciones anteriores: todos los temas
                // retirados vuelven al tema de fábrica sin romper la configuración.
                saved = "Oscuro";
                Database.SetSetting("theme", saved);
            }
            else
            {
                saved = AvailableThemes.First(t => string.Equals(t, saved, StringComparison.OrdinalIgnoreCase));
            }
            return saved;
        }
    }
    // La fuente del programa es fija: Segoe UI. Solo se permite ajustar el tamaño.
    public const string FixedFontFamily = "Segoe UI";
    public static string CurrentFontFamily => FixedFontFamily;
    public static float CurrentFontSize => float.TryParse(Database.GetSetting("font_size", "9.0"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) ? Math.Clamp(s, 7f, 20f) : 10f;

    private static bool _globalHooked;
    private static readonly HashSet<Form> _globallyStyledForms = new();

    /// <summary>
    /// Aplica automáticamente Ferrari Dark Chrome a cualquier formulario que
    /// se abra, incluso si una ventana antigua no llama explícitamente a
    /// ThemeService.Apply. Esto unifica búsquedas, inventario, clientes,
    /// reportes y diálogos sin tocar su lógica.
    /// </summary>
    public static void EnableGlobalWindows10Theme()
    {
        if (_globalHooked || !OperatingSystem.IsWindowsVersionAtLeast(10, 0)) return;
        _globalHooked = true;
        Application.Idle += (_, _) =>
        {
            foreach (Form form in Application.OpenForms)
            {
                if (form.IsDisposed || form.Disposing || _globallyStyledForms.Contains(form)) continue;
                if (form.GetType().Name is "MainForm" or "LoginForm") continue;
                try
                {
                    Apply(form);
                    _globallyStyledForms.Add(form);
                    form.Disposed += (_, _) => _globallyStyledForms.Remove(form);
                }
                catch { }
            }
        };
    }

    public static void SetTheme(string theme)
    {
        Database.SetSetting("theme", theme);
        foreach (Form form in Application.OpenForms) Apply(form);
    }

    public static int BrightnessLevel
    {
        get
        {
            if (!int.TryParse(Database.GetSetting("ui_brightness", "140"), out var value)) value = 140;
            return Math.Clamp(value, 70, 140);
        }
    }

    public static void SetBrightness(int level)
    {
        Database.SetSetting("ui_brightness", Math.Clamp(level, 70, 140).ToString(CultureInfo.InvariantCulture));
        foreach (Form form in Application.OpenForms) Apply(form);
    }

    private static Color BrightnessColor(Color color)
    {
        // 100 = paleta original. Por encima se acerca gradualmente al blanco;
        // por debajo se acerca al negro. El ajuste es global y queda guardado
        // en SQLite, por lo que afecta a todos los empleados que usan la misma
        // base de datos. Los blancos casi puros se conservan para no perder contraste.
        var level = BrightnessLevel;
        if (level == 100) return color;
        if (color.A < 255) return color;
        if (level > 100)
        {
            var amount = (level - 100) / 100f * 0.72f;
            return Blend(color, Color.White, amount);
        }
        var darkAmount = (100 - level) / 100f * 0.55f;
        return Blend(color, Color.Black, darkAmount);
    }

    private static Color BrightnessSurface(Color color) => BrightnessColor(color);

    public static void SetFontSize(float size)
    {
        Database.SetSetting("font_family", FixedFontFamily);
        Database.SetSetting("font_size", Math.Clamp(size, 7f, 20f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        foreach (Form form in Application.OpenForms) Apply(form);
    }

    public static void Apply(Control root)
    {
        // Solo optimización de repintado: evita destellos al cambiar de pantalla
        // o actualizar importes. No cambia tamaños, colores, fuentes ni lógica.
        EnableDoubleBufferRecursive(root);

        var palette = Palette(CurrentTheme);
        bool lightTheme = IsLightTheme;
        bool secondaryGlass = string.Equals(CurrentTheme, "Oscuro", StringComparison.OrdinalIgnoreCase) && root is Form f && f.GetType().Name is not "MainForm" and not "LoginForm" && OperatingSystem.IsWindowsVersionAtLeast(10, 0);

        // La barra nativa de TODAS las ventanas se fuerza a oscuro. Antes solo
        // se hacía para ventanas secundarias, por eso la principal podía quedar
        // con la barra blanca de Windows 10/11. Esto no toca ninguna conexión,
        // servicio ni lógica del POS: es únicamente renderizado de ventana.
        if (root is Form form)
        {
            if (lightTheme) ApplyNativeLightTitleBar(form);
            else ApplyNativeDarkTitleBar(form);
        }

        ApplyControl(root, palette, false, secondaryGlass);
        ApplyDarkTabsRecursive(root);
        ApplySecondaryWindowBackground(root);
        ApplyFont(root);
        root.Invalidate(true);
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, GlassSurfaceState> GlassSurfaces = new();

    private sealed class GlassSurfaceState
    {
        public Bitmap? Image;
        public EventHandler? ResizeHandler;
        public PaintEventHandler? PaintHandler;
    }

    private static void ApplySecondaryWindowBackground(Control root)
    {
        if (root is not Form form) return;
        var typeName = form.GetType().Name;
        if (typeName == "MainForm" || typeName == "LoginForm") return;
        // V33: se elimina la antigua imagen/fondo glass de las ventanas.
        // Cada tema usa ahora una superficie sólida alineada con Android Manager.
        try
        {
            form.BackgroundImage = null;
            form.BackgroundImageLayout = ImageLayout.None;
            form.BackColor = Palette(CurrentTheme).Window;
            form.ForeColor = Palette(CurrentTheme).Text;
        }
        catch { }
    }

    private static void ApplyNativeLightTitleBar(Form form)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            if (!form.IsHandleCreated) form.CreateControl();
            if (!form.IsHandleCreated || !OperatingSystem.IsWindowsVersionAtLeast(10, 0)) return;

            int light = 0;
            _ = DwmSetWindowAttribute(form.Handle, 20, ref light, sizeof(int));
            _ = DwmSetWindowAttribute(form.Handle, 19, ref light, sizeof(int));

            int caption = BrightnessColor(Color.FromArgb(247, 249, 252)).ToArgb();
            int text = Color.FromArgb(35, 43, 52).ToArgb();
            int border = BrightnessColor(Color.FromArgb(205, 213, 222)).ToArgb();
            _ = DwmSetWindowAttribute(form.Handle, 35, ref caption, sizeof(int));
            _ = DwmSetWindowAttribute(form.Handle, 36, ref text, sizeof(int));
            _ = DwmSetWindowAttribute(form.Handle, 34, ref border, sizeof(int));
            _ = SetWindowTheme(form.Handle, null, null);
        }
        catch { }
    }

    private static void ApplyNativeDarkTitleBar(Form form)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            if (!form.IsHandleCreated) form.CreateControl();
            if (!form.IsHandleCreated) return;

            // Windows 10/11: usar ambos identificadores de modo oscuro porque
            // algunas compilaciones de Windows 10 responden al 19 y otras al 20.
            // Los atributos 35/36 fijan además color de caption y texto cuando
            // la versión de DWM los admite.
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0))
            {
                int dark = 1;
                _ = DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
                _ = DwmSetWindowAttribute(form.Handle, 19, ref dark, sizeof(int));

                int caption = Color.FromArgb(8, 11, 16).ToArgb();
                int text = Color.White.ToArgb();
                int border = Color.FromArgb(8, 11, 16).ToArgb();
                _ = DwmSetWindowAttribute(form.Handle, 35, ref caption, sizeof(int));
                _ = DwmSetWindowAttribute(form.Handle, 36, ref text, sizeof(int));
                _ = DwmSetWindowAttribute(form.Handle, 34, ref border, sizeof(int));

                // Refuerzo para Windows 10/Tiny/Lite donde DWM puede conservar
                // el tema claro de Explorer aunque la app solicite dark mode.
                _ = SetWindowTheme(form.Handle, "DarkMode_Explorer", null);
            }
        }
        catch { }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, TabPaintState> TabPaintStates = new();

    private sealed class TabPaintState
    {
        public DrawItemEventHandler? Handler;
    }

    private static void ApplyDarkTabsRecursive(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is TabControl tabs && child is not FerrarisPOS.Forms.BlackTicketTabControl)
            {
                try
                {
                    tabs.DrawMode = TabDrawMode.OwnerDrawFixed;
                    tabs.SizeMode = TabSizeMode.Fixed;
                    tabs.ItemSize = new Size(Math.Max(105, tabs.ItemSize.Width), Math.Max(30, tabs.ItemSize.Height));
                    tabs.BackColor = IsLightTheme ? BrightnessColor(Color.FromArgb(247, 249, 252)) : BrightnessColor(Color.FromArgb(8, 11, 16));
                    tabs.ForeColor = IsLightTheme ? Color.FromArgb(35, 43, 52) : Color.White;
                    tabs.Padding = new Point(2, 2);

                    var state = TabPaintStates.GetOrCreateValue(tabs);
                    if (state.Handler != null)
                        tabs.DrawItem -= state.Handler;

                    state.Handler = (sender, e) => DrawDarkTab(sender, e);
                    tabs.DrawItem += state.Handler;
                    tabs.Invalidate();
                }
                catch { }
            }

            ApplyDarkTabsRecursive(child);
        }
    }

    private static void DrawDarkTab(object? sender, DrawItemEventArgs e)
    {
        if (sender is not TabControl tabs || e.Index < 0 || e.Index >= tabs.TabPages.Count) return;
        try
        {
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            var rect = e.Bounds;
            rect.Inflate(-1, -1);

            var light = IsLightTheme;
            using var bg = new SolidBrush(light
                ? (selected ? Color.FromArgb(220, 232, 244) : Color.FromArgb(242, 245, 248))
                : (selected ? BrightnessColor(Color.FromArgb(45, 52, 62)) : BrightnessColor(Color.FromArgb(22, 27, 34))));
            using var border = new Pen(light
                ? (selected ? Color.FromArgb(0, 120, 190) : Color.FromArgb(190, 201, 214))
                : (selected ? Color.FromArgb(0, 174, 239) : Color.FromArgb(75, 84, 96)), selected ? 2f : 1f);
            e.Graphics.FillRectangle(bg, rect);
            e.Graphics.DrawRectangle(border, rect);

            string text = tabs.TabPages[e.Index].Text ?? string.Empty;
            using var font = new Font("Segoe UI", 9f, selected ? FontStyle.Bold : FontStyle.Regular);
            using var brush = new SolidBrush(light ? Color.FromArgb(35, 43, 52) : Color.White);
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
            TextRenderer.DrawText(e.Graphics, text, font, rect, light ? Color.FromArgb(35, 43, 52) : Color.White, flags);
        }
        catch { }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? pszSubAppName, string? pszSubIdList);

    private static Bitmap CreateStableBackground(Image source, float opacity, Color baseColor, Size targetSize)
    {
        int w = Math.Max(1, targetSize.Width);
        int h = Math.Max(1, targetSize.Height);
        var result = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        using (var g = Graphics.FromImage(result))
        using (var attributes = new System.Drawing.Imaging.ImageAttributes())
        {
            g.Clear(baseColor);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

            float scale = Math.Max((float)w / source.Width, (float)h / source.Height);
            int dw = Math.Max(1, (int)Math.Ceiling(source.Width * scale));
            int dh = Math.Max(1, (int)Math.Ceiling(source.Height * scale));
            int x = (w - dw) / 2;
            int y = (h - dh) / 2;

            var brightnessFactor = BrightnessLevel >= 100
                ? 1f + ((BrightnessLevel - 100) / 100f) * 0.28f
                : 1f - ((100 - BrightnessLevel) / 100f) * 0.22f;
            attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix
            {
                Matrix00 = brightnessFactor,
                Matrix11 = brightnessFactor,
                Matrix22 = brightnessFactor,
                Matrix33 = Math.Clamp(opacity, 0f, 1f)
            });
            g.DrawImage(source, new Rectangle(x, y, dw, dh), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        }

        // Desenfoque determinista por reducción/ampliación. Al ser una imagen
        // final opaca, Windows 8/10/11 la compone exactamente igual.
        const int factor = 7;
        int sw = Math.Max(1, w / factor);
        int sh = Math.Max(1, h / factor);
        using var small = new Bitmap(sw, sh, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(result, new Rectangle(0, 0, sw, sh));
        }
        using (var g = Graphics.FromImage(result))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(small, new Rectangle(0, 0, w, h));
        }
        return result;
    }

    public static Bitmap CreateSecondaryBackground(Image source, Size targetSize)
        => CreateStableBackground(source, 0.49f, Color.FromArgb(10, 14, 24), targetSize);

    private static Bitmap CreateGlassBackground(Image source, float opacity)
    {
        // Fondo estable entre Windows 8/10/11: la transparencia se compone en
        // una imagen real, no en BackColor con alfa, evitando que WinForms la
        // convierta en blanco en algunos controles.
        using var opacityCopy = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(opacityCopy))
        using (var attributes = new System.Drawing.Imaging.ImageAttributes())
        {
            attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = Math.Clamp(opacity, 0f, 1f) });
            g.Clear(Color.Transparent);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, new Rectangle(0, 0, source.Width, source.Height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
        }

        const int factor = 6;
        int smallW = Math.Max(1, opacityCopy.Width / factor);
        int smallH = Math.Max(1, opacityCopy.Height / factor);
        using var small = new Bitmap(smallW, smallH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(opacityCopy, new Rectangle(0, 0, smallW, smallH));
        }

        var result = new Bitmap(source.Width, source.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(small, new Rectangle(0, 0, result.Width, result.Height));
        }
        return result;
    }

    private static void ApplySecondaryGlassControls(Control parent)
    {
        if (parent is not Form form) return;
        if (form.ClientSize.Width <= 0 || form.ClientSize.Height <= 0) return;

        Bitmap? formBackground = null;
        try
        {
            if (form.BackgroundImage != null)
            {
                formBackground = new Bitmap(form.BackgroundImage);
            }
            else
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Resources", "pos_background.png");
                if (File.Exists(path))
                {
                    using var source = Image.FromFile(path);
                    formBackground = CreateStableBackground(source, 0.49f, BrightnessColor(Color.FromArgb(10, 14, 24)), form.ClientSize);
                }
            }
            if (formBackground == null) return;

            foreach (Control c in form.Controls)
                ApplyGlassToContainer(c, form, formBackground);
        }
        catch { }
        finally { formBackground?.Dispose(); }
    }

    private static void ApplyGlassToContainer(Control c, Form form, Bitmap formBackground)
    {
        var tag = c.Tag?.ToString();
        bool excluded = tag is "TicketTabs" or "SalonCanvas" or "SalonGlassBar" or "WatermarkTransparent" or "ThemeAccent";

        if (!excluded && c is Panel or GroupBox or FlowLayoutPanel or TableLayoutPanel or TabControl or TabPage)
        {
            SetGlassSurface(c, form, formBackground);
        }
        else if (c is Label label && !excluded)
        {
            label.BackColor = Color.Transparent;
        }

        foreach (Control child in c.Controls)
            ApplyGlassToContainer(child, form, formBackground);
    }

    private static void SetGlassSurface(Control control, Form form, Bitmap formBackground)
    {
        if (control.Width <= 0 || control.Height <= 0) return;

        try
        {
            Point p = form.PointToClient(control.PointToScreen(Point.Empty));
            var crop = new Bitmap(control.Width, control.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(crop))
            {
                g.Clear(Color.FromArgb(34, 39, 48));
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(formBackground,
                    new Rectangle(0, 0, crop.Width, crop.Height),
                    new Rectangle(p.X, p.Y, crop.Width, crop.Height),
                    GraphicsUnit.Pixel);

                // Capa glass oscura: opaca y estable entre Windows, pero deja
                // visible la imagen debajo para conservar el efecto de vidrio.
                using var overlay = new SolidBrush(Color.FromArgb(105, 12, 17, 24));
                g.FillRectangle(overlay, 0, 0, crop.Width, crop.Height);
            }

            var state = GlassSurfaces.GetOrCreateValue(control);
            state.Image?.Dispose();
            state.Image = crop;
            control.BackgroundImage = crop;
            control.BackgroundImageLayout = ImageLayout.Stretch;

            var glassState = GlassSurfaces.GetOrCreateValue(control);
            if (glassState.PaintHandler != null)
                control.Paint -= glassState.PaintHandler;
            glassState.PaintHandler = (sender, e) =>
            {
                try
                {
                    var image = control.BackgroundImage;
                    if (image == null) return;
                    // El evento Paint ocurre después del fondo nativo. Volvemos a
                    // pintar el vidrio aquí para impedir que WinForms/DWM deje la
                    // superficie blanca en Windows 10/11.
                    // En GroupBox dejamos una franja superior limpia para que el
                    // título nativo quede siempre por delante del vidrio. Antes la
                    // pintura comenzaba demasiado arriba y podía tapar parcialmente
                    // el texto del encabezado ("CAJA FÍSICA", "PROVEEDORES", etc.).
                    int top = control is GroupBox ? 22 : 0;
                    var dest = new Rectangle(0, top, control.ClientSize.Width, Math.Max(0, control.ClientSize.Height - top));
                    if (dest.Width <= 0 || dest.Height <= 0) return;
                    e.Graphics.DrawImage(image, dest);
                    using var veil = new SolidBrush(Color.FromArgb(42, 5, 9, 15));
                    e.Graphics.FillRectangle(veil, dest);
                    if (control is GroupBox)
                    {
                        using var pen = new Pen(BrightnessColor(Color.FromArgb(105, 115, 130)), 1f);
                        e.Graphics.DrawRectangle(pen, 0, 7, Math.Max(0, control.Width - 1), Math.Max(0, control.Height - 8));
                    }
                }
                catch { }
            };
            control.Paint += glassState.PaintHandler;
            // No usar superficies claras de la paleta aquí: son las que
            // provocaban las tarjetas blancas en Windows 10/11. El fondo ya
            // está compuesto en 'crop', por lo que la tarjeta queda como vidrio
            // oscuro y deja ver la misma imagen que la sección de venta.
            control.BackColor = OperatingSystem.IsWindowsVersionAtLeast(10, 0)
                ? BrightnessColor(Color.FromArgb(12, 16, 22))
                : BrightnessColor(Color.FromArgb(28, 33, 41));
            control.ForeColor = Color.White;
        }
        catch { }
    }

    private static void EnableDoubleBufferRecursive(Control root)
    {
        try
        {
            if (root is Control)
            {
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var prop = typeof(Control).GetProperty("DoubleBuffered", flags);
                prop?.SetValue(root, true, null);
                var setStyle = typeof(Control).GetMethod("SetStyle", flags);
                setStyle?.Invoke(root, new object[] { ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true });
                var updateStyles = typeof(Control).GetMethod("UpdateStyles", flags);
                updateStyles?.Invoke(root, null);
            }
        }
        catch { }

        foreach (Control child in root.Controls)
            EnableDoubleBufferRecursive(child);
    }

    private static void ApplyControl(Control c, ThemePalette p, bool clientGlass, bool secondaryGlass = false)
    {
        if (string.Equals(c.Tag?.ToString(), "WatermarkTransparent", StringComparison.Ordinal))
        {
            c.BackColor = Color.Transparent;
            c.ForeColor = p.Text;
            foreach (Control child in c.Controls)
                ApplyControl(child, p, true, secondaryGlass);
            return;
        }

        // El área de venta/salón es una superficie visual propia de FerrariPOS.
        // No dependemos de la composición/transparencia de Windows: los paneles
        // intermedios se dejan realmente transparentes para que el fondo pintado
        // por MainForm sea el que se vea en Windows 8, 10 y 11.
        bool insideClientGlass = clientGlass;

        if (c.Tag?.ToString() == "SalonGlassBar")
        {
            c.BackColor = BrightnessColor(Color.FromArgb(24, 28, 35));
            c.ForeColor = Color.White;
            foreach (Control child in c.Controls)
                ApplyControl(child, p, true, secondaryGlass);
            return;
        }
        // Las mesas del salón tienen una paleta propia compatible con temas oscuros.
        // Así nunca vuelven a convertirse en cuadrados blancos al aplicar Grafito/Oscuro.
        var salonTag = c.Tag?.ToString();
        if (!string.IsNullOrWhiteSpace(salonTag) && salonTag.StartsWith("SalonTable:", StringComparison.Ordinal))
        {
            var occupied = salonTag.EndsWith(":occupied", StringComparison.Ordinal);
            var reserved = salonTag.EndsWith(":reserved", StringComparison.Ordinal);
            c.BackColor = p.Surface;
            c.ForeColor = occupied || reserved ? Color.FromArgb(255, 70, 90) : Color.FromArgb(57, 255, 255);
            if (c is Button salonButton)
            {
                salonButton.FlatStyle = FlatStyle.Flat;
                salonButton.UseVisualStyleBackColor = false;
                salonButton.FlatAppearance.BorderColor = p.Border;
                salonButton.FlatAppearance.BorderSize = 1;
            }
            return;
        }

        if (c.Tag?.ToString() == "SalonCanvas")
        {
            // V37: el área de mesas es una superficie lisa del tema, sin
            // fotografía/fondo externo. Esto evita zonas negras o transparencias
            // inconsistentes y mantiene el mismo Graphite en todo el salón.
            c.BackColor = p.Window;
            c.ForeColor = p.Text;
            c.BackgroundImage = null;
            c.BackgroundImageLayout = ImageLayout.None;
        }

        if (c.Tag?.ToString() == "ThemeAccent")
        {
            c.BackColor = p.Accent;
            return;
        }

        if (c is Form)
        {
            c.BackColor = secondaryGlass ? BrightnessColor(Color.FromArgb(10, 14, 24)) : p.Window;
            c.ForeColor = secondaryGlass ? Color.White : p.Text;
        }
        else if (c is Panel || c is GroupBox || c is TabControl || c is TabPage || c is FlowLayoutPanel || c is TableLayoutPanel)
        {
            if (c.Tag?.ToString() == "TicketTabs")
            {
                c.BackColor = IsLightTheme ? p.Surface : Color.Black;
                c.ForeColor = IsLightTheme ? p.Text : Color.Gainsboro;
                if (c is FerrarisPOS.Forms.BlackTicketTabControl ticketControl)
                {
                    ticketControl.SetHeaderBlackMode(!IsLightTheme);
                    foreach (TabPage page in ticketControl.TabPages)
                    {
                        page.BackColor = IsLightTheme ? p.Surface : Color.Black;
                        page.ForeColor = IsLightTheme ? p.Text : Color.Gainsboro;
                    }
                }
            }
            else if (insideClientGlass || secondaryGlass)
            {
                // Windows 10/11: nunca dejamos que WinForms pinte una tarjeta
                // secundaria con la superficie clara de la paleta. La imagen y
                // el velo glass se pintan en SetGlassSurface/GlassPaintHandler.
                c.BackColor = Color.Transparent;
                c.ForeColor = Color.White;
            }
            else
            {
                c.BackColor = p.Surface;
                c.ForeColor = p.Text;
            }
        }
        else if (c is Label label)
        {
            if (label.Tag?.ToString() != "FixedMainTotal")
            {
                var text = label.Text ?? string.Empty;
                bool money = text.Contains('$') || text.Contains("€") || text.Contains("ARS", StringComparison.OrdinalIgnoreCase) ||
                             text.Contains("PRECIO", StringComparison.OrdinalIgnoreCase) ||
                             text.Contains("IMPORTE", StringComparison.OrdinalIgnoreCase) ||
                             text.Contains("TOTAL", StringComparison.OrdinalIgnoreCase) ||
                             text.Contains("MONTO", StringComparison.OrdinalIgnoreCase) ||
                             text.Contains("SALDO", StringComparison.OrdinalIgnoreCase) ||
                             text.Contains("DEUDA", StringComparison.OrdinalIgnoreCase) ||
                             text.Contains("COSTO", StringComparison.OrdinalIgnoreCase);
                // Tema Claro: texto negro/gris oscuro en todos los productos y
                // etiquetas, sin verde ni naranja fluorescente.
                label.ForeColor = IsLightTheme
                    ? p.Text
                    : (money && OperatingSystem.IsWindowsVersionAtLeast(10, 0)
                        ? NeonGreen
                        : ((insideClientGlass || secondaryGlass) ? Color.White : p.Text));
            }
            label.BackColor = Color.Transparent;
        }
        else if (c is Button button)
        {
            button.BackColor = (insideClientGlass || secondaryGlass) ? BrightnessColor(Color.FromArgb(42, 49, 59)) : p.Button;
            button.ForeColor = (insideClientGlass || secondaryGlass) ? Color.White : p.Text;
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.FlatAppearance.BorderColor = p.Border;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = Blend(p.Button, p.Accent, 0.16f);
            button.FlatAppearance.MouseDownBackColor = Blend(p.Button, p.Accent, 0.28f);
            button.Cursor = Cursors.Hand;

            // Un toque visual moderno sin sacrificar rendimiento.
            button.MouseEnter -= ButtonMouseEnter;
            button.MouseLeave -= ButtonMouseLeave;
            button.MouseEnter += ButtonMouseEnter;
            button.MouseLeave += ButtonMouseLeave;
        }
        else if (c is TextBox textBox)
        {
            textBox.BackColor = (insideClientGlass || secondaryGlass) ? BrightnessColor(Color.FromArgb(28, 34, 42)) : p.Input;
            textBox.ForeColor = (insideClientGlass || secondaryGlass) ? Color.White : p.Text;
        }
        else if (c is ComboBox combo)
        {
            combo.BackColor = (insideClientGlass || secondaryGlass) ? BrightnessColor(Color.FromArgb(28, 34, 42)) : p.Input;
            combo.ForeColor = (insideClientGlass || secondaryGlass) ? Color.White : p.Text;
        }
        else if (c is CheckBox check)
        {
            check.BackColor = (insideClientGlass || secondaryGlass) ? Color.Transparent : p.Surface;
            check.ForeColor = (insideClientGlass || secondaryGlass) ? Color.White : p.Text;
        }
        else if (c is DataGridView grid)
        {
            bool isCartGrid = string.Equals(grid.Tag?.ToString(), "CartGridWatermark", StringComparison.Ordinal);
            // El carrito deja ver la marca de agua detrás de las filas mediante
            // un velo negro semitransparente. Los encabezados siguen siendo opacos.
            // DataGridView no admite colores con canal alfa en BackgroundColor.
            // El intento de usar Color.FromArgb(125, ...) provoca el error de
            // arranque "No se puede establecer el color BackgroundColor como
            // un color transparente". El efecto de opacidad del carrito se
            // conserva mediante su pintado personalizado (MainForm), usando
            // el mismo fondo y la misma marca de agua difuminada.
            bool fixedClientCart = isCartGrid;
            // Tema Claro: grillas sólidas y de alto contraste. Tema de fábrica:
            // se conserva exactamente la base oscura actual.
            if (IsLightTheme)
            {
                grid.BackgroundImage = null;
                grid.BackgroundColor = p.Surface;
                grid.GridColor = p.Border;

                // Si antes se usó Oscuro, pueden quedar estilos por columna o
                // el evento de formato neón. En Claro se limpian explícitamente
                // para que PRODUCTO, STOCK, COSTO, TOTAL, etc. queden en negro.
                foreach (DataGridViewColumn column in grid.Columns)
                {
                    column.DefaultCellStyle.ForeColor = p.Text;
                    column.DefaultCellStyle.SelectionForeColor = p.Text;
                }
                grid.CellFormatting -= NeonGridCellFormatting;
            }
            else
            {
                grid.BackgroundColor = OperatingSystem.IsWindowsVersionAtLeast(10, 0)
                    ? BrightnessColor(Color.FromArgb(10, 13, 18))
                    : (fixedClientCart ? Color.FromArgb(28, 33, 40) : p.Input);
                grid.GridColor = OperatingSystem.IsWindowsVersionAtLeast(10, 0)
                    ? BrightnessColor(Color.FromArgb(55, 65, 78))
                    : (fixedClientCart ? Color.FromArgb(58, 68, 82) : p.Border);
            }
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = IsLightTheme
                    ? p.Accent
                    : (OperatingSystem.IsWindowsVersionAtLeast(10, 0) ? Color.FromArgb(0, 174, 239) : (fixedClientCart ? Color.FromArgb(0, 174, 239) : p.Accent)),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                Alignment = DataGridViewContentAlignment.MiddleCenter
            };
            grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                // Los estilos de celda tampoco deben recibir colores alfa.
                // La transparencia visual se compone al pintar la celda.
                BackColor = IsLightTheme ? p.Input : (OperatingSystem.IsWindowsVersionAtLeast(10, 0) ? BrightnessColor(Color.FromArgb(17, 21, 27)) : (fixedClientCart ? Color.FromArgb(28, 33, 40) : p.Input)),
                ForeColor = IsLightTheme ? p.Text : (OperatingSystem.IsWindowsVersionAtLeast(10, 0) ? Color.White : (fixedClientCart ? Color.White : p.Text)),
                SelectionBackColor = IsLightTheme ? p.Selection : (OperatingSystem.IsWindowsVersionAtLeast(10, 0) ? BrightnessColor(Color.FromArgb(52, 65, 55)) : (fixedClientCart ? BrightnessColor(Color.FromArgb(52, 65, 55)) : p.Selection)),
                SelectionForeColor = IsLightTheme ? p.Text : (OperatingSystem.IsWindowsVersionAtLeast(10, 0) ? Color.FromArgb(57, 255, 20) : (fixedClientCart ? Color.FromArgb(57, 255, 255) : p.HeaderText)),
                Font = new Font("Segoe UI", 10)
            };
            grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = IsLightTheme ? p.Alternate : (OperatingSystem.IsWindowsVersionAtLeast(10, 0) ? BrightnessColor(Color.FromArgb(30, 36, 44)) : (fixedClientCart ? Color.FromArgb(42, 48, 57) : p.Alternate))
            };

            if (!IsLightTheme && (fixedClientCart || IsNeonDarkTheme()))
                ApplyNeonGridStyle(grid);
        }

        foreach (Control child in c.Controls)
            ApplyControl(child, p, insideClientGlass, secondaryGlass);
    }


    private static bool IsNeonDarkTheme() =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0) &&
        (string.Equals(CurrentTheme, "Oscuro", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(CurrentTheme, "Graphite", StringComparison.OrdinalIgnoreCase));

    private static bool ShouldUseNeonGrid(DataGridView grid) =>
        !IsLightTheme && (string.Equals(grid.Tag?.ToString(), "CartGridWatermark", StringComparison.Ordinal) || IsNeonDarkTheme());

    private static readonly Color NeonGreen = Color.FromArgb(57, 255, 20);
    private static readonly Color NeonCyan = Color.FromArgb(57, 255, 255);
    private static readonly Color StrongWhite = Color.White;

    private static void ApplyNeonGridStyle(DataGridView grid)
    {
        // En Grafito/Oscuro la selección siempre se lee en verde flúor.
        // Los códigos de barras quedan en naranja flúor y los importes/precios
        // en verde flúor, incluso cuando la fila no está seleccionada.
        grid.DefaultCellStyle.SelectionForeColor = NeonGreen;
        bool isCartGrid = string.Equals(grid.Tag?.ToString(), "CartGridWatermark", StringComparison.Ordinal);
        grid.DefaultCellStyle.SelectionBackColor = BrightnessColor(Color.FromArgb(52, 65, 55));

        foreach (DataGridViewColumn column in grid.Columns)
        {
            var key = $"{column.Name} {column.HeaderText}".ToLowerInvariant();

            if (IsBarcodeColumn(key))
            {
                column.DefaultCellStyle.ForeColor = NeonCyan;
                column.DefaultCellStyle.SelectionForeColor = NeonCyan;
            }
            else if (IsStockColumn(key))
            {
                column.DefaultCellStyle.ForeColor = NeonCyan;
                column.DefaultCellStyle.SelectionForeColor = NeonCyan;
            }
            else if (IsMoneyColumn(key))
            {
                column.DefaultCellStyle.ForeColor = NeonGreen;
                column.DefaultCellStyle.SelectionForeColor = NeonGreen;
            }
            else if (IsDescriptionColumn(key))
            {
                column.DefaultCellStyle.ForeColor = StrongWhite;
                column.DefaultCellStyle.SelectionForeColor = NeonGreen;
            }
            else
            {
                column.DefaultCellStyle.SelectionForeColor = NeonGreen;
            }
        }

        // Una sola suscripción por grilla: la función se desengancha antes de
        // volver a engancharse cuando se reaplica el tema.
        grid.CellFormatting -= NeonGridCellFormatting;
        grid.CellFormatting += NeonGridCellFormatting;
    }

    private static void NeonGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (sender is not DataGridView grid || !ShouldUseNeonGrid(grid) || e.RowIndex < 0 || e.ColumnIndex < 0)
            return;

        var column = grid.Columns[e.ColumnIndex];
        var key = $"{column.Name} {column.HeaderText}".ToLowerInvariant();
        var selected = grid.Rows[e.RowIndex].Selected;

        // Si el contenido es un importe/precio con símbolo de moneda, siempre
        // prevalece el verde flúor. Esto cubre columnas con nombres distintos.
        var formatted = e.Value?.ToString() ?? string.Empty;
        var looksLikeMoney = formatted.Contains('$') || IsMoneyColumn(key);

        if (IsBarcodeColumn(key))
            e.CellStyle.ForeColor = NeonCyan;
        else if (IsStockColumn(key))
            e.CellStyle.ForeColor = NeonCyan;
        else if (looksLikeMoney)
            e.CellStyle.ForeColor = NeonGreen;
        else if (selected)
            e.CellStyle.ForeColor = NeonGreen;
        else if (IsDescriptionColumn(key))
            e.CellStyle.ForeColor = StrongWhite;
    }

    private static bool IsBarcodeColumn(string key)
        => key.Contains("barcode") || key.Contains("código") || key.Contains("codigo") ||
           key.Contains("ean") || key.Contains("upc") || key.Contains("code");

    private static bool IsDescriptionColumn(string key)
        => key.Contains("description") || key.Contains("descripción") || key.Contains("descripcion") ||
           key.Contains("producto") || key.Contains("nombre");

    private static bool IsStockColumn(string key)
        => key.Contains("stock") || key.Contains("stock") || key.Contains("existencia") ||
           key.Contains("disponible") || key.Contains("existencias");

    private static bool IsMoneyColumn(string key)
        => key.Contains("price") || key.Contains("precio") || key.Contains("importe") ||
           key.Contains("amount") || key.Contains("monto") || key.Contains("total") ||
           key.Contains("subtotal") || key.Contains("cost") || key.Contains("costo") ||
           key.Contains("sale") || key.Contains("venta") || key.Contains("deuda") ||
           key.Contains("saldo") || key.Contains("pagado") || key.Contains("payment") ||
           key.Contains("pago") || key.Contains("discount") || key.Contains("descuento");



    private static void ApplyFont(Control root)
    {
        var family = FixedFontFamily;
        var configured = CurrentFontSize;
        foreach (Control c in EnumerateControls(root))
        {
            // Estos dos controles de la pantalla principal tienen tamaño visual
            // fijo para que la configuración general de fuente nunca vuelva a
            // reducir TOTAL A PAGAR ni la barra de código de barras.
            var fixedTag = c.Tag?.ToString();
            if (fixedTag is "FixedMainTotal" or "FixedMainBarcode") continue;

            var old = c.Font;
            var baseSize = old.SizeInPoints;
            var newSize = Math.Clamp(baseSize * (configured / 9.5f), 7f, 30f);
            try { c.Font = new Font(family, newSize, old.Style, GraphicsUnit.Point); } catch { }
        }
    }

    private static IEnumerable<Control> EnumerateControls(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (var child in EnumerateControls(c)) yield return child;
        }
    }

    private static Color Blend(Color a, Color b, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * amount),
            (int)(a.G + (b.G - a.G) * amount),
            (int)(a.B + (b.B - a.B) * amount));
    }

    private static void ButtonMouseEnter(object? sender, EventArgs e)
    {
        if (sender is Button b)
            b.FlatAppearance.BorderSize = 2;
    }

    private static void ButtonMouseLeave(object? sender, EventArgs e)
    {
        if (sender is Button b)
            b.FlatAppearance.BorderSize = 1;
    }

    private static ThemePalette Palette(string name)
    {
        return name switch
        {
            "Claro" => new(Color.FromArgb(246,248,251), Color.White, Color.FromArgb(232,237,243), Color.White, Color.FromArgb(25,30,36), Color.FromArgb(25,30,36), Color.FromArgb(218,232,245), Color.FromArgb(242,245,248), Color.FromArgb(25,30,36), Color.FromArgb(190,201,214)),
            "Ejecutivo Azul" => new(Color.FromArgb(7,17,31),Color.FromArgb(14,27,46),Color.FromArgb(20,38,64),Color.FromArgb(7,17,31),Color.White,Color.FromArgb(66,165,245),Color.FromArgb(35,72,110),Color.FromArgb(18,43,70),Color.White,Color.FromArgb(65,125,175)),
            "Esmeralda" => new(Color.FromArgb(6,19,15),Color.FromArgb(13,33,26),Color.FromArgb(18,48,38),Color.FromArgb(6,19,15),Color.White,Color.FromArgb(0,201,141),Color.FromArgb(30,91,67),Color.FromArgb(14,43,34),Color.White,Color.FromArgb(45,135,105)),
            "Rojo Ferrari" => new(Color.FromArgb(8,2,3),Color.FromArgb(20,8,11),Color.FromArgb(35,13,19),Color.FromArgb(8,2,3),Color.White,Color.FromArgb(255,23,68),Color.FromArgb(90,20,35),Color.FromArgb(28,9,14),Color.White,Color.FromArgb(150,35,55)),
            "Titanium" => new(Color.FromArgb(11,13,15),Color.FromArgb(23,26,30),Color.FromArgb(34,39,45),Color.FromArgb(11,13,15),Color.FromArgb(224,224,224),Color.FromArgb(224,224,224),Color.FromArgb(65,72,80),Color.FromArgb(28,32,37),Color.White,Color.FromArgb(100,108,118)),
            "Black" => new(Color.Black,Color.FromArgb(10,10,10),Color.FromArgb(22,22,22),Color.FromArgb(0,0,0),Color.White,Color.FromArgb(255,59,48),Color.FromArgb(45,45,45),Color.FromArgb(15,15,15),Color.White,Color.FromArgb(75,75,75)),
            "Celeste" => new(Color.FromArgb(8,20,28),Color.FromArgb(13,31,43),Color.FromArgb(20,47,61),Color.FromArgb(8,20,28),Color.White,Color.FromArgb(3,169,244),Color.FromArgb(30,82,105),Color.FromArgb(15,40,53),Color.White,Color.FromArgb(45,130,160)),
            "Cyber Neon" => new(Color.FromArgb(5,1,11),Color.FromArgb(22,7,29),Color.FromArgb(29,13,43),Color.FromArgb(5,1,11),Color.White,Color.FromArgb(255,45,117),Color.FromArgb(65,20,90),Color.FromArgb(28,10,43),Color.White,Color.FromArgb(145,45,180)),
            "Aurora" => new(Color.FromArgb(3,16,15),Color.FromArgb(9,27,24),Color.FromArgb(17,41,37),Color.FromArgb(3,16,15),Color.White,Color.FromArgb(0,229,160),Color.FromArgb(20,82,67),Color.FromArgb(10,34,29),Color.White,Color.FromArgb(30,140,115)),
            "Graphite" => new(Color.FromArgb(9,11,14),Color.FromArgb(22,25,29),Color.FromArgb(35,40,46),Color.FromArgb(9,11,14),Color.White,Color.FromArgb(57,255,255),Color.FromArgb(45,75,82),Color.FromArgb(25,29,34),Color.White,Color.FromArgb(70,160,175)),
            "Quantum" => new(Color.FromArgb(8,4,18),Color.FromArgb(18,10,36),Color.FromArgb(30,18,53),Color.FromArgb(8,4,18),Color.White,Color.FromArgb(138,92,255),Color.FromArgb(65,35,115),Color.FromArgb(25,13,48),Color.White,Color.FromArgb(125,80,200)),
            "Negro Neón" => new(Color.FromArgb(2,3,5),Color.FromArgb(10,13,18),Color.FromArgb(21,27,36),Color.FromArgb(2,3,5),Color.White,Color.FromArgb(57,255,136),Color.FromArgb(20,85,55),Color.FromArgb(12,18,23),Color.White,Color.FromArgb(50,180,110)),
            "Blanco Neón" => new(Color.FromArgb(247,249,252),Color.White,Color.FromArgb(232,237,243),Color.White,Color.FromArgb(35,43,52),Color.FromArgb(0,168,168),Color.FromArgb(205,220,232),Color.FromArgb(242,245,248),Color.FromArgb(35,43,52),Color.FromArgb(150,170,185)),
            "Plateado" => new(Color.FromArgb(11,14,18),Color.FromArgb(27,32,38),Color.FromArgb(40,47,54),Color.FromArgb(11,14,18),Color.FromArgb(232,237,242),Color.FromArgb(232,237,242),Color.FromArgb(70,78,86),Color.FromArgb(30,36,42),Color.White,Color.FromArgb(110,120,130)),
            _ => new(Color.FromArgb(5,6,8),Color.FromArgb(13,15,18),Color.FromArgb(26,29,34),Color.FromArgb(5,6,8),Color.White,Color.FromArgb(255,22,61),Color.FromArgb(60,15,25),Color.FromArgb(18,9,13),Color.White,Color.FromArgb(115,40,55))
        };
    }

    private sealed class ThemePalette
    {
        public Color Window { get; }
        public Color Surface { get; }
        public Color Button { get; }
        public Color Input { get; }
        public Color Text { get; }
        public Color Accent { get; }
        public Color Selection { get; }
        public Color Alternate { get; }
        public Color HeaderText { get; }
        public Color Border { get; }

        public ThemePalette(Color window, Color surface, Color button, Color input, Color text, Color accent, Color selection, Color alternate, Color headerText, Color border)
        {
            Window = BrightnessColor(window);
            Surface = BrightnessColor(surface);
            Button = BrightnessColor(button);
            Input = BrightnessColor(input);
            // El brillo modifica superficies y bordes, no el texto ni los
            // colores de marca/acento, para mantener siempre el contraste.
            Text = text;
            Accent = accent;
            Selection = BrightnessColor(selection);
            Alternate = BrightnessColor(alternate);
            HeaderText = headerText;
            Border = BrightnessColor(border);
        }
    }
}

public class RoundedButton : Button
{
    private int _radius = 10;

    public RoundedButton()
    {
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
        FlatAppearance.BorderSize = 1;
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateRegion();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Region?.Dispose();
        base.Dispose(disposing);
    }

    private void UpdateRegion()
    {
        if (Width <= 0 || Height <= 0) return;
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        var r = Math.Min(_radius * 2, Math.Min(Width, Height));
        path.AddArc(0, 0, r, r, 180, 90);
        path.AddArc(Width - r, 0, r, r, 270, 90);
        path.AddArc(Width - r, Height - r, r, r, 0, 90);
        path.AddArc(0, Height - r, r, r, 90, 90);
        path.CloseFigure();
        Region?.Dispose();
        Region = new Region(path);
    }
}
