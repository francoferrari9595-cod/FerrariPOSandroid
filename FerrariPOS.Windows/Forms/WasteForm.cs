using FerrarisPOS.Services;
using FerrarisPOS.Models;
using FerrarisPOS.Data;

namespace FerrarisPOS.Forms;

public class WasteForm : Form
{
    private readonly SafeComboBox product=new(), reason=new(); private readonly TextBox qty=new(),note=new(); private readonly DataGridView history=new();
    public WasteForm(){Text="FerrarisPOS · Merma y desperdicio";Width=980;Height=620;StartPosition=FormStartPosition.CenterParent;Build();LoadProducts();LoadHistory();}
    private void Build()
    {
        AutoScroll = true;
        MinimumSize = new Size(900, 560);
        Padding = new Padding(12);

        Controls.Add(new Label
        {
            Text = "MERMA / DESPERDICIO",
            Location = new Point(25, 20),
            AutoSize = true,
            Font = new Font("Segoe UI", 18, FontStyle.Bold)
        });
        Controls.Add(new Label
        {
            Text = "Registrar pérdidas sin mezclar con ventas ni compras.",
            Location = new Point(25, 52),
            AutoSize = true,
            ForeColor = Color.DimGray
        });

        Field("PRODUCTO", 90, product);

        // Lista de productos: se limita la altura del desplegable para que
        // Windows cree la barra de desplazamiento cuando hay muchos productos.
        // Esto evita que la lista quede fuera de pantalla y permite recorrerla
        // con rueda, barra lateral, PageUp/PageDown, Home y End.
        product.DropDownStyle = ComboBoxStyle.DropDownList;
        product.IntegralHeight = false;
        product.MaxDropDownItems = 14;
        product.DropDownHeight = 14 * product.ItemHeight + 8;
        product.DropDownWidth = 520;
        product.Cursor = Cursors.Hand;
        product.KeyDown += (_, e) =>
        {
            if (!product.DroppedDown) return;
            if (e.KeyCode == Keys.PageDown || e.KeyCode == Keys.PageUp ||
                e.KeyCode == Keys.Home || e.KeyCode == Keys.End ||
                e.KeyCode == Keys.Down || e.KeyCode == Keys.Up)
                e.Handled = false;
        };
        product.MouseWheel += (_, _) =>
        {
            // Mantiene abierto el selector y deja que el ComboBox procese
            // normalmente la rueda aun cuando el puntero esté sobre el campo.
            if (!product.DroppedDown) product.DroppedDown = true;
        };

        Field("CANTIDAD", 145, qty);
        reason.Location = new Point(140, 200);
        reason.Width = 315;
        reason.DropDownStyle = ComboBoxStyle.DropDownList;
        reason.IntegralHeight = false;
        reason.MaxDropDownItems = 8;
        reason.DropDownHeight = 8 * reason.ItemHeight + 8;
        reason.Items.AddRange(new object[] { "VENCIMIENTO", "ROTURA", "DESPERDICIO", "ERROR DE PRODUCCIÓN", "DEVOLUCIÓN", "OTRO" });
        if (reason.Items.Count > 0) reason.SelectedIndex = 0;
        Controls.Add(new Label { Text = "MOTIVO", Location = new Point(20, 205), AutoSize = true });
        Controls.Add(reason);

        Controls.Add(new Label { Text = "OBSERVACIÓN", Location = new Point(20, 255), AutoSize = true });
        note.Location = new Point(140, 250);
        note.Width = 315;
        note.Height = 70;
        note.Multiline = true;
        Controls.Add(note);

        var save = new Button { Text = "REGISTRAR MERMA", Location = new Point(255, 345), Width = 200, Height = 45 };
        save.Click += (_, _) => Save();
        Controls.Add(save);

        history.Location = new Point(500, 70);
        history.Size = new Size(440, 470);
        history.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        history.ReadOnly = true;
        history.AutoGenerateColumns = true;
        history.BackgroundColor = Color.White;
        history.AllowUserToResizeRows = false;
        history.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        history.ScrollBars = ScrollBars.Both;
        Controls.Add(history);
    }
    private void Field(string label,int y,Control c){Controls.Add(new Label{Text=label,Location=new Point(20,y+5),AutoSize=true,Font=new Font("Segoe UI",9,FontStyle.Bold)});c.Location=new Point(140,y);c.Width=315;Controls.Add(c);}
    private void LoadProducts(){product.DataSource=ProductService.Search();product.DisplayMember="Description";product.ValueMember="Id";}
    private void LoadHistory(){using var cn=Database.Open();using var cmd=cn.CreateCommand();cmd.CommandText="SELECT w.id,p.description,w.quantity,w.reason,w.notes,w.created_at FROM waste_records w JOIN products p ON p.id=w.product_id ORDER BY w.id DESC LIMIT 200";using var r=cmd.ExecuteReader();var dt=new System.Data.DataTable();dt.Load(r);history.DataSource=dt;}
    private void Save(){if(!InventoryControlService.IsGlobalEnabled){MessageBox.Show("El inventario global está deshabilitado. Los contadores están congelados y no se registrarán mermas sobre el stock.","Inventario deshabilitado",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}if(product.SelectedItem is not Product p){MessageBox.Show("Seleccioná un producto.");return;}if(!double.TryParse(qty.Text.Replace(',','.'),System.Globalization.NumberStyles.Any,System.Globalization.CultureInfo.InvariantCulture,out var q)||q<=0){MessageBox.Show("Ingresá una cantidad válida.");return;}if(q>p.Stock+0.000001){MessageBox.Show($"La cantidad supera el stock disponible ({p.Stock:N3}).","Merma",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}try{using var cn=Database.Open();using var tx=cn.BeginTransaction();using(var a=cn.CreateCommand()){a.Transaction=tx;a.CommandText="INSERT INTO waste_records(product_id,quantity,reason,notes,user_id) VALUES($p,$q,$r,$n,$u)";a.Parameters.AddWithValue("$p",p.Id);a.Parameters.AddWithValue("$q",q);a.Parameters.AddWithValue("$r",reason.Text);a.Parameters.AddWithValue("$n",note.Text.Trim());a.Parameters.AddWithValue("$u",Session.UserId);a.ExecuteNonQuery();}using(var b=cn.CreateCommand()){b.Transaction=tx;b.CommandText="UPDATE products SET stock=stock-$q,updated_at=CURRENT_TIMESTAMP WHERE id=$p";b.Parameters.AddWithValue("$q",q);b.Parameters.AddWithValue("$p",p.Id);b.ExecuteNonQuery();}using(var c=cn.CreateCommand()){c.Transaction=tx;c.CommandText="INSERT INTO stock_movements(product_id,movement_type,quantity,reference,user_id) VALUES($p,'WASTE',$q,$r,$u)";c.Parameters.AddWithValue("$p",p.Id);c.Parameters.AddWithValue("$q",q);c.Parameters.AddWithValue("$r",$"MERMA · {reason.Text} · {note.Text.Trim()}");c.Parameters.AddWithValue("$u",Session.UserId);c.ExecuteNonQuery();}tx.Commit();AuditService.Log(Session.UserId,"WASTE_CREATE","INVENTARIO",$"{p.Description} · {q:N3} · {reason.Text}");MessageBox.Show("Merma registrada y descontada del stock.");LoadProducts();LoadHistory();qty.Clear();note.Clear();}catch(Exception ex){MessageBox.Show(ex.Message,"Error",MessageBoxButtons.OK,MessageBoxIcon.Error);}}
}
