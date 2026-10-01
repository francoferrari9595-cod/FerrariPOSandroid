using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace FerrarisPOS.Forms;

/// <summary>
/// Mesa 3D dibujada directamente por GDI+. No usa un rectángulo de fondo ni un
/// PNG fijo: se adapta al ancho/alto configurado y calcula las sillas según la
/// capacidad de la mesa. Esto permite estirar/alargar la mesa sin deformar la
/// cantidad ni la distribución de sillas.
/// </summary>
internal sealed class NeonTableControl : Control
{
    public string DisplayText { get; set; } = "MESA";
    public string Shape { get; set; } = "RECTANGLE";
    public int Capacity { get; set; } = 4;
    public bool Occupied { get; set; }

    private Color Accent => Occupied ? Color.FromArgb(255, 58, 76) : Color.FromArgb(57, 255, 255);

    public NeonTableControl()
    {
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (ClientSize.Width < 20 || ClientSize.Height < 20) return;

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingMode = CompositingMode.SourceOver;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        var w = ClientSize.Width;
        var h = ClientSize.Height;
        var chairCount = Math.Clamp(Capacity, 1, 24);

        // Dejar espacio real para las sillas. El tablero crece/encoge con el control.
        var tableRect = new RectangleF(
            Math.Max(10, w * .16f),
            Math.Max(10, h * .18f),
            Math.Max(20, w * .68f),
            Math.Max(20, h * .56f));

        DrawGlow(g, tableRect);
        DrawChairs(g, tableRect, chairCount);
        DrawTable(g, tableRect);
        DrawText(g, tableRect);
    }

    private void DrawGlow(Graphics g, RectangleF rect)
    {
        for (int i = 6; i >= 1; i--)
        {
            var grow = i * 2.2f;
            var r = RectangleF.Inflate(rect, grow, grow);
            using var path = BuildShape(r);
            using var brush = new SolidBrush(Color.FromArgb(Math.Max(2, 7 - i), Accent));
            g.FillPath(brush, path);
        }
    }

    private void DrawTable(Graphics g, RectangleF rect)
    {
        var extrusion = Math.Clamp(Math.Min(rect.Width, rect.Height) * .09f, 4f, 14f);

        // Sombra blanda desplazada: no hay borde rectangular externo.
        for (int i = 7; i >= 1; i--)
        {
            var shadow = rect;
            shadow.Offset(0, extrusion + i * 1.3f);
            shadow.Inflate(i * 1.5f, i * .8f);
            using var p = BuildShape(shadow);
            using var b = new SolidBrush(Color.FromArgb(Math.Max(3, 17 - i * 2), Color.Black));
            g.FillPath(b, p);
        }

        // Cuerpo inferior 3D / espesor.
        for (int layer = 5; layer >= 1; layer--)
        {
            var body = rect;
            body.Offset(0, extrusion * layer / 5f);
            using var p = BuildShape(body);
            using var b = new LinearGradientBrush(body,
                Color.FromArgb(30, 42, 48),
                Color.FromArgb(8, 14, 19),
                LinearGradientMode.Vertical);
            g.FillPath(b, p);
        }

        // Superficie principal: grafito profundo con iluminación cian/roja integrada.
        using (var p = BuildShape(rect))
        using (var b = new LinearGradientBrush(rect,
                   Color.FromArgb(52, 66, 73),
                   Color.FromArgb(10, 16, 21),
                   LinearGradientMode.Vertical))
            g.FillPath(b, p);

        // Reflejo superior suave, siguiendo la forma de la mesa.
        var highlight = RectangleF.Inflate(rect, -rect.Width * .035f, -rect.Height * .035f);
        highlight.Height *= .36f;
        using (var p = BuildShape(highlight))
        using (var b = new LinearGradientBrush(highlight,
                   Color.FromArgb(42, Accent),
                   Color.FromArgb(0, Accent),
                   LinearGradientMode.Vertical))
            g.FillPath(b, p);

        // Brillo interior: relleno, no delineado. La mesa queda limpia y sin marco.
        var inner = RectangleF.Inflate(rect, -3f, -3f);
        using (var p = BuildShape(inner))
        using (var pen = new Pen(Color.FromArgb(34, Accent), 2.0f))
            g.DrawPath(pen, p);

        // Punto de luz inferior para reforzar volumen 3D.
        var lightRect = new RectangleF(rect.Left + rect.Width * .18f,
            rect.Bottom - rect.Height * .18f,
            rect.Width * .64f,
            rect.Height * .07f);
        using var light = new LinearGradientBrush(lightRect,
            Color.FromArgb(0, Accent), Color.FromArgb(45, Accent), LinearGradientMode.Horizontal);
        g.FillEllipse(light, lightRect);
    }

    private void DrawChairs(Graphics g, RectangleF table, int count)
    {
        if (count <= 0) return;
        var chairW = Math.Clamp(Math.Min(ClientSize.Width, ClientSize.Height) * .16f, 12f, 34f);
        var chairH = Math.Clamp(chairW * .62f, 8f, 22f);
        var chairOffset = Math.Max(3f, Math.Min(ClientSize.Width, ClientSize.Height) * .025f);

        var positions = new List<PointF>(count);
        bool oval = string.Equals(Shape, "OVAL", StringComparison.OrdinalIgnoreCase);
        bool round = string.Equals(Shape, "ROUND", StringComparison.OrdinalIgnoreCase);

        // Mesas redondas/ovales: distribución radial. Rectangulares: primero lados largos,
        // luego laterales, hasta completar la capacidad indicada.
        if (oval || round)
        {
            var cx = table.Left + table.Width / 2f;
            var cy = table.Top + table.Height / 2f;
            var rx = table.Width / 2f + chairOffset + chairW * .25f;
            var ry = table.Height / 2f + chairOffset + chairH * .25f;
            for (int i = 0; i < count; i++)
            {
                var angle = -MathF.PI / 2f + i * (MathF.PI * 2f / count);
                positions.Add(new PointF(cx + MathF.Cos(angle) * rx, cy + MathF.Sin(angle) * ry));
            }
        }
        else
        {
            int top = (count + 1) / 2;
            int bottom = count / 2;
            for (int i = 0; i < top; i++)
            {
                var x = table.Left + table.Width * (i + 1f) / (top + 1f);
                positions.Add(new PointF(x, table.Top - chairOffset - chairH / 2f));
            }
            for (int i = 0; i < bottom; i++)
            {
                var x = table.Left + table.Width * (i + 1f) / (bottom + 1f);
                positions.Add(new PointF(x, table.Bottom + chairOffset + chairH / 2f));
            }
        }

        foreach (var center in positions.Take(count))
            DrawChair(g, center, chairW, chairH);
    }

    private void DrawChair(Graphics g, PointF center, float width, float height)
    {
        var rect = new RectangleF(center.X - width / 2f, center.Y - height / 2f, width, height);
        rect.X = Math.Clamp(rect.X, 2, Math.Max(2, ClientSize.Width - rect.Width - 2));
        rect.Y = Math.Clamp(rect.Y, 2, Math.Max(2, ClientSize.Height - rect.Height - 2));

        var shadow = rect;
        shadow.Offset(0, 2.5f);
        using (var path = RoundedRect(shadow, Math.Min(height * .35f, 7f)))
        using (var brush = new SolidBrush(Color.FromArgb(55, Color.Black)))
            g.FillPath(brush, path);

        using (var path = RoundedRect(rect, Math.Min(height * .35f, 7f)))
        using (var brush = new LinearGradientBrush(rect,
                   Color.FromArgb(45, Accent),
                   Color.FromArgb(10, 22, 27),
                   LinearGradientMode.Vertical))
            g.FillPath(brush, path);

        var seat = new RectangleF(rect.Left + 2, rect.Top + 1, Math.Max(2, rect.Width - 4), Math.Max(2, rect.Height * .45f));
        using var seatBrush = new SolidBrush(Color.FromArgb(35, Accent));
        g.FillEllipse(seatBrush, seat);
    }

    private void DrawText(Graphics g, RectangleF table)
    {
        if (string.IsNullOrWhiteSpace(DisplayText)) return;
        var fontSize = Math.Clamp(Math.Min(ClientSize.Width, ClientSize.Height) / 15f, 7f, 13f);
        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Point);
        using var brush = new SolidBrush(Color.FromArgb(235, Accent));
        using var shadow = new SolidBrush(Color.FromArgb(150, Color.Black));
        var area = new RectangleF(table.Left + 4, table.Bottom - Math.Max(30, table.Height * .28f), Math.Max(1, table.Width - 8), Math.Max(22, table.Height * .24f));
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        var shadowArea = area; shadowArea.Offset(0, 1.5f);
        g.DrawString(DisplayText, font, shadow, shadowArea, fmt);
        g.DrawString(DisplayText, font, brush, area, fmt);
    }

    private GraphicsPath BuildShape(RectangleF rect)
    {
        if (string.Equals(Shape, "OVAL", StringComparison.OrdinalIgnoreCase))
        {
            var p = new GraphicsPath();
            p.AddEllipse(rect);
            return p;
        }
        var radius = string.Equals(Shape, "ROUND", StringComparison.OrdinalIgnoreCase)
            ? Math.Min(rect.Width, rect.Height) * .45f
            : Math.Min(rect.Width, rect.Height) * .18f;
        return RoundedRect(rect, Math.Max(6f, radius));
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2f);
        var d = radius * 2f;
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
