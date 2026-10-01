using FerrarisPOS.Services;

namespace FerrarisPOS.Forms;

public sealed class AssignTableForm : Form
{
    public int SelectedTableId { get; private set; }
    private readonly ListBox list = new();
    private readonly IReadOnlyDictionary<int,int> occupied;
    private List<RestaurantTable> tables = new();

    public AssignTableForm(IReadOnlyDictionary<int,int> occupied)
    {
        this.occupied = occupied;
        Text = "Asignar ticket a una mesa";
        Width = 460;
        Height = 560;
        MinimumSize = new Size(430, 500);
        StartPosition = FormStartPosition.CenterParent;
        Padding = new Padding(18);
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = Color.Gainsboro;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        Controls.Add(layout);

        var title = new Label
        {
            Text = "Seleccioná la mesa para enlazar el ticket actual",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 5, 0, 5),
            AutoEllipsis = true
        };
        layout.Controls.Add(title, 0, 0);

        list.Dock = DockStyle.Fill;
        list.Font = new Font("Segoe UI", 12, FontStyle.Regular);
        list.IntegralHeight = false;
        list.HorizontalScrollbar = false;
        list.Margin = new Padding(0, 6, 0, 6);
        list.SelectionMode = SelectionMode.One;
        list.DoubleClick += (_, _) => Accept();
        list.KeyDown += List_KeyDown;
        list.MouseDown += List_MouseDown;

        var contextMenu = new ContextMenuStrip();
        var deleteItem = new ToolStripMenuItem("Eliminar mesa");
        deleteItem.Click += (_, _) => DeleteSelectedTable();
        contextMenu.Items.Add(deleteItem);
        list.ContextMenuStrip = contextMenu;
        layout.Controls.Add(list, 0, 1);

        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 6, 0, 0),
            Margin = Padding.Empty
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var ok = new Button { Text = "ACEPTAR", Dock = DockStyle.Fill, Height = 38 };
        ok.Click += (_, _) => Accept();
        var cancel = new Button { Text = "CANCELAR", Dock = DockStyle.Fill, Height = 38, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(cancel, 0, 0);
        buttons.Controls.Add(ok, 1, 0);
        layout.Controls.Add(buttons, 0, 2);

        ReloadTables();
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void ReloadTables(int preferredIndex = 0)
    {
        tables = TableService.GetTables();
        list.BeginUpdate();
        try
        {
            list.Items.Clear();
            foreach (var t in tables)
            {
                var busy = occupied.ContainsKey(t.Id);
                var reservation = ReservationService.GetTableReservationWithinWindow(t.Id, DateTime.Now, 120);
                if (busy) list.Items.Add($"{t.Name}  ·  OCUPADA");
                else if (reservation is not null) list.Items.Add($"{t.Name}  ·  RESERVADA {reservation.Hour}");
                else list.Items.Add(t.Name);
            }
        }
        finally
        {
            list.EndUpdate();
        }

        if (list.Items.Count > 0)
            list.SelectedIndex = Math.Clamp(preferredIndex, 0, list.Items.Count - 1);
    }

    private void List_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        var index = list.IndexFromPoint(e.Location);
        if (index >= 0 && index < list.Items.Count)
            list.SelectedIndex = index;
    }

    private void List_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Delete) return;
        e.Handled = true;
        e.SuppressKeyPress = true;
        DeleteSelectedTable();
    }

    private void DeleteSelectedTable()
    {
        if (list.SelectedIndex < 0 || list.SelectedIndex >= tables.Count) return;

        var t = tables[list.SelectedIndex];
        if (occupied.ContainsKey(t.Id))
        {
            MessageBox.Show("No podés eliminar esta mesa porque tiene un ticket abierto.", "Mesa ocupada", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"¿Querés eliminar la mesa «{t.Name}» de este salón?\n\nLa mesa dejará de aparecer en esta lista y podrás crear otra cuando quieras.",
            "Eliminar mesa",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (confirm != DialogResult.Yes) return;

        var nextIndex = Math.Max(0, list.SelectedIndex - (list.SelectedIndex == list.Items.Count - 1 ? 1 : 0));
        TableService.DeleteTable(t.Id);
        ReloadTables(nextIndex);
    }

    private void Accept()
    {
        if (list.SelectedIndex < 0 || list.SelectedIndex >= tables.Count) return;
        var t = tables[list.SelectedIndex];
        if (occupied.ContainsKey(t.Id))
        {
            MessageBox.Show("Esa mesa ya tiene un ticket abierto.", "Mesa ocupada", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var reservation = ReservationService.GetTableReservationWithinWindow(t.Id, DateTime.Now, 120);
        if (reservation is not null)
        {
            MessageBox.Show($"La mesa {t.Name} está reservada para las {reservation.Hour}.\n\nEstá dentro del período de protección de 2 horas y no puede asignarse a otra venta.", "Mesa reservada", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        SelectedTableId = t.Id;
        DialogResult = DialogResult.OK;
        Close();
    }
}
