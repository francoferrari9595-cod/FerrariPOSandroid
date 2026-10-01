using FerrarisPOS.Services;

namespace FerrarisPOS.Forms;

public sealed class ModifierPickerForm:Form
{
    private readonly CheckedListBox list=new(); public string Summary{get;private set;}=""; public double TotalDelta{get;private set;}
    public ModifierPickerForm(int productId,string productName){Text=$"Opciones · {productName}";Width=520;Height=430;StartPosition=FormStartPosition.CenterParent;Controls.Add(new Label{Text="ELEGÍ OPCIONES / ADICIONALES",Location=new Point(20,18),AutoSize=true,Font=new Font("Segoe UI",15,FontStyle.Bold)});list.Location=new Point(20,55);list.Size=new Size(460,260);list.CheckOnClick=true;foreach(var m in ProductModifierService.Get(productId))list.Items.Add(new ModifierItem(m),false);Controls.Add(list);var ok=new Button{Text="APLICAR",Location=new Point(280,330),Width=100,Height=42};ok.Click+=(_,_)=>Accept();var cancel=new Button{Text="CANCELAR",DialogResult=DialogResult.Cancel,Location=new Point(390,330),Width=100,Height=42};Controls.Add(ok);Controls.Add(cancel);}
    private void Accept(){var chosen=list.CheckedItems.Cast<ModifierItem>().ToList();Summary=string.Join(", ",chosen.Select(x=>x.Mod.Name));TotalDelta=chosen.Sum(x=>x.Mod.PriceDelta);DialogResult=DialogResult.OK;Close();}
    private sealed record ModifierItem(ProductModifier Mod){public override string ToString()=>Mod.PriceDelta>0?$"{Mod.Name}  +${Mod.PriceDelta:N2}":$"{Mod.Name}  ${Mod.PriceDelta:N2}";}
}
