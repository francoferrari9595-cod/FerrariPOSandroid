using FerrarisPOS.Data;
using FerrarisPOS.Services;
using System.Data;

namespace FerrarisPOS.Forms;

public class AuditForm : Form
{
    private readonly DataGridView grid = new();
    private readonly TextBox filter = new();
    public AuditForm()
    {
        Text = "FerrarisPOS · Auditoría"; Width = 1100; Height = 680; MinimumSize = new Size(900, 560);
        StartPosition = FormStartPosition.CenterParent; Build(); LoadData(); ThemeService.Apply(this);
    }
    private void Build()
    {
        var top = new Panel { Dock = DockStyle.Top, Height = 48, Padding = new Padding(8) };
        filter.Dock = DockStyle.Left; filter.Width = 420; filter.PlaceholderText = "Buscar acción, módulo, usuario o detalle...";
        var refresh = new Button { Text = "ACTUALIZAR", Dock = DockStyle.Right, Width = 120 }; refresh.Click += (_, _) => LoadData();
        top.Controls.Add(filter); top.Controls.Add(refresh); Controls.Add(top);
        grid.Dock = DockStyle.Fill; grid.ReadOnly = true; grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill; grid.RowHeadersVisible = false; Controls.Add(grid);
        filter.TextChanged += (_, _) => LoadData();
    }
    private void LoadData()
    {
        using var cn = Database.Open(); using var q = cn.CreateCommand();
        q.CommandText = "SELECT a.created_at AS Fecha, COALESCE(u.full_name,u.username,'SISTEMA') AS Usuario, a.module AS Módulo, a.action AS Acción, a.details AS Detalle FROM audit_log a LEFT JOIN users u ON u.id=a.user_id WHERE ($q='') OR u.full_name LIKE $like OR u.username LIKE $like OR a.module LIKE $like OR a.action LIKE $like OR a.details LIKE $like ORDER BY a.id DESC LIMIT 1000";
        q.Parameters.AddWithValue("$q", filter.Text.Trim()); q.Parameters.AddWithValue("$like", "%" + filter.Text.Trim() + "%");
        using var r = q.ExecuteReader(); var dt = new DataTable(); dt.Load(r); grid.DataSource = dt;
    }
}
