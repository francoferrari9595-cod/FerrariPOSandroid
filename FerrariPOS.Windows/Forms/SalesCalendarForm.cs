using FerrarisPOS.Data;

namespace FerrarisPOS.Forms;

public sealed class SalesCalendarForm : Form
{
    private readonly MonthCalendar calendar = new() { MaxSelectionCount = 1 };
    private readonly DataGridView grid = new();
    private readonly Label summary = new();

    public SalesCalendarForm(DateTime initial)
    {
        Text = "FerrarisPOS · Calendario de ventas";
        Width = 980;
        Height = 650;
        StartPosition = FormStartPosition.CenterParent;
        calendar.SetDate(initial);
        Build();
        LoadDay(initial);
    }

    private void Build()
    {
        Controls.Add(new Label { Text = "CALENDARIO DE VENTAS", Location = new Point(20, 18), AutoSize = true, Font = new Font("Segoe UI", 18, FontStyle.Bold) });
        Controls.Add(new Label { Text = "Elegí cualquier día para ver qué se vendió y cuánto se recaudó.", Location = new Point(20, 52), AutoSize = true, ForeColor = Color.DimGray });

        calendar.Location = new Point(20, 90);
        calendar.DateSelected += (_, e) => LoadDay(e.Start);
        Controls.Add(calendar);

        summary.Location = new Point(285, 90);
        summary.Size = new Size(650, 70);
        summary.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        Controls.Add(summary);

        grid.Location = new Point(285, 175);
        grid.Size = new Size(650, 390);
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AutoGenerateColumns = true;
        grid.BackgroundColor = Color.White;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        Controls.Add(grid);
    }

    private void LoadDay(DateTime day)
    {
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            SELECT s.ticket_no AS Ticket,
                   substr(datetime(s.created_at,'localtime'),1,16) AS Hora,
                   COALESCE(c.name,'Consumidor Final') AS Cliente,
                   ROUND(s.total,2) AS Total,
                   COALESCE(s.payment_method,'') AS Medio
            FROM sales s
            LEFT JOIN customers c ON c.id=s.customer_id
            WHERE date(s.created_at,'localtime')=$d AND s.status='COMPLETED'
            ORDER BY s.id DESC
            """;
        cmd.Parameters.AddWithValue("$d", day.ToString("yyyy-MM-dd"));
        using var r = cmd.ExecuteReader();
        var dt = new System.Data.DataTable();
        dt.Load(r);
        grid.DataSource = dt;

        using var q = cn.CreateCommand();
        q.CommandText = "SELECT COUNT(*),COALESCE(SUM(total),0) FROM sales WHERE date(created_at,'localtime')=$d AND status='COMPLETED'";
        q.Parameters.AddWithValue("$d", day.ToString("yyyy-MM-dd"));
        using var rr = q.ExecuteReader();
        rr.Read();
        summary.Text = $"{day:dddd dd/MM/yyyy}\nTickets: {rr.GetInt32(0):N0} · Total vendido: ${rr.GetDouble(1):N2}";
    }
}
