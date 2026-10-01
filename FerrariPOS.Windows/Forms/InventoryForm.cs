using FerrarisPOS.Services;
using FerrarisPOS.Models;

namespace FerrarisPOS.Forms;

public class InventoryForm : Form
{
    private readonly DataGridView grid=new(); private readonly Label summary=new(); private readonly TextBox search=new(), qty=new(), reference=new();
    public InventoryForm(){Text="FerrarisPOS - Inventario";Width=1300;Height=760;MinimumSize=new Size(1200,700);AutoScroll=true;BackColor=Color.Gainsboro;Build();LoadData();}
    private void Build(){Controls.Add(new Label{Text="INVENTARIO Y EXISTENCIAS",Location=new Point(20,15),AutoSize=true,Font=new Font("Segoe UI",18,FontStyle.Bold)});summary.Location=new Point(20,55);summary.Size=new Size(900,45);summary.Font=new Font("Segoe UI",12,FontStyle.Bold);Controls.Add(summary);search.Location=new Point(20,110);search.Width=350;search.PlaceholderText="Buscar producto...";search.TextChanged+=(_,_)=>LoadData();Controls.Add(search);grid.Location=new Point(20,150);grid.Size=new Size(900,520);grid.Anchor=AnchorStyles.Top|AnchorStyles.Bottom|AnchorStyles.Left;grid.ReadOnly=true;grid.AutoGenerateColumns=true;grid.BackgroundColor=Color.White;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.SelectionChanged+=Selected;Controls.Add(grid);
        Controls.Add(new Label{Text="AJUSTE DE STOCK",Location=new Point(950,150),AutoSize=true,Font=new Font("Segoe UI",13,FontStyle.Bold)});Controls.Add(new Label{Text="Cantidad a sumar/restar",Location=new Point(950,195),AutoSize=true});qty.Location=new Point(950,220);qty.Width=230;Controls.Add(qty);Controls.Add(new Label{Text="Referencia / motivo",Location=new Point(950,270),AutoSize=true});reference.Location=new Point(950,295);reference.Width=230;Controls.Add(reference);var add=new Button{Text="ENTRADA +",Location=new Point(950,350),Width=110,Height=40};add.Click+=(_,_)=>Adjust(1);Controls.Add(add);var sub=new Button{Text="SALIDA -",Location=new Point(1070,350),Width=110,Height=40};sub.Click+=(_,_)=>Adjust(-1);Controls.Add(sub);var count=new Button{Text="CONTEO FÍSICO",Location=new Point(950,410),Width=110,Height=40};count.Visible=Session.HasPermission("CONTEO_FISICO");count.Click+=(_,_)=>{if(!Session.HasPermission("CONTEO_FISICO")){MessageBox.Show("Tu usuario no tiene permiso para realizar el conteo físico. Solicitá al ADMIN que lo habilite desde F8 Usuarios.","Permiso insuficiente",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}if(!InventoryControlService.IsGlobalEnabled){MessageBox.Show("El conteo físico está deshabilitado mientras el inventario global esté apagado. Los contadores permanecen congelados.","Inventario deshabilitado",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}using var f=new InventoryCountForm();ThemeService.Apply(f);f.ShowDialog(this);LoadData();};Controls.Add(count);var waste=new Button{Text="MERMA",Location=new Point(1070,410),Width=110,Height=40};waste.Click+=(_,_)=>{if(!InventoryControlService.IsGlobalEnabled){MessageBox.Show("La merma está deshabilitada mientras el inventario global esté apagado.","Inventario deshabilitado",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}using var f=new WasteForm();ThemeService.Apply(f);f.ShowDialog(this);LoadData();};Controls.Add(waste);
        var history=new Button{Text="HISTORIAL MOVIMIENTOS",Location=new Point(950,470),Width=230,Height=42};history.Click+=(_,_)=>{using var f=new StockMovementHistoryForm();ThemeService.Apply(f);f.ShowDialog(this);};Controls.Add(history);
        // IMPORTAR / EXPORTAR INVENTARIO: solo ADMINISTRADOR.
        if (Session.IsAdmin)
        {
            var export=new Button{Text="EXPORTAR INVENTARIO",Location=new Point(950,530),Width=110,Height=42};
            export.Click+=(_,_)=>ExportInventory();
            Controls.Add(export);

            var import=new Button{Text="IMPORTAR INVENTARIO",Location=new Point(1070,530),Width=110,Height=42};
            import.Click+=(_,_)=>ImportInventory();
            Controls.Add(import);
        }}
    private int SelectedId=>grid.CurrentRow?.DataBoundItem is Product p?p.Id:0;
    private void Selected(object? s,EventArgs e){if(grid.CurrentRow?.DataBoundItem is Product){qty.Text="1";reference.Clear();}}
    private void LoadData()
    {
        var text = search.Text.Trim();
        List<Product> list;

        // En Inventario y Existencias la búsqueda manual es exclusivamente
        // por el comienzo de la descripción.
        // Excepción: si el texto coincide exactamente con un código de barras
        // (principal o generado/asociado), mostramos directamente ese producto.
        if (text.Length > 0)
        {
            var byBarcode = ProductService.FindByBarcode(text);
            list = byBarcode is not null
                ? new List<Product> { byBarcode }
                : ProductService.SearchByDescriptionPrefix(text);
        }
        else
        {
            list = ProductService.SearchByDescriptionPrefix();
        }

        grid.DataSource = list;
        summary.Text=$"CLASES DE PRODUCTO: {list.Count}   ·   UNIDADES TOTALES: {list.Sum(x=>x.Stock):N2}   ·   BAJO MÍNIMO: {list.Count(x=>x.Stock<=x.MinStock)}";
    }
    private static double Num(TextBox t)=>double.TryParse(t.Text.Replace(',','.'),System.Globalization.NumberStyles.Any,System.Globalization.CultureInfo.InvariantCulture,out var v)?v:0;
    private void ImportInventory()
    {
        if (!Session.IsAdmin)
        {
            MessageBox.Show("Solo el usuario ADMINISTRADOR puede importar inventario.",
                "Permiso restringido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var result = ExcelExportService.ImportInventory();
            if (result is null) return;
            LoadData();
            MessageBox.Show(
                $"Importación completada correctamente.\n\nNuevos productos: {result.Inserted}\nProductos actualizados: {result.Updated}\nFilas omitidas: {result.Skipped}",
                "Importar inventario", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo importar el inventario:\n" + ex.Message, "Importar inventario", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportInventory()
    {
        if (!Session.IsAdmin)
        {
            MessageBox.Show("Solo el usuario ADMINISTRADOR puede exportar inventario.",
                "Permiso restringido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var path = ExcelExportService.ExportInventory();
            if (!string.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show($"Inventario exportado correctamente.\\n\\nArchivo:\\n{path}",
                    "Exportar inventario", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo exportar el inventario:\\n" + ex.Message,
                "Exportar inventario", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Adjust(int sign){if(!InventoryControlService.IsGlobalEnabled){MessageBox.Show("El inventario global está deshabilitado. Los contadores están congelados.","Inventario deshabilitado",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}try{var id=SelectedId;if(id==0){MessageBox.Show("Seleccioná un producto.");return;}var q=Num(qty);if(q<=0){MessageBox.Show("Ingresá una cantidad mayor a cero.");return;}var reason=reference.Text.Trim();
            if(sign<0 && string.IsNullOrWhiteSpace(reason)){MessageBox.Show("Para realizar una SALIDA (-) de inventario tenés que escribir el motivo. Esta razón quedará registrada en el corte de caja junto con el cajero que hizo el movimiento.","Motivo obligatorio",MessageBoxButtons.OK,MessageBoxIcon.Warning);reference.Focus();return;}
            if(sign>0 && string.IsNullOrWhiteSpace(reason)) reason="Entrada manual";
            ProductService.AdjustStock(id,q*sign,sign>0?"ENTRY":"EXIT",reason,Session.UserId);
            LoadData();
            MessageBox.Show("Inventario actualizado.");
            // Dejar el formulario listo para cargar inmediatamente otro producto.
            search.Clear();
            qty.Clear();
            reference.Clear();
            search.Focus();
        }catch(Exception ex){MessageBox.Show(ex.Message);}}
}
