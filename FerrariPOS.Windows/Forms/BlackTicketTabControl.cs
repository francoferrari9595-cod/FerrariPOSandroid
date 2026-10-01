using System.Drawing;
using System.Windows.Forms;

namespace FerrarisPOS.Forms;

/// <summary>
/// TabControl for sales/tickets. Windows Forms paints the unused part of the
/// native tab header with a system/visual-style color. This control repaints
/// that unused header area black after the native paint, while leaving the
/// actual tab pages and owner-drawn tabs intact.
/// </summary>
public sealed class BlackTicketTabControl : TabControl
{
    private bool blackHeader = true;

    public BlackTicketTabControl()
    {
        BackColor = Color.Black;
        ForeColor = Color.Gainsboro;
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        SetStyle(ControlStyles.ResizeRedraw, true);
        ItemSize = new Size(112, 28);
        SizeMode = TabSizeMode.Fixed;
        Multiline = false;
        Padding = new Point(0, 0);
    }

    public void SetHeaderBlackMode(bool enabled)
    {
        blackHeader = enabled;
        BackColor = blackHeader ? Color.Black : Color.FromArgb(255, 255, 255);
        ForeColor = blackHeader ? Color.Gainsboro : Color.FromArgb(35, 43, 52);
        foreach (TabPage page in TabPages) ApplyPageTheme(page);
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (blackHeader)
        {
            e.Graphics.Clear(Color.Black);
            return;
        }
        base.OnPaintBackground(e);
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (e.Control is TabPage page)
            ApplyPageTheme(page);
    }

    protected override void OnCreateControl()
    {
        base.OnCreateControl();
        foreach (TabPage page in TabPages)
            ApplyPageTheme(page);
    }

    private void ApplyPageTheme(TabPage page)
    {
        page.BackColor = blackHeader ? Color.Black : Color.FromArgb(255, 255, 255);
        page.ForeColor = blackHeader ? Color.Gainsboro : Color.FromArgb(35, 43, 52);
        page.BorderStyle = BorderStyle.None;
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_PAINT = 0x000F;
        const int WM_ERASEBKGND = 0x0014;

        if (blackHeader && m.Msg == WM_ERASEBKGND)
        {
            m.Result = (IntPtr)1;
            return;
        }

        // No modificamos TCM_ADJUSTRECT. En esta barra las TabPages solo se usan
        // como contenedores de los nombres de las ventas; el contenido real
        // (DataGridView) está fuera del TabControl. Manipular RECT aquí provoca
        // errores de compilación en algunas versiones del SDK y puede generar
        // una franja gris/blanca debajo de las pestañas.

        base.WndProc(ref m);

        if (blackHeader && m.Msg == WM_PAINT && IsHandleCreated && Width > 0 && Height > 0)
        {
            using var g = Graphics.FromHwnd(Handle);
            var headerBottom = GetHeaderBottom();
            if (headerBottom > 0)
            {
                // Only cover the unused header area to the right of the last tab.
                // The owner-drawn tab itself is never overwritten here.
                var right = 0;
                for (var i = 0; i < TabPages.Count; i++)
                    right = Math.Max(right, GetTabRect(i).Right);

                using var brush = new SolidBrush(Color.Black);

                // Todo lo que no pertenece a una pestaña debe ser NEGRO.
                // Esto incluye especialmente la franja inferior que Windows
                // puede dejar con el color del tema visual.
                if (right < ClientSize.Width)
                    g.FillRectangle(brush, right, 0, ClientSize.Width - right, ClientSize.Height);

                if (headerBottom < ClientSize.Height)
                    g.FillRectangle(brush, 0, headerBottom, ClientSize.Width, ClientSize.Height - headerBottom);
            }
        }
    }

    private int GetHeaderBottom()
    {
        if (TabPages.Count > 0)
        {
            var r = GetTabRect(0);
            return Math.Min(ClientSize.Height, Math.Max(1, r.Bottom));
        }
        return Math.Min(Height, ItemSize.Height + 2);
    }
}
