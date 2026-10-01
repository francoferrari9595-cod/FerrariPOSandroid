using FerrarisPOS.Data;
using System.Globalization;

namespace FerrarisPOS.Services;

public static class ReportService
{
    public static (int count, double total, double cash, double card, double mercadoPago, double transfer, double credit) Today()
    {
        using var cn=Database.Open();
        using var q=cn.CreateCommand();
        q.CommandText="SELECT COUNT(*),COALESCE(SUM(total),0) FROM sales WHERE date(created_at,'localtime')=date('now','localtime') AND status='COMPLETED'";
        using var r=q.ExecuteReader(); r.Read(); var count=r.GetInt32(0); var total=r.GetDouble(1); r.Close();
        using var p=cn.CreateCommand(); p.CommandText="SELECT COALESCE(SUM(CASE WHEN method='EFECTIVO' THEN amount ELSE 0 END),0),COALESCE(SUM(CASE WHEN method='TARJETA' THEN amount ELSE 0 END),0),COALESCE(SUM(CASE WHEN method='MERCADO PAGO' THEN amount ELSE 0 END),0),COALESCE(SUM(CASE WHEN method='TRANSFERENCIA' THEN amount ELSE 0 END),0),COALESCE(SUM(CASE WHEN method LIKE 'CRÉDITO%' OR method LIKE 'CREDITO%' THEN amount ELSE 0 END),0) FROM payments WHERE date(created_at,'localtime')=date('now','localtime') AND status='APPROVED'";
        using var x=p.ExecuteReader(); x.Read(); return(count,total,x.GetDouble(0),x.GetDouble(1),x.GetDouble(2),x.GetDouble(3),x.GetDouble(4));
    }

    public static string CashSessionDetailed(long sessionId)
    {
        using var cn = Database.Open();
        string user = "", opened = "", closed = "";
        double opening = 0, closing = 0, expected = 0, difference = 0;
        bool mpEnabled = false; double mpOpening = 0, mpClosing = 0, mpExpected = 0, mpDifference = 0, mpRetention = 0;
        using (var q = cn.CreateCommand())
        {
            q.CommandText = """
                SELECT COALESCE(u.full_name,''), cs.opened_at, COALESCE(cs.closed_at,''), cs.opening_amount,
                       COALESCE(cs.closing_amount,0), COALESCE(cs.expected_amount,0), COALESCE(cs.difference,0),
                       COALESCE(cs.mercado_pago_enabled,0), COALESCE(cs.mercado_pago_opening_amount,0),
                       COALESCE(cs.mercado_pago_closing_amount,0), COALESCE(cs.mercado_pago_expected_amount,0),
                       COALESCE(cs.mercado_pago_difference,0), COALESCE(cs.mercado_pago_retention_percent,0)
                FROM cash_sessions cs LEFT JOIN users u ON u.id=cs.user_id WHERE cs.id=$s
                """;
            q.Parameters.AddWithValue("$s", sessionId);
            using var r = q.ExecuteReader();
            if (!r.Read()) return "No se encontró el cierre de caja.";
            user=r.GetString(0); opened=r.GetString(1); closed=r.GetString(2); opening=r.GetDouble(3); closing=r.GetDouble(4); expected=r.GetDouble(5); difference=r.GetDouble(6);
            mpEnabled=r.GetInt32(7)!=0; mpOpening=r.GetDouble(8); mpClosing=r.GetDouble(9); mpExpected=r.GetDouble(10); mpDifference=r.GetDouble(11); mpRetention=r.GetDouble(12);
        }

        var tickets = Scalar(cn, "SELECT COUNT(*) FROM sales WHERE session_id=$s AND status='COMPLETED'", sessionId);
        var sales = Scalar(cn, "SELECT COALESCE(SUM(total),0) FROM sales WHERE session_id=$s AND status='COMPLETED'", sessionId);
        var returns = Scalar(cn, "SELECT COALESCE(SUM(r.amount),0) FROM sale_returns r JOIN sales s ON s.id=r.sale_id WHERE s.session_id=$s", sessionId);
        var wasteQty = Scalar(cn, "SELECT COALESCE(SUM(w.quantity),0) FROM waste_records w WHERE w.created_at >= (SELECT opened_at FROM cash_sessions WHERE id=$s) AND w.created_at <= (SELECT COALESCE(closed_at,CURRENT_TIMESTAMP) FROM cash_sessions WHERE id=$s)", sessionId);
        var wasteCount = Scalar(cn, "SELECT COUNT(*) FROM waste_records w WHERE w.created_at >= (SELECT opened_at FROM cash_sessions WHERE id=$s) AND w.created_at <= (SELECT COALESCE(closed_at,CURRENT_TIMESTAMP) FROM cash_sessions WHERE id=$s)", sessionId);

        // La ganancia se informa como BRUTA ESTIMADA porque el sistema guarda el costo actual
        // del producto, no un costo histórico por línea de venta.
        var costOfGoods = Scalar(cn, """
            SELECT COALESCE(SUM(MAX(0, si.quantity-COALESCE(si.returned_quantity,0)) * COALESCE(p.cost_price,0)),0)
            FROM sale_items si JOIN sales s ON s.id=si.sale_id JOIN products p ON p.id=si.product_id
            WHERE s.session_id=$s AND s.status='COMPLETED'
            """, sessionId);
        var grossProfit = sales - returns - costOfGoods;

        var paymentTotals = new List<(string method,double amount)>();
        using (var q=cn.CreateCommand())
        {
            q.CommandText="SELECT p.method,COALESCE(SUM(p.amount),0) FROM payments p JOIN sales s ON s.id=p.sale_id WHERE s.session_id=$s AND s.status='COMPLETED' AND p.status='APPROVED' GROUP BY p.method ORDER BY SUM(p.amount) DESC";
            q.Parameters.AddWithValue("$s",sessionId); using var r=q.ExecuteReader(); while(r.Read()) paymentTotals.Add((r.GetString(0),r.GetDouble(1)));
        }

        var lines = new List<string>
        {
            "FERRARISPOS · CIERRE DE CAJA",
            $"Cajero: {user} · {opened} → {closed}",
            new string('─', 52),
            "VENTAS",
            $"Total vendido:        ${sales:N2}",
            $"Tickets:              {tickets:N0}",
            $"Devoluciones:         ${returns:N2}",
            $"Ganancia bruta est.:  ${grossProfit:N2}",
            "",
            "COBROS POR MEDIO"
        };
        foreach (var x in paymentTotals) lines.Add($"  {x.method}: ${x.amount:N2}");

        lines.Add("");
        lines.Add("VENTAS POR CATEGORÍA / DEPARTAMENTO · DINERO VENDIDO");
        var categorySales = SalesCategoryReportService.ForSession(cn, sessionId);
        if (categorySales.Count == 0) lines.Add("  Sin ventas clasificadas por categoría.");
        else foreach (var x in categorySales) lines.Add($"  {x.Category}: EFECTIVO ${x.Cash:N2} + MERCADO PAGO ${x.MercadoPago:N2} + TARJETA ${x.Card:N2} + TRANSFERENCIA ${x.Transfer:N2} + CRÉDITO ${x.Credit:N2} = TOTAL VENDIDO ${x.Total:N2}");

        lines.Add("");
        lines.Add("CAJA · ARQUEO");
        lines.Add($"  Fondo inicial:      ${opening:N2}");
        lines.Add($"  Efectivo esperado:  ${expected:N2}");
        lines.Add($"  Efectivo contado:   ${closing:N2}");
        lines.Add($"  Diferencia:         {(difference >= 0 ? "+" : "")}${difference:N2}");
        if (mpEnabled)
        {
            lines.Add("");
            lines.Add("MERCADO PAGO");
            lines.Add($"  Saldo inicial:      ${mpOpening:N2}");
            lines.Add($"  Saldo esperado:     ${mpExpected:N2}");
            lines.Add($"  Saldo informado:    ${mpClosing:N2}");
            lines.Add($"  Retención:          {mpRetention:N2}%");
            lines.Add($"  Diferencia:         {(mpDifference >= 0 ? "+" : "")}${mpDifference:N2}");
        }

        lines.Add("");
        lines.Add("INGRESOS / EGRESOS DEL TURNO");
        using (var q=cn.CreateCommand())
        {
            q.CommandText="SELECT cm.movement_type,cm.payment_method,cm.concept,cm.amount FROM cash_movements cm WHERE cm.session_id=$s AND cm.movement_type IN ('INCOME','EXPENSE') AND COALESCE(cm.voided,0)=0 ORDER BY cm.id";
            q.Parameters.AddWithValue("$s",sessionId); using var r=q.ExecuteReader();
            var any=false;
            while(r.Read())
            {
                any=true;
                var tipo=r.GetString(0)=="INCOME" ? "INGRESO" : "EGRESO";
                var medio=r.GetString(1);
                var concepto=r.GetString(2);
                lines.Add($"  {tipo} · {medio} · ${r.GetDouble(3):N2} · {concepto}");
            }
            if(!any) lines.Add("  Sin ingresos ni egresos manuales.");
        }
        lines.Add("");
        lines.Add("MOVIMIENTOS ANULADOS");
        using (var q=cn.CreateCommand())
        {
            q.CommandText="SELECT cm.created_at,cm.movement_type,cm.payment_method,cm.concept,cm.amount,COALESCE(cm.void_reason,''),COALESCE(v.full_name,v.username,'') FROM cash_movements cm LEFT JOIN users v ON v.id=cm.voided_by WHERE cm.session_id=$s AND cm.movement_type IN ('INCOME','EXPENSE') AND COALESCE(cm.voided,0)=1 ORDER BY cm.id";
            q.Parameters.AddWithValue("$s",sessionId); using var r=q.ExecuteReader(); var anyVoided=false;
            while(r.Read()){anyVoided=true; var tipo=r.GetString(1)=="INCOME"?"INGRESO":"EGRESO"; lines.Add($"  {r.GetString(0)} · {tipo} · {r.GetString(2)} · ${r.GetDouble(4):N2} · {r.GetString(3)} · Motivo: {r.GetString(5)} · Anuló: {r.GetString(6)}");}
            if(!anyVoided) lines.Add("  Sin movimientos anulados.");
        }

        lines.Add("");
        lines.Add("CLIENTES ELIMINADOS · MOTIVO");
        using (var q=cn.CreateCommand())
        {
            q.CommandText="SELECT d.created_at,d.customer_name,COALESCE(d.customer_document,''),d.reason,COALESCE(u.full_name,u.username,'') FROM customer_deletion_log d LEFT JOIN users u ON u.id=d.user_id WHERE d.created_at >= (SELECT opened_at FROM cash_sessions WHERE id=$s) AND d.created_at <= (SELECT COALESCE(closed_at,CURRENT_TIMESTAMP) FROM cash_sessions WHERE id=$s) ORDER BY d.id";
            q.Parameters.AddWithValue("$s",sessionId); using var r=q.ExecuteReader(); var any=false; while(r.Read()){any=true; lines.Add($"  {r.GetString(0)} · {r.GetString(1)} · Documento: {r.GetString(2)} · Motivo: {r.GetString(3)} · Responsable: {r.GetString(4)}");} if(!any) lines.Add("  Sin clientes eliminados durante el turno.");
        }
        lines.Add("");
        lines.Add("CAMBIOS DE PRECIO DE VENTA · MOTIVO OBLIGATORIO");
        using (var q = cn.CreateCommand())
        {
            q.CommandText = """
                SELECT p.created_at, p.product_name, COALESCE(p.product_barcode,''), p.old_price, p.new_price,
                       p.reason, COALESCE(u.full_name,u.username,'')
                FROM price_change_log p
                LEFT JOIN users u ON u.id=p.user_id
                WHERE p.created_at >= (SELECT opened_at FROM cash_sessions WHERE id=$s)
                  AND p.created_at <= (SELECT COALESCE(closed_at,CURRENT_TIMESTAMP) FROM cash_sessions WHERE id=$s)
                ORDER BY p.id
                """;
            q.Parameters.AddWithValue("$s", sessionId);
            using var r = q.ExecuteReader();
            var anyPriceChanges = false;
            while (r.Read())
            {
                anyPriceChanges = true;
                lines.Add($"  {r.GetString(0)} · {r.GetString(1)} · Código: {r.GetString(2)} · ${r.GetDouble(3):N2} → ${r.GetDouble(4):N2} · Motivo: {r.GetString(5)} · Usuario: {r.GetString(6)}");
            }
            if (!anyPriceChanges) lines.Add("  Sin cambios de precio durante el turno.");
        }
        lines.Add("");
        lines.Add("PROMOCIONES ELIMINADAS · MOTIVO OBLIGATORIO");
        using (var q = cn.CreateCommand())
        {
            q.CommandText = "SELECT datetime(a.created_at,'localtime'),COALESCE(u.full_name,u.username,'SISTEMA'),a.details FROM audit_log a LEFT JOIN users u ON u.id=a.user_id WHERE a.created_at >= (SELECT opened_at FROM cash_sessions WHERE id=$s) AND a.created_at <= (SELECT COALESCE(closed_at,CURRENT_TIMESTAMP) FROM cash_sessions WHERE id=$s) AND a.action='PROMOTION_DELETE' ORDER BY a.id";
            q.Parameters.AddWithValue("$s", sessionId);
            using var r = q.ExecuteReader();
            var anyPromotionDeletes = false;
            while (r.Read())
            {
                anyPromotionDeletes = true;
                lines.Add($"  {r.GetString(0)} · Usuario: {r.GetString(1)} · {r.GetString(2)}");
            }
            if (!anyPromotionDeletes) lines.Add("  Sin promociones eliminadas durante el turno.");
        }

        lines.Add("");
        lines.Add("MERMAS · DETALLE DEL TURNO");
        lines.Add($"  Registros: {wasteCount:N0} · Cantidad total: {wasteQty:N3}");
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
            var anyWaste = false;
            while (r.Read())
            {
                anyWaste = true;
                lines.Add($"  {r.GetString(0)} · {r.GetString(1)} · Código: {r.GetString(2)} · Cantidad: {r.GetDouble(3):N3} · Motivo: {r.GetString(4)} · Observación: {r.GetString(5)} · Responsable: {r.GetString(6)}");
            }
            if (!anyWaste) lines.Add("  Sin mermas registradas en el turno.");
        }
        lines.Add("");
        lines.Add("LECTURA RÁPIDA");
        lines.Add($"  Vendiste: ${sales:N2} · Pagaste/egresaste: ver EGRESOS arriba.");
        lines.Add($"  Para mañana debería quedar: efectivo ${closing:N2}" + (mpEnabled ? $" · Mercado Pago ${mpClosing:N2}" : "") + ".");
        lines.Add("  La ganancia es bruta estimada y no descuenta egresos operativos.");
        lines.Add("");
        lines.Add("El Excel del cierre conserva el detalle completo de operaciones.");
        return string.Join(Environment.NewLine, lines);
    }

    public static string DailySummary(DateTime day)
    {
        using var cn = Database.Open();
        var d = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var total = Scalar(cn, "SELECT COALESCE(SUM(total),0) FROM sales WHERE date(created_at,'localtime')=$d AND status='COMPLETED'", d);
        var tickets = Scalar(cn, "SELECT COUNT(*) FROM sales WHERE date(created_at,'localtime')=$d AND status='COMPLETED'", d);
        var returns = Scalar(cn, "SELECT COALESCE(SUM(amount),0) FROM sale_returns WHERE date(created_at,'localtime')=$d", d);
        var cost = Scalar(cn, "SELECT COALESCE(SUM(MAX(0,si.quantity-COALESCE(si.returned_quantity,0))*COALESCE(p.cost_price,0)),0) FROM sale_items si JOIN sales s ON s.id=si.sale_id JOIN products p ON p.id=si.product_id WHERE date(s.created_at,'localtime')=$d AND s.status='COMPLETED'", d);
        var profit = total - returns - cost;
        var wasteQty = Scalar(cn, "SELECT COALESCE(SUM(quantity),0) FROM waste_records WHERE date(created_at,'localtime')=$d", d);
        var wasteCount = Scalar(cn, "SELECT COUNT(*) FROM waste_records WHERE date(created_at,'localtime')=$d", d);
        var customerPayments = Scalar(cn, "SELECT COALESCE(SUM(amount),0) FROM customer_accounts WHERE date(created_at,'localtime')=$d AND entry_type='PAYMENT'", d);
        var lines = new List<string>
        {
            "FERRARISPOS · RESUMEN DEL DÍA",
            $"Fecha: {day:dd/MM/yyyy}",
            new string('─',52),
            $"Total vendido:       ${total:N2}",
            $"Tickets:             {tickets:N0}",
            $"Devoluciones:        ${returns:N2}",
            $"Ganancia bruta est.: ${profit:N2}",
            "",
            "COBROS POR MEDIO"
        };
        using(var q=cn.CreateCommand())
        {
            q.CommandText="SELECT method,COALESCE(SUM(amount),0) FROM payments WHERE date(created_at,'localtime')=$d AND status='APPROVED' GROUP BY method ORDER BY SUM(amount) DESC";
            q.Parameters.AddWithValue("$d",d); using var r=q.ExecuteReader(); while(r.Read()) lines.Add($"  {r.GetString(0)}: ${r.GetDouble(1):N2}");
        }
        lines.Add("");
        lines.Add("INGRESOS / EGRESOS");
        using(var q=cn.CreateCommand())
        {
            q.CommandText="SELECT movement_type,payment_method,concept,amount FROM cash_movements WHERE date(created_at,'localtime')=$d AND movement_type IN ('INCOME','EXPENSE') AND COALESCE(voided,0)=0 ORDER BY id";
            q.Parameters.AddWithValue("$d",d); using var r=q.ExecuteReader(); var any=false;
            while(r.Read()){any=true; var tipo=r.GetString(0)=="INCOME"?"INGRESO":"EGRESO"; lines.Add($"  {tipo} · {r.GetString(1)} · ${r.GetDouble(3):N2} · {r.GetString(2)}");}
            if(!any) lines.Add("  Sin ingresos ni egresos manuales.");
        }
        lines.Add("");
        lines.Add("CLIENTES CON DEUDA / ABONOS DEL DÍA");
        using (var q = cn.CreateCommand())
        {
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
                var nombre = r.GetString(0);
                var deuda = r.GetDouble(1);
                var abonosHoy = r.GetDouble(2);
                var estado = deuda > 0.005 ? "PENDIENTE" : "AL DÍA";
                lines.Add($"  {nombre} | Deuda actual: ${deuda:N2} | Abonos hoy: ${abonosHoy:N2} | Estado: {estado}");
            }
            if (!any) lines.Add("  No hay clientes con deuda pendiente ni abonos registrados hoy.");
        }
        lines.Add($"  TOTAL ABONOS DE CLIENTES HOY: ${customerPayments:N2}");
        lines.Add("");
        lines.Add("MOVIMIENTOS ANULADOS");
        using (var q = cn.CreateCommand())
        {
            q.CommandText = "SELECT cm.created_at,cm.movement_type,cm.payment_method,cm.concept,cm.amount,COALESCE(cm.void_reason,''),COALESCE(v.full_name,v.username,'') FROM cash_movements cm LEFT JOIN users v ON v.id=cm.voided_by WHERE date(cm.created_at,'localtime')=$d AND cm.movement_type IN ('INCOME','EXPENSE') AND COALESCE(cm.voided,0)=1 ORDER BY cm.id";
            q.Parameters.AddWithValue("$d", d);
            using var r = q.ExecuteReader();
            var anyVoided = false;
            while (r.Read())
            {
                anyVoided = true;
                var tipo = r.GetString(1) == "INCOME" ? "INGRESO" : "EGRESO";
                lines.Add($"  {r.GetString(0)} · {tipo} · {r.GetString(2)} · ${r.GetDouble(4):N2} · {r.GetString(3)} · Motivo: {r.GetString(5)} · Anuló: {r.GetString(6)}");
            }
            if (!anyVoided) lines.Add("  Sin movimientos anulados.");
        }
        lines.Add("");
        lines.Add($"Mermas: {wasteCount:N0} registros · {wasteQty:N3} u.");
        return string.Join(Environment.NewLine, lines);
    }

    public static string DailyDetailed(DateTime day)
    {
        using var cn = Database.Open();
        var d = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var lines = new List<string> { "FERRARISPOS - REPORTE COMPLETO DEL DÍA", $"FECHA: {day:dd/MM/yyyy}", new string('-', 55) };
        using (var q = cn.CreateCommand())
        {
            q.CommandText = """
                SELECT COUNT(*), COALESCE(SUM(total),0)
                FROM sales WHERE date(created_at,'localtime')=$d AND status='COMPLETED'
                """;
            q.Parameters.AddWithValue("$d", d);
            using var r = q.ExecuteReader(); r.Read();
            lines.Add($"VENTAS COMPLETADAS: {r.GetInt32(0)}");
            lines.Add($"TOTAL VENDIDO: ${r.GetDouble(1):N2}");
        }
        using (var q = cn.CreateCommand())
        {
            q.CommandText = """
                SELECT method, COALESCE(SUM(amount),0)
                FROM payments WHERE date(created_at,'localtime')=$d AND status='APPROVED'
                GROUP BY method ORDER BY method
                """;
            q.Parameters.AddWithValue("$d", d);
            using var r = q.ExecuteReader();
            lines.Add("COBROS POR MEDIO:");
            while (r.Read()) lines.Add($"  {r.GetString(0)}: ${r.GetDouble(1):N2}");
        }
        lines.Add("DETALLE DE PAGOS / MERCADO PAGO:");
        using (var q = cn.CreateCommand())
        {
            q.CommandText = """
                SELECT s.ticket_no,p.created_at,p.method,p.amount,COALESCE(p.reference,'')
                FROM payments p JOIN sales s ON s.id=p.sale_id
                WHERE date(s.created_at,'localtime')=$d AND s.status='COMPLETED' AND p.status='APPROVED'
                ORDER BY p.id
                """;
            q.Parameters.AddWithValue("$d", d);
            using var r = q.ExecuteReader();
            while (r.Read()) lines.Add($"  Ticket #{r.GetInt64(0)} | {r.GetString(1)} | {r.GetString(2)} | ${r.GetDouble(3):N2} | Ref: {r.GetString(4)}");
        }
        using (var q = cn.CreateCommand())
        {
            q.CommandText = """
                SELECT COALESCE(u.full_name,'') AS cajero, COUNT(*), COALESCE(SUM(s.total),0)
                FROM sales s LEFT JOIN users u ON u.id=s.user_id
                WHERE date(s.created_at,'localtime')=$d AND s.status='COMPLETED'
                GROUP BY s.user_id ORDER BY cajero
                """;
            q.Parameters.AddWithValue("$d", d);
            using var r = q.ExecuteReader();
            lines.Add("VENTAS POR CAJERO:");
            while (r.Read()) lines.Add($"  {r.GetString(0)}: {r.GetInt32(1)} ventas / ${r.GetDouble(2):N2}");
        }
        double income = Scalar(cn, "SELECT COALESCE(SUM(amount),0) FROM cash_movements WHERE date(created_at,'localtime')=$d AND movement_type='INCOME' AND COALESCE(voided,0)=0", d);
        double expense = Scalar(cn, "SELECT COALESCE(SUM(amount),0) FROM cash_movements WHERE date(created_at,'localtime')=$d AND movement_type='EXPENSE' AND COALESCE(voided,0)=0", d);
        lines.Add($"INGRESOS DE CAJA: ${income:N2}");
        lines.Add($"EGRESOS DE CAJA: ${expense:N2}");
        double credits = Scalar(cn, "SELECT COALESCE(SUM(amount),0) FROM customer_accounts WHERE date(created_at,'localtime')=$d AND entry_type='SALE'", d);
        double customerPayments = Scalar(cn, "SELECT COALESCE(SUM(amount),0) FROM customer_accounts WHERE date(created_at,'localtime')=$d AND entry_type='PAYMENT'", d);
        lines.Add($"CRÉDITOS OTORGADOS: ${credits:N2}");
        lines.Add($"ABONOS DE CLIENTES: ${customerPayments:N2}");

        lines.Add(new string('-', 55));
        lines.Add("⚠ DESCUENTOS OTORGADOS · MOTIVO Y DETALLE:");
        using (var q = cn.CreateCommand())
        {
            q.CommandText = """
                SELECT s.ticket_no, s.created_at, COALESCE(u.full_name,''),
                       s.discount, COALESCE(s.discount_reason,''),
                       COALESCE(GROUP_CONCAT(CASE WHEN si.discount > 0.005 THEN si.description || ' ($' || printf('%.2f',si.discount) || ')' END, ', '),'')
                FROM sales s
                LEFT JOIN users u ON u.id=s.user_id
                LEFT JOIN sale_items si ON si.sale_id=s.id
                WHERE date(s.created_at,'localtime')=$d AND s.status='COMPLETED' AND s.discount > 0.005
                GROUP BY s.id
                ORDER BY s.id
                """;
            q.Parameters.AddWithValue("$d", d);
            using var r=q.ExecuteReader();
            var any=false;
            while(r.Read())
            {
                any=true;
                lines.Add($"  ⚠ Ticket #{r.GetInt64(0)} | {r.GetString(1)} | Responsable: {r.GetString(2)} | Descuento: ${r.GetDouble(3):N2} | Motivo: {r.GetString(4)}");
                if(!string.IsNullOrWhiteSpace(r.GetString(5))) lines.Add($"    Productos afectados: {r.GetString(5)}");
            }
            if(!any) lines.Add("  Sin descuentos otorgados.");
        }

        lines.Add(new string('-', 55));
        lines.Add("MERMA / DESPERDICIO DEL DÍA:");
        using (var q = cn.CreateCommand())
        {
            q.CommandText = """
                SELECT w.created_at, p.description, w.quantity, w.reason,
                       COALESCE(w.notes,''), COALESCE(u.full_name,'')
                FROM waste_records w
                JOIN products p ON p.id=w.product_id
                LEFT JOIN users u ON u.id=w.user_id
                WHERE date(w.created_at,'localtime')=$d
                ORDER BY w.id
                """;
            q.Parameters.AddWithValue("$d", d);
            using var r = q.ExecuteReader();
            var any = false;
            while (r.Read())
            {
                any = true;
                lines.Add($"{r.GetString(0)} | {r.GetString(1)} | Cantidad {r.GetDouble(2):N3} | Motivo: {r.GetString(3)} | Observación: {r.GetString(4)} | Responsable: {r.GetString(5)}");
            }
            if (!any) lines.Add("Sin mermas registradas.");
        }

        lines.Add(new string('-', 55));
        lines.Add("RECEPCIÓN DE MERCADERÍA Y DIFERENCIAS DEL DÍA:");
        using (var q = cn.CreateCommand())
        {
            q.CommandText = """
                SELECT po.received_date, po.order_no, COALESCE(s.name,''),
                       p.description, poi.quantity, poi.received_quantity,
                       (poi.received_quantity - poi.quantity), COALESCE(poi.notes,''),
                       COALESCE(po.notes,'')
                FROM purchase_order_items poi
                JOIN purchase_orders po ON po.id=poi.order_id
                JOIN products p ON p.id=poi.product_id
                LEFT JOIN suppliers s ON s.id=po.supplier_id
                WHERE date(po.received_date,'localtime')=$d
                ORDER BY po.id, poi.id
                """;
            q.Parameters.AddWithValue("$d", d);
            using var r = q.ExecuteReader();
            var any = false;
            while (r.Read())
            {
                any = true;
                lines.Add($"{r.GetString(0)} | OC {r.GetString(1)} | Proveedor: {r.GetString(2)} | {r.GetString(3)} | Pedido {r.GetDouble(4):N3} | Recibido {r.GetDouble(5):N3} | DIFERENCIA {r.GetDouble(6):N3} | Nota: {r.GetString(7)} {r.GetString(8)}");
            }
            if (!any) lines.Add("Sin recepciones con diferencias registradas.");
        }

        lines.Add(new string('-', 55));
        lines.Add("DETALLE DE DEUDAS Y ABONOS DE CLIENTES DEL DÍA:");
        using (var q = cn.CreateCommand())
        {
            q.CommandText = """
                SELECT ca.created_at, COALESCE(c.name,'Cliente'), ca.entry_type, ca.amount,
                       COALESCE(ca.payment_method,''), COALESCE(ca.concept,''),
                       COALESCE(u.full_name,'')
                FROM customer_accounts ca
                JOIN customers c ON c.id=ca.customer_id
                LEFT JOIN users u ON u.id=ca.user_id
                WHERE date(ca.created_at,'localtime')=$d
                ORDER BY ca.id
                """;
            q.Parameters.AddWithValue("$d", d);
            using var r = q.ExecuteReader();
            var any = false;
            while (r.Read())
            {
                any = true;
                var tipo = r.GetString(2).Equals("PAYMENT", StringComparison.OrdinalIgnoreCase) ? "ABONO COBRADO" : "DEUDA GENERADA";
                lines.Add($"{r.GetString(0)} | {r.GetString(1)} | {tipo} | ${r.GetDouble(3):N2} | Medio: {r.GetString(4)} | {r.GetString(5)} | Responsable: {r.GetString(6)}");
            }
            if (!any) lines.Add("Sin movimientos de cuenta corriente.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static Dictionary<string, double> PaymentTotals(Microsoft.Data.Sqlite.SqliteConnection cn, long sessionId)
    {
        using var q = cn.CreateCommand();
        q.CommandText = """
            SELECT p.method, COALESCE(SUM(p.amount),0)
            FROM payments p JOIN sales s ON s.id=p.sale_id
            WHERE s.session_id=$s AND s.status='COMPLETED' AND p.status='APPROVED'
            GROUP BY p.method
            """;
        q.Parameters.AddWithValue("$s", sessionId);
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        using var r = q.ExecuteReader();
        while (r.Read()) result[r.GetString(0)] = r.GetDouble(1);
        return result;
    }

    private static double Payment(Dictionary<string, double> totals, string method) =>
        totals.TryGetValue(method, out var value) ? value : 0;

    private static double Scalar(Microsoft.Data.Sqlite.SqliteConnection cn, string sql, long session)
    {
        using var q = cn.CreateCommand(); q.CommandText = sql; q.Parameters.AddWithValue("$s", session);
        return Convert.ToDouble(q.ExecuteScalar() ?? 0);
    }

    private static double Scalar(Microsoft.Data.Sqlite.SqliteConnection cn, string sql, string day)
    {
        using var q = cn.CreateCommand(); q.CommandText = sql; q.Parameters.AddWithValue("$d", day);
        return Convert.ToDouble(q.ExecuteScalar() ?? 0);
    }
}
