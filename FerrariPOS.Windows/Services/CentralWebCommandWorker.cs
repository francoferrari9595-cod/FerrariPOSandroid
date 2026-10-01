using FerrarisPOS.Data;
using System.Runtime.CompilerServices;
using System.Net.Http.Json;
using System.Text.Json;

namespace FerrarisPOS.Services;

public static class CentralWebCommandWorker
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static int _started;

    [ModuleInitializer]
    public static void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        _ = Task.Run(LoopAsync);
    }

    private static async Task LoopAsync()
    {
        for (var i = 0; i < 120 && !File.Exists(Database.DbPath); i++) await Task.Delay(500);
        while (true)
        {
            try { await PollAsync(); } catch { }
            try { await Task.Delay(1500); } catch { return; }
        }
    }

    private static async Task PollAsync()
    {
        var url=(Environment.GetEnvironmentVariable("FERRARIPOS_CENTRAL_URL")??Database.GetSetting("central_url","https://ferraripos-central.onrender.com")).TrimEnd('/');
        var store=Database.GetSetting("central_store_id","");var token=Database.GetSetting("central_token","");
        if(string.IsNullOrWhiteSpace(store)||string.IsNullOrWhiteSpace(token))
        {
            await CentralApiClient.RegisterInstallationAsync();
            store=Database.GetSetting("central_store_id","");
            token=Database.GetSetting("central_token","");
        }
        if(string.IsNullOrWhiteSpace(store)||string.IsNullOrWhiteSpace(token))return;
        using var req=new HttpRequestMessage(HttpMethod.Get,$"{url}/api/v1/stores/{Uri.EscapeDataString(store)}/commands?limit=10");
        req.Headers.Add("X-FerrariPOS-Token",token);using var response=await Http.SendAsync(req);if(!response.IsSuccessStatusCode)return;
        using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        foreach(var cmd in doc.RootElement.EnumerateArray())
        {
            var id=cmd.GetProperty("id").GetString()??"";var type=cmd.GetProperty("type").GetString()??"";var payload=cmd.GetProperty("payload").GetString()??"{}";
            bool ok=false;object result=new{};string? error=null;
            try{using var p=JsonDocument.Parse(payload);(ok,result)=Execute(type,p.RootElement);}catch(Exception ex){error=ex.Message;}
            using var outReq=new HttpRequestMessage(HttpMethod.Post,$"{url}/api/v1/stores/{Uri.EscapeDataString(store)}/commands/{Uri.EscapeDataString(id)}/result");
            outReq.Headers.Add("X-FerrariPOS-Token",token);outReq.Content=JsonContent.Create(new{ok,result,error});try{await Http.SendAsync(outReq);}catch{}
        }
    }

    private static (bool ok,object result) Execute(string type,JsonElement p)=>type.ToLowerInvariant() switch
    {
        "stock.adjust"=>Stock(p),"customer.upsert"=>Customer(p),"customer.payment"=>Payment(p),
        "customer.delete"=>DeleteCustomer(p),"product.price"=>Price(p),"cash.movement"=>Cash(p),
        "refresh.snapshot"=>(true,new{refreshed=true}),_=>throw new InvalidOperationException("Comando web no soportado: "+type)
    };

    private static (bool,object) Stock(JsonElement p)
    {
        var id=p.GetProperty("productId").GetInt32();var delta=p.GetProperty("quantityDelta").GetDecimal();var reason=p.TryGetProperty("reason",out var rr)?rr.GetString()??"Ajuste web":"Ajuste web";
        using var cn=Database.Open();using var tx=cn.BeginTransaction();decimal before;string name;
        using(var c=cn.CreateCommand()){c.Transaction=tx;c.CommandText="SELECT stock,description FROM products WHERE id=$id";c.Parameters.AddWithValue("$id",id);using var r=c.ExecuteReader();if(!r.Read())throw new InvalidOperationException("Producto no encontrado.");before=r.GetDecimal(0);name=r.GetString(1);}
        var after=before+delta;if(after<0)throw new InvalidOperationException("El stock no puede quedar negativo.");
        using(var c=cn.CreateCommand()){c.Transaction=tx;c.CommandText="UPDATE products SET stock=$s,updated_at=CURRENT_TIMESTAMP WHERE id=$id";c.Parameters.AddWithValue("$s",after);c.Parameters.AddWithValue("$id",id);c.ExecuteNonQuery();}
        using(var c=cn.CreateCommand()){c.Transaction=tx;c.CommandText="INSERT INTO stock_movements(product_id,movement_type,quantity,reference,user_id) VALUES($id,'AJUSTE_WEB',$q,$r,NULL)";c.Parameters.AddWithValue("$id",id);c.Parameters.AddWithValue("$q",delta);c.Parameters.AddWithValue("$r",reason);c.ExecuteNonQuery();}
        tx.Commit();return(true,new{productId=id,description=name,before,delta,after});
    }

    private static (bool,object) Customer(JsonElement p)
    {
        var id=p.TryGetProperty("id",out var ie)?ie.GetInt32():0;var name=p.GetProperty("name").GetString()?.Trim()??"";
        if(string.IsNullOrWhiteSpace(name))throw new InvalidOperationException("El nombre es obligatorio.");
        var doc=p.TryGetProperty("document",out var d)?d.GetString()??"":"";var phone=p.TryGetProperty("phone",out var ph)?ph.GetString()??"":"";
        var email=p.TryGetProperty("email",out var e)?e.GetString()??"":"";
        var address=p.TryGetProperty("address",out var a)?a.GetString()??"":"";
        var limit=p.TryGetProperty("creditLimit",out var l)?l.GetDecimal():0m;
        using var cn=Database.Open();long cid;
        if(id>0){using var c=cn.CreateCommand();c.CommandText="UPDATE customers SET name=$n,document=$d,phone=$p,email=$e,address=$a,credit_limit=$l WHERE id=$id";c.Parameters.AddWithValue("$n",name);c.Parameters.AddWithValue("$d",doc);c.Parameters.AddWithValue("$p",phone);c.Parameters.AddWithValue("$e",email);c.Parameters.AddWithValue("$a",address);c.Parameters.AddWithValue("$l",limit);c.Parameters.AddWithValue("$id",id);if(c.ExecuteNonQuery()==0)throw new InvalidOperationException("Cliente no encontrado.");cid=id;}
        else{using var c=cn.CreateCommand();c.CommandText="INSERT INTO customers(name,document,phone,email,address,credit_limit,active) VALUES($n,$d,$p,$e,$a,$l,1);SELECT last_insert_rowid();";c.Parameters.AddWithValue("$n",name);c.Parameters.AddWithValue("$d",doc);c.Parameters.AddWithValue("$p",phone);c.Parameters.AddWithValue("$e",email);c.Parameters.AddWithValue("$a",address);c.Parameters.AddWithValue("$l",limit);cid=Convert.ToInt64(c.ExecuteScalar());}
        return(true,new{id=cid,name,document=doc,phone,email,address,creditLimit=limit});
    }

    private static (bool,object) Payment(JsonElement p)
    {
        var id=p.GetProperty("customerId").GetInt32();var amount=p.GetProperty("amount").GetDecimal();var method=p.TryGetProperty("method",out var m)?m.GetString()??"EFECTIVO":"EFECTIVO";var concept=p.TryGetProperty("concept",out var conceptEl)?conceptEl.GetString()??"Abono web":"Abono web";
        if(amount<=0)throw new InvalidOperationException("El abono debe ser mayor que 0.");using var cn=Database.Open();using var tx=cn.BeginTransaction();
        using(var c=cn.CreateCommand()){c.Transaction=tx;c.CommandText="INSERT INTO customer_accounts(customer_id,entry_type,amount,concept,payment_method,user_id) VALUES($id,'PAGO',$a,$c,$m,NULL)";c.Parameters.AddWithValue("$id",id);c.Parameters.AddWithValue("$a",amount);c.Parameters.AddWithValue("$c",concept);c.Parameters.AddWithValue("$m",method);c.ExecuteNonQuery();}
        if(method.Equals("EFECTIVO",StringComparison.OrdinalIgnoreCase)){using var c=cn.CreateCommand();c.Transaction=tx;c.CommandText="INSERT INTO cash_movements(session_id,user_id,movement_type,concept,amount,payment_method,reference_id) SELECT id,NULL,'INGRESO',$c,$a,'EFECTIVO',$id FROM cash_sessions WHERE status='OPEN' ORDER BY id DESC LIMIT 1";c.Parameters.AddWithValue("$c",concept);c.Parameters.AddWithValue("$a",amount);c.Parameters.AddWithValue("$id",id);c.ExecuteNonQuery();}
        tx.Commit();return(true,new{customerId=id,amount,method,concept});
    }

    private static (bool,object) DeleteCustomer(JsonElement p){var id=p.GetProperty("id").GetInt32();using var cn=Database.Open();using var c=cn.CreateCommand();c.CommandText="UPDATE customers SET active=0 WHERE id=$id";c.Parameters.AddWithValue("$id",id);if(c.ExecuteNonQuery()==0)throw new InvalidOperationException("Cliente no encontrado.");return(true,new{id});}

    private static (bool,object) Price(JsonElement p)
    {
        var id=p.GetProperty("productId").GetInt32();
        var price=p.GetProperty("price").GetDecimal();
        if(price<0)throw new InvalidOperationException("Precio inválido.");
        using var cn=Database.Open();
        using var tx=cn.BeginTransaction();
        decimal oldPrice; string name;
        using(var c=cn.CreateCommand()){c.Transaction=tx;c.CommandText="SELECT sale_price,description FROM products WHERE id=$id AND active=1";c.Parameters.AddWithValue("$id",id);using var r=c.ExecuteReader();if(!r.Read())throw new InvalidOperationException("Producto no encontrado.");oldPrice=r.GetDecimal(0);name=r.GetString(1);}
        using(var c=cn.CreateCommand()){c.Transaction=tx;c.CommandText="UPDATE products SET sale_price=$p,updated_at=CURRENT_TIMESTAMP WHERE id=$id";c.Parameters.AddWithValue("$p",price);c.Parameters.AddWithValue("$id",id);c.ExecuteNonQuery();}
        PriceGuardService.RecordProductPriceChange(cn,tx,id,(double)oldPrice,(double)price);
        tx.Commit();
        return(true,new{productId=id,description=name,oldPrice,price});
    }

    private static (bool,object) Cash(JsonElement p){var type=p.TryGetProperty("movementType",out var t)?t.GetString()??"INGRESO":"INGRESO";var concept=p.TryGetProperty("concept",out var conceptEl)?conceptEl.GetString()??"Movimiento web":"Movimiento web";var amount=p.GetProperty("amount").GetDecimal();var method=p.TryGetProperty("paymentMethod",out var m)?m.GetString()??"EFECTIVO":"EFECTIVO";if(amount<=0)throw new InvalidOperationException("Importe inválido.");using var cn=Database.Open();using var cmd=cn.CreateCommand();cmd.CommandText="INSERT INTO cash_movements(session_id,user_id,movement_type,concept,amount,payment_method) SELECT id,NULL,$t,$c,$a,$m FROM cash_sessions WHERE status='OPEN' ORDER BY id DESC LIMIT 1";cmd.Parameters.AddWithValue("$t",type);cmd.Parameters.AddWithValue("$c",concept);cmd.Parameters.AddWithValue("$a",amount);cmd.Parameters.AddWithValue("$m",method);if(cmd.ExecuteNonQuery()==0)throw new InvalidOperationException("No hay una caja abierta.");return(true,new{type,concept,amount,method});}
}
