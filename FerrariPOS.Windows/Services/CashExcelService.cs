using ClosedXML.Excel;
using FerrarisPOS.Data;
using System.Globalization;
using System.Text;

namespace FerrarisPOS.Services;

public static class CashExcelService
{
    private static string Desktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    private static string DesktopFile => Path.Combine(Desktop, "FerrariPOS_Arqueos.xlsx");
    // Los TXT de cada cierre se agrupan en una única carpeta en el escritorio.
    // El nombre identifica al cajero y el momento exacto del cierre.
    private static string ClosureDirectory => Path.Combine(Desktop, "CIERRE DE CAJA");
    private static string DatedTextFile(long sessionId, string cashierName)
    {
        var safeCashier = SanitizeFileName(string.IsNullOrWhiteSpace(cashierName) ? "Cajero" : cashierName);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture);
        return Path.Combine(ClosureDirectory, $"{safeCashier}_{timestamp}.txt");
    }

    private static string SanitizeFileName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            value = value.Replace(c, '_');
        return string.Join("_", value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)).Trim('_');
    }

    /// <summary>
    /// Genera/actualiza un libro histórico y un TXT extremadamente detallado
    /// cada vez que se cierra una caja. El Excel conserva todos los cierres.
    /// </summary>
    public static string ExportClosedSession(long sessionId)
    {
        Directory.CreateDirectory(Desktop);
        Directory.CreateDirectory(ClosureDirectory);
        using var cn = Database.Open();

        var header = LoadSession(cn, sessionId);

        using var wb = File.Exists(DesktopFile) ? new XLWorkbook(DesktopFile) : new XLWorkbook();

        WriteClosureSheet(wb, cn, sessionId, header);
        WriteSalesSheet(wb, cn, sessionId);
        WriteItemsSheet(wb, cn, sessionId);
        WritePaymentsSheet(wb, cn, sessionId);
        WriteMovementsSheet(wb, cn, sessionId);
        WriteReturnsSheet(wb, cn, sessionId);
        WriteStockSheet(wb, cn, sessionId);
        WriteWasteSheet(wb, cn, sessionId);
        WriteReceptionDifferencesSheet(wb, cn, sessionId);
        WriteCustomerAccountsSheet(wb, cn, sessionId);
        WriteCategorySalesSheet(wb, cn, sessionId);
        WriteCustomerDeletionsSheet(wb, cn, sessionId);
        WriteDailySummarySheet(wb, cn, header.Item3);

        foreach (var ws in wb.Worksheets)
        {
            ws.Columns().AdjustToContents();
            foreach (var col in ws.ColumnsUsed())
                if (col.Width > 45) col.Width = 45;
        }
        wb.SaveAs(DesktopFile);

        var detailedText = BuildDetailedText(cn, sessionId, header);
        var textPath = DatedTextFile(sessionId, header.fullName);
        File.WriteAllText(textPath, detailedText, new UTF8Encoding(true));
        _lastDetailedTextFilePath = textPath;
        return DesktopFile;
    }

    private static string? _lastDetailedTextFilePath;
    public static string DetailedTextFilePath => _lastDetailedTextFilePath ?? ClosureDirectory;

    private static (long id, string openedAt, string closedAt, double opening, double closing, double expected,
                    double difference, bool mercadoPagoEnabled, double mercadoPagoOpening, double mercadoPagoClosing,
                    double mercadoPagoExpected, double mercadoPagoDifference, string fullName, string username) LoadSession(Microsoft.Data.Sqlite.SqliteConnection cn, long sessionId)
    {
        using var q = cn.CreateCommand();
        q.CommandText = """
            SELECT cs.id, cs.opened_at, COALESCE(cs.closed_at,''), cs.opening_amount,
                   COALESCE(cs.closing_amount,0), COALESCE(cs.expected_amount,0),
                   COALESCE(cs.difference,0), COALESCE(cs.mercado_pago_enabled,0),
                   COALESCE(cs.mercado_pago_opening_amount,0), COALESCE(cs.mercado_pago_closing_amount,0),
                   COALESCE(cs.mercado_pago_expected_amount,0), COALESCE(cs.mercado_pago_difference,0),
                   COALESCE(u.full_name,''), COALESCE(u.username,'')
            FROM cash_sessions cs LEFT JOIN users u ON u.id=cs.user_id
            WHERE cs.id=$id
            """;
        q.Parameters.AddWithValue("$id", sessionId);
        using var r = q.ExecuteReader();
        if (!r.Read()) throw new InvalidOperationException("No se encontró el cierre de caja.");
        return (r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetDouble(3), r.GetDouble(4),
            r.GetDouble(5), r.GetDouble(6), r.GetInt32(7) != 0, r.GetDouble(8), r.GetDouble(9),
            r.GetDouble(10), r.GetDouble(11), r.GetString(12), r.GetString(13));
    }

    private static void Prepare(IXLWorksheet ws, string[] headers)
    {
        ws.Clear();
        for (int i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        ws.Row(1).Style.Font.Bold = true;
        ws.Row(1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.SheetView.FreezeRows(1);
        ws.AutoFilter.Clear();
    }

    private static int NextRow(IXLWorksheet ws) => (ws.LastRowUsed()?.RowNumber() ?? 1) + 1;

    private static IXLWorksheet GetOrCreateWorksheet(XLWorkbook wb, string name, string[]? headers = null)
    {
        var ws = wb.Worksheets.FirstOrDefault(x =>
            string.Equals(x.Name?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));

        if (ws == null)
            ws = wb.Worksheets.Add(name);

        if (headers != null && ws.LastRowUsed() == null)
            Prepare(ws, headers);

        return ws;
    }

    private static void WriteClosureSheet(XLWorkbook wb, Microsoft.Data.Sqlite.SqliteConnection cn, long id,
        (long id,string openedAt,string closedAt,double opening,double closing,double expected,double difference,bool mercadoPagoEnabled,double mercadoPagoOpening,double mercadoPagoClosing,double mercadoPagoExpected,double mercadoPagoDifference,string fullName,string username) h)
    {
        var ws = GetOrCreateWorksheet(wb, "CIERRES", new[]{"ID CAJA","APERTURA","CIERRE","CAJERO","USUARIO","FONDO INICIAL","EFECTIVO ESPERADO","EFECTIVO CONTADO","DIFERENCIA","MP HABILITADO","MP APERTURA","MP ESPERADO","MP CONTADO","MP DIFERENCIA","VENTAS","TOTAL VENTAS","INGRESOS","EGRESOS","DEVOLUCIONES","MERMA CANTIDAD","DEUDA GENERADA","ABONOS CLIENTES","EFECTIVO","MERCADO PAGO","MIXTO","TRANSFERENCIA","CRÉDITO","DÓLARES"});
        var row = NextRow(ws);
        string[] paymentHeaders = {"EFECTIVO","MERCADO PAGO","MIXTO","TRANSFERENCIA","CRÉDITO","DÓLARES"};
        for (int i=0; i<paymentHeaders.Length; i++) if (ws.Cell(1,23+i).IsEmpty()) ws.Cell(1,23+i).Value = paymentHeaders[i];
        var salesCount = Scalar(cn,"SELECT COUNT(*) FROM sales WHERE session_id=$s AND status='COMPLETED'",id);
        var salesTotal = Scalar(cn,"SELECT COALESCE(SUM(total),0) FROM sales WHERE session_id=$s AND status='COMPLETED'",id);
        var income = Scalar(cn,"SELECT COALESCE(SUM(amount),0) FROM cash_movements WHERE session_id=$s AND movement_type='INCOME' AND COALESCE(voided,0)=0",id);
        var expense = Scalar(cn,"SELECT COALESCE(SUM(amount),0) FROM cash_movements WHERE session_id=$s AND movement_type='EXPENSE' AND COALESCE(voided,0)=0",id);
        var returns = Scalar(cn,"SELECT COALESCE(SUM(amount),0) FROM sale_returns r JOIN sales s ON s.id=r.sale_id WHERE s.session_id=$s",id);
        var wasteQty = Scalar(cn,"SELECT COALESCE(SUM(w.quantity),0) FROM waste_records w WHERE w.created_at >= (SELECT opened_at FROM cash_sessions WHERE id=$s) AND w.created_at <= (SELECT COALESCE(closed_at,CURRENT_TIMESTAMP) FROM cash_sessions WHERE id=$s)",id);
        var debtGenerated = Scalar(cn,"SELECT COALESCE(SUM(amount),0) FROM customer_accounts WHERE entry_type='SALE' AND created_at >= (SELECT opened_at FROM cash_sessions WHERE id=$s) AND created_at <= (SELECT COALESCE(closed_at,CURRENT_TIMESTAMP) FROM cash_sessions WHERE id=$s)",id);
        var customerPayments = Scalar(cn,"SELECT COALESCE(SUM(amount),0) FROM customer_accounts WHERE entry_type='PAYMENT' AND created_at >= (SELECT opened_at FROM cash_sessions WHERE id=$s) AND created_at <= (SELECT COALESCE(closed_at,CURRENT_TIMESTAMP) FROM cash_sessions WHERE id=$s)",id);
        var paymentTotals = PaymentTotals(cn, id);
        object[] v={h.id,h.openedAt,h.closedAt,h.fullName,h.username,h.opening,h.expected,h.closing,h.difference,h.mercadoPagoEnabled ? "SI" : "NO",h.mercadoPagoOpening,h.mercadoPagoExpected,h.mercadoPagoClosing,h.mercadoPagoDifference,salesCount,salesTotal,income,expense,returns,wasteQty,debtGenerated,customerPayments,Payment(paymentTotals,"EFECTIVO"),Payment(paymentTotals,"MERCADO PAGO"),Payment(paymentTotals,"MIXTO"),Payment(paymentTotals,"TRANSFERENCIA"),Payment(paymentTotals,"CRÉDITO"),Payment(paymentTotals,"DÓLARES")};
        for(int i=0;i<v.Length;i++) ws.Cell(row,i+1).Value=XLCellValue.FromObject(v[i]);
        for (int c=6; c<=9; c++) ws.Cell(row,c).Style.NumberFormat.Format="#,##0.00";
        for (int c=11; c<=14; c++) ws.Cell(row,c).Style.NumberFormat.Format="#,##0.00";
        for (int c=15; c<=28; c++) ws.Cell(row,c).Style.NumberFormat.Format="#,##0.00";
    }

    private static void WriteSalesSheet(XLWorkbook wb, Microsoft.Data.Sqlite.SqliteConnection cn, long sessionId)
    {
        var ws=GetOrCreateWorksheet(wb,"VENTAS",new[]{"ID","TICKET","FECHA/HORA","CAJERO","CLIENTE","SUBTOTAL","DESCUENTO","TOTAL","MEDIO","MEDIOS DE PAGO DETALLADOS","MERCADO PAGO"});
        if (ws.Cell(1,10).IsEmpty()) ws.Cell(1,10).Value = "MEDIOS DE PAGO DETALLADOS";
        if (ws.Cell(1,11).IsEmpty()) ws.Cell(1,11).Value = "MERCADO PAGO";
        using var q=cn.CreateCommand(); q.CommandText="""
            SELECT s.id,s.ticket_no,s.created_at,COALESCE(u.full_name,''),COALESCE(c.name,'Público General'),
                   s.subtotal,s.discount,s.total,s.payment_method,
                   COALESCE((SELECT GROUP_CONCAT(p.method || ': $' || printf('%.2f',p.amount),' + ') FROM payments p WHERE p.sale_id=s.id AND p.status='APPROVED'),''),
                   COALESCE((SELECT SUM(p.amount) FROM payments p WHERE p.sale_id=s.id AND p.method='MERCADO PAGO' AND p.status='APPROVED'),0)
            FROM sales s LEFT JOIN users u ON u.id=s.user_id LEFT JOIN customers c ON c.id=s.customer_id
            WHERE s.session_id=$s AND s.status='COMPLETED' ORDER BY s.id
            """; q.Parameters.AddWithValue("$s",sessionId);
        using var r=q.ExecuteReader();
        while(r.Read()){int row=NextRow(ws); object[] v={r.GetInt64(0),r.GetInt64(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetDouble(5),r.GetDouble(6),r.GetDouble(7),r.GetString(8),r.GetString(9),r.GetDouble(10)};for(int i=0;i<v.Length;i++)ws.Cell(row,i+1).Value=XLCellValue.FromObject(v[i]); ws.Cell(row,11).Style.NumberFormat.Format="#,##0.00";}
    }

    private static void WriteItemsSheet(XLWorkbook wb, Microsoft.Data.Sqlite.SqliteConnection cn, long sessionId)
    {
        var ws=GetOrCreateWorksheet(wb,"DETALLE VENTAS",new[]{"TICKET","FECHA","CÓDIGO","PRODUCTO","CANTIDAD","PRECIO UNITARIO","DESCUENTO","TOTAL","DEVUELTO"});
        using var q=cn.CreateCommand(); q.CommandText="""
            SELECT s.ticket_no,s.created_at,si.barcode,si.description,si.quantity,si.unit_price,si.discount,si.total,COALESCE(si.returned_quantity,0)
            FROM sale_items si JOIN sales s ON s.id=si.sale_id
            WHERE s.session_id=$s AND s.status='COMPLETED' ORDER BY s.id,si.id
            """; q.Parameters.AddWithValue("$s",sessionId);
        using var r=q.ExecuteReader();
        while(r.Read()){int row=NextRow(ws);object[]v={r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetDouble(4),r.GetDouble(5),r.GetDouble(6),r.GetDouble(7),r.GetDouble(8)};for(int i=0;i<v.Length;i++)ws.Cell(row,i+1).Value=XLCellValue.FromObject(v[i]);}
    }

    private static void WritePaymentsSheet(XLWorkbook wb, Microsoft.Data.Sqlite.SqliteConnection cn, long sessionId)
    {
        var ws=GetOrCreateWorksheet(wb,"PAGOS",new[]{"TICKET","FECHA","MEDIO","IMPORTE","REFERENCIA","ESTADO"});
        using var q=cn.CreateCommand();q.CommandText="""
            SELECT s.ticket_no,p.created_at,p.method,p.amount,p.reference,p.status
            FROM payments p JOIN sales s ON s.id=p.sale_id WHERE s.session_id=$s ORDER BY p.id
            """;q.Parameters.AddWithValue("$s",sessionId);using var r=q.ExecuteReader();
        while(r.Read()){int row=NextRow(ws);object[]v={r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetDouble(3),r.GetString(4),r.GetString(5)};for(int i=0;i<v.Length;i++)ws.Cell(row,i+1).Value=XLCellValue.FromObject(v[i]);}
    }

    private static void WriteMovementsSheet(XLWorkbook wb, Microsoft.Data.Sqlite.SqliteConnection cn, long sessionId)
    {
        var ws=GetOrCreateWorksheet(wb,"MOVIMIENTOS",new[]{"FECHA","TIPO","MEDIO","CONCEPTO","IMPORTE","USUARIO","REFERENCIA","ESTADO","EXPLICACIÓN ANULACIÓN","ANULÓ"});
        using var q=cn.CreateCommand();q.CommandText="""
            SELECT cm.created_at,cm.movement_type,cm.payment_method,cm.concept,cm.amount,COALESCE(u.full_name,''),COALESCE(cm.reference_id,''),COALESCE(cm.voided,0),COALESCE(cm.void_reason,''),COALESCE(v.full_name,v.username,'')
            FROM cash_movements cm LEFT JOIN users u ON u.id=cm.user_id LEFT JOIN users v ON v.id=cm.voided_by
            WHERE cm.session_id=$s AND cm.movement_type IN ('INCOME','EXPENSE') ORDER BY cm.id
            """;q.Parameters.AddWithValue("$s",sessionId);using var r=q.ExecuteReader();
        while(r.Read()){int row=NextRow(ws);object[]v={r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetDouble(4),r.GetString(5),r.GetValue(6)?.ToString()??"",r.GetInt32(7)!=0?"ANULADO":"ACTIVO",r.GetString(8),r.GetString(9)};for(int i=0;i<v.Length;i++)ws.Cell(row,i+1).Value=XLCellValue.FromObject(v[i]);}
    }

    private static void WriteReturnsSheet(XLWorkbook wb, Microsoft.Data.Sqlite.SqliteConnection cn, long sessionId)
    {
        var ws=GetOrCreateWorksheet(wb,"DEVOLUCIONES",new[]{"TICKET","FECHA/HORA","CÓDIGO","PRODUCTO","CANTIDAD","IMPORTE","MOTIVO","USUARIO"});
        using var q=cn.CreateCommand();q.CommandText="""
            SELECT s.ticket_no,r.created_at,COALESCE(si.barcode,''),COALESCE(si.description,''),r.quantity,r.amount,r.reason,COALESCE(u.full_name,'')
            FROM sale_returns r JOIN sales s ON s.id=r.sale_id
            LEFT JOIN sale_items si ON si.id=r.sale_item_id
            LEFT JOIN users u ON u.id=r.user_id
            WHERE s.session_id=$s ORDER BY r.id
            """;q.Parameters.AddWithValue("$s",sessionId);using var r=q.ExecuteReader();
        while(r.Read()){int row=NextRow(ws);object[]v={r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetDouble(4),r.GetDouble(5),r.GetString(6),r.GetString(7)};for(int i=0;i<v.Length;i++)ws.Cell(row,i+1).Value=XLCellValue.FromObject(v[i]);}
    }

    private static void WriteStockSheet(XLWorkbook wb, Microsoft.Data.Sqlite.SqliteConnection cn, long sessionId)
    {
        var ws=GetOrCreateWorksheet(wb,"STOCK",new[]{"FECHA","PRODUCTO","CÓDIGO","TIPO","CANTIDAD","REFERENCIA","USUARIO"});
        using var q=cn.CreateCommand();q.CommandText="""
            SELECT sm.created_at,p.description,p.barcode,sm.movement_type,sm.quantity,sm.reference,COALESCE(u.full_name,'')
            FROM stock_movements sm JOIN products p ON p.id=sm.product_id LEFT JOIN users u ON u.id=sm.user_id
            WHERE sm.created_at >= (SELECT opened_at FROM cash_sessions WHERE id=$s)
              AND (sm.created_at <= (SELECT COALESCE(closed_at,CURRENT_TIMESTAMP) FROM cash_sessions WHERE id=$s))
            ORDER BY sm.id
            """;q.Parameters.AddWithValue("$s",sessionId);using var r=q.ExecuteReader();
        while(r.Read()){int row=NextRow(ws);object[]v={r.GetString(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetDouble(4),r.GetString(5),r.GetString(6)};for(int i=0;i<v.Length;i++)ws.Cell(row,i+1).Value=XLCellValue.FromObject(v[i]);}
    }


    private static void WriteWasteSheet(XLWorkbook wb, Microsoft.Data.Sqlite.SqliteConnection cn, long sessionId)
    {
        var ws = GetOrCreateWorksheet(wb, "MERMAS", new[] { "FECHA", "PRODUCTO", "CÓDIGO", "CANTIDAD", "MOTIVO", "OBSERVACIÓN", "RESPONSABLE" });
        using var q = cn.CreateCommand();
        q.CommandText = """
            SELECT w.created_at,p.description,p.barcode,w.quantity,w.reason,COALESCE(w.notes,''),COALESCE(u.full_name,'')
            FROM waste_records w
            JOIN products p ON p.id=w.product_id
            LEFT JOIN users u ON u.id=w.user_id
            WHERE w.created_at >= (SELECT opened_at FROM cash_sessions WHERE id=$s)
              AND w.created_at <= (SELECT COALESCE(closed_at,CURRENT_TIMESTAMP) FROM cash_sessions WHERE id=$s)
            ORDER BY w.id
            """;
        q.Parameters.AddWithValue("$s", sessionId);
        using var r = q.ExecuteReader();
        while (r.Read())
        {
            int row = NextRow(ws);
            object[] v = { r.GetString(0), r.GetString(1), r.GetString(2), r.GetDouble(3), r.GetString(4), r.GetString(5), r.GetString(6) };
            for (int i = 0; i < v.Length; i++) ws.Cell(row, i + 1).Value = XLCellValue.FromObject(v[i]);
        }
    }

    private static void WriteReceptionDifferencesSheet(XLWorkbook wb, Microsoft.Data.Sqlite.SqliteConnection cn, long sessionId)
    {
        var ws = GetOrCreateWorksheet(wb, "RECEPCIONES", new[] { "FECHA", "ORDEN", "PROVEEDOR", "PRODUCTO", "PEDIDO", "RECIBIDO", "DIFERENCIA", "NOTA LÍNEA", "NOTA RECEPCIÓN" });
        using var q = cn.CreateCommand();
        q.CommandText = """
            SELECT po.received_date,po.order_no,COALESCE(s.name,''),p.description,poi.quantity,
                   poi.received_quantity,(poi.received_quantity-poi.quantity),
                   COALESCE(poi.notes,''),COALESCE(po.notes,'')
            FROM purchase_order_items poi
            JOIN purchase_orders po ON po.id=poi.order_id
            JOIN products p ON p.id=poi.product_id
            LEFT JOIN suppliers s ON s.id=po.supplier_id
            WHERE po.received_date >= (SELECT opened_at FROM cash_sessions WHERE id=$s)
              AND po.received_date <= (SELECT COALESCE(closed_at,CURRENT_TIMESTAMP) FROM cash_sessions WHERE id=$s)
            ORDER BY po.id,poi.id
            """;
        q.Parameters.AddWithValue("$s", sessionId);
        using var r = q.ExecuteReader();
        while (r.Read())
        {
            int row = NextRow(ws);
            object[] v = { r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetDouble(4), r.GetDouble(5), r.GetDouble(6), r.GetString(7), r.GetString(8) };
            for (int i = 0; i < v.Length; i++) ws.Cell(row, i + 1).Value = XLCellValue.FromObject(v[i]);
        }
    }

    private static void WriteCustomerAccountsSheet(XLWorkbook wb, Microsoft.Data.Sqlite.SqliteConnection cn, long sessionId)
    {
        var ws = GetOrCreateWorksheet(wb, "DEUDAS CLIENTES", new[] { "FECHA", "CLIENTE", "MOVIMIENTO", "IMPORTE", "MEDIO", "CONCEPTO", "RESPONSABLE" });
        using var q = cn.CreateCommand();
        q.CommandText = """
            SELECT ca.created_at,COALESCE(c.name,'Cliente'),ca.entry_type,ca.amount,
                   COALESCE(ca.payment_method,''),COALESCE(ca.concept,''),COALESCE(u.full_name,'')
            FROM customer_accounts ca
            JOIN customers c ON c.id=ca.customer_id
            LEFT JOIN users u ON u.id=ca.user_id
            WHERE ca.created_at >= (SELECT opened_at FROM cash_sessions WHERE id=$s)
              AND ca.created_at <= (SELECT COALESCE(closed_at,CURRENT_TIMESTAMP) FROM cash_sessions WHERE id=$s)
            ORDER BY ca.id
            """;
        q.Parameters.AddWithValue("$s", sessionId);
        using var r = q.ExecuteReader();
        while (r.Read())
        {
            int row = NextRow(ws);
            var movement = r.GetString(2).Equals("PAYMENT", StringComparison.OrdinalIgnoreCase)
                ? "ABONO COBRADO" : "DEUDA GENERADA";
            object[] v = { r.GetString(0), r.GetString(1), movement, r.GetDouble(3), r.GetString(4), r.GetString(5), r.GetString(6) };
            for (int i = 0; i < v.Length; i++) ws.Cell(row, i + 1).Value = XLCellValue.FromObject(v[i]);
        }
    }

    private static void WriteCategorySalesSheet(XLWorkbook wb, Microsoft.Data.Sqlite.SqliteConnection cn, long sessionId)
    {
        var ws = GetOrCreateWorksheet(wb, "POR CATEGORIA", new[] { "Categoría / Departamento", "Efectivo", "Mercado Pago", "Tarjeta", "Transferencia", "Crédito", "Total vendido" });
        ws.Clear();
        Prepare(ws, new[] { "Categoría / Departamento", "Efectivo", "Mercado Pago", "Tarjeta", "Transferencia", "Crédito", "Total vendido" });
        var row = 2;
        foreach (var x in SalesCategoryReportService.ForSession(cn, sessionId))
        {
            ws.Cell(row,1).Value=x.Category; ws.Cell(row,2).Value=x.Cash; ws.Cell(row,3).Value=x.MercadoPago; ws.Cell(row,4).Value=x.Card;
            ws.Cell(row,5).Value=x.Transfer; ws.Cell(row,6).Value=x.Credit; ws.Cell(row,7).Value=x.Total; row++;
        }
    }

    private static void WriteCustomerDeletionsSheet(XLWorkbook wb, Microsoft.Data.Sqlite.SqliteConnection cn, long sessionId)
    {
        var ws = GetOrCreateWorksheet(wb, "CLIENTES ELIMINADOS", new[] { "Fecha/Hora", "Cliente", "Documento", "Motivo", "Responsable" });
        ws.Clear();
        Prepare(ws, new[] { "Fecha/Hora", "Cliente", "Documento", "Motivo", "Responsable" });
        using var q=cn.CreateCommand();
        q.CommandText="SELECT d.created_at,d.customer_name,COALESCE(d.customer_document,''),d.reason,COALESCE(u.full_name,u.username,'') FROM customer_deletion_log d LEFT JOIN users u ON u.id=d.user_id WHERE d.created_at >= (SELECT opened_at FROM cash_sessions WHERE id=$s) AND d.created_at <= (SELECT COALESCE(closed_at,CURRENT_TIMESTAMP) FROM cash_sessions WHERE id=$s) ORDER BY d.id";
        q.Parameters.AddWithValue("$s",sessionId); using var r=q.ExecuteReader(); var row=2;
        while(r.Read()){ws.Cell(row,1).Value=r.GetString(0);ws.Cell(row,2).Value=r.GetString(1);ws.Cell(row,3).Value=r.GetString(2);ws.Cell(row,4).Value=r.GetString(3);ws.Cell(row,5).Value=r.GetString(4);row++;}
    }

    private static void WriteDailySummarySheet(XLWorkbook wb, Microsoft.Data.Sqlite.SqliteConnection cn, string closedAt)
    {
        var ws=GetOrCreateWorksheet(wb,"RESUMEN DÍA");
        ws.Clear();
        ws.Cell(1,1).Value="FERRARISPOS - RESUMEN DEL DÍA";ws.Cell(1,1).Style.Font.Bold=true;ws.Cell(2,1).Value="Fecha";ws.Cell(2,2).Value=closedAt;
        using var q=cn.CreateCommand();q.CommandText="""
            SELECT COUNT(*),COALESCE(SUM(total),0),COALESCE(SUM(discount),0)
            FROM sales WHERE date(created_at,'localtime')=date($d,'localtime') AND status='COMPLETED'
            """;q.Parameters.AddWithValue("$d",closedAt);using var r=q.ExecuteReader();r.Read();
        ws.Cell(4,1).Value="TICKETS";ws.Cell(4,2).Value=r.GetInt32(0);ws.Cell(5,1).Value="TOTAL VENDIDO";ws.Cell(5,2).Value=r.GetDouble(1);ws.Cell(6,1).Value="DESCUENTOS";ws.Cell(6,2).Value=r.GetDouble(2);
    }

    private static string BuildDetailedText(Microsoft.Data.Sqlite.SqliteConnection cn, long sessionId,
        (long id,string openedAt,string closedAt,double opening,double closing,double expected,double difference,bool mercadoPagoEnabled,double mercadoPagoOpening,double mercadoPagoClosing,double mercadoPagoExpected,double mercadoPagoDifference,string fullName,string username) h)
    {
        var b=new StringBuilder();
        void L(string s="")=>b.AppendLine(s);
        L("==============================================================");
        L("FERRARISPOS - REPORTE DETALLADO DE CIERRE DE CAJERO");
        L("==============================================================");
        L($"Generado: {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
        L($"ID DE CAJA: {h.id}"); L($"CAJERO: {h.fullName}"); L($"USUARIO: {h.username}");
        L($"APERTURA: {h.openedAt}"); L($"CIERRE: {h.closedAt}");
        L(); L("RESUMEN FINANCIERO"); L(new string('-',60));
        L($"FONDO INICIAL:        ${h.opening:N2}");
        L($"EFECTIVO ESPERADO:    ${h.expected:N2}");
        L($"EFECTIVO CONTADO:     ${h.closing:N2}");
        L($"DIFERENCIA:           ${h.difference:N2}");
        if (h.mercadoPagoEnabled)
        {
            L();
            L("CAJA PARALELA MERCADO PAGO");
            L($"SALDO INICIAL MP:     ${h.mercadoPagoOpening:N2}");
            L($"SALDO ESPERADO MP:    ${h.mercadoPagoExpected:N2}");
            L($"SALDO INFORMADO MP:   ${h.mercadoPagoClosing:N2}");
            L($"DIFERENCIA MP:        ${h.mercadoPagoDifference:N2}");
        }
        L($"TICKETS DEL TURNO:    {Scalar(cn,"SELECT COUNT(*) FROM sales WHERE session_id=$s AND status='COMPLETED'",sessionId):N0}");
        L($"TOTAL VENTAS TURNO:   ${Scalar(cn,"SELECT COALESCE(SUM(total),0) FROM sales WHERE session_id=$s AND status='COMPLETED'",sessionId):N2}");
        var paymentTotals = PaymentTotals(cn, sessionId);
        L($"COBROS EFECTIVO:       ${Payment(paymentTotals,"EFECTIVO"):N2}");
        L($"COBROS MERCADO PAGO:   ${Payment(paymentTotals,"MERCADO PAGO"):N2}");
        L($"COBROS MIXTOS:         ${Payment(paymentTotals,"MIXTO"):N2}");
        L($"COBROS TRANSFERENCIA:  ${Payment(paymentTotals,"TRANSFERENCIA"):N2}");
        L($"COBROS CRÉDITO:        ${Payment(paymentTotals,"CRÉDITO"):N2}");
        L($"COBROS DÓLARES:        ${Payment(paymentTotals,"DÓLARES"):N2}");
        L(); L("VENTAS POR CATEGORÍA / DEPARTAMENTO · DINERO VENDIDO"); L(new string('-',60));
        foreach(var x in SalesCategoryReportService.ForSession(cn, sessionId))
            L($"{x.Category}: EFECTIVO ${x.Cash:N2} + MERCADO PAGO ${x.MercadoPago:N2} + TARJETA ${x.Card:N2} + TRANSFERENCIA ${x.Transfer:N2} + CRÉDITO ${x.Credit:N2} = TOTAL VENDIDO ${x.Total:N2}");
        L($"TOTAL CATEGORÍAS: ${SalesCategoryReportService.ForSession(cn, sessionId).Sum(x=>x.Total):N2}");
        L($"DESCUENTOS OTORGADOS: ${Scalar(cn,"SELECT COALESCE(SUM(discount),0) FROM sales WHERE session_id=$s AND status='COMPLETED'",sessionId):N2}");
        L(); L("CLIENTES ELIMINADOS · MOTIVO"); L(new string('-',60));
        using(var dq=cn.CreateCommand())
        {
            dq.CommandText="SELECT d.created_at,d.customer_name,COALESCE(d.customer_document,''),d.reason,COALESCE(u.full_name,u.username,'') FROM customer_deletion_log d LEFT JOIN users u ON u.id=d.user_id WHERE d.created_at >= (SELECT opened_at FROM cash_sessions WHERE id=$s) AND d.created_at <= (SELECT COALESCE(closed_at,CURRENT_TIMESTAMP) FROM cash_sessions WHERE id=$s) ORDER BY d.id";
            dq.Parameters.AddWithValue("$s",sessionId); using var dr=dq.ExecuteReader(); var anyDeleted=false;
            while(dr.Read()){anyDeleted=true; L($"{dr.GetString(0)} | {dr.GetString(1)} | Documento: {dr.GetString(2)} | Motivo: {dr.GetString(3)} | Responsable: {dr.GetString(4)}");}
            if(!anyDeleted) L("Sin clientes eliminados en el turno.");
        }
        L(); L("⚠ DESCUENTOS OTORGADOS · MOTIVO Y DETALLE"); L(new string('-',60));
        using(var q=cn.CreateCommand())
        {
            q.CommandText="""
                SELECT s.ticket_no,s.created_at,COALESCE(u.full_name,''),s.discount,COALESCE(s.discount_reason,''),
                       COALESCE(GROUP_CONCAT(CASE WHEN si.discount>0.005 THEN si.description || ' ($' || printf('%.2f',si.discount) || ')' END, ', '),'')
                FROM sales s LEFT JOIN users u ON u.id=s.user_id LEFT JOIN sale_items si ON si.sale_id=s.id
                WHERE s.session_id=$s AND s.status='COMPLETED' AND s.discount>0.005
                GROUP BY s.id ORDER BY s.id
                """;
            q.Parameters.AddWithValue("$s",sessionId); using var r=q.ExecuteReader(); var any=false;
            while(r.Read()){any=true; L($"⚠ Ticket #{r.GetInt64(0)} | {r.GetString(1)} | Responsable {r.GetString(2)} | Descuento ${r.GetDouble(3):N2} | Motivo: {r.GetString(4)}"); if(!string.IsNullOrWhiteSpace(r.GetString(5))) L($"  Productos afectados: {r.GetString(5)}");}
            if(!any) L("Sin descuentos otorgados en el turno.");
        }
        L($"DEVOLUCIONES:         ${Scalar(cn,"SELECT COALESCE(SUM(amount),0) FROM sale_returns r JOIN sales s ON s.id=r.sale_id WHERE s.session_id=$s",sessionId):N2}");
        L(); L("VENTAS Y TICKETS"); L(new string('-',60));
        using(var q=cn.CreateCommand()){q.CommandText="SELECT s.ticket_no,s.created_at,COALESCE(u.full_name,''),COALESCE(c.name,'Público General'),s.subtotal,s.discount,s.total,s.payment_method,COALESCE(s.discount_reason,'') FROM sales s LEFT JOIN users u ON u.id=s.user_id LEFT JOIN customers c ON c.id=s.customer_id WHERE s.session_id=$s AND s.status='COMPLETED' ORDER BY s.id";q.Parameters.AddWithValue("$s",sessionId);using var r=q.ExecuteReader();while(r.Read())L($"Ticket #{r.GetInt64(0)} | {r.GetString(1)} | {r.GetString(2)} | Cliente: {r.GetString(3)} | Subtotal ${r.GetDouble(4):N2} | Desc ${r.GetDouble(5):N2} | Total ${r.GetDouble(6):N2} | {r.GetString(7)} | Motivo descuento: {r.GetString(8)}");}
        L(); L("DETALLE DE PRODUCTOS"); L(new string('-',60));
        using(var q=cn.CreateCommand()){q.CommandText="SELECT s.ticket_no,si.barcode,si.description,si.quantity,si.unit_price,si.discount,si.total,COALESCE(si.returned_quantity,0),COALESCE(si.discount_reason,'') FROM sale_items si JOIN sales s ON s.id=si.sale_id WHERE s.session_id=$s AND s.status='COMPLETED' ORDER BY s.id,si.id";q.Parameters.AddWithValue("$s",sessionId);using var r=q.ExecuteReader();while(r.Read())L($"Ticket #{r.GetInt64(0)} | {r.GetString(1)} | {r.GetString(2)} | Cantidad {r.GetDouble(3):N3} | P.Unit ${r.GetDouble(4):N2} | Desc ${r.GetDouble(5):N2} | Total ${r.GetDouble(6):N2} | Devuelto {r.GetDouble(7):N3} | Motivo descuento: {r.GetString(8)}");}
        L(); L("PAGOS"); L(new string('-',60));
        using(var q=cn.CreateCommand()){q.CommandText="SELECT s.ticket_no,p.created_at,p.method,p.amount,p.reference,p.status FROM payments p JOIN sales s ON s.id=p.sale_id WHERE s.session_id=$s ORDER BY p.id";q.Parameters.AddWithValue("$s",sessionId);using var r=q.ExecuteReader();while(r.Read())L($"Ticket #{r.GetInt64(0)} | {r.GetString(1)} | {r.GetString(2)} | ${r.GetDouble(3):N2} | Ref: {r.GetString(4)} | {r.GetString(5)}");}
        L(); L("MOVIMIENTOS DE CAJA"); L(new string('-',60));
        using(var q=cn.CreateCommand()){q.CommandText="SELECT cm.created_at,cm.movement_type,cm.payment_method,cm.concept,cm.amount,COALESCE(u.full_name,'') FROM cash_movements cm LEFT JOIN users u ON u.id=cm.user_id WHERE cm.session_id=$s AND COALESCE(cm.voided,0)=0 ORDER BY cm.id";q.Parameters.AddWithValue("$s",sessionId);using var r=q.ExecuteReader();while(r.Read())L($"{r.GetString(0)} | {r.GetString(1)} | {r.GetString(2)} | {r.GetString(3)} | ${r.GetDouble(4):N2} | {r.GetString(5)}");}
        L(); L("DEVOLUCIONES"); L(new string('-',60));
        using(var q=cn.CreateCommand()){q.CommandText="SELECT s.ticket_no,r.created_at,r.quantity,r.amount,r.reason,COALESCE(u.full_name,''),si.description FROM sale_returns r JOIN sales s ON s.id=r.sale_id JOIN sale_items si ON si.id=r.sale_item_id LEFT JOIN users u ON u.id=r.user_id WHERE s.session_id=$s ORDER BY r.id";q.Parameters.AddWithValue("$s",sessionId);using var r=q.ExecuteReader();while(r.Read())L($"Ticket #{r.GetInt64(0)} | {r.GetString(1)} | {r.GetString(6)} | Cantidad {r.GetDouble(2):N3} | ${r.GetDouble(3):N2} | {r.GetString(4)} | {r.GetString(5)}");}
        L(); L("STOCK / MOVIMIENTOS DE INVENTARIO DEL TURNO"); L(new string('-',60));
        using(var q=cn.CreateCommand()){q.CommandText="SELECT sm.created_at,p.description,p.barcode,sm.movement_type,sm.quantity,sm.reference,COALESCE(u.full_name,'') FROM stock_movements sm JOIN products p ON p.id=sm.product_id LEFT JOIN users u ON u.id=sm.user_id WHERE sm.created_at >= (SELECT opened_at FROM cash_sessions WHERE id=$s) AND sm.created_at <= (SELECT COALESCE(closed_at,CURRENT_TIMESTAMP) FROM cash_sessions WHERE id=$s) ORDER BY sm.id";q.Parameters.AddWithValue("$s",sessionId);using var r=q.ExecuteReader();while(r.Read())L($"{r.GetString(0)} | {r.GetString(1)} | {r.GetString(2)} | {r.GetString(3)} | Cantidad {r.GetDouble(4):N3} | {r.GetString(5)} | {r.GetString(6)}");}
        L(); L("MERMA / DESPERDICIO DEL TURNO"); L(new string('-',60));
        using(var q=cn.CreateCommand())
        {
            q.CommandText="SELECT w.created_at,p.description,p.barcode,w.quantity,w.reason,COALESCE(w.notes,''),COALESCE(u.full_name,'') FROM waste_records w JOIN products p ON p.id=w.product_id LEFT JOIN users u ON u.id=w.user_id WHERE w.created_at >= (SELECT opened_at FROM cash_sessions WHERE id=$s) AND w.created_at <= (SELECT COALESCE(closed_at,CURRENT_TIMESTAMP) FROM cash_sessions WHERE id=$s) ORDER BY w.id";
            q.Parameters.AddWithValue("$s",sessionId); using var r=q.ExecuteReader();
            var any=false; while(r.Read()){any=true; L($"{r.GetString(0)} | {r.GetString(1)} | {r.GetString(2)} | Cantidad {r.GetDouble(3):N3} | Motivo {r.GetString(4)} | {r.GetString(5)} | Responsable {r.GetString(6)}");}
            if(!any) L("Sin mermas registradas en el turno.");
        }

        L(); L("RECEPCIÓN DE MERCADERÍA Y DIFERENCIAS"); L(new string('-',60));
        using(var q=cn.CreateCommand())
        {
            q.CommandText="SELECT po.received_date,po.order_no,COALESCE(s.name,''),p.description,poi.quantity,poi.received_quantity,(poi.received_quantity-poi.quantity),COALESCE(poi.notes,''),COALESCE(po.notes,'') FROM purchase_order_items poi JOIN purchase_orders po ON po.id=poi.order_id JOIN products p ON p.id=poi.product_id LEFT JOIN suppliers s ON s.id=po.supplier_id WHERE po.received_date >= (SELECT opened_at FROM cash_sessions WHERE id=$s) AND po.received_date <= (SELECT COALESCE(closed_at,CURRENT_TIMESTAMP) FROM cash_sessions WHERE id=$s) ORDER BY po.id,poi.id";
            q.Parameters.AddWithValue("$s",sessionId); using var r=q.ExecuteReader();
            var any=false; while(r.Read()){any=true; L($"{r.GetString(0)} | OC {r.GetString(1)} | Proveedor {r.GetString(2)} | {r.GetString(3)} | Pedido {r.GetDouble(4):N3} | Recibido {r.GetDouble(5):N3} | DIFERENCIA {r.GetDouble(6):N3} | Nota línea {r.GetString(7)} | Nota recepción {r.GetString(8)}");}
            if(!any) L("Sin recepciones de mercadería registradas en el turno.");
        }

        L(); L("DEUDAS Y ABONOS DE CLIENTES"); L(new string('-',60));
        using(var q=cn.CreateCommand())
        {
            q.CommandText="SELECT ca.created_at,COALESCE(c.name,'Cliente'),ca.entry_type,ca.amount,COALESCE(ca.payment_method,''),COALESCE(ca.concept,''),COALESCE(u.full_name,'') FROM customer_accounts ca JOIN customers c ON c.id=ca.customer_id LEFT JOIN users u ON u.id=ca.user_id WHERE ca.created_at >= (SELECT opened_at FROM cash_sessions WHERE id=$s) AND ca.created_at <= (SELECT COALESCE(closed_at,CURRENT_TIMESTAMP) FROM cash_sessions WHERE id=$s) ORDER BY ca.id";
            q.Parameters.AddWithValue("$s",sessionId); using var r=q.ExecuteReader();
            var any=false; while(r.Read()){any=true; var mov=r.GetString(2).Equals("PAYMENT",StringComparison.OrdinalIgnoreCase)?"ABONO COBRADO":"DEUDA GENERADA"; L($"{r.GetString(0)} | {r.GetString(1)} | {mov} | ${r.GetDouble(3):N2} | Medio {r.GetString(4)} | {r.GetString(5)} | Responsable {r.GetString(6)}");}
            if(!any) L("Sin movimientos de cuenta corriente en el turno.");
        }

        L(); L("RESUMEN COMPLETO DEL DÍA"); L(new string('-',60));
        var day = h.closedAt.Length >= 10 ? h.closedAt.Substring(0,10) : DateTime.Now.ToString("yyyy-MM-dd");
        using(var q=cn.CreateCommand())
        {
            q.CommandText = "SELECT COUNT(*),COALESCE(SUM(total),0),COALESCE(SUM(discount),0) FROM sales WHERE date(created_at,'localtime')=date($d) AND status='COMPLETED'";
            q.Parameters.AddWithValue("$d", day);
            using var r=q.ExecuteReader(); if(r.Read()) L($"Tickets del día: {r.GetInt32(0)} | Total vendido: ${r.GetDouble(1):N2} | Descuentos: ${r.GetDouble(2):N2}");
        }
        using(var q=cn.CreateCommand())
        {
            q.CommandText = "SELECT method,COUNT(*),COALESCE(SUM(amount),0) FROM payments WHERE date(created_at,'localtime')=date($d) AND status='APPROVED' GROUP BY method ORDER BY method";
            q.Parameters.AddWithValue("$d", day);
            using var r=q.ExecuteReader();
            L("COBROS POR MEDIO:");
            while(r.Read()) L($"  {r.GetString(0)} | operaciones {r.GetInt32(1)} | ${r.GetDouble(2):N2}");
        }
        using(var q=cn.CreateCommand())
        {
            q.CommandText = "SELECT si.description,COALESCE(SUM(si.quantity),0),COALESCE(SUM(si.total),0) FROM sale_items si JOIN sales s ON s.id=si.sale_id WHERE date(s.created_at,'localtime')=date($d) AND s.status='COMPLETED' GROUP BY si.product_id,si.description ORDER BY SUM(si.total) DESC LIMIT 30";
            q.Parameters.AddWithValue("$d", day);
            using var r=q.ExecuteReader();
            L("PRODUCTOS MÁS VENDIDOS / MAYOR FACTURACIÓN (TOP 30):");
            while(r.Read()) L($"  {r.GetString(0)} | Cantidad {r.GetDouble(1):N3} | Facturación ${r.GetDouble(2):N2}");
        }
        using(var q=cn.CreateCommand())
        {
            q.CommandText = "SELECT COALESCE(u.full_name,''),COUNT(*),COALESCE(SUM(s.total),0) FROM sales s LEFT JOIN users u ON u.id=s.user_id WHERE date(s.created_at,'localtime')=date($d) AND s.status='COMPLETED' GROUP BY s.user_id ORDER BY SUM(s.total) DESC";
            q.Parameters.AddWithValue("$d", day);
            using var r=q.ExecuteReader();
            L("VENTAS POR CAJERO DEL DÍA:");
            while(r.Read()) L($"  {r.GetString(0)} | Tickets {r.GetInt32(1)} | ${r.GetDouble(2):N2}");
        }
        L();
        L("FIN DEL REPORTE"); L(new string('=',60));
        return b.ToString();
    }

    private static Dictionary<string,double> PaymentTotals(Microsoft.Data.Sqlite.SqliteConnection cn,long sessionId)
    {
        using var q=cn.CreateCommand();
        q.CommandText="""
            SELECT p.method,COALESCE(SUM(p.amount),0)
            FROM payments p JOIN sales s ON s.id=p.sale_id
            WHERE s.session_id=$s AND s.status='COMPLETED' AND p.status='APPROVED'
            GROUP BY p.method
            """;
        q.Parameters.AddWithValue("$s",sessionId);
        var result=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        using var r=q.ExecuteReader();
        while(r.Read()) result[r.GetString(0)]=r.GetDouble(1);
        return result;
    }

    private static double Payment(Dictionary<string,double> totals,string method)=>totals.TryGetValue(method,out var value)?value:0;

    private static double Scalar(Microsoft.Data.Sqlite.SqliteConnection cn,string sql,long sessionId)
    {
        using var q=cn.CreateCommand();q.CommandText=sql;q.Parameters.AddWithValue("$s",sessionId);return Convert.ToDouble(q.ExecuteScalar()??0);
    }
}
