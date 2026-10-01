using FerrarisPOS.Data;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text;

namespace FerrarisPOS.Services;

/// <summary>
/// Reporte ejecutivo de cierre. No incluye ventas ticket por ticket.
/// El Excel histórico conserva el detalle completo.
/// </summary>
public static class CashClosingPdfService
{
    private sealed record ProductStat(string Name, double Quantity, double Sales);
    private sealed record StockStat(string Name, double Stock, double MinStock, string Supplier = "");
    private sealed record PaymentStat(string Method, double Amount);
    private sealed record CustomerDeletionDetail(string CreatedAt, string CustomerName, string Document, string Reason, string User);
    private sealed record InventoryAlert(string Name, double Quantity, string Reason, string User, string Reference);
    private sealed record WasteDetail(string CreatedAt, string Product, string Barcode, double Quantity, string Reason, string Notes, string User);
    private sealed record DiscountDetail(long Ticket, string CreatedAt, string User, double Amount, string Reason, string Products);
    private sealed record ExecutiveData(
        string Business, string Cashier, string Opened, string Closed, double Opening, double Expected,
        double Closing, double Difference, bool MercadoPagoEnabled, double MpOpening, double MpExpected, double MpClosing, double MpDifference, double MpRetention, int Tickets, double Sales, double Returns, double AverageTicket,
        List<PaymentStat> Payments, List<SalesCategoryReportService.CategorySales> CategorySales, List<CustomerDeletionDetail> CustomerDeletions,
        List<ProductStat> TopProducts, List<StockStat> LowStock, List<StockStat> Excess, List<StockStat> NeverSold, List<InventoryAlert> InventoryAlerts, List<WasteDetail> WasteDetails, List<DiscountDetail> DiscountDetails,
        double CustomerDebt, double SupplierDebt, double Purchases, double WasteQty, double Credits, double CustomerPayments, double GrossProfit);

    public static string Generate(long sessionId)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var dir = Path.Combine(desktop, "CIERRE DE CAJA");
        Directory.CreateDirectory(dir);
        using var cn = Database.Open();
        var d = Load(cn, sessionId);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        var safe = Sanitize(string.IsNullOrWhiteSpace(d.Cashier) ? "Cajero" : d.Cashier);
        var path = Path.Combine(dir, $"FerrariPOS_Reporte_Cierre_{safe}_{stamp}.pdf");
        MinimalPdf.Write(path, d, "", "", "");
        return path;
    }

    public static string BuildEmailSummary(long sessionId)
    {
        using var cn = Database.Open();
        var d = Load(cn, sessionId);
        var b = new StringBuilder();
        b.AppendLine("FERRARISPOS · CIERRE DE CAJA");
        b.AppendLine($"Cajero: {d.Cashier} · {d.Opened} → {d.Closed}");
        b.AppendLine(new string('─', 52));
        b.AppendLine("VENTAS");
        b.AppendLine($"Total vendido: ${d.Sales:N2} · Tickets: {d.Tickets:N0}");
        b.AppendLine($"Devoluciones: ${d.Returns:N2}");
        b.AppendLine($"Ganancia bruta estimada: ${d.GrossProfit:N2}");
        b.AppendLine();
        AppendCommonProducts(b, cn, sessionId);
        b.AppendLine();
        b.AppendLine("COBROS POR MEDIO");
        foreach (var p in d.Payments) b.AppendLine($"  {p.Method}: ${p.Amount:N2}");
        b.AppendLine();
        AppendCategorySales(b, d.CategorySales);
        b.AppendLine();
        b.AppendLine("CAJA · ARQUEO");
        b.AppendLine($"  Fondo inicial: ${d.Opening:N2}");
        b.AppendLine($"  Efectivo esperado: ${d.Expected:N2}");
        b.AppendLine($"  Efectivo contado: ${d.Closing:N2}");
        b.AppendLine($"  Diferencia: {(d.Difference >= 0 ? "+" : "")}${d.Difference:N2}");
        if (d.MercadoPagoEnabled)
        {
            b.AppendLine($"  Mercado Pago esperado: ${d.MpExpected:N2}");
            b.AppendLine($"  Mercado Pago informado: ${d.MpClosing:N2}");
            b.AppendLine($"  Diferencia MP: {(d.MpDifference >= 0 ? "+" : "")}${d.MpDifference:N2}");
        }
        b.AppendLine();
        b.AppendLine("INGRESOS / EGRESOS DEL TURNO");
        AppendCashMovements(b, cn, sessionId);
        b.AppendLine();
        b.AppendLine("FERRARIPOS MANAGER · MOVIMIENTOS DESDE ANDROID");
        AppendAndroidManagerActivity(b, cn, sessionId);
        b.AppendLine();
        AppendAndroidStockAdjustments(b, cn, sessionId);
        b.AppendLine();
        b.AppendLine("CLIENTES CON DEUDA / ABONOS DEL DÍA");
        AppendCustomerDebtAndPayments(b, cn, DateTime.Now);
        b.AppendLine();
        b.AppendLine($"MERMAS: {d.WasteQty:N3} unidades registradas");
        if (d.WasteDetails.Count == 0)
            b.AppendLine("  Sin mermas registradas en el turno.");
        else
            foreach (var w in d.WasteDetails)
                b.AppendLine($"  {w.CreatedAt} · {w.Product} · Código: {w.Barcode} · Cantidad: {w.Quantity:N3} · Motivo: {w.Reason} · Observación: {w.Notes} · Responsable: {w.User}");
        b.AppendLine();
        AppendCustomerDeletions(b, d.CustomerDeletions);
        b.AppendLine();
        b.AppendLine("⚠ DESCUENTOS OTORGADOS · MOTIVO Y DETALLE");
        if (d.DiscountDetails.Count == 0) b.AppendLine("  Sin descuentos otorgados en el turno.");
        else foreach (var x in d.DiscountDetails)
        {
            b.AppendLine($"  ⚠ Ticket #{x.Ticket} · {x.CreatedAt} · Responsable: {x.User} · Descuento: ${x.Amount:N2} · Motivo: {x.Reason}");
            if (!string.IsNullOrWhiteSpace(x.Products)) b.AppendLine($"    Productos afectados: {x.Products}");
        }
        b.AppendLine();
        b.AppendLine($"PARA MAÑANA: ${d.Closing:N2} en efectivo" + (d.MercadoPagoEnabled ? $" · ${d.MpClosing:N2} en Mercado Pago" : "") + ".");
        b.AppendLine();
        b.AppendLine("El Excel del cierre conserva el detalle completo.");
        return b.ToString();
    }

    private static void AppendCommonProducts(StringBuilder b, Microsoft.Data.Sqlite.SqliteConnection cn, long sessionId)
    {
        b.AppendLine("PRODUCTOS EN COMÚN · OPERACIONES MANUALES");
        using var q = cn.CreateCommand();
        q.CommandText = "SELECT s.ticket_no,si.description,si.quantity,si.unit_price,si.total FROM sale_items si JOIN sales s ON s.id=si.sale_id WHERE s.session_id=$s AND s.status='COMPLETED' AND (si.barcode='COMÚN' OR si.product_id=(SELECT id FROM products WHERE barcode='__COMUN__' LIMIT 1)) ORDER BY s.ticket_no,si.id";
        q.Parameters.AddWithValue("$s", sessionId);
        using var r = q.ExecuteReader();
        var any=false;
        while(r.Read())
        {
            any=true;
            b.AppendLine($"  Ticket #{r.GetInt64(0)} · {r.GetString(1)} · Cantidad: {r.GetDouble(2):N2} · Precio: ${r.GetDouble(3):N2} · Total: ${r.GetDouble(4):N2}");
        }
        if(!any) b.AppendLine("  Sin productos en común en este turno.");
    }

    private static void AppendAndroidManagerActivity(StringBuilder b, Microsoft.Data.Sqlite.SqliteConnection cn, long sessionId)
    {
        var any=false;
        using(var q=cn.CreateCommand())
        {
            q.CommandText="SELECT COUNT(*),COALESCE(SUM(total),0) FROM sales WHERE session_id=$s AND status='COMPLETED' AND sale_channel='ANDROID'"; q.Parameters.AddWithValue("$s",sessionId); using var r=q.ExecuteReader(); if(r.Read()){any=r.GetInt64(0)>0; b.AppendLine($"  Ventas cobradas desde Android: {r.GetInt64(0):N0} · Total: ${r.GetDouble(1):N2}");}
        }
        using(var q=cn.CreateCommand())
        {
            q.CommandText="SELECT COUNT(*),COALESCE(SUM(amount),0) FROM customer_accounts WHERE entry_type='PAYMENT' AND concept LIKE '%Manager%' AND created_at >= (SELECT opened_at FROM cash_sessions WHERE id=$s) AND created_at <= (SELECT COALESCE(closed_at,CURRENT_TIMESTAMP) FROM cash_sessions WHERE id=$s)"; q.Parameters.AddWithValue("$s",sessionId); using var r=q.ExecuteReader(); if(r.Read()){var n=r.GetInt64(0);var total=r.GetDouble(1); if(n>0) any=true; b.AppendLine($"  Abonos de clientes desde Android: {n:N0} · Total: ${total:N2}");}
        }
        using(var q=cn.CreateCommand())
        {
            q.CommandText="SELECT action,COUNT(*) FROM audit_log WHERE created_at >= (SELECT opened_at FROM cash_sessions WHERE id=$s) AND created_at <= (SELECT COALESCE(closed_at,CURRENT_TIMESTAMP) FROM cash_sessions WHERE id=$s) AND action LIKE 'MOBILE_%' GROUP BY action ORDER BY action"; q.Parameters.AddWithValue("$s",sessionId); using var r=q.ExecuteReader(); while(r.Read()){any=true; b.AppendLine($"  {r.GetString(0)}: {r.GetInt64(1):N0} operación(es)");}
        }
        using(var q=cn.CreateCommand())
        {
            q.CommandText="SELECT action,details FROM audit_log WHERE created_at >= (SELECT opened_at FROM cash_sessions WHERE id=$s) AND created_at <= (SELECT COALESCE(closed_at,CURRENT_TIMESTAMP) FROM cash_sessions WHERE id=$s) AND action IN ('MOBILE_STOCK_ADJUSTMENT','MOBILE_SALE_RETURN','MOBILE_TICKET_CANCEL','MOBILE_CUSTOMER_PAYMENT') ORDER BY created_at"; q.Parameters.AddWithValue("$s",sessionId); using var r=q.ExecuteReader(); while(r.Read()){any=true; b.AppendLine($"  {r.GetString(0)} · {r.GetString(1)}");}
        }
        using(var q=cn.CreateCommand())
        {
            q.CommandText="SELECT datetime(a.created_at,'localtime'),COALESCE(u.full_name,u.username,'SISTEMA'),a.details FROM audit_log a LEFT JOIN users u ON u.id=a.user_id WHERE a.created_at >= (SELECT opened_at FROM cash_sessions WHERE id=$s) AND a.created_at <= (SELECT COALESCE(closed_at,CURRENT_TIMESTAMP) FROM cash_sessions WHERE id=$s) AND a.action='MOBILE_PROMOTION_DELETE' ORDER BY a.id"; q.Parameters.AddWithValue("$s",sessionId); using var r=q.ExecuteReader(); while(r.Read()){any=true; b.AppendLine($"  PROMOCIÓN ELIMINADA · {r.GetString(0)} · Usuario: {r.GetString(1)} · {r.GetString(2)}");}
        }
        using(var q=cn.CreateCommand())
        {
            q.CommandText="SELECT datetime(a.created_at,'localtime'),COALESCE(u.full_name,u.username,'SISTEMA'),a.details FROM audit_log a LEFT JOIN users u ON u.id=a.user_id WHERE a.created_at >= (SELECT opened_at FROM cash_sessions WHERE id=$s) AND a.created_at <= (SELECT COALESCE(closed_at,CURRENT_TIMESTAMP) FROM cash_sessions WHERE id=$s) AND a.action='PROMOTION_DELETE' ORDER BY a.id"; q.Parameters.AddWithValue("$s",sessionId); using var r=q.ExecuteReader(); while(r.Read()){any=true; b.AppendLine($"  PROMOCIÓN ELIMINADA EN WINDOWS · {r.GetString(0)} · Usuario: {r.GetString(1)} · {r.GetString(2)}");}
        }
        if(!any) b.AppendLine("  Sin operaciones realizadas desde FerrariPOS Manager en este turno.");
    }

    private static void AppendAndroidStockAdjustments(StringBuilder b, Microsoft.Data.Sqlite.SqliteConnection cn, long sessionId)
    {
        b.AppendLine("REDUCCIONES DE STOCK · MOTIVO Y DETALLE");
        using var q = cn.CreateCommand();
        q.CommandText = "SELECT datetime(sm.created_at,'localtime'),p.description,sm.quantity,COALESCE(sm.reference,''),COALESCE(u.full_name,u.username,'SISTEMA') FROM stock_movements sm JOIN products p ON p.id=sm.product_id LEFT JOIN users u ON u.id=sm.user_id WHERE sm.movement_type='ADJUSTMENT_OUT' AND sm.created_at >= (SELECT opened_at FROM cash_sessions WHERE id=$s) AND sm.created_at <= (SELECT COALESCE(closed_at,CURRENT_TIMESTAMP) FROM cash_sessions WHERE id=$s) ORDER BY sm.id";
        q.Parameters.AddWithValue("$s", sessionId);
        using var r = q.ExecuteReader();
        var any = false;
        while (r.Read())
        {
            any = true;
            b.AppendLine($"  {r.GetString(0)} · Producto: {r.GetString(1)} · Cantidad salida: {r.GetDouble(2):N3} · Motivo: {r.GetString(3)} · Responsable: {r.GetString(4)}");
        }
        if (!any) b.AppendLine("  Sin reducciones de stock realizadas desde la modificación de productos en este turno.");
    }

    private static void AppendCustomerDebtAndPayments(StringBuilder b, Microsoft.Data.Sqlite.SqliteConnection cn, DateTime day)
    {
        var d = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        using var q = cn.CreateCommand();
        q.CommandText = """
            SELECT c.name,
                   ROUND(COALESCE(SUM(CASE WHEN ca.entry_type='SALE' THEN ca.amount WHEN ca.entry_type='PAYMENT' THEN -ca.amount ELSE 0 END),0),2) AS deuda_actual,
                   ROUND(COALESCE((SELECT SUM(p2.amount) FROM customer_accounts p2 WHERE p2.customer_id=c.id AND p2.entry_type='PAYMENT' AND date(p2.created_at,'localtime')=$d),0),2) AS abonos_hoy
            FROM customers c
            LEFT JOIN customer_accounts ca ON ca.customer_id=c.id
            WHERE c.active=1
            GROUP BY c.id,c.name
            HAVING deuda_actual > 0.005 OR abonos_hoy > 0.005
            ORDER BY CASE WHEN abonos_hoy > 0.005 THEN 0 ELSE 1 END, deuda_actual DESC, c.name
            """;
        q.Parameters.AddWithValue("$d", d);
        using var r = q.ExecuteReader();
        var any = false;
        while (r.Read())
        {
            any = true;
            var name = r.GetString(0);
            var debt = r.GetDouble(1);
            var paid = r.GetDouble(2);
            b.AppendLine($"  {name} | Deuda actual: ${debt:N2} | Abonos hoy: ${paid:N2} | Estado: {(debt > 0.005 ? "PENDIENTE" : "AL DÍA")}");
        }
        if (!any) b.AppendLine("  No hay clientes con deuda pendiente ni abonos registrados hoy.");
        using var total = cn.CreateCommand();
        total.CommandText = "SELECT COALESCE(SUM(amount),0) FROM customer_accounts WHERE date(created_at,'localtime')=$d AND entry_type='PAYMENT'";
        total.Parameters.AddWithValue("$d", d);
        b.AppendLine($"  TOTAL ABONOS DE CLIENTES HOY: ${Convert.ToDouble(total.ExecuteScalar() ?? 0):N2}");
    }

    private static void AppendCashMovements(StringBuilder b, Microsoft.Data.Sqlite.SqliteConnection cn, long sessionId)
    {
        using var q=cn.CreateCommand();
        q.CommandText="SELECT movement_type,payment_method,concept,amount FROM cash_movements WHERE session_id=$s AND movement_type IN ('INCOME','EXPENSE') AND COALESCE(voided,0)=0 ORDER BY id";
        q.Parameters.AddWithValue("$s",sessionId); using var r=q.ExecuteReader(); var any=false;
        while(r.Read())
        {
            any=true;
            var tipo=r.GetString(0)=="INCOME" ? "INGRESO" : "EGRESO";
            b.AppendLine($"  {tipo} · {r.GetString(1)} · ${r.GetDouble(3):N2} · {r.GetString(2)}");
        }
        if(!any) b.AppendLine("  Sin ingresos ni egresos manuales.");
        b.AppendLine();
        b.AppendLine("MOVIMIENTOS ANULADOS");
        using var vq=cn.CreateCommand();
        vq.CommandText="SELECT cm.created_at,cm.movement_type,cm.payment_method,cm.concept,cm.amount,COALESCE(cm.void_reason,''),COALESCE(v.full_name,v.username,'') FROM cash_movements cm LEFT JOIN users v ON v.id=cm.voided_by WHERE cm.session_id=$s AND cm.movement_type IN ('INCOME','EXPENSE') AND COALESCE(cm.voided,0)=1 ORDER BY cm.id";
        vq.Parameters.AddWithValue("$s",sessionId); using var vr=vq.ExecuteReader(); var anyVoided=false;
        while(vr.Read()){anyVoided=true; var tipo=vr.GetString(1)=="INCOME"?"INGRESO":"EGRESO"; b.AppendLine($"  {vr.GetString(0)} · {tipo} · {vr.GetString(2)} · ${vr.GetDouble(4):N2} · {vr.GetString(3)} · Motivo: {vr.GetString(5)} · Anuló: {vr.GetString(6)}");}
        if(!anyVoided) b.AppendLine("  Sin movimientos anulados.");
    }


    private static ExecutiveData Load(Microsoft.Data.Sqlite.SqliteConnection cn, long sessionId)
    {
        string cashier = "", opened = "", closed = ""; double opening = 0, expected = 0, closing = 0, difference = 0; bool mpEnabled = false; double mpOpening = 0, mpExpected = 0, mpClosing = 0, mpDifference = 0, mpRetention = 0;
        using (var q = cn.CreateCommand())
        {
            q.CommandText = "SELECT COALESCE(u.full_name,''),cs.opened_at,COALESCE(cs.closed_at,''),cs.opening_amount,COALESCE(cs.expected_amount,0),COALESCE(cs.closing_amount,0),COALESCE(cs.difference,0),COALESCE(cs.mercado_pago_enabled,0),COALESCE(cs.mercado_pago_opening_amount,0),COALESCE(cs.mercado_pago_expected_amount,0),COALESCE(cs.mercado_pago_closing_amount,0),COALESCE(cs.mercado_pago_difference,0),COALESCE(cs.mercado_pago_retention_percent,0) FROM cash_sessions cs LEFT JOIN users u ON u.id=cs.user_id WHERE cs.id=$s";
            q.Parameters.AddWithValue("$s", sessionId); using var r = q.ExecuteReader();
            if (!r.Read()) throw new InvalidOperationException("No se encontró el cierre de caja.");
            cashier = r.GetString(0); opened = r.GetString(1); closed = r.GetString(2); opening = r.GetDouble(3); expected = r.GetDouble(4); closing = r.GetDouble(5); difference = r.GetDouble(6); mpEnabled=r.GetInt32(7)!=0; mpOpening=r.GetDouble(8); mpExpected=r.GetDouble(9); mpClosing=r.GetDouble(10); mpDifference=r.GetDouble(11); mpRetention=r.GetDouble(12);
        }
        var tickets = Scalar(cn, "SELECT COUNT(*) FROM sales WHERE session_id=$s AND status='COMPLETED'", sessionId);
        var sales = Scalar(cn, "SELECT COALESCE(SUM(total),0) FROM sales WHERE session_id=$s AND status='COMPLETED'", sessionId);
        var returns = Scalar(cn, "SELECT COALESCE(SUM(r.amount),0) FROM sale_returns r JOIN sales s ON s.id=r.sale_id WHERE s.session_id=$s", sessionId);
        var waste = Scalar(cn, "SELECT COALESCE(SUM(w.quantity),0) FROM waste_records w WHERE w.created_at >= (SELECT opened_at FROM cash_sessions WHERE id=$s) AND w.created_at <= (SELECT COALESCE(closed_at,CURRENT_TIMESTAMP) FROM cash_sessions WHERE id=$s)", sessionId);
        var purchases = Scalar(cn, "SELECT COALESCE(SUM(total),0) FROM purchase_orders WHERE received_date >= (SELECT opened_at FROM cash_sessions WHERE id=$s) AND received_date <= (SELECT COALESCE(closed_at,CURRENT_TIMESTAMP) FROM cash_sessions WHERE id=$s)", sessionId);
        var supplierDebt = Scalar(cn, "SELECT COALESCE(SUM(total-paid),0) FROM supplier_invoices WHERE status<>'PAID'", sessionId);
        var customerCredits = Scalar(cn, "SELECT COALESCE(SUM(amount),0) FROM customer_accounts WHERE entry_type='SALE' AND created_at >= (SELECT opened_at FROM cash_sessions WHERE id=$s) AND created_at <= (SELECT COALESCE(closed_at,CURRENT_TIMESTAMP) FROM cash_sessions WHERE id=$s)", sessionId);
        var customerPayments = Scalar(cn, "SELECT COALESCE(SUM(amount),0) FROM customer_accounts WHERE entry_type='PAYMENT' AND created_at >= (SELECT opened_at FROM cash_sessions WHERE id=$s) AND created_at <= (SELECT COALESCE(closed_at,CURRENT_TIMESTAMP) FROM cash_sessions WHERE id=$s)", sessionId);
        var customerDebt = Scalar(cn, "SELECT COALESCE(SUM(CASE WHEN entry_type='SALE' THEN amount WHEN entry_type='PAYMENT' THEN -amount ELSE 0 END),0) FROM customer_accounts", sessionId);

        var payments = new List<PaymentStat>();
        using (var q = cn.CreateCommand())
        {
            q.CommandText = "SELECT p.method,COALESCE(SUM(p.amount),0) FROM payments p JOIN sales s ON s.id=p.sale_id WHERE s.session_id=$s AND s.status='COMPLETED' AND p.status='APPROVED' GROUP BY p.method ORDER BY SUM(p.amount) DESC";
            q.Parameters.AddWithValue("$s", sessionId); using var r = q.ExecuteReader(); while (r.Read()) payments.Add(new(r.GetString(0), r.GetDouble(1)));
        }
        var categorySales = SalesCategoryReportService.ForSession(cn, sessionId);
        var customerDeletions = new List<CustomerDeletionDetail>();
        using (var q = cn.CreateCommand())
        {
            q.CommandText = "SELECT d.created_at,d.customer_name,COALESCE(d.customer_document,''),d.reason,COALESCE(u.full_name,u.username,'') FROM customer_deletion_log d LEFT JOIN users u ON u.id=d.user_id WHERE d.created_at >= (SELECT opened_at FROM cash_sessions WHERE id=$s) AND d.created_at <= (SELECT COALESCE(closed_at,CURRENT_TIMESTAMP) FROM cash_sessions WHERE id=$s) ORDER BY d.id";
            q.Parameters.AddWithValue("$s", sessionId); using var r = q.ExecuteReader(); while (r.Read()) customerDeletions.Add(new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4)));
        }
        var top = new List<ProductStat>();
        using (var q = cn.CreateCommand())
        {
            q.CommandText = "SELECT si.description,COALESCE(SUM(si.quantity),0),COALESCE(SUM(si.total),0) FROM sale_items si JOIN sales s ON s.id=si.sale_id WHERE s.session_id=$s AND s.status='COMPLETED' GROUP BY si.product_id,si.description ORDER BY SUM(si.quantity) DESC LIMIT 10";
            q.Parameters.AddWithValue("$s", sessionId); using var r = q.ExecuteReader(); while (r.Read()) top.Add(new(r.GetString(0), r.GetDouble(1), r.GetDouble(2)));
        }
        var low = new List<StockStat>();
        using (var q = cn.CreateCommand())
        {
            q.CommandText = "SELECT p.description,p.stock,p.min_stock,COALESCE((SELECT s.name FROM supplier_products sp JOIN suppliers s ON s.id=sp.supplier_id WHERE sp.product_id=p.id ORDER BY sp.unit_cost LIMIT 1),'Sin proveedor asignado') FROM products p WHERE p.active=1 AND p.barcode<>'__COMUN__' AND p.uses_inventory=1 AND p.stock<=p.min_stock ORDER BY p.stock-p.min_stock ASC,p.description LIMIT 12";
            using var r = q.ExecuteReader(); while (r.Read()) low.Add(new(r.GetString(0), r.GetDouble(1), r.GetDouble(2), r.GetString(3)));
        }
        var excess = new List<StockStat>();
        using (var q = cn.CreateCommand())
        {
            q.CommandText = "SELECT p.description,p.stock,p.min_stock FROM products p WHERE p.active=1 AND p.barcode<>'__COMUN__' AND p.uses_inventory=1 AND p.stock>0 AND p.stock>p.min_stock*2 AND NOT EXISTS (SELECT 1 FROM sale_items si JOIN sales s ON s.id=si.sale_id WHERE si.product_id=p.id AND s.status='COMPLETED' AND date(s.created_at,'localtime')>=date('now','localtime','-30 day')) ORDER BY p.stock DESC LIMIT 12";
            using var r = q.ExecuteReader(); while (r.Read()) excess.Add(new(r.GetString(0), r.GetDouble(1), r.GetDouble(2)));
        }
        var never = new List<StockStat>();
        using (var q = cn.CreateCommand())
        {
            q.CommandText = "SELECT p.description,p.stock,p.min_stock FROM products p WHERE p.active=1 AND p.barcode<>'__COMUN__' AND p.uses_inventory=1 AND p.stock>=0 AND NOT EXISTS (SELECT 1 FROM sale_items si JOIN sales s ON s.id=si.sale_id WHERE si.product_id=p.id AND s.status='COMPLETED') ORDER BY p.stock DESC,p.description LIMIT 12";
            using var r = q.ExecuteReader(); while (r.Read()) never.Add(new(r.GetString(0), r.GetDouble(1), r.GetDouble(2)));
        }
        var wasteDetails = new List<WasteDetail>();
        using (var q = cn.CreateCommand())
        {
            q.CommandText = """
                SELECT w.created_at, p.description, COALESCE(p.barcode,''), w.quantity,
                       COALESCE(w.reason,''), COALESCE(w.notes,''), COALESCE(u.full_name,'')
                FROM waste_records w
                JOIN products p ON p.id=w.product_id
                LEFT JOIN users u ON u.id=w.user_id
                WHERE w.created_at >= (SELECT opened_at FROM cash_sessions WHERE id=$s)
                  AND w.created_at <= (SELECT COALESCE(closed_at,CURRENT_TIMESTAMP) FROM cash_sessions WHERE id=$s)
                ORDER BY w.id
                """;
            q.Parameters.AddWithValue("$s", sessionId);
            using var r = q.ExecuteReader();
            while (r.Read()) wasteDetails.Add(new(r.GetString(0), r.GetString(1), r.GetString(2), r.GetDouble(3), r.GetString(4), r.GetString(5), r.GetString(6)));
        }

        var alerts = new List<InventoryAlert>();
        using (var q = cn.CreateCommand())
        {
            q.CommandText = "SELECT sm.created_at,p.description,sm.quantity,sm.movement_type,COALESCE(u.full_name,''),COALESCE(sm.reference,'') FROM stock_movements sm JOIN products p ON p.id=sm.product_id LEFT JOIN users u ON u.id=sm.user_id WHERE sm.quantity>0 AND (UPPER(sm.movement_type) IN ('CONTEO_FISICO','AJUSTE','AJUSTE_STOCK','SALIDA','BAJA') OR UPPER(sm.reference) LIKE '%AJUST%') AND sm.created_at >= (SELECT opened_at FROM cash_sessions WHERE id=$s) AND sm.created_at <= (SELECT COALESCE(closed_at,CURRENT_TIMESTAMP) FROM cash_sessions WHERE id=$s) ORDER BY sm.id DESC LIMIT 20";
            q.Parameters.AddWithValue("$s", sessionId); using var r = q.ExecuteReader(); while (r.Read()) alerts.Add(new(r.GetString(1), r.GetDouble(2), r.GetString(3), r.GetString(4), r.GetString(5)));
        }
        var discountDetails = new List<DiscountDetail>();
        using (var q = cn.CreateCommand())
        {
            q.CommandText = """
                SELECT s.ticket_no,s.created_at,COALESCE(u.full_name,''),s.discount,COALESCE(s.discount_reason,''),
                       COALESCE(GROUP_CONCAT(CASE WHEN si.discount>0.005 THEN si.description || ' ($' || printf('%.2f',si.discount) || ')' END, ', '),'')
                FROM sales s LEFT JOIN users u ON u.id=s.user_id LEFT JOIN sale_items si ON si.sale_id=s.id
                WHERE s.session_id=$s AND s.status='COMPLETED' AND s.discount>0.005
                GROUP BY s.id ORDER BY s.id
                """;
            q.Parameters.AddWithValue("$s", sessionId);
            using var r=q.ExecuteReader();
            while(r.Read()) discountDetails.Add(new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetDouble(3),r.GetString(4),r.GetString(5)));
        }
        var business = Database.GetSetting("business_name", "Ferrari's Punto de Venta");
        var costOfGoods = Scalar(cn, """
            SELECT COALESCE(SUM(MAX(0, si.quantity-COALESCE(si.returned_quantity,0)) * COALESCE(p.cost_price,0)),0)
            FROM sale_items si JOIN sales s ON s.id=si.sale_id JOIN products p ON p.id=si.product_id
            WHERE s.session_id=$s AND s.status='COMPLETED'
            """, sessionId);
        var grossProfit = sales - returns - costOfGoods;
        return new(business, cashier, opened, closed, opening, expected, closing, difference, mpEnabled, mpOpening, mpExpected, mpClosing, mpDifference, mpRetention, Convert.ToInt32(tickets), sales, returns, tickets > 0 ? sales / tickets : 0, payments, categorySales, customerDeletions, top, low, excess, never, alerts, wasteDetails, discountDetails, customerDebt, supplierDebt, purchases, waste, customerCredits, customerPayments, grossProfit);
    }

    private static void AppendCategorySales(StringBuilder b, List<SalesCategoryReportService.CategorySales> items)
    {
        b.AppendLine("VENTAS POR CATEGORÍA / DEPARTAMENTO · DINERO VENDIDO");
        if (items.Count == 0) { b.AppendLine("  Sin ventas clasificadas por categoría."); return; }
        foreach (var x in items)
            b.AppendLine($"  {x.Category}: EFECTIVO ${x.Cash:N2} + MERCADO PAGO ${x.MercadoPago:N2} + TARJETA ${x.Card:N2} + TRANSFERENCIA ${x.Transfer:N2} + CRÉDITO ${x.Credit:N2} = TOTAL VENDIDO ${x.Total:N2}");
        b.AppendLine($"  TOTAL DE TODAS LAS CATEGORÍAS: ${items.Sum(x => x.Total):N2}");
    }

    private static void AppendCustomerDeletions(StringBuilder b, List<CustomerDeletionDetail> items)
    {
        b.AppendLine("CLIENTES ELIMINADOS · MOTIVO");
        if (items.Count == 0) { b.AppendLine("  Sin clientes eliminados durante el turno."); return; }
        foreach (var x in items)
            b.AppendLine($"  {x.CreatedAt} · {x.CustomerName} · Documento: {x.Document} · Motivo: {x.Reason} · Responsable: {x.User}");
    }

    private static void AppendTop(StringBuilder b, string title, List<ProductStat> items, int max, Func<ProductStat,string> fmt)
    { b.AppendLine(title + ":"); if (items.Count == 0) { b.AppendLine("  Sin datos."); return; } foreach (var x in items.Take(max)) b.AppendLine("  " + fmt(x)); }
    private static void AppendStock(StringBuilder b, string title, List<StockStat> items, int max)
    { b.AppendLine(title + ":"); if (items.Count == 0) { b.AppendLine("  Ninguno."); return; } foreach (var x in items.Take(max)) b.AppendLine($"  {x.Name}: stock {x.Stock:N2} / mínimo {x.MinStock:N2} · proveedor: {x.Supplier}"); }

    private static double Scalar(Microsoft.Data.Sqlite.SqliteConnection cn, string sql, long id)
    { using var q = cn.CreateCommand(); q.CommandText = sql; q.Parameters.AddWithValue("$s", id); return Convert.ToDouble(q.ExecuteScalar() ?? 0); }
    private static string Sanitize(string s) { foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_'); return s.Replace(' ', '_'); }
    private static void SafeDelete(string p) { try { if (File.Exists(p)) File.Delete(p); } catch { } }

    private static void CreateBarChart(List<PaymentStat> data, string path, string title)
    {
        using var bmp = new Bitmap(1000, 520); using var g = Graphics.FromImage(bmp); g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.White);
        using var titleFont = new Font("Segoe UI", 20, FontStyle.Bold); using var font = new Font("Segoe UI", 11); using var brush = new SolidBrush(Color.FromArgb(35,35,40));
        g.DrawString(title, titleFont, brush, 30, 22); if (data.Count == 0) { g.DrawString("Sin datos", font, brush, 30, 90); bmp.Save(path, ImageFormat.Jpeg); return; }
        var max = Math.Max(1, data.Max(x => x.Amount)); var left=80; var bottom=455; var chartH=320; var bw=Math.Max(35,(880/data.Count)-15);
        for(int i=0;i<data.Count;i++){var x=left+i*(880/data.Count)+10;var h=(int)(chartH*data[i].Amount/max);var y=bottom-h;using var b=new SolidBrush(Color.FromArgb(35,120,210));g.FillRectangle(b,x,y,bw,h);g.DrawString(data[i].Method, font, brush, x, bottom+12);g.DrawString(data[i].Amount.ToString("N0",CultureInfo.CurrentCulture),font,brush,x,y-24);}
        bmp.Save(path, ImageFormat.Jpeg);
    }

    private static void CreateTopChart(List<ProductStat> data, string path, string title)
    {
        using var bmp = new Bitmap(1000, 620); using var g = Graphics.FromImage(bmp); g.SmoothingMode = SmoothingMode.AntiAlias; g.Clear(Color.White);
        using var titleFont = new Font("Segoe UI", 20, FontStyle.Bold); using var font = new Font("Segoe UI", 10); using var brush = new SolidBrush(Color.FromArgb(35,35,40));
        g.DrawString(title, titleFont, brush, 30, 20); if (data.Count == 0) { g.DrawString("Sin ventas",font,brush,30,90);bmp.Save(path,ImageFormat.Jpeg);return; }
        var max=data.Max(x=>x.Quantity); var top=85; var rowH=Math.Max(38,500/Math.Max(1,data.Count));
        for(int i=0;i<data.Count;i++){var y=top+i*rowH;var w=(int)(700*(data[i].Quantity/Math.Max(1,max)));g.DrawString($"{i+1}. {Trim(data[i].Name,34)}",font,brush,25,y);using var b=new SolidBrush(Color.FromArgb(25,160,100));g.FillRectangle(b,310,y,w,24);g.DrawString(data[i].Quantity.ToString("N2"),font,brush,320+w,y+2);}
        bmp.Save(path,ImageFormat.Jpeg);
    }

    private static void CreatePieChart(List<PaymentStat> data, string path, string title)
    {
        using var bmp=new Bitmap(1000,560);using var g=Graphics.FromImage(bmp);g.SmoothingMode=SmoothingMode.AntiAlias;g.Clear(Color.White);using var titleFont=new Font("Segoe UI",20,FontStyle.Bold);using var font=new Font("Segoe UI",11);using var brush=new SolidBrush(Color.FromArgb(35,35,40));g.DrawString(title,titleFont,brush,30,20);
        var total=data.Sum(x=>x.Amount);if(total<=0){g.DrawString("Sin datos",font,brush,30,90);bmp.Save(path,ImageFormat.Jpeg);return;}
        var colors=new[]{Color.FromArgb(35,120,210),Color.FromArgb(25,160,100),Color.FromArgb(235,155,45),Color.FromArgb(180,75,150),Color.FromArgb(220,70,70),Color.FromArgb(90,90,100)};float start=0;var rect=new Rectangle(60,100,380,380);
        for(int i=0;i<data.Count;i++){var sweep=(float)(360*data[i].Amount/total);using var b=new SolidBrush(colors[i%colors.Length]);g.FillPie(b,rect,start,sweep);start+=sweep;var pct=100*data[i].Amount/total;g.DrawString($"{data[i].Method}: {pct:N1}%",font,brush,500,120+i*45);}
        bmp.Save(path,ImageFormat.Jpeg);
    }

    private static string Trim(string s,int n)=>s.Length<=n?s:s.Substring(0,n-1)+"…";

    private static class MinimalPdf
    {
        private sealed class Obj { public string Body = ""; public byte[]? Stream; }

        // PDF rasterizado intencionalmente: todo el contenido de cada página se compone
        // primero como una imagen. Esto evita que lectores PDF incompatibles oculten
        // texto o XObjects y garantiza que gráficos + datos sean visibles.
        public static void Write(string path, ExecutiveData d, string bar, string pie, string top)
        {
            var temp = Path.Combine(Path.GetTempPath(), "FerrariPOS_PDF_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                var pages = new[] { RenderCompactPage(d, temp) };
                WriteImagePdf(path, pages);
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { }
            }
        }

        private static string RenderCompactPage(ExecutiveData d, string dir)
        {
            var file = Path.Combine(dir, "page1.jpg");
            using var bmp = NewPage(); using var g = Graphics.FromImage(bmp);
            DrawHeader(g, d.Business, "CIERRE DE CAJA · RESUMEN");
            using var h = new Font("Segoe UI", 13, FontStyle.Bold);
            using var f = new Font("Segoe UI", 10);
            using var small = new Font("Segoe UI", 9);
            using var ink = new SolidBrush(Color.FromArgb(35,35,40));
            g.DrawString($"Cajero: {d.Cashier}    {d.Opened} → {d.Closed}", f, ink, 45, 105);
            DrawMetric(g,45,140,250,"TOTAL VENDIDO",$"${d.Sales:N2}");
            DrawMetric(g,315,140,250,"GANANCIA BRUTA EST.",$"${d.GrossProfit:N2}");
            DrawMetric(g,585,140,250,"TICKETS",d.Tickets.ToString("N0"));
            var y=225;
            g.DrawString("COBROS POR MEDIO", h, ink, 45, y); y+=30;
            foreach(var p in d.Payments) { g.DrawString($"• {p.Method}: ${p.Amount:N2}",f,ink,55,y); y+=24; }
            g.DrawString("VENTAS POR CATEGORÍA / DEPARTAMENTO", h, ink, 45, y+10); y+=40;
            if(d.CategorySales.Count==0) { g.DrawString("• Sin ventas clasificadas por categoría.",f,ink,55,y); y+=22; }
            else foreach(var x in d.CategorySales.Take(20))
            {
                g.DrawString($"• {Trim(x.Category,26)} · EF ${x.Cash:N2} · MP ${x.MercadoPago:N2} · TOTAL ${x.Total:N2}",f,ink,55,y); y+=19;
                if(y>760) break;
            }
            if(d.CategorySales.Count>0) { g.DrawString($"TOTAL CATEGORÍAS: ${d.CategorySales.Sum(x=>x.Total):N2}",f,ink,55,y); y+=24; }
            g.DrawString("CAJA · ARQUEO", h, ink, 45, y+10); y+=42;
            g.DrawString($"Fondo inicial: ${d.Opening:N2}",f,ink,55,y); y+=24;
            g.DrawString($"Efectivo esperado: ${d.Expected:N2}",f,ink,55,y); y+=24;
            g.DrawString($"Efectivo contado: ${d.Closing:N2}",f,ink,55,y); y+=24;
            g.DrawString($"Diferencia: {(d.Difference>=0?"+":"")}${d.Difference:N2}",f,ink,55,y); y+=24;
            g.DrawString($"PARA MAÑANA: ${d.Closing:N2} EN EFECTIVO" + (d.MercadoPagoEnabled ? $" · MP ${d.MpClosing:N2}" : ""),h,ink,55,y); y+=38;
            g.DrawString("CLIENTES ELIMINADOS", h, ink, 45, y); y+=26;
            if(d.CustomerDeletions.Count==0) g.DrawString("• Sin clientes eliminados durante el turno.",f,ink,55,y);
            else foreach(var x in d.CustomerDeletions.Take(4)) { g.DrawString($"• {Trim(x.CustomerName,30)} · Motivo: {Trim(x.Reason,55)}",f,ink,55,y); y+=19; if(y>980) break; }
            y+=15;
            g.DrawString("⚠ DESCUENTOS OTORGADOS", h, ink, 45, y); y+=26;
            if(d.DiscountDetails.Count==0) g.DrawString("• Sin descuentos otorgados durante el turno.",f,ink,55,y);
            else foreach(var x in d.DiscountDetails.Take(4)){ g.DrawString($"• Ticket #{x.Ticket} — ${x.Amount:N2} — Motivo: {Trim(x.Reason,55)}",f,ink,55,y); y+=20; if(y>780) break; }
            y+=15;
            g.DrawString("INGRESOS / EGRESOS DEL TURNO", h, ink, 45, y); y+=30;
            using(var cn=Database.Open())
            using(var q=cn.CreateCommand())
            {
                q.CommandText="SELECT COALESCE(SUM(CASE WHEN movement_type='INCOME' THEN amount ELSE 0 END),0),COALESCE(SUM(CASE WHEN movement_type='EXPENSE' THEN amount ELSE 0 END),0) FROM cash_movements WHERE session_id=(SELECT id FROM cash_sessions WHERE opened_at=$o AND COALESCE(closed_at,'')=$c ORDER BY id DESC LIMIT 1) AND COALESCE(voided,0)=0";
                q.Parameters.AddWithValue("$o",d.Opened); q.Parameters.AddWithValue("$c",d.Closed);
                using var r=q.ExecuteReader(); if(r.Read()){ g.DrawString($"Ingresos: ${r.GetDouble(0):N2}    ·    Egresos: ${r.GetDouble(1):N2}",f,ink,55,y); y+=28; }
            }
            y+=15; g.DrawString($"MERMAS: {d.WasteQty:N3} unidades",h,ink,45,y); y+=32;
            if (d.MercadoPagoEnabled)
                g.DrawString($"MERCADO PAGO · Esperado: ${d.MpExpected:N2} · Informado: ${d.MpClosing:N2} · Diferencia: {(d.MpDifference>=0?"+":"")}${d.MpDifference:N2}",f,ink,45,y);
            else
                g.DrawString("Mercado Pago: no habilitado en este turno.",f,ink,45,y);
            y+=32; g.DrawString("El Excel conserva el detalle completo de operaciones.",small,ink,45,y);
            bmp.Save(file, ImageFormat.Jpeg); return file;
        }


        private static string RenderPage1(ExecutiveData d, string bar, string pie, string dir)
        {
            var file = Path.Combine(dir, "page1.jpg");
            using var bmp = NewPage(); using var g = Graphics.FromImage(bmp);
            DrawHeader(g, d.Business, "REPORTE EJECUTIVO DE CIERRE");
            using var bold = new Font("Segoe UI", 12, FontStyle.Bold);
            using var normal = new Font("Segoe UI", 9.5f);
            using var small = new Font("Segoe UI", 8.5f);
            using var ink = new SolidBrush(Color.FromArgb(35,35,40));
            g.DrawString($"Cajero: {d.Cashier}    Apertura: {d.Opened}    Cierre: {d.Closed}", normal, ink, 45, 105);
            DrawMetric(g, 45, 135, 145, "TICKETS", d.Tickets.ToString("N0"));
            DrawMetric(g, 205, 135, 170, "VENTAS", $"${d.Sales:N2}");
            DrawMetric(g, 390, 135, 165, "PROMEDIO", $"${d.AverageTicket:N2}");
            DrawMetric(g, 570, 135, 150, "DEVOLUCIONES", $"${d.Returns:N2}");
            g.DrawString($"Efectivo esperado: ${d.Expected:N2}    |    Contado: ${d.Closing:N2}    |    Diferencia: ${d.Difference:N2}", small, ink, 45, 205);
            g.DrawString($"Fondo inicial: ${d.Opening:N2}    |    Compras: ${d.Purchases:N2}    |    Merma: {d.WasteQty:N2} u.", small, ink, 45, 228);
            g.DrawString($"Deuda clientes: ${d.CustomerDebt:N2}    |    Deuda proveedores: ${d.SupplierDebt:N2}", small, ink, 45, 251);
            g.DrawString($"Créditos del período: ${d.Credits:N2}    |    Abonos: ${d.CustomerPayments:N2}", small, ink, 45, 274);
            DrawImage(g, bar, new Rectangle(45, 310, 330, 225));
            DrawImage(g, pie, new Rectangle(395, 310, 330, 225));
            g.DrawString("Cobros por medio de pago", bold, ink, 45, 545);
            g.DrawString("Distribución de cobros", bold, ink, 395, 545);
            g.DrawString("Resumen económico y operativo del turno", small, ink, 45, 570);
            bmp.Save(file, ImageFormat.Jpeg);
            return file;
        }

        private static string RenderPage2(ExecutiveData d, string top, string dir)
        {
            var file = Path.Combine(dir, "page2.jpg");
            using var bmp = NewPage(); using var g = Graphics.FromImage(bmp);
            DrawHeader(g, d.Business, "GESTIÓN COMERCIAL E INVENTARIO");
            using var h = new Font("Segoe UI", 12, FontStyle.Bold); using var f = new Font("Segoe UI", 8.5f); using var ink = new SolidBrush(Color.FromArgb(35,35,40));
            DrawImage(g, top, new Rectangle(45, 100, 330, 300));
            g.DrawString("Productos más vendidos", h, ink, 45, 415);
            var y=445; foreach(var x in d.TopProducts.Take(8)){ g.DrawString($"• {Trim(x.Name,42)} — {x.Quantity:N2} u. — ${x.Sales:N2}",f,ink,50,y); y+=22; }
            g.DrawString("Stock bajo · solicitar al proveedor", h, ink, 395, 100); y=130;
            foreach(var x in d.LowStock.Take(10)){ g.DrawString($"• {Trim(x.Name,35)} — {x.Stock:N2}/{x.MinStock:N2} — {Trim(x.Supplier,20)}",f,ink,400,y); y+=21; }
            g.DrawString("Exceso / mercadería inmovilizada", h, ink, 395, 365); y=395;
            foreach(var x in d.Excess.Take(8)){ g.DrawString($"• {Trim(x.Name,38)} — stock {x.Stock:N2}",f,ink,400,y); y+=21; }
            g.DrawString("Productos sin ventas históricas", h, ink, 395, 575); y=605;
            foreach(var x in d.NeverSold.Take(6)){ g.DrawString($"• {Trim(x.Name,38)} — stock {x.Stock:N2}",f,ink,400,y); y+=21; }
            g.DrawString($"Productos vendidos: {d.TopProducts.Count:N0}    |    Críticos: {d.LowStock.Count:N0}",f,ink,45,690);
            bmp.Save(file, ImageFormat.Jpeg); return file;
        }

        private static string RenderPage3(ExecutiveData d, string dir)
        {
            var file = Path.Combine(dir, "page3.jpg");
            using var bmp = NewPage(); using var g = Graphics.FromImage(bmp);
            DrawHeader(g, d.Business, "ACCIONES RECOMENDADAS Y CONTROL");
            using var h = new Font("Segoe UI", 11, FontStyle.Bold); using var f = new Font("Segoe UI", 8.5f); using var ink = new SolidBrush(Color.FromArgb(35,35,40));
            var y=105; g.DrawString("Solicitar al proveedor",h,ink,45,y); y+=28;
            if(d.LowStock.Count==0) g.DrawString("• No hay productos por debajo del stock mínimo.",f,ink,50,y);
            else foreach(var x in d.LowStock.Take(12)){ g.DrawString($"• PEDIR: {Trim(x.Name,42)} — faltan aprox. {Math.Max(0,x.MinStock-x.Stock):N2} u. — {Trim(x.Supplier,24)}",f,ink,50,y); y+=19; }
            y+=12; g.DrawString("Exceso de mercadería",h,ink,45,y); y+=28;
            if(d.Excess.Count==0) g.DrawString("• No se detectó mercadería inmovilizada.",f,ink,50,y);
            else foreach(var x in d.Excess.Take(10)){ g.DrawString($"• Revisar/promocionar: {Trim(x.Name,50)} — stock {x.Stock:N2}",f,ink,50,y); y+=19; }
            y+=12; g.DrawString("Productos sin ventas históricas",h,ink,45,y); y+=28;
            if(d.NeverSold.Count==0) g.DrawString("• No hay productos sin ventas históricas.",f,ink,50,y);
            else foreach(var x in d.NeverSold.Take(10)){ g.DrawString($"• Evaluar eliminar: {Trim(x.Name,50)} — stock {x.Stock:N2}",f,ink,50,y); y+=19; }
            y+=15; g.DrawString("Indicadores adicionales",h,ink,45,y); y+=25;
            g.DrawString($"• Compras recibidas: ${d.Purchases:N2}    • Deuda proveedores: ${d.SupplierDebt:N2}",f,ink,50,y); y+=19;
            g.DrawString($"• Deuda clientes: ${d.CustomerDebt:N2}    • Abonos: ${d.CustomerPayments:N2}",f,ink,50,y); y+=19;
            g.DrawString($"• Créditos generados: ${d.Credits:N2}    • Merma: {d.WasteQty:N2} u.",f,ink,50,y); y+=28;
            g.DrawString("Ajustes de inventario que redujeron stock",h,ink,45,y); y+=26;
            if(d.InventoryAlerts.Count==0) g.DrawString("• Sin ajustes registrados durante el turno.",f,ink,50,y);
            else foreach(var x in d.InventoryAlerts.Take(9)){ g.DrawString($"• {Trim(x.Name,40)} — -{x.Quantity:N2} — {x.Reason} — {Trim(x.Reference,35)}",f,ink,50,y); y+=18; }
            y += 14;
            g.DrawString("⚠ DESCUENTOS OTORGADOS · MOTIVO Y DETALLE",h,ink,45,y); y+=26;
            if(d.DiscountDetails.Count==0) g.DrawString("• Sin descuentos otorgados durante el turno.",f,ink,50,y);
            else foreach(var x in d.DiscountDetails.Take(8))
            {
                g.DrawString($"• Ticket #{x.Ticket} · ${x.Amount:N2} · Motivo: {Trim(x.Reason,52)} · Responsable: {Trim(x.User,22)}",f,ink,50,y); y+=18;
                if(!string.IsNullOrWhiteSpace(x.Products) && y<780){ g.DrawString($"  Productos: {Trim(x.Products,100)}",f,ink,58,y); y+=18; }
                if(y>785) break;
            }
            y += 14;
            g.DrawString("MERMA / DESPERDICIO · DETALLE",h,ink,45,y); y+=26;
            if(d.WasteDetails.Count==0) g.DrawString("• Sin mermas registradas durante el turno.",f,ink,50,y);
            else foreach(var w in d.WasteDetails.Take(10))
            {
                g.DrawString($"• {w.CreatedAt} · {Trim(w.Product,30)} · Cant. {w.Quantity:N3} · Motivo: {Trim(w.Reason,28)} · Responsable: {Trim(w.User,22)}",f,ink,50,y); y+=18;
                if(!string.IsNullOrWhiteSpace(w.Notes) && y < 790) { g.DrawString($"  Observación: {Trim(w.Notes,105)}",f,ink,58,y); y+=18; }
                if(y > 785) break;
            }
            g.DrawString("El Excel histórico conserva el detalle completo de cada turno.",f,ink,45,805);
            bmp.Save(file, ImageFormat.Jpeg); return file;
        }

        private static Bitmap NewPage()
        {
            var bmp = new Bitmap(1200, 1700, PixelFormat.Format24bppRgb);
            using var g = Graphics.FromImage(bmp); g.Clear(Color.White); g.SmoothingMode = SmoothingMode.AntiAlias;
            return bmp;
        }
        private static void DrawHeader(Graphics g,string business,string title)
        {
            using var bg=new SolidBrush(Color.FromArgb(25,25,30)); g.FillRectangle(bg,0,0,1200,90);
            using var white=new SolidBrush(Color.White); using var accent=new SolidBrush(Color.FromArgb(35,120,210));
            using var b=new Font("Segoe UI",20,FontStyle.Bold); using var t=new Font("Segoe UI",14,FontStyle.Bold);
            g.DrawString(business,b,white,45,18); g.DrawString(title,t,white,45,55); g.FillRectangle(accent,45,88,1110,4);
        }
        private static void DrawMetric(Graphics g,int x,int y,int w,string label,string value)
        {
            using var bg=new SolidBrush(Color.FromArgb(244,246,249)); using var pen=new Pen(Color.FromArgb(220,224,230));
            g.FillRectangle(bg,x,y,w,55); g.DrawRectangle(pen,x,y,w,55);
            using var l=new Font("Segoe UI",8,FontStyle.Bold); using var v=new Font("Segoe UI",15,FontStyle.Bold); using var ink=new SolidBrush(Color.FromArgb(35,35,40));
            g.DrawString(label,l,ink,x+10,y+7); g.DrawString(value,v,ink,x+10,y+25);
        }
        private static void DrawImage(Graphics g,string path,Rectangle rect)
        {
            using var img=Image.FromFile(path); using var border=new Pen(Color.FromArgb(215,220,225),2);
            g.DrawImage(img,rect); g.DrawRectangle(border,rect);
        }

        private static void WriteImagePdf(string path,string[] pages)
        {
            using var fs=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None);
            var objs=new List<Obj>();
            int catalog=Add(objs,"<< /Type /Catalog /Pages 2 0 R >>");
            int pagesId=Add(objs,"");
            var pageIds=new List<int>();
            foreach(var file in pages){
                var data=File.ReadAllBytes(file); using var ms=new MemoryStream(data); using var img=Image.FromStream(ms);
                int image=Add(objs,$"<< /Type /XObject /Subtype /Image /Width {img.Width} /Height {img.Height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {data.Length} >>",data);
                var content=Encoding.ASCII.GetBytes($"q 595 0 0 842 0 0 cm /Im{image} Do Q");
                int contentId=Add(objs,$"<< /Length {content.Length} >>",content);
                int page=Add(objs,$"<< /Type /Page /Parent {pagesId} 0 R /MediaBox [0 0 595 842] /Resources << /XObject << /Im{image} {image} 0 R >> >> /Contents {contentId} 0 R >>");
                pageIds.Add(page);
            }
            objs[pagesId-1].Body=$"<< /Type /Pages /Kids [ {string.Join(" ",pageIds.Select(x=>$"{x} 0 R"))} ] /Count {pageIds.Count} >>";
            WriteAll(fs,objs,catalog);
        }
        private static int Add(List<Obj> o,string body,byte[]? stream=null){o.Add(new Obj{Body=body,Stream=stream});return o.Count;}
        private static void WriteAll(FileStream fs,List<Obj> o,int root)
        {
            var enc=new UTF8Encoding(false); fs.Write(enc.GetBytes("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n")); var offsets=new long[o.Count+1];
            for(int i=1;i<=o.Count;i++){ offsets[i]=fs.Position; var ob=o[i-1]; fs.Write(enc.GetBytes($"{i} 0 obj\n{ob.Body}\n")); if(ob.Stream!=null){fs.Write(enc.GetBytes("stream\n"));fs.Write(ob.Stream);fs.Write(enc.GetBytes("\nendstream\n"));} fs.Write(enc.GetBytes("endobj\n")); }
            var xref=fs.Position; fs.Write(enc.GetBytes($"xref\n0 {o.Count+1}\n0000000000 65535 f \n")); for(int i=1;i<=o.Count;i++) fs.Write(enc.GetBytes($"{offsets[i]:D10} 00000 n \n")); fs.Write(enc.GetBytes($"trailer\n<< /Size {o.Count+1} /Root {root} 0 R >>\nstartxref\n{xref}\n%%EOF"));
        }
    }
}
