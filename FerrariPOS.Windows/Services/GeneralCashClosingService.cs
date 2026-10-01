using ClosedXML.Excel;
using FerrarisPOS.Data;
using System.Globalization;
using System.Text;

namespace FerrarisPOS.Services;

/// <summary>
/// Corte Z general: consolida todas las cajas/cajeros del día sin cerrar ninguna caja.
/// </summary>
public static class GeneralCashClosingService
{
    private static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    private static string DirectoryPath => Path.Combine(Desktop, "CORTE Z");

    public static (string text, string excelFile, int sessions, int cashiers, int tickets, double totalSales, double totalExpectedCash) Generate(DateTime date)
    {
        Directory.CreateDirectory(DirectoryPath);
        using var cn = Database.Open();
        var d = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var sessions = new List<(long id, int userId, string cashier, string username, string opened, string closed, string status, double opening, double closing, double expected, double difference, double mpExpected, double mpClosing, double mpDiff)>();
        using (var q = cn.CreateCommand())
        {
            q.CommandText = """
                SELECT cs.id, COALESCE(cs.user_id,0), COALESCE(u.full_name,''), COALESCE(u.username,''),
                       cs.opened_at, COALESCE(cs.closed_at,''), cs.status, cs.opening_amount,
                       COALESCE(cs.closing_amount,0), COALESCE(cs.expected_amount,0), COALESCE(cs.difference,0),
                       COALESCE(cs.mercado_pago_expected_amount,0), COALESCE(cs.mercado_pago_closing_amount,0), COALESCE(cs.mercado_pago_difference,0)
                FROM cash_sessions cs LEFT JOIN users u ON u.id=cs.user_id
                WHERE date(cs.opened_at,'localtime')=$d OR date(COALESCE(cs.closed_at,cs.opened_at),'localtime')=$d
                ORDER BY cs.id
                """;
            q.Parameters.AddWithValue("$d", d);
            using var r = q.ExecuteReader();
            while (r.Read()) sessions.Add((r.GetInt64(0), r.GetInt32(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetString(6), r.GetDouble(7), r.GetDouble(8), r.GetDouble(9), r.GetDouble(10), r.GetDouble(11), r.GetDouble(12), r.GetDouble(13)));
        }

        double Scalar(string sql)
        {
            using var q = cn.CreateCommand(); q.CommandText = sql; q.Parameters.AddWithValue("$d", d); return Convert.ToDouble(q.ExecuteScalar() ?? 0);
        }

        var tickets = (int)Scalar("SELECT COUNT(*) FROM sales WHERE date(created_at,'localtime')=$d AND status='COMPLETED'");
        var totalSales = Scalar("SELECT COALESCE(SUM(total),0) FROM sales WHERE date(created_at,'localtime')=$d AND status='COMPLETED'");
        var returns = Scalar("SELECT COALESCE(SUM(r.amount),0) FROM sale_returns r JOIN sales s ON s.id=r.sale_id WHERE date(r.created_at,'localtime')=$d");
        var cashSales = Scalar("SELECT COALESCE(SUM(p.amount),0) FROM payments p JOIN sales s ON s.id=p.sale_id WHERE date(s.created_at,'localtime')=$d AND s.status='COMPLETED' AND p.status='APPROVED' AND p.method='EFECTIVO'");
        var mpSales = Scalar("SELECT COALESCE(SUM(p.amount),0) FROM payments p JOIN sales s ON s.id=p.sale_id WHERE date(s.created_at,'localtime')=$d AND s.status='COMPLETED' AND p.status='APPROVED' AND p.method='MERCADO PAGO'");
        var cardSales = Scalar("SELECT COALESCE(SUM(p.amount),0) FROM payments p JOIN sales s ON s.id=p.sale_id WHERE date(s.created_at,'localtime')=$d AND s.status='COMPLETED' AND p.status='APPROVED' AND p.method='TARJETA'");
        var transferSales = Scalar("SELECT COALESCE(SUM(p.amount),0) FROM payments p JOIN sales s ON s.id=p.sale_id WHERE date(s.created_at,'localtime')=$d AND s.status='COMPLETED' AND p.status='APPROVED' AND p.method='TRANSFERENCIA'");
        var creditSales = Scalar("SELECT COALESCE(SUM(p.amount),0) FROM payments p JOIN sales s ON s.id=p.sale_id WHERE date(s.created_at,'localtime')=$d AND s.status='COMPLETED' AND p.status='APPROVED' AND p.method='CRÉDITO'");
        var incomes = Scalar("SELECT COALESCE(SUM(amount),0) FROM cash_movements WHERE date(created_at,'localtime')=$d AND movement_type='INCOME' AND COALESCE(voided,0)=0");
        var expenses = Scalar("SELECT COALESCE(SUM(amount),0) FROM cash_movements WHERE date(created_at,'localtime')=$d AND movement_type='EXPENSE' AND COALESCE(voided,0)=0");

        var expectedCash = 0d;
        foreach (var s in sessions)
        {
            if (string.Equals(s.status, "CLOSED", StringComparison.OrdinalIgnoreCase) && Math.Abs(s.expected) > 0.0001)
                expectedCash += s.expected;
            else
            {
                using var q = cn.CreateCommand();
                q.CommandText = "SELECT COALESCE(SUM(amount),0) FROM cash_movements WHERE session_id=$s AND movement_type='SALE' AND payment_method='EFECTIVO'";
                q.Parameters.AddWithValue("$s", s.id); var sales = Convert.ToDouble(q.ExecuteScalar() ?? 0);
                using var q2 = cn.CreateCommand();
                q2.CommandText = "SELECT COALESCE(SUM(CASE WHEN movement_type='INCOME' AND COALESCE(voided,0)=0 THEN amount WHEN movement_type='EXPENSE' AND COALESCE(voided,0)=0 THEN -amount ELSE 0 END),0) FROM cash_movements WHERE session_id=$s AND payment_method='EFECTIVO'";
                q2.Parameters.AddWithValue("$s", s.id); var movements = Convert.ToDouble(q2.ExecuteScalar() ?? 0);
                expectedCash += s.opening + sales + movements;
            }
        }

        var byCashier = new List<(string cashier, string username, int tickets, double sales, double cash, double mp, double card, double transfer, double credit)>();
        using (var q = cn.CreateCommand())
        {
            q.CommandText = """
                SELECT COALESCE(u.full_name,''), COALESCE(u.username,''), COUNT(*), COALESCE(SUM(s.total),0),
                       COALESCE(SUM((SELECT COALESCE(SUM(CASE WHEN p.method='EFECTIVO' THEN p.amount ELSE 0 END),0) FROM payments p WHERE p.sale_id=s.id AND p.status='APPROVED')),0),
                       COALESCE(SUM((SELECT COALESCE(SUM(CASE WHEN p.method='MERCADO PAGO' THEN p.amount ELSE 0 END),0) FROM payments p WHERE p.sale_id=s.id AND p.status='APPROVED')),0),
                       COALESCE(SUM((SELECT COALESCE(SUM(CASE WHEN p.method='TARJETA' THEN p.amount ELSE 0 END),0) FROM payments p WHERE p.sale_id=s.id AND p.status='APPROVED')),0),
                       COALESCE(SUM((SELECT COALESCE(SUM(CASE WHEN p.method='TRANSFERENCIA' THEN p.amount ELSE 0 END),0) FROM payments p WHERE p.sale_id=s.id AND p.status='APPROVED')),0),
                       COALESCE(SUM((SELECT COALESCE(SUM(CASE WHEN p.method='CRÉDITO' THEN p.amount ELSE 0 END),0) FROM payments p WHERE p.sale_id=s.id AND p.status='APPROVED')),0)
                FROM sales s LEFT JOIN users u ON u.id=s.user_id
                WHERE date(s.created_at,'localtime')=$d AND s.status='COMPLETED'
                GROUP BY s.user_id, u.full_name, u.username ORDER BY SUM(s.total) DESC
                """;
            q.Parameters.AddWithValue("$d", d);
            using var r = q.ExecuteReader();
            while (r.Read()) byCashier.Add((r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetDouble(3), r.GetDouble(4), r.GetDouble(5), r.GetDouble(6), r.GetDouble(7), r.GetDouble(8)));
        }

        var categorySales = SalesCategoryReportService.ForDate(cn, date);
        var deletedCustomers = new List<(string createdAt, string customerName, string document, string reason, string user)>();
        using (var q = cn.CreateCommand())
        {
            q.CommandText = "SELECT d.created_at,d.customer_name,COALESCE(d.customer_document,''),d.reason,COALESCE(u.full_name,u.username,'') FROM customer_deletion_log d LEFT JOIN users u ON u.id=d.user_id WHERE date(d.created_at,'localtime')=$d ORDER BY d.id";
            q.Parameters.AddWithValue("$d", d);
            using var r = q.ExecuteReader();
            while (r.Read()) deletedCustomers.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4)));
        }

        var deletedProducts = new List<(string createdAt, string barcode, string name, double stock, string reason, string user)>();
        using (var q = cn.CreateCommand())
        {
            q.CommandText = "SELECT d.created_at,d.product_barcode,d.product_name,d.stock_before,d.reason,COALESCE(u.full_name,u.username,'') FROM product_deletion_log d LEFT JOIN users u ON u.id=d.user_id WHERE date(d.created_at,'localtime')=$d ORDER BY d.id";
            q.Parameters.AddWithValue("$d", d);
            using var r = q.ExecuteReader();
            while (r.Read()) deletedProducts.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetDouble(3), r.GetString(4), r.GetString(5)));
        }
        var deletedSuppliers = new List<(string createdAt, string name, string reason, string user)>();
        using (var q = cn.CreateCommand())
        {
            q.CommandText = "SELECT d.created_at,d.supplier_name,d.reason,COALESCE(u.full_name,u.username,'') FROM supplier_deletion_log d LEFT JOIN users u ON u.id=d.user_id WHERE date(d.created_at,'localtime')=$d ORDER BY d.id";
            q.Parameters.AddWithValue("$d", d);
            using var r = q.ExecuteReader();
            while (r.Read()) deletedSuppliers.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3)));
        }

        var b = new StringBuilder();
        b.AppendLine("FERRARISPOS® · CORTE Z GENERAL");
        b.AppendLine($"FECHA: {date:dd/MM/yyyy}");
        b.AppendLine($"GENERADO: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
        b.AppendLine("============================================================");
        b.AppendLine("CONSOLIDADO DE TODO EL DÍA · TODOS LOS CAJEROS");
        b.AppendLine($"Cajas/turnos: {sessions.Count}");
        b.AppendLine($"Cajeros con ventas: {byCashier.Count}");
        b.AppendLine($"Tickets: {tickets}");
        b.AppendLine($"VENTAS TOTALES: ${totalSales:N2}");
        b.AppendLine($"DEVOLUCIONES: ${returns:N2}");
        b.AppendLine($"EFECTIVO: ${cashSales:N2}");
        b.AppendLine($"MERCADO PAGO: ${mpSales:N2}");
        b.AppendLine($"TARJETA: ${cardSales:N2}");
        b.AppendLine($"TRANSFERENCIA: ${transferSales:N2}");
        b.AppendLine($"CRÉDITO: ${creditSales:N2}");
        b.AppendLine();
        b.AppendLine("VENTAS POR CATEGORÍA / DEPARTAMENTO · DINERO VENDIDO");
        if (categorySales.Count == 0) b.AppendLine("  Sin ventas clasificadas por categoría.");
        else foreach (var x in categorySales)
            b.AppendLine($"  {x.Category}: EFECTIVO ${x.Cash:N2} + MERCADO PAGO ${x.MercadoPago:N2} + TARJETA ${x.Card:N2} + TRANSFERENCIA ${x.Transfer:N2} + CRÉDITO ${x.Credit:N2} = TOTAL VENDIDO ${x.Total:N2}");
        b.AppendLine($"  TOTAL CATEGORÍAS: ${categorySales.Sum(x => x.Total):N2}");
        b.AppendLine($"INGRESOS DE CAJA: ${incomes:N2}");
        b.AppendLine($"EGRESOS DE CAJA: ${expenses:N2}");
        b.AppendLine($"EFECTIVO ESPERADO CONSOLIDADO: ${expectedCash:N2}");
        b.AppendLine();
        b.AppendLine("DETALLE POR CAJERO");
        foreach (var c in byCashier)
            b.AppendLine($"{(string.IsNullOrWhiteSpace(c.cashier) ? c.username : c.cashier)} · Tickets {c.tickets} · Ventas ${c.sales:N2} · Efectivo ${c.cash:N2} · MP ${c.mp:N2} · Tarjeta ${c.card:N2} · Transferencia ${c.transfer:N2} · Crédito ${c.credit:N2}");
        b.AppendLine();
        b.AppendLine("TURNOS / CAJAS DEL DÍA");
        foreach (var s in sessions)
            b.AppendLine($"Caja #{s.id} · {(string.IsNullOrWhiteSpace(s.cashier) ? s.username : s.cashier)} · {s.status} · Apertura ${s.opening:N2} · Esperado ${s.expected:N2} · Cierre ${s.closing:N2} · Diferencia ${s.difference:N2}");
        b.AppendLine();
        b.AppendLine("CLIENTES ELIMINADOS · MOTIVO");
        if (deletedCustomers.Count == 0) b.AppendLine("  Sin clientes eliminados durante el día.");
        else foreach (var x in deletedCustomers)
            b.AppendLine($"  {x.createdAt} · {x.customerName} · Documento: {x.document} · Motivo: {x.reason} · Responsable: {x.user}");

        b.AppendLine();
        b.AppendLine("PRODUCTOS ELIMINADOS · STOCK ANTERIOR · MOTIVO · RESPONSABLE");
        if (deletedProducts.Count == 0) b.AppendLine("  Sin productos eliminados durante el día.");
        else foreach (var x in deletedProducts)
            b.AppendLine($"  {x.createdAt} · {x.name} · Código: {x.barcode} · Stock anterior: {x.stock:N3} · Motivo: {x.reason} · Responsable: {x.user}");
        b.AppendLine();
        b.AppendLine("PROVEEDORES ELIMINADOS · MOTIVO · RESPONSABLE");
        if (deletedSuppliers.Count == 0) b.AppendLine("  Sin proveedores eliminados durante el día.");
        else foreach (var x in deletedSuppliers)
            b.AppendLine($"  {x.createdAt} · {x.name} · Motivo: {x.reason} · Responsable: {x.user}");

        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        var txtFile = Path.Combine(DirectoryPath, $"CORTE_Z_GENERAL_{d}_{stamp}.txt");
        File.WriteAllText(txtFile, b.ToString(), new UTF8Encoding(true));

        var excelFile = Path.Combine(DirectoryPath, $"CORTE_Z_GENERAL_{d}_{stamp}.xlsx");
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("CORTE Z GENERAL");
        ws.Cell(1,1).Value = "FERRARISPOS® · CORTE Z GENERAL"; ws.Cell(1,1).Style.Font.Bold = true;
        ws.Cell(2,1).Value = "Fecha"; ws.Cell(2,2).Value = date.ToString("dd/MM/yyyy");
        var rows = new[] { ("Cajas/turnos", sessions.Count.ToString()), ("Cajeros", byCashier.Count.ToString()), ("Tickets", tickets.ToString()), ("Ventas totales", totalSales.ToString("0.00")), ("Devoluciones", returns.ToString("0.00")), ("Efectivo", cashSales.ToString("0.00")), ("Mercado Pago", mpSales.ToString("0.00")), ("Tarjeta", cardSales.ToString("0.00")), ("Transferencia", transferSales.ToString("0.00")), ("Crédito", creditSales.ToString("0.00")), ("Ingresos", incomes.ToString("0.00")), ("Egresos", expenses.ToString("0.00")), ("Efectivo esperado consolidado", expectedCash.ToString("0.00")) };
        var row = 4; foreach (var x in rows) { ws.Cell(row,1).Value=x.Item1; ws.Cell(row,2).Value=x.Item2; row++; }
        var wc = wb.Worksheets.Add("POR CAJERO");
        wc.Cell(1,1).Value="Cajero"; wc.Cell(1,2).Value="Usuario"; wc.Cell(1,3).Value="Tickets"; wc.Cell(1,4).Value="Ventas"; wc.Cell(1,5).Value="Efectivo"; wc.Cell(1,6).Value="Mercado Pago"; wc.Cell(1,7).Value="Tarjeta"; wc.Cell(1,8).Value="Transferencia"; wc.Cell(1,9).Value="Crédito"; wc.Row(1).Style.Font.Bold=true;
        row=2; foreach(var c in byCashier){wc.Cell(row,1).Value=c.cashier;wc.Cell(row,2).Value=c.username;wc.Cell(row,3).Value=c.tickets;wc.Cell(row,4).Value=c.sales;wc.Cell(row,5).Value=c.cash;wc.Cell(row,6).Value=c.mp;wc.Cell(row,7).Value=c.card;wc.Cell(row,8).Value=c.transfer;wc.Cell(row,9).Value=c.credit;row++;}
        var wcat=wb.Worksheets.Add("POR CATEGORIA");
        string[] hc={"Categoría / Departamento","Efectivo","Mercado Pago","Tarjeta","Transferencia","Crédito","Total vendido"};
        for(int i=0;i<hc.Length;i++) wcat.Cell(1,i+1).Value=hc[i]; wcat.Row(1).Style.Font.Bold=true;
        row=2; foreach(var x in categorySales){wcat.Cell(row,1).Value=x.Category;wcat.Cell(row,2).Value=x.Cash;wcat.Cell(row,3).Value=x.MercadoPago;wcat.Cell(row,4).Value=x.Card;wcat.Cell(row,5).Value=x.Transfer;wcat.Cell(row,6).Value=x.Credit;wcat.Cell(row,7).Value=x.Total;row++;}
        var wdel=wb.Worksheets.Add("CLIENTES ELIMINADOS");
        string[] hd={"Fecha/Hora","Cliente","Documento","Motivo","Responsable"};
        for(int i=0;i<hd.Length;i++) wdel.Cell(1,i+1).Value=hd[i]; wdel.Row(1).Style.Font.Bold=true;
        row=2; foreach(var x in deletedCustomers){wdel.Cell(row,1).Value=x.createdAt;wdel.Cell(row,2).Value=x.customerName;wdel.Cell(row,3).Value=x.document;wdel.Cell(row,4).Value=x.reason;wdel.Cell(row,5).Value=x.user;row++;}
        var wpdel=wb.Worksheets.Add("PRODUCTOS ELIMINADOS");
        string[] hpd={"Fecha/Hora","Código","Producto","Stock anterior","Motivo","Responsable"};
        for(int i=0;i<hpd.Length;i++) wpdel.Cell(1,i+1).Value=hpd[i]; wpdel.Row(1).Style.Font.Bold=true;
        row=2; foreach(var x in deletedProducts){wpdel.Cell(row,1).Value=x.createdAt;wpdel.Cell(row,2).Value=x.barcode;wpdel.Cell(row,3).Value=x.name;wpdel.Cell(row,4).Value=x.stock;wpdel.Cell(row,5).Value=x.reason;wpdel.Cell(row,6).Value=x.user;row++;}
        var wsdel=wb.Worksheets.Add("PROVEEDORES ELIMINADOS");
        string[] hsd={"Fecha/Hora","Proveedor","Motivo","Responsable"};
        for(int i=0;i<hsd.Length;i++) wsdel.Cell(1,i+1).Value=hsd[i]; wsdel.Row(1).Style.Font.Bold=true;
        row=2; foreach(var x in deletedSuppliers){wsdel.Cell(row,1).Value=x.createdAt;wsdel.Cell(row,2).Value=x.name;wsdel.Cell(row,3).Value=x.reason;wsdel.Cell(row,4).Value=x.user;row++;}
        var wt=wb.Worksheets.Add("TURNOS");
        string[] h={"Caja","Cajero","Usuario","Estado","Apertura","Cierre","Esperado","Diferencia","MP Esperado","MP Cierre","MP Diferencia"}; for(int i=0;i<h.Length;i++)wt.Cell(1,i+1).Value=h[i]; wt.Row(1).Style.Font.Bold=true;
        row=2; foreach(var s in sessions){wt.Cell(row,1).Value=s.id;wt.Cell(row,2).Value=s.cashier;wt.Cell(row,3).Value=s.username;wt.Cell(row,4).Value=s.status;wt.Cell(row,5).Value=s.opening;wt.Cell(row,6).Value=s.closing;wt.Cell(row,7).Value=s.expected;wt.Cell(row,8).Value=s.difference;wt.Cell(row,9).Value=s.mpExpected;wt.Cell(row,10).Value=s.mpClosing;wt.Cell(row,11).Value=s.mpDiff;row++;}
        foreach(var sheet in wb.Worksheets){sheet.Columns().AdjustToContents(); foreach(var col in sheet.ColumnsUsed()) if(col.Width>40) col.Width=40;}
        wb.SaveAs(excelFile);
        return (b.ToString(), excelFile, sessions.Count, byCashier.Count, tickets, totalSales, expectedCash);
    }
}
