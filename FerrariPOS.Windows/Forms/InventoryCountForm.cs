using FerrarisPOS.Data;
using FerrarisPOS.Services;

namespace FerrarisPOS.Forms;

public class InventoryCountForm : Form
{
    private readonly DataGridView grid = new();
    private readonly TextBox search = new();
    private readonly Label summary = new();
    private readonly Dictionary<int,double> physical = new();

    public InventoryCountForm()
    {
        if (!Session.HasPermission("CONTEO_FISICO"))
        {
            MessageBox.Show("Tu usuario no tiene permiso para realizar el conteo físico. Solicitá al ADMIN que lo habilite desde F8 Usuarios.", "Permiso insuficiente", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
            return;
        }
        Text = "FerrarisPOS - Conteo físico";
        Width = 1300; Height = 760; MinimumSize = new Size(1000,650);
        StartPosition = FormStartPosition.CenterParent;
        Build(); LoadData();
    }

    private void Build()
    {
        Controls.Add(new Label { Text="CONTEO FÍSICO DE INVENTARIO", Location=new Point(20,15), AutoSize=true, Font=new Font("Segoe UI",18,FontStyle.Bold) });
        summary.Location=new Point(20,55); summary.Size=new Size(1180,38); summary.Font=new Font("Segoe UI",11,FontStyle.Bold); Controls.Add(summary);
        search.Location=new Point(20,105); search.Width=360; search.PlaceholderText="Buscar producto..."; search.TextChanged+=(_,_)=>LoadData(); Controls.Add(search);

        var apply = new Button { Text="APLICAR DIFERENCIAS", Location=new Point(980,103), Width=220, Height=38 };
        apply.Click += (_,_)=>ApplyDifferences(); Controls.Add(apply);
        var zero = new Button { Text="CARGAR STOCK TEÓRICO", Location=new Point(745,103), Width=220, Height=38 };
        zero.Click += (_,_)=>LoadData(); Controls.Add(zero);

        grid.Location=new Point(20,155); grid.Size=new Size(1180,535); grid.AllowUserToAddRows=false; grid.AllowUserToDeleteRows=false; grid.RowHeadersVisible=false; grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect; grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill; grid.BackgroundColor=Color.White;
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Id",Visible=false});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Producto",HeaderText="PRODUCTO",FillWeight=34,ReadOnly=true});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Unidad",HeaderText="UNIDAD",FillWeight=10,ReadOnly=true});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Teorico",HeaderText="STOCK SISTEMA",FillWeight=16,ReadOnly=true,DefaultCellStyle=new DataGridViewCellStyle{Format="N3",Alignment=DataGridViewContentAlignment.MiddleRight}});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Fisico",HeaderText="CONTEO FÍSICO",FillWeight=16,DefaultCellStyle=new DataGridViewCellStyle{Format="N3",Alignment=DataGridViewContentAlignment.MiddleRight}});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Diferencia",HeaderText="DIFERENCIA",FillWeight=16,ReadOnly=true,DefaultCellStyle=new DataGridViewCellStyle{Format="N3",Alignment=DataGridViewContentAlignment.MiddleRight}});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Estado",HeaderText="ESTADO",FillWeight=12,ReadOnly=true});
        grid.CellValueChanged += (_,e)=>{if(e.RowIndex>=0 && e.ColumnIndex==4) RecalcRow(e.RowIndex);};
        grid.CurrentCellDirtyStateChanged += (_,_)=>{if(grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};
        Controls.Add(grid);
    }

    private void LoadData()
    {
        grid.Rows.Clear(); physical.Clear();
        foreach(var p in ProductService.Search(search.Text))
        {
            int row=grid.Rows.Add(p.Id,p.Description,p.Unit,p.Stock,p.Stock,p.Stock==0?"OK":"PENDIENTE");
            grid.Rows[row].Cells[5].Value=0d;
            grid.Rows[row].Cells[6].Value="SIN CAMBIO";
            physical[p.Id]=p.Stock;
        }
        UpdateSummary();
    }

    private void RecalcRow(int row)
    {
        if(row<0 || row>=grid.Rows.Count) return;
        var id=Convert.ToInt32(grid.Rows[row].Cells[0].Value);
        var theoretical=ToDouble(grid.Rows[row].Cells[3].Value);
        var counted=ToDouble(grid.Rows[row].Cells[4].Value);
        var diff=counted-theoretical;
        grid.Rows[row].Cells[5].Value=diff;
        grid.Rows[row].Cells[6].Value=Math.Abs(diff)<0.000001?"SIN CAMBIO":(diff>0?"SOBRA STOCK":"FALTA STOCK");
        physical[id]=counted;
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        double diff=0; int changes=0;
        foreach(DataGridViewRow r in grid.Rows){ if(r.IsNewRow) continue; var d=ToDouble(r.Cells[5].Value); diff+=d; if(Math.Abs(d)>0.000001) changes++; }
        summary.Text=$"PRODUCTOS: {grid.Rows.Count}   ·   DIFERENCIA NETA: {diff:N3}   ·   PRODUCTOS A AJUSTAR: {changes}";
    }

    private void ApplyDifferences()
    {
        if (!InventoryControlService.IsGlobalEnabled)
        {
            MessageBox.Show("El inventario global está deshabilitado. Los contadores actuales están congelados y no pueden modificarse.", "Inventario deshabilitado", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var changes=new List<(int id,double diff,string name)>();
        foreach(DataGridViewRow r in grid.Rows)
        {
            if(r.IsNewRow) continue;
            var diff=ToDouble(r.Cells[5].Value); if(Math.Abs(diff)<0.000001) continue;
            changes.Add((Convert.ToInt32(r.Cells[0].Value),diff,r.Cells[1].Value?.ToString()??"Producto"));
        }
        if(changes.Count==0){MessageBox.Show("No hay diferencias para aplicar.","Conteo físico",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
        if(MessageBox.Show($"Se ajustarán {changes.Count} productos según el conteo físico.\n\nLos movimientos quedarán registrados como CONTEO_FISICO.","Confirmar conteo",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
        try
        {
            foreach(var c in changes) ProductService.AdjustStock(c.id,c.diff,"CONTEO_FISICO",$"Conteo físico {DateTime.Now:yyyy-MM-dd HH:mm} - {c.name}",Session.UserId);
            MessageBox.Show("Conteo aplicado correctamente. El stock del sistema quedó sincronizado con el conteo físico.","Inventario",MessageBoxButtons.OK,MessageBoxIcon.Information);
            LoadData();
        }
        catch(Exception ex){MessageBox.Show(ex.Message,"Error",MessageBoxButtons.OK,MessageBoxIcon.Error);}
    }

    private static double ToDouble(object? value)=>double.TryParse(Convert.ToString(value)?.Replace(',','.'),System.Globalization.NumberStyles.Any,System.Globalization.CultureInfo.InvariantCulture,out var n)?n:0;
}
