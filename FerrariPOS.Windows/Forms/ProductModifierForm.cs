using FerrarisPOS.Services;

namespace FerrarisPOS.Forms;

public sealed class ProductModifierForm:Form
{
    private readonly int productId; private readonly DataGridView grid=new(); private readonly TextBox name=new(),delta=new(); private int id;
    public ProductModifierForm(int productId,string productName){this.productId=productId;Text=$"Modificadores · {productName}";Width=760;Height=520;Build();LoadGrid();}
    private void Build(){Controls.Add(new Label{Text="MODIFICADORES DEL PRODUCTO",Location=new Point(20,18),AutoSize=true,Font=new Font("Segoe UI",16,FontStyle.Bold)});Field("NOMBRE",70,name);Field("ADICIONAL",115,delta);var save=Btn("GUARDAR",20,170);save.Click+=(_,_)=>Save();Controls.Add(save);var n=Btn("NUEVO",130,170);n.Click+=(_,_)=>Clear();Controls.Add(n);var del=Btn("DESACTIVAR",240,170);del.Click+=(_,_)=>Delete();Controls.Add(del);grid.Location=new Point(20,225);grid.Size=new Size(700,230);grid.ReadOnly=true;grid.AutoGenerateColumns=true;grid.BackgroundColor=Color.White;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.SelectionChanged+=Selected;Controls.Add(grid);}
    private void Field(string l,int y,TextBox t){Controls.Add(new Label{Text=l,Location=new Point(20,y+5),AutoSize=true});t.Location=new Point(120,y);t.Width=320;Controls.Add(t);}
    private Button Btn(string t,int x,int y)=>new(){Text=t,Location=new Point(x,y),Width=100,Height=38};
    private void LoadGrid(){grid.DataSource=ProductModifierService.Get(productId).Select(x=>new{x.Id,x.Name,Adicional=x.PriceDelta}).ToList();}
    private void Selected(object? s,EventArgs e){if(grid.CurrentRow?.DataBoundItem is null)return;id=Convert.ToInt32(grid.CurrentRow.Cells[0].Value);name.Text=Convert.ToString(grid.CurrentRow.Cells[1].Value)??"";delta.Text=Convert.ToString(grid.CurrentRow.Cells[2].Value)??"0";}
    private void Save(){if(string.IsNullOrWhiteSpace(name.Text)){MessageBox.Show("Ingresá el nombre.");return;}if(!double.TryParse(delta.Text.Replace(',','.'),System.Globalization.NumberStyles.Any,System.Globalization.CultureInfo.InvariantCulture,out var d))d=0;ProductModifierService.Save(id,productId,name.Text,d);LoadGrid();Clear();}
    private void Delete(){if(id<=0)return;ProductModifierService.Delete(id);LoadGrid();Clear();}
    private void Clear(){id=0;name.Clear();delta.Text="0";name.Focus();}
}
