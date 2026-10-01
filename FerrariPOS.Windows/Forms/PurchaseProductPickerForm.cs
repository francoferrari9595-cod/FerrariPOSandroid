using FerrarisPOS.Services;
using FerrarisPOS.Data;
namespace FerrarisPOS.Forms;
public sealed class PurchaseProductPickerForm:Form{
 public sealed record Pick(int Id,string Description,double Stock,double Qty,double Cost);
 public List<Pick> Selected{get;private set;}=new(); private readonly int supplierId; private readonly DataGridView grid=new(); private readonly TextBox search=new();
 public PurchaseProductPickerForm(int supplierId){this.supplierId=supplierId;Text="Seleccionar productos";Width=1000;Height=600;StartPosition=FormStartPosition.CenterParent;Build();LoadGrid();ThemeService.Apply(this);}
 void Build(){Controls.Add(new Label{Text="PRODUCTOS DEL PROVEEDOR",Location=new Point(20,18),AutoSize=true,Font=new Font("Segoe UI",15,FontStyle.Bold)});search.Location=new Point(20,52);search.Width=350;search.PlaceholderText="Buscar producto...";search.TextChanged+=(_,_)=>LoadGrid();Controls.Add(search);grid.Location=new Point(20,90);grid.Size=new Size(940,410);grid.AllowUserToAddRows=false;grid.RowHeadersVisible=false;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.MultiSelect=false;grid.AutoGenerateColumns=false;grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;grid.Columns.Add(new DataGridViewCheckBoxColumn{Name="Sel",HeaderText="",FillWeight=6});grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Id",Visible=false});grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Prod",HeaderText="PRODUCTO",ReadOnly=true,FillWeight=42});grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Stock",HeaderText="STOCK",ReadOnly=true,FillWeight=15});grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Qty",HeaderText="CANTIDAD",FillWeight=15});grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Cost",HeaderText="COSTO",FillWeight=18});
grid.DataError += (_, e) => { e.ThrowException = false; e.Cancel = true; };
Controls.Add(grid);var ok=new Button{Text="AGREGAR SELECCIONADOS",Location=new Point(20,515),Width=210,Height=38};ok.Click+=(_,_)=>Accept();Controls.Add(ok);var no=new Button{Text="CANCELAR",Location=new Point(240,515),Width=110,Height=38};no.Click+=(_,_)=>Close();Controls.Add(no);}
 void LoadGrid(){grid.Rows.Clear();using var cn=Database.Open();using var c=cn.CreateCommand();c.CommandText="SELECT p.id,p.description,p.stock,COALESCE(sp.unit_cost,p.cost_price) FROM products p JOIN supplier_products sp ON sp.product_id=p.id AND sp.supplier_id=$sid WHERE p.active=1 AND ($q='' OR p.description LIKE $like OR p.barcode LIKE $like) ORDER BY p.description";c.Parameters.AddWithValue("$sid",supplierId);var q=search.Text.Trim();c.Parameters.AddWithValue("$q",q);c.Parameters.AddWithValue("$like","%"+q+"%");using var r=c.ExecuteReader();while(r.Read())grid.Rows.Add(false,r.GetInt32(0),r.GetString(1),r.GetDouble(2),1,r.GetDouble(3));}
 double ParseNumber(object? value)
 {
  var raw=Convert.ToString(value)?.Trim()??"";
  if(double.TryParse(raw,System.Globalization.NumberStyles.Any,System.Globalization.CultureInfo.CurrentCulture,out var a))return a;
  if(double.TryParse(raw,System.Globalization.NumberStyles.Any,System.Globalization.CultureInfo.InvariantCulture,out var b))return b;
  return 0;
 }
 void Accept(){Selected=new();foreach(DataGridViewRow r in grid.Rows){if(!Convert.ToBoolean(r.Cells[0].Value??false))continue;var q=ParseNumber(r.Cells[4].Value);if(q<=0)q=1;var cost=ParseNumber(r.Cells[5].Value);Selected.Add(new Pick(Convert.ToInt32(r.Cells[1].Value),Convert.ToString(r.Cells[2].Value)??"",Convert.ToDouble(r.Cells[3].Value),q,cost));}if(Selected.Count==0){MessageBox.Show("Seleccioná al menos un producto.");return;}DialogResult=DialogResult.OK;Close();}
}
