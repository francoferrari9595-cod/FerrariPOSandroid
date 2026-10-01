using FerrarisPOS.Data;

namespace FerrarisPOS.Services;

public static class DisplayService
{
    public static readonly (string Name, int Width, int Height)[] Resolutions =
    {
        ("AUTOMÁTICA · RESOLUCIÓN NATIVA", 0, 0),
        ("1024 × 768 (Clásica)", 1024, 768),
        ("1280 × 720 (HD / Widescreen)", 1280, 720),
        ("1280 × 1024 (SXGA)", 1280, 1024),
        ("1366 × 768 (Widescreen)", 1366, 768),
        ("1440 × 900 (Widescreen)", 1440, 900),
        ("1600 × 900 (HD+ / Widescreen)", 1600, 900),
        ("1680 × 1050 (Widescreen)", 1680, 1050),
        ("1920 × 1080 (Full HD / Widescreen)", 1920, 1080),
        ("1920 × 1200 (WUXGA)", 1920, 1200)
    };

    public static (int Width, int Height) GetNativeResolution()
    {
        try
        {
            var screen = Screen.PrimaryScreen;
            if (screen != null)
                return (screen.Bounds.Width, screen.Bounds.Height);
        }
        catch { }

        return (1366, 768);
    }

    public static void Apply(Form form)
    {
        var fullscreen = Database.GetSetting("fullscreen", "0") == "1";
        var resolution = Database.GetSetting("resolution", "auto");

        if (fullscreen)
        {
            form.StartPosition = FormStartPosition.CenterScreen;
            form.FormBorderStyle = FormBorderStyle.None;
            form.WindowState = FormWindowState.Maximized;
            return;
        }

        form.FormBorderStyle = FormBorderStyle.Sizable;
        form.WindowState = FormWindowState.Normal;
        form.StartPosition = FormStartPosition.CenterScreen;

        int w;
        int h;

        if (string.IsNullOrWhiteSpace(resolution) ||
            resolution.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            (w, h) = GetNativeResolution();
        }
        else
        {
            var parts = resolution.Split('x');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out w) &&
                int.TryParse(parts[1], out h))
            {
                // Validación básica para evitar una ventana imposible de usar.
                w = Math.Max(1000, w);
                h = Math.Max(650, h);
            }
            else
            {
                (w, h) = GetNativeResolution();
            }
        }

        // El programa se adapta al monitor, sin salir de su área de trabajo.
        var screen = Screen.PrimaryScreen?.WorkingArea;
        if (screen != null)
        {
            w = Math.Min(w, screen.Value.Width);
            h = Math.Min(h, screen.Value.Height);
        }

        form.ClientSize = new Size(w, h);
    }
}
