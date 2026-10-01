using FerrarisPOS.Services;
using FerrarisPOS.Models;

namespace FerrarisPOS.Forms;
public sealed class FavoritesForm:Form
{
    private readonly DataGridView grid=new(); public Product? SelectedProduct{get;private set;}
    public FavoritesForm(){Text="Favoritos / Venta rápida";Width=760;Height=540;StartPosition=FormStartPosition.CenterParent;Controls.Add(new Label{Text="PRODUCTOS FAVORITOS",Location=new Point(20,18),AutoSize=true,Font=new Font("Segoe UI",17,FontStyle.Bold)});grid.Location=new Point(20,60);grid.Size=new Size(700,390);grid.ReadOnly=true;grid.AutoGenerateColumns=true;grid.BackgroundColor=Color.White;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.DoubleClick+=(_,_)=>Select();Controls.Add(grid);var add=new Button{Text="AGREGAR A VENTA",Location=new Point(500,465),Width=140,Height=40};add.Click+=(_,_)=>Select();Controls.Add(add);var close=new Button{Text="CERRAR",DialogResult=DialogResult.Cancel,Location=new Point(610,465),Width=100,Height=40};Controls.Add(close);LoadGrid();}
    private void LoadGrid(){grid.DataSource=FavoriteService.Get().Select(p=>new{p.Id,p.Description,p.SalePrice,p.Stock,p.Category}).ToList();}
    private new void Select(){if(grid.CurrentRow?.Cells["Id"].Value is null)return;var id=Convert.ToInt32(grid.CurrentRow.Cells["Id"].Value);SelectedProduct=FavoriteService.Get().FirstOrDefault(x=>x.Id==id);if(SelectedProduct!=null)DialogResult=DialogResult.OK;}
}
