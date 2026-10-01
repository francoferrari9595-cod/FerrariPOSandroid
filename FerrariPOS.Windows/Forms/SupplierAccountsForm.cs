using FerrarisPOS.Data;
using FerrarisPOS.Services;
using System.Data;

namespace FerrarisPOS.Forms;

public sealed class SupplierAccountsForm : Form
{
    private readonly SafeComboBox supplier = new();
    private readonly DataGridView grid = new();
    private readonly Label balance = new();
    private readonly TextBox invoiceNo = new();
    private readonly TextBox total = new();
    private readonly DateTimePicker due = new();

    public SupplierAccountsForm(int supplierId = 0)
    {
        Text = "FerrarisPOS · Cuentas de proveedores"; Width = 1120; Height = 680; StartPosition = FormStartPosition.CenterParent;
        Build(); LoadSuppliers(); if (supplierId > 0) supplier.SelectedValue = supplierId; LoadGrid(); ThemeService.Apply(this);
    }
    private void Build()
    {
        Controls.Add(new Label { Text="CUENTAS DE PROVEEDORES", Location=new Point(20,18), AutoSize=true, Font=new Font("Segoe UI",17,FontStyle.Bold) });
        Controls.Add(new Label { Text="PROVEEDOR", Location=new Point(20,62), AutoSize=true });
        supplier.Location=new Point(95,58); supplier.Width=280; supplier.SelectedIndexChanged += (_,_)=>LoadGrid(); Controls.Add(supplier);
        var add=Btn("NUEVA FACTURA",395,55,140); add.Click+=(_,_)=>AddInvoice(); Controls.Add(add);
        var pay=Btn("REGISTRAR PAGO",545,55,150); pay.Click+=(_,_)=>PayInvoice(); Controls.Add(pay);
        var refresh=Btn("ACTUALIZAR",705,55,110); refresh.Click+=(_,_)=>LoadGrid(); Controls.Add(refresh);
        balance.Location=new Point(835,58); balance.AutoSize=true; balance.Font=new Font("Segoe UI",12,FontStyle.Bold); Controls.Add(balance);
        grid.Location=new Point(20,105); grid.Size=new Size(1060,470); grid.ReadOnly=true; grid.AllowUserToAddRows=false; grid.RowHeadersVisible=false; grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect; grid.AutoGenerateColumns=true; grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill; Controls.Add(grid);
        var close=Btn("CERRAR",20,590,110); close.Click+=(_,_)=>Close(); Controls.Add(close);
    }
    private Button Btn(string t,int x,int y,int w)=>new(){Text=t,Location=new Point(x,y),Width=w,Height=36};
    private void LoadSuppliers(){using var cn=Database.Open();using var c=cn.CreateCommand();c.CommandText="SELECT id,name FROM suppliers WHERE active=1 ORDER BY name";using var r=c.ExecuteReader();var list=new List<S>();while(r.Read())list.Add(new S(r.GetInt32(0),r.GetString(1)));supplier.DataSource=list;supplier.DisplayMember="Name";supplier.ValueMember="Id";supplier.SelectedIndex=list.Count>0?0:-1;}
    private sealed record S(int Id,string Name);
    private int SupplierId=>supplier.SelectedValue is int x?x:0;
    private void LoadGrid(){if(SupplierId==0)return;using var cn=Database.Open();using var c=cn.CreateCommand();c.CommandText=@"SELECT id AS ID,invoice_no AS FACTURA,invoice_date AS FECHA,due_date AS VENCIMIENTO,ROUND(total,2) AS TOTAL,ROUND(paid,2) AS PAGADO,ROUND(total-paid,2) AS SALDO,status AS ESTADO FROM supplier_invoices WHERE supplier_id=$s ORDER BY id DESC";c.Parameters.AddWithValue("$s",SupplierId);using var r=c.ExecuteReader();var dt=new DataTable();dt.Load(r);grid.DataSource=dt;using var q=cn.CreateCommand();q.CommandText="SELECT COALESCE(SUM(total-paid),0) FROM supplier_invoices WHERE supplier_id=$s AND status<>'CANCELLED'";q.Parameters.AddWithValue("$s",SupplierId);balance.Text=$"SALDO: ${Convert.ToDouble(q.ExecuteScalar()??0):N2}";}
    private int SelectedId()=>grid.CurrentRow?.Cells[0].Value is null?0:Convert.ToInt32(grid.CurrentRow.Cells[0].Value);
    private void AddInvoice(){if(SupplierId==0)return; using var f=new Form{Text="Nueva factura de proveedor",Width=430,Height=300,StartPosition=FormStartPosition.CenterParent}; var l1=new Label{Text="N° FACTURA",Location=new Point(20,25),AutoSize=true}; invoiceNo.SetBounds(120,20,250,28); var l2=new Label{Text="TOTAL",Location=new Point(20,70),AutoSize=true}; total.SetBounds(120,65,250,28); due.SetBounds(120,110,250,28); var l3=new Label{Text="VENCIMIENTO",Location=new Point(20,115),AutoSize=true}; var ok=Btn("GUARDAR",120,160,120); var cancel=Btn("CANCELAR",250,160,120); ok.Click+=(_,_)=>{if(!double.TryParse(total.Text,System.Globalization.NumberStyles.Any,System.Globalization.CultureInfo.InvariantCulture,out var t)||t<=0){MessageBox.Show("Ingresá un total válido.");return;}using var cn=Database.Open();using var c=cn.CreateCommand();c.CommandText="INSERT INTO supplier_invoices(supplier_id,invoice_no,invoice_date,due_date,total,paid,status,created_by) VALUES($s,$n,CURRENT_TIMESTAMP,$d,$t,0,'PENDING',$u)";c.Parameters.AddWithValue("$s",SupplierId);c.Parameters.AddWithValue("$n",invoiceNo.Text.Trim());c.Parameters.AddWithValue("$d",due.Value.ToString("yyyy-MM-dd"));c.Parameters.AddWithValue("$t",t);c.Parameters.AddWithValue("$u",Session.UserId);c.ExecuteNonQuery();f.DialogResult=DialogResult.OK;f.Close();};cancel.Click+=(_,_)=>f.Close();f.Controls.AddRange(new Control[]{l1,invoiceNo,l2,total,l3,due,ok,cancel});f.ShowDialog(this);LoadGrid();}
    private void PayInvoice()
    {
        var id = SelectedId();
        if (id == 0) return;
        using var cn = Database.Open();
        using var q = cn.CreateCommand();
        q.CommandText = "SELECT supplier_id,total-paid FROM supplier_invoices WHERE id=$id";
        q.Parameters.AddWithValue("$id", id);
        using var r = q.ExecuteReader();
        if (!r.Read()) return;
        var sid = r.GetInt32(0);
        var dueAmount = r.GetDouble(1);
        r.Close();

        using var f = new Form { Text = "Registrar pago a proveedor", Width = 440, Height = 220, StartPosition = FormStartPosition.CenterParent };
        var amountLabel = new Label { Text = "IMPORTE", Location = new Point(20, 25), AutoSize = true };
        var amountBox = new TextBox { Text = dueAmount.ToString("0.00"), Location = new Point(120, 20), Width = 280 };
        var methodLabel = new Label { Text = "MEDIO", Location = new Point(20, 70), AutoSize = true };
        var methodBox = new SafeComboBox { Location = new Point(120, 65), Width = 280, DropDownStyle = ComboBoxStyle.DropDownList };
        methodBox.Items.AddRange(new object[] { "EFECTIVO", "MERCADO PAGO", "TRANSFERENCIA", "TARJETA" });
        methodBox.SelectedIndex = 0;
        var ok = new Button { Text = "REGISTRAR", Location = new Point(210, 120), Width = 90, Height = 35, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "CANCELAR", Location = new Point(310, 120), Width = 90, Height = 35, DialogResult = DialogResult.Cancel };
        f.Controls.AddRange(new Control[] { amountLabel, amountBox, methodLabel, methodBox, ok, cancel });
        f.AcceptButton = ok; f.CancelButton = cancel;
        if (f.ShowDialog(this) != DialogResult.OK) return;

        if (!double.TryParse(amountBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var pay) || pay <= 0 || pay > dueAmount)
        { MessageBox.Show("Importe inválido."); return; }
        var method = methodBox.Text;
        if (!CashService.IsOpen())
        { MessageBox.Show("La caja está cerrada. Abrí la caja antes de registrar el pago al proveedor.", "Pago a proveedor", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (method == "MERCADO PAGO" && !CashService.IsMercadoPagoEnabled())
        { MessageBox.Show("Mercado Pago no está habilitado en esta sesión. Activá la caja paralela desde la apertura.", "Pago a proveedor", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (method == "EFECTIVO" && !CashService.IsOpen())
        { MessageBox.Show("La caja está cerrada. Abrí la caja antes de pagar en efectivo.", "Pago a proveedor", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        using var tx = cn.BeginTransaction();
        using var pmt = cn.CreateCommand();
        pmt.Transaction = tx;
        pmt.CommandText = "INSERT INTO supplier_payments(supplier_id,invoice_id,amount,payment_method,user_id) VALUES($s,$i,$a,$m,$u)";
        pmt.Parameters.AddWithValue("$s", sid); pmt.Parameters.AddWithValue("$i", id); pmt.Parameters.AddWithValue("$a", pay); pmt.Parameters.AddWithValue("$m", method); pmt.Parameters.AddWithValue("$u", Session.UserId); pmt.ExecuteNonQuery();

        using var u = cn.CreateCommand();
        u.Transaction = tx;
        u.CommandText = "UPDATE supplier_invoices SET paid=paid+$a,status=CASE WHEN paid+$a>=total THEN 'PAID' ELSE 'PARTIAL' END WHERE id=$i";
        u.Parameters.AddWithValue("$a", pay); u.Parameters.AddWithValue("$i", id); u.ExecuteNonQuery();

        if (method is "EFECTIVO" or "MERCADO PAGO" or "TARJETA" or "TRANSFERENCIA")
        {
            using var cm = cn.CreateCommand();
            cm.Transaction = tx;
            cm.CommandText = "INSERT INTO cash_movements(session_id,user_id,movement_type,concept,amount,payment_method,reference_id) VALUES((SELECT id FROM cash_sessions WHERE status='OPEN' ORDER BY id DESC LIMIT 1),$u,'EXPENSE',$c,$a,$m,$ref)";
            cm.Parameters.AddWithValue("$u", Session.UserId);
            cm.Parameters.AddWithValue("$c", $"Pago proveedor #{sid} - factura #{id}");
            cm.Parameters.AddWithValue("$a", pay);
            cm.Parameters.AddWithValue("$m", method);
            cm.Parameters.AddWithValue("$ref", id);
            cm.ExecuteNonQuery();
        }
        tx.Commit();
        LoadGrid();
    }
    private static string Prompt(string title,string initial){using var f=new Form{Text=title,Width=380,Height=150,StartPosition=FormStartPosition.CenterParent};var t=new TextBox{Text=initial,Location=new Point(20,20),Width=320};var ok=new Button{Text="ACEPTAR",Location=new Point(170,60),Width=90,DialogResult=DialogResult.OK};var ca=new Button{Text="CANCELAR",Location=new Point(265,60),Width=90,DialogResult=DialogResult.Cancel};f.Controls.AddRange(new Control[]{t,ok,ca});f.AcceptButton=ok;f.CancelButton=ca;return f.ShowDialog()==DialogResult.OK?t.Text:"";}
}
