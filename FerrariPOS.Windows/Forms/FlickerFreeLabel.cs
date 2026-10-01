using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace FerrarisPOS.Forms;

/// <summary>
/// Label de alto contraste para importes principales del POS.
/// El volumen 3D se genera completamente por código y se dibuja dentro
/// del mismo control existente, sin imágenes ni recursos externos.
/// </summary>
public sealed class FlickerFreeLabel : Label
{
    public bool Use3DText { get; set; }

    public FlickerFreeLabel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer |
                  ControlStyles.AllPaintingInWmPaint |
                  ControlStyles.UserPaint, true);
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (!Use3DText || string.IsNullOrWhiteSpace(Text))
        {
            base.OnPaint(e);
            return;
        }

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var text = Text ?? string.Empty;
        // Área de seguridad REAL del control. No usamos mínimos artificiales
        // (como 160x30) porque durante un reacomodo del formulario el Label
        // puede medir temporalmente menos y el texto terminaría fuera de él.
        var available = new RectangleF(
            ClientRectangle.Left + Padding.Left + 2,
            ClientRectangle.Top + Padding.Top + 2,
            Math.Max(1f, ClientRectangle.Width - Padding.Horizontal - 4f),
            Math.Max(1f, ClientRectangle.Height - Padding.Vertical - 4f));

        const float depth = 3.2f;
        const float safety = 5f;
        const float minHorizontalScale = 0.58f;
        const float minFontSize = 12f;

        // Construye la geometría con el tamaño actual y, si hace falta,
        // reduce fuente + escala horizontal hasta que TODO el texto entre.
        // Esto hace que el TOTAL nunca pueda salirse ni quedar cortado,
        // incluso después de muchas ventas o cambios de tamaño/DPI.
        GraphicsPath? path = null;
        RectangleF bounds = RectangleF.Empty;
        float horizontalScale = 1f;
        float fontSize = Math.Max(minFontSize, Font.SizeInPoints);

        for (var size = fontSize; size >= minFontSize - 0.01f; size -= 0.5f)
        {
            using var candidate = new GraphicsPath();
            var emSize = size * g.DpiY / 72f;
            candidate.AddString(text, Font.FontFamily, (int)Font.Style, emSize,
                new PointF(0, 0), StringFormat.GenericTypographic);
            var b = candidate.GetBounds();
            if (b.Width <= 0 || b.Height <= 0) continue;

            var usableWidth = Math.Max(1f, available.Width - depth - safety);
            var usableHeight = Math.Max(1f, available.Height - depth - safety);
            var scaleForWidth = usableWidth / b.Width;
            var scaleForHeight = usableHeight / b.Height;

            // Prefer mantener la geometría normal. Solo comprimimos si el
            // importe es demasiado largo; nunca permitimos una geometría que
            // exceda el área útil.
            var scale = Math.Min(1f, scaleForWidth);
            if (scale >= minHorizontalScale && b.Height <= usableHeight)
            {
                path = (GraphicsPath)candidate.Clone();
                bounds = b;
                horizontalScale = scale;
                fontSize = size;
                break;
            }

            // Si la altura es el límite, el siguiente tamaño menor será probado.
            _ = scaleForHeight;
        }

        // Última garantía: usa el tamaño mínimo y la escala necesaria. Aun en
        // una resolución extremadamente pequeña, se prioriza que el texto
        // permanezca completamente visible antes que conservar tamaño.
        if (path is null)
        {
            path = new GraphicsPath();
            var emSize = minFontSize * g.DpiY / 72f;
            path.AddString(text, Font.FontFamily, (int)Font.Style, emSize,
                new PointF(0, 0), StringFormat.GenericTypographic);
            bounds = path.GetBounds();
            var usableWidth = Math.Max(1f, available.Width - depth - safety);
            horizontalScale = Math.Min(1f, usableWidth / Math.Max(1f, bounds.Width));
            horizontalScale = Math.Max(0.20f, horizontalScale);
            fontSize = minFontSize;
        }

        // Si la fuente elegida no alcanza por ancho, comprime solo la geometría.
        if (horizontalScale < 0.999f)
        {
            using var scale = new Matrix();
            scale.Scale(horizontalScale, 1f, MatrixOrder.Append);
            path.Transform(scale);
            bounds = path.GetBounds();
        }

        var contentWidth = bounds.Width;
        var contentHeight = bounds.Height;
        var x = available.Left + (available.Width - contentWidth - depth) / 2f - bounds.Left;
        var y = available.Top + (available.Height - contentHeight - depth) / 2f - bounds.Top;

        // Clamps de seguridad para que ni la cara ni la extrusión puedan
        // abandonar el rectángulo visible del Label.
        x = Math.Max(available.Left - bounds.Left,
            Math.Min(x, available.Right - contentWidth - depth - bounds.Left));
        y = Math.Max(available.Top - bounds.Top,
            Math.Min(y, available.Bottom - contentHeight - depth - bounds.Top));

        // Clip final al área útil. La geometría ya fue ajustada para entrar;
        // este clip es únicamente una última barrera ante redondeos de píxel.
        var oldClip = g.Clip;
        g.SetClip(available, CombineMode.Intersect);

        var faceColor = ForeColor.A > 0 ? ForeColor : Color.FromArgb(80, 245, 255);
        var dark = Darken(faceColor, 0.72f);
        var mid = Darken(faceColor, 0.30f);
        var bright = Lighten(faceColor, 0.48f);

        using (var glow = new Pen(Color.FromArgb(38, faceColor.R, faceColor.G, faceColor.B), 3.8f))
        using (var glowPath = (GraphicsPath)path.Clone())
        {
            using var matrix = new Matrix();
            matrix.Translate(x, y);
            glowPath.Transform(matrix);
            g.DrawPath(glow, glowPath);
        }

        using (var shadow = new SolidBrush(Color.FromArgb(145, 0, 0, 0)))
        using (var shadowPath = (GraphicsPath)path.Clone())
        {
            using var matrix = new Matrix();
            matrix.Translate(x + 4f, y + 4.5f);
            shadowPath.Transform(matrix);
            g.FillPath(shadow, shadowPath);
        }

        for (var layer = depth; layer >= 0.7f; layer -= 0.65f)
        {
            using var extrusionPath = (GraphicsPath)path.Clone();
            using var matrix = new Matrix();
            matrix.Translate(x + layer, y + layer);
            extrusionPath.Transform(matrix);

            var t = 1f - layer / depth;
            using var extrusionBrush = new SolidBrush(Color.FromArgb(
                238,
                Math.Max(0, dark.R + (int)((mid.R - dark.R) * t)),
                Math.Max(0, dark.G + (int)((mid.G - dark.G) * t)),
                Math.Max(0, dark.B + (int)((mid.B - dark.B) * t))));
            g.FillPath(extrusionBrush, extrusionPath);
        }

        using var face = (GraphicsPath)path.Clone();
        using (var matrix = new Matrix())
        {
            matrix.Translate(x, y);
            face.Transform(matrix);
        }

        using (var fill = new LinearGradientBrush(
                   new RectangleF(x, y, Math.Max(1, contentWidth), Math.Max(1, contentHeight)),
                   bright, mid, LinearGradientMode.Vertical))
        using (var outline = new Pen(Color.FromArgb(238, Lighten(faceColor, 0.18f)), 1.1f))
        {
            g.FillPath(fill, face);
            g.DrawPath(outline, face);
        }

        using var highlight = new Pen(Color.FromArgb(145, 235, 255, 255), 0.7f);
        using var highlightPath = (GraphicsPath)face.Clone();
        using (var hm = new Matrix())
        {
            hm.Translate(0f, -0.55f);
            highlightPath.Transform(hm);
        }
        g.DrawPath(highlight, highlightPath);

        g.Clip = oldClip;
        path.Dispose();
    }

    private static Color Lighten(Color c, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(c.A,
            c.R + (int)((255 - c.R) * amount),
            c.G + (int)((255 - c.G) * amount),
            c.B + (int)((255 - c.B) * amount));
    }

    private static Color Darken(Color c, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(c.A,
            (int)(c.R * (1f - amount)),
            (int)(c.G * (1f - amount)),
            (int)(c.B * (1f - amount)));
    }
}
