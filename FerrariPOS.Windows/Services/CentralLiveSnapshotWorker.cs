using FerrarisPOS.Data;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using QRCoder;

namespace FerrarisPOS.Services;

public static class CentralLiveSnapshotWorker
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
        for (var i = 0; i < 120 && !File.Exists(Database.DbPath); i++)
            await Task.Delay(500);

        var failures = 0;
        while (true)
        {
            try
            {
                // Si Render estuvo caído al arrancar Windows, no dejamos la conexión
                // muerta: registramos/reparamos automáticamente y continuamos.
                if (string.IsNullOrWhiteSpace(Database.GetSetting("central_store_id", "")) ||
                    string.IsNullOrWhiteSpace(Database.GetSetting("central_token", "")))
                {
                    await CentralApiClient.RegisterInstallationAsync();
                }

                var ok = await UploadAsync();
                failures = ok ? 0 : failures + 1;

                if (failures >= 3)
                {
                    failures = 0;
                    await CentralApiClient.RegisterInstallationAsync();
                }
            }
            catch
            {
                failures++;
                Database.SetSetting("central_snapshot_ok", "0");
                Database.SetSetting("central_connected", "0");
            }

            try { await Task.Delay(2500); }
            catch { return; }
        }
    }

    private static async Task<bool> UploadAsync()
    {
        var url = (Environment.GetEnvironmentVariable("FERRARIPOS_CENTRAL_URL")
            ?? Database.GetSetting("central_url", "https://ferraripos-central.onrender.com")).TrimEnd('/');
        var store = Database.GetSetting("central_store_id", "");
        var token = Database.GetSetting("central_token", "");

        if (string.IsNullOrWhiteSpace(store) || string.IsNullOrWhiteSpace(token))
            return false;

        var json = Build();
        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"{url}/api/v1/stores/{Uri.EscapeDataString(store)}/snapshot");
        req.Headers.TryAddWithoutValidation("X-FerrariPOS-Token", token);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var res = await Http.SendAsync(req);
        var ok = res.IsSuccessStatusCode;
        Database.SetSetting("central_snapshot_ok", ok ? "1" : "0");
        Database.SetSetting("central_connected", ok ? "1" : "0");
        if (ok)
        {
            var now = DateTimeOffset.UtcNow.ToString("O");
            Database.SetSetting("central_last_snapshot_utc", now);
            Database.SetSetting("central_last_ok_utc", now);
        }
        return ok;
    }

    private static string Build()
    {
        using var cn = Database.Open();

        long Count(string table)
        {
            try
            {
                using var c = cn.CreateCommand();
                c.CommandText = $"SELECT COUNT(*) FROM {table}";
                return Convert.ToInt64(c.ExecuteScalar() ?? 0);
            }
            catch { return 0; }
        }

        decimal Num(string sql)
        {
            try
            {
                using var c = cn.CreateCommand();
                c.CommandText = sql;
                return Convert.ToDecimal(c.ExecuteScalar() ?? 0);
            }
            catch { return 0; }
        }

        var products = new List<object>();
        try
        {
            using var c = cn.CreateCommand();
            c.CommandText = "SELECT id,barcode,description,sale_price,wholesale_price,cost_price,stock,min_stock,category,unit,is_bulk,active,updated_at FROM products ORDER BY active DESC,description COLLATE NOCASE";
            using var r = c.ExecuteReader();
            while (r.Read())
            {
                products.Add(new
                {
                    id = r.GetInt32(0), barcode = r.GetString(1), description = r.GetString(2),
                    sale_price = r.GetDecimal(3), wholesale_price = r.GetDecimal(4), cost_price = r.GetDecimal(5),
                    stock = r.GetDecimal(6), min_stock = r.GetDecimal(7), category = r.GetString(8), unit = r.GetString(9),
                    is_bulk = r.GetInt32(10) != 0, active = r.GetInt32(11) != 0, updated_at = r.GetString(12)
                });
            }
        }
        catch { }

        var customers = new List<object>();
        try
        {
            using var c = cn.CreateCommand();
            c.CommandText = "SELECT c.id,c.name,c.document,c.phone,c.email,c.address,c.credit_limit,COALESCE((SELECT SUM(CASE WHEN UPPER(a.entry_type) LIKE '%PAGO%' OR UPPER(a.entry_type) LIKE '%ABONO%' THEN -a.amount ELSE a.amount END) FROM customer_accounts a WHERE a.customer_id=c.id),0) FROM customers c WHERE c.active=1 ORDER BY c.name COLLATE NOCASE";
            using var r = c.ExecuteReader();
            while (r.Read())
            {
                customers.Add(new
                {
                    id = r.GetInt32(0), name = r.GetString(1), document = r.GetString(2), phone = r.GetString(3),
                    email = r.GetString(4), address = r.GetString(5), credit_limit = r.GetDecimal(6), debt = r.GetDecimal(7)
                });
            }
        }
        catch { }

        var sales = new List<object>();
        try
        {
            using var c = cn.CreateCommand();
            c.CommandText = "SELECT s.id,s.ticket_no,s.subtotal,s.discount,s.tax,s.total,s.payment_method,s.amount_received,s.change_amount,s.status,s.sale_channel,s.notes,s.created_at,COALESCE(c.name,''),COALESCE(u.username,'') FROM sales s LEFT JOIN customers c ON c.id=s.customer_id LEFT JOIN users u ON u.id=s.user_id ORDER BY s.id DESC LIMIT 1000";
            using var r = c.ExecuteReader();
            while (r.Read())
            {
                sales.Add(new
                {
                    id = r.GetInt64(0), ticket = r.GetInt64(1), subtotal = r.GetDecimal(2), discount = r.GetDecimal(3),
                    tax = r.GetDecimal(4), total = r.GetDecimal(5), payment = r.GetString(6), received = r.GetDecimal(7),
                    change = r.GetDecimal(8), status = r.GetString(9), channel = r.GetString(10), notes = r.GetString(11),
                    date = r.GetString(12), customer = r.GetString(13), cashier = r.GetString(14)
                });
            }
        }
        catch { }

        var items = new List<object>();
        try
        {
            using var c = cn.CreateCommand();
            c.CommandText = "SELECT sale_id,product_id,barcode,description,quantity,unit_price,discount,total FROM sale_items ORDER BY id DESC LIMIT 3000";
            using var r = c.ExecuteReader();
            while (r.Read())
                items.Add(new { sale_id = r.GetInt64(0), product_id = r.GetInt32(1), barcode = r.GetString(2), description = r.GetString(3), quantity = r.GetDecimal(4), unit_price = r.GetDecimal(5), discount = r.GetDecimal(6), total = r.GetDecimal(7) });
        }
        catch { }

        var cash = new Dictionary<string, object?>();
        try
        {
            using var c = cn.CreateCommand();
            c.CommandText = "SELECT cs.id,cs.status,COALESCE(u.username,''),cs.opened_at,cs.closed_at,cs.opening_amount,COALESCE(cs.expected_amount,0),COALESCE(cs.closing_amount,0),COALESCE(cs.difference,0),cs.mercado_pago_enabled,cs.mercado_pago_opening_amount,COALESCE(cs.mercado_pago_expected_amount,0),COALESCE(cs.mercado_pago_closing_amount,0),COALESCE(cs.mercado_pago_difference,0) FROM cash_sessions cs LEFT JOIN users u ON u.id=cs.user_id ORDER BY cs.id DESC LIMIT 1";
            using var r = c.ExecuteReader();
            if (r.Read())
            {
                cash["session_id"] = r.GetInt64(0); cash["status"] = r.GetString(1); cash["cashier"] = r.GetString(2);
                cash["opened_at"] = r.GetString(3); cash["closed_at"] = r.IsDBNull(4) ? "" : r.GetString(4);
                cash["opening_amount"] = r.GetDecimal(5); cash["expected_amount"] = r.GetDecimal(6); cash["closing_amount"] = r.GetDecimal(7);
                cash["difference"] = r.GetDecimal(8); cash["mercado_pago_enabled"] = r.GetInt32(9) != 0;
                cash["mercado_pago_opening"] = r.GetDecimal(10); cash["mercado_pago_expected"] = r.GetDecimal(11);
                cash["mercado_pago_closing"] = r.GetDecimal(12); cash["mercado_pago_difference"] = r.GetDecimal(13);
            }
        }
        catch { }

        var payments = new List<object>();
        try
        {
            using var c = cn.CreateCommand();
            c.CommandText = "SELECT payment_method,COUNT(*),COALESCE(SUM(total),0) FROM sales WHERE status='COMPLETED' GROUP BY payment_method ORDER BY 3 DESC";
            using var r = c.ExecuteReader();
            while (r.Read()) payments.Add(new { method = r.GetString(0), tickets = r.GetInt64(1), amount = r.GetDecimal(2) });
        }
        catch { }

        var stockMoves = new List<object>();
        try
        {
            using var c = cn.CreateCommand();
            c.CommandText = "SELECT sm.id,sm.product_id,COALESCE(p.description,''),sm.movement_type,sm.quantity,sm.reference,sm.created_at FROM stock_movements sm LEFT JOIN products p ON p.id=sm.product_id ORDER BY sm.id DESC LIMIT 500";
            using var r = c.ExecuteReader();
            while (r.Read()) stockMoves.Add(new { id = r.GetInt64(0), product_id = r.GetInt32(1), product = r.GetString(2), type = r.GetString(3), quantity = r.GetDecimal(4), reference = r.GetString(5), date = r.GetString(6) });
        }
        catch { }

        var cashMoves = new List<object>();
        try
        {
            using var c = cn.CreateCommand();
            c.CommandText = "SELECT id,movement_type,concept,amount,payment_method,created_at FROM cash_movements ORDER BY id DESC LIMIT 300";
            using var r = c.ExecuteReader();
            while (r.Read()) cashMoves.Add(new { id = r.GetInt64(0), type = r.GetString(1), concept = r.GetString(2), amount = r.GetDecimal(3), payment_method = r.GetString(4), date = r.GetString(5) });
        }
        catch { }

        var tables = new List<object>();
        try
        {
            using var c = cn.CreateCommand();
            c.CommandText = "SELECT id,name,salon_id,enabled,shape,capacity FROM restaurant_tables ORDER BY salon_id,id";
            using var r = c.ExecuteReader();
            while (r.Read()) tables.Add(new { id = r.GetInt32(0), name = r.GetString(1), salon_id = r.GetInt32(2), enabled = r.GetInt32(3) != 0, shape = r.GetString(4), capacity = r.GetInt32(5) });
        }
        catch { }

        var reservations = new List<object>();
        try
        {
            using var c = cn.CreateCommand();
            c.CommandText = "SELECT id,customer_name,phone,reservation_date,hour,people,table_id,status,notes FROM reservations ORDER BY reservation_date DESC,id DESC LIMIT 200";
            using var r = c.ExecuteReader();
            while (r.Read()) reservations.Add(new { id = r.GetInt64(0), customer = r.GetString(1), phone = r.GetString(2), date = r.GetString(3), hour = r.GetString(4), people = r.GetInt32(5), table_id = r.IsDBNull(6) ? 0 : r.GetInt32(6), status = r.GetString(7), notes = r.GetString(8) });
        }
        catch { }

        var purchaseOrders = new List<object>();
        try
        {
            using var c = cn.CreateCommand();
            c.CommandText = "SELECT id,order_no,status,order_date,expected_date,received_date,total,received_total,notes FROM purchase_orders ORDER BY id DESC LIMIT 200";
            using var r = c.ExecuteReader();
            while (r.Read()) purchaseOrders.Add(new { id = r.GetInt64(0), order_no = r.GetString(1), status = r.GetString(2), order_date = r.GetString(3), expected_date = r.IsDBNull(4) ? "" : r.GetString(4), received_date = r.IsDBNull(5) ? "" : r.GetString(5), total = r.GetDecimal(6), received_total = r.GetDecimal(7), notes = r.GetString(8) });
        }
        catch { }

        var salesTotal = Num("SELECT COALESCE(SUM(total),0) FROM sales WHERE status='COMPLETED'");
        var debt = Num("SELECT COALESCE(SUM(CASE WHEN UPPER(entry_type) LIKE '%PAGO%' OR UPPER(entry_type) LIKE '%ABONO%' THEN -amount ELSE amount END),0) FROM customer_accounts");
        var stock = Num("SELECT COALESCE(SUM(stock),0) FROM products WHERE active=1");
        var low = Num("SELECT COUNT(*) FROM products WHERE active=1 AND min_stock>0 AND stock<=min_stock");
        var now = DateTimeOffset.UtcNow;
        var pairingPayload = WebDashboardServer.GetMobilePairingPayload();
        var pairingCode = WebDashboardServer.GetMobilePairingCode();
        string pairingQrBase64 = "";
        try
        {
            using var qrGenerator = new QRCodeGenerator();
            using var qrData = qrGenerator.CreateQrCode(pairingPayload, QRCodeGenerator.ECCLevel.Q);
            var png = new PngByteQRCode(qrData).GetGraphic(8);
            pairingQrBase64 = Convert.ToBase64String(png);
        }
        catch { }

        return JsonSerializer.Serialize(new
        {
            generated_utc = now,
            business_name = Database.GetSetting("business_name", "FerrariPOS"),
            store_id = Database.GetSetting("central_store_id", ""),
            store_name = Database.GetSetting("business_name", "FerrariPOS"),
            ventas_del_turno = salesTotal, sales_total = salesTotal, total_sales = salesTotal, ventas = salesTotal,
            tickets_del_turno = Count("sales"), sales_count = Count("sales"), ticket_count = Count("sales"),
            productos_activos = Count("products"), products_count = Count("products"), active_products = Count("products"),
            unidades_en_stock = stock, stock_units = stock, total_stock = stock, stock_bajo = low, low_stock = low, low_stock_count = low,
            deuda_clientes = debt, credit_balance = debt, customer_debt = debt,
            cash, pagos = payments, payments, payment_summary = payments,
            products, productos = products, inventory = products, inventario = products,
            customers, clientes = customers,
            sales, recent_sales = sales, tickets = sales, sale_items = items,
            stock_data = new { total_units = stock, low_stock = low, movements = stockMoves },
            tables = new { total = tables.Count, occupied = 0, items = tables },
            operations = new { tables = tables.Count, occupied_tables = 0, reservations = reservations.Count, purchase_orders = purchaseOrders.Count },
            orders = new { reservations, purchase_orders = purchaseOrders },
            activity = stockMoves.Take(100).ToList(), cash_movements = cashMoves, stock_movements = stockMoves,
            cloudflare_url = GetLiveCloudflareUrl(),
            mobile_pairing = new { code = pairingCode, payload = pairingPayload, qr_png_base64 = pairingQrBase64, generated_utc = now },
            technical = new { db_path = Database.DbPath, generated_utc = now, central_connected = Database.GetSetting("central_connected", "0"), snapshot_ok = Database.GetSetting("central_snapshot_ok", "0"), last_central_ok_utc = Database.GetSetting("central_last_ok_utc", ""), last_snapshot_utc = Database.GetSetting("central_last_snapshot_utc", "") }
        });
    }
    private static string GetLiveCloudflareUrl()
    {
        try
        {
            var url = WebDashboardServer.Current?.AccessUrl?.Trim();
            if (!string.IsNullOrWhiteSpace(url) && url.Contains(".trycloudflare.com", StringComparison.OrdinalIgnoreCase))
                return url.TrimEnd('/');
        }
        catch { }
        return "";
    }

}
