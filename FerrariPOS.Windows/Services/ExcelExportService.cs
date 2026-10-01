using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using FerrarisPOS.Data;
using System.Windows.Forms;
using ClosedXML.Excel;

namespace FerrarisPOS.Services
{
    public static class ExcelExportService
    {

        public static string? ExportInventory()
        {
            using var dialog = new SaveFileDialog
            {
                Filter = "Excel (*.xlsx)|*.xlsx",
                DefaultExt = "xlsx",
                AddExtension = true,
                FileName = $"FerrariPOS_Inventario_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                Title = "Guardar inventario como Excel"
            };

            if (dialog.ShowDialog() != DialogResult.OK)
                return null;

            var products = ProductService.Search("");
            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("INVENTARIO");

            var headers = new[]
            {
                "CÓDIGO DE BARRAS", "PRODUCTO", "CATEGORÍA", "UNIDAD",
                "PRECIO DE COSTO", "PRECIO DE VENTA", "PRECIO MAYOREO",
                "STOCK", "STOCK MÍNIMO", "ACTIVO", "GRANEL", "USA INVENTARIO", "ID INTERNO"
            };

            for (var i = 0; i < headers.Length; i++)
                sheet.Cell(1, i + 1).Value = headers[i];

            sheet.Row(1).Style.Font.Bold = true;
            sheet.Row(1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            sheet.SheetView.FreezeRows(1);

            var row = 2;
            foreach (var p in products)
            {
                sheet.Cell(row, 1).Value = p.Barcode;
                sheet.Cell(row, 2).Value = p.Description;
                sheet.Cell(row, 3).Value = p.Category;
                sheet.Cell(row, 4).Value = p.Unit;
                sheet.Cell(row, 5).Value = p.CostPrice;
                sheet.Cell(row, 6).Value = p.SalePrice;
                sheet.Cell(row, 7).Value = p.WholesalePrice;
                sheet.Cell(row, 8).Value = p.Stock;
                sheet.Cell(row, 9).Value = p.MinStock;
                sheet.Cell(row, 10).Value = p.Active ? "SÍ" : "NO";
                sheet.Cell(row, 11).Value = p.IsBulk ? "SÍ" : "NO";
                sheet.Cell(row, 12).Value = p.UsesInventory ? "SÍ" : "NO";
                sheet.Cell(row, 13).Value = p.Id;
                row++;
            }

            if (row > 2)
            {
                var table = sheet.Range(1, 1, row - 1, headers.Length).CreateTable("InventarioExportado");
                table.Theme = XLTableTheme.TableStyleMedium2;
            }

            for (var col = 5; col <= 9; col++)
                sheet.Column(col).Style.NumberFormat.Format = "#,##0.00";
            sheet.Column(13).Hide();

            sheet.Columns().AdjustToContents();
            foreach (var col in sheet.ColumnsUsed())
                if (col.Width > 45) col.Width = 45;

            workbook.SaveAs(dialog.FileName);
            return dialog.FileName;
        }
        public static InventoryImportResult? ImportInventory()
        {
            if (!InventoryControlService.IsGlobalEnabled)
                throw new InvalidOperationException("El inventario global está deshabilitado. La importación de inventario queda bloqueada para proteger los contadores congelados.");

            using var dialog = new OpenFileDialog
            {
                Filter = "Excel (*.xlsx)|*.xlsx",
                DefaultExt = "xlsx",
                Multiselect = false,
                Title = "Importar lista de inventario"
            };

            if (dialog.ShowDialog() != DialogResult.OK)
                return null;

            using var workbook = new XLWorkbook(dialog.FileName);
            var sheet = workbook.Worksheets.FirstOrDefault(x =>
                string.Equals(x.Name.Trim(), "INVENTARIO", StringComparison.OrdinalIgnoreCase))
                ?? workbook.Worksheets.FirstOrDefault();

            if (sheet == null)
                throw new InvalidOperationException("El archivo Excel no contiene ninguna hoja.");

            var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var cell in sheet.Row(1).CellsUsed())
            {
                var header = cell.GetString().Trim();
                if (!string.IsNullOrWhiteSpace(header))
                    headerMap[header] = cell.Address.ColumnNumber;
            }

            string RequiredHeader(params string[] names)
            {
                foreach (var name in names)
                    if (headerMap.TryGetValue(name, out _)) return name;
                throw new InvalidOperationException($"Falta la columna obligatoria '{names[0]}'. Usá el mismo formato generado por EXPORTAR INVENTARIO.");
            }

            var barcodeHeader = RequiredHeader("CÓDIGO DE BARRAS", "CODIGO DE BARRAS");
            var descriptionHeader = RequiredHeader("PRODUCTO");
            var categoryHeader = RequiredHeader("CATEGORÍA", "CATEGORIA");
            var unitHeader = RequiredHeader("UNIDAD");
            var costHeader = RequiredHeader("PRECIO DE COSTO");
            var priceHeader = RequiredHeader("PRECIO DE VENTA");
            var wholesaleHeader = RequiredHeader("PRECIO MAYOREO");
            var stockHeader = RequiredHeader("STOCK");
            var minStockHeader = RequiredHeader("STOCK MÍNIMO", "STOCK MINIMO");
            var activeHeader = RequiredHeader("ACTIVO");
            var bulkHeader = RequiredHeader("GRANEL");
            var inventoryHeader = RequiredHeader("USA INVENTARIO");
            var idHeader = headerMap.ContainsKey("ID INTERNO") ? "ID INTERNO" : null;

            int Col(string header) => headerMap[header];

            string Text(IXLRow row, string header) => row.Cell(Col(header)).GetString().Trim();

            double Number(IXLRow row, string header)
            {
                var cell = row.Cell(Col(header));
                if (cell.TryGetValue<double>(out var numeric)) return numeric;
                var text = cell.GetString().Trim();
                if (string.IsNullOrWhiteSpace(text)) return 0;

                if (double.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out var value)) return value;
                if (double.TryParse(text.Replace(".", "").Replace(",", "."), NumberStyles.Any, CultureInfo.InvariantCulture, out value)) return value;
                if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value)) return value;
                throw new InvalidOperationException($"El valor '{text}' de la columna '{header}' no es numérico.");
            }

            long OptionalId(IXLRow row)
            {
                if (idHeader is null) return 0;
                var cell = row.Cell(Col(idHeader));
                if (cell.TryGetValue<long>(out var numeric)) return numeric;
                return long.TryParse(cell.GetString().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
            }

            bool YesNo(IXLRow row, string header)
            {
                var text = Text(row, header).Trim().ToUpperInvariant();
                return text is "SÍ" or "SI" or "1" or "TRUE" or "VERDADERO" or "YES";
            }

            var inserted = 0;
            var updated = 0;
            var skipped = 0;

            using var cn = Database.Open();
            using var tx = cn.BeginTransaction();

            try
            {
                foreach (var row in sheet.RowsUsed().Skip(1))
                {
                    var barcode = Text(row, barcodeHeader);
                    var description = Text(row, descriptionHeader);
                    if (string.IsNullOrWhiteSpace(barcode) && string.IsNullOrWhiteSpace(description))
                        continue;

                    if (string.IsNullOrWhiteSpace(barcode) || string.IsNullOrWhiteSpace(description) || barcode == "__COMUN__")
                    {
                        skipped++;
                        continue;
                    }

                    var category = Text(row, categoryHeader);
                    var unit = Text(row, unitHeader);
                    if (string.IsNullOrWhiteSpace(unit)) unit = "UN";
                    var cost = Number(row, costHeader);
                    var price = Number(row, priceHeader);
                    var wholesale = Number(row, wholesaleHeader);
                    var stock = Number(row, stockHeader);
                    var minStock = Number(row, minStockHeader);
                    var active = YesNo(row, activeHeader);
                    var bulk = YesNo(row, bulkHeader);
                    var usesInventory = YesNo(row, inventoryHeader);

                    long existingId = OptionalId(row);
                    using (var find = cn.CreateCommand())
                    {
                        find.Transaction = tx;
                        if (existingId > 0)
                        {
                            find.CommandText = "SELECT id FROM products WHERE id=$id LIMIT 1";
                            find.Parameters.AddWithValue("$id", existingId);
                        }
                        else
                        {
                            find.CommandText = "SELECT id FROM products WHERE barcode=$barcode LIMIT 1";
                            find.Parameters.AddWithValue("$barcode", barcode);
                        }
                        var result = find.ExecuteScalar();
                        existingId = result == null || result == DBNull.Value ? 0 : Convert.ToInt64(result, CultureInfo.InvariantCulture);
                    }

                    if (existingId == 0)
                    {
                        using var insert = cn.CreateCommand();
                        insert.Transaction = tx;
                        insert.CommandText = """
                            INSERT INTO products
                                (barcode,description,sale_price,wholesale_price,cost_price,stock,min_stock,category,unit,active,is_bulk,uses_inventory)
                            VALUES
                                ($b,$d,$s,$w,$c,$st,$m,$cat,$u,$a,$bulk,$inv)
                            """;
                        insert.Parameters.AddWithValue("$b", barcode);
                        insert.Parameters.AddWithValue("$d", description);
                        insert.Parameters.AddWithValue("$s", price);
                        insert.Parameters.AddWithValue("$w", wholesale);
                        insert.Parameters.AddWithValue("$c", cost);
                        insert.Parameters.AddWithValue("$st", stock);
                        insert.Parameters.AddWithValue("$m", minStock);
                        insert.Parameters.AddWithValue("$cat", category);
                        insert.Parameters.AddWithValue("$u", unit);
                        insert.Parameters.AddWithValue("$a", active ? 1 : 0);
                        insert.Parameters.AddWithValue("$bulk", bulk ? 1 : 0);
                        insert.Parameters.AddWithValue("$inv", usesInventory ? 1 : 0);
                        insert.ExecuteNonQuery();
                        inserted++;
                    }
                    else
                    {
                        using var update = cn.CreateCommand();
                        update.Transaction = tx;
                        update.CommandText = """
                            UPDATE products SET
                                barcode=$b, description=$d, sale_price=$s, wholesale_price=$w, cost_price=$c,
                                stock=$st, min_stock=$m, category=$cat, unit=$u, active=$a,
                                is_bulk=$bulk, uses_inventory=$inv, updated_at=CURRENT_TIMESTAMP
                            WHERE id=$id
                            """;
                        update.Parameters.AddWithValue("$b", barcode);
                        update.Parameters.AddWithValue("$d", description);
                        update.Parameters.AddWithValue("$s", price);
                        update.Parameters.AddWithValue("$w", wholesale);
                        update.Parameters.AddWithValue("$c", cost);
                        update.Parameters.AddWithValue("$st", stock);
                        update.Parameters.AddWithValue("$m", minStock);
                        update.Parameters.AddWithValue("$cat", category);
                        update.Parameters.AddWithValue("$u", unit);
                        update.Parameters.AddWithValue("$a", active ? 1 : 0);
                        update.Parameters.AddWithValue("$bulk", bulk ? 1 : 0);
                        update.Parameters.AddWithValue("$inv", usesInventory ? 1 : 0);
                        update.Parameters.AddWithValue("$id", existingId);
                        update.ExecuteNonQuery();
                        updated++;
                    }
                }

                tx.Commit();
                return new InventoryImportResult(inserted, updated, skipped, dialog.FileName);
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        public static string? ExportCustomerDebtDetails()
        {
            using var dialog = new SaveFileDialog
            {
                Filter = "Excel (*.xlsx)|*.xlsx",
                DefaultExt = "xlsx",
                AddExtension = true,
                FileName = $"FerrariPOS_Estado_Deuda_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                Title = "Guardar estado de deuda de clientes como Excel"
            };

            if (dialog.ShowDialog() != DialogResult.OK)
                return null;

            using var cn = FerrarisPOS.Data.Database.Open();
            using var workbook = new XLWorkbook();

            var summary = workbook.Worksheets.Add("RESUMEN DEUDAS");
            var summaryHeaders = new[]
            {
                "CLIENTE", "DOCUMENTO / CUIT", "TELÉFONO", "LÍMITE DE CRÉDITO",
                "TOTAL CRÉDITOS", "TOTAL ABONOS", "DEUDA ACTUAL", "CRÉDITO DISPONIBLE"
            };
            for (var i = 0; i < summaryHeaders.Length; i++) summary.Cell(1, i + 1).Value = summaryHeaders[i];
            summary.Row(1).Style.Font.Bold = true;
            summary.Row(1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            summary.SheetView.FreezeRows(1);

            var detail = workbook.Worksheets.Add("DETALLE DEUDAS");
            var detailHeaders = new[]
            {
                "CLIENTE", "DOCUMENTO / CUIT", "FECHA", "TIPO", "TICKET", "CONCEPTO",
                "MEDIO DE PAGO", "DÉBITO", "ABONO", "SALDO"
            };
            for (var i = 0; i < detailHeaders.Length; i++) detail.Cell(1, i + 1).Value = detailHeaders[i];
            detail.Row(1).Style.Font.Bold = true;
            detail.Row(1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            detail.SheetView.FreezeRows(1);

            var summaryRow = 2;
            var detailRow = 2;

            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT c.id, c.name, c.document, c.phone, c.credit_limit,
                           COALESCE(SUM(CASE WHEN ca.entry_type='SALE' THEN ca.amount ELSE 0 END),0) AS credits,
                           COALESCE(SUM(CASE WHEN ca.entry_type='PAYMENT' THEN ca.amount ELSE 0 END),0) AS payments
                    FROM customers c
                    LEFT JOIN customer_accounts ca ON ca.customer_id=c.id
                    WHERE c.active=1
                    GROUP BY c.id, c.name, c.document, c.phone, c.credit_limit
                    ORDER BY c.name
                    """;
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var creditLimit = r.GetDouble(4);
                    var credits = r.GetDouble(5);
                    var payments = r.GetDouble(6);
                    var balance = Math.Max(0, credits - payments);
                    var available = creditLimit <= 0 ? double.PositiveInfinity : Math.Max(0, creditLimit - balance);

                    summary.Cell(summaryRow, 1).Value = r.GetString(1);
                    summary.Cell(summaryRow, 2).Value = r.GetString(2);
                    summary.Cell(summaryRow, 3).Value = r.GetString(3);
                    summary.Cell(summaryRow, 4).Value = creditLimit;
                    summary.Cell(summaryRow, 5).Value = credits;
                    summary.Cell(summaryRow, 6).Value = payments;
                    summary.Cell(summaryRow, 7).Value = balance;
                    summary.Cell(summaryRow, 8).Value = double.IsPositiveInfinity(available) ? "INFINITO" : available;
                    summaryRow++;
                }
            }

            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT c.name, c.document, ca.created_at, ca.entry_type,
                           COALESCE(ca.sale_id,0), ca.concept, ca.payment_method, ca.amount, ca.id, ca.customer_id
                    FROM customer_accounts ca
                    INNER JOIN customers c ON c.id=ca.customer_id
                    WHERE c.active=1
                    ORDER BY c.name, ca.created_at, ca.id
                    """;
                using var r = cmd.ExecuteReader();
                string? currentCustomer = null;
                double runningBalance = 0;
                while (r.Read())
                {
                    var customerName = r.GetString(0);
                    if (!string.Equals(currentCustomer, customerName, StringComparison.Ordinal))
                    {
                        currentCustomer = customerName;
                        runningBalance = 0;
                    }

                    var type = r.GetString(3);
                    var amount = r.GetDouble(7);
                    var debit = string.Equals(type, "SALE", StringComparison.OrdinalIgnoreCase) ? amount : 0;
                    var payment = string.Equals(type, "PAYMENT", StringComparison.OrdinalIgnoreCase) ? amount : 0;
                    runningBalance += debit - payment;

                    detail.Cell(detailRow, 1).Value = customerName;
                    detail.Cell(detailRow, 2).Value = r.GetString(1);
                    detail.Cell(detailRow, 3).Value = r.GetString(2);
                    detail.Cell(detailRow, 4).Value = string.Equals(type, "SALE", StringComparison.OrdinalIgnoreCase) ? "CRÉDITO" : "ABONO";
                    detail.Cell(detailRow, 5).Value = r.GetInt64(4) == 0 ? "" : r.GetInt64(4).ToString();
                    detail.Cell(detailRow, 6).Value = r.GetString(5);
                    detail.Cell(detailRow, 7).Value = r.GetString(6);
                    detail.Cell(detailRow, 8).Value = debit;
                    detail.Cell(detailRow, 9).Value = payment;
                    detail.Cell(detailRow, 10).Value = runningBalance;
                    detailRow++;
                }
            }

            if (summaryRow > 2)
            {
                var table = summary.Range(1, 1, summaryRow - 1, summaryHeaders.Length).CreateTable("ResumenDeudasExportado");
                table.Theme = XLTableTheme.TableStyleMedium2;
            }
            if (detailRow > 2)
            {
                var table = detail.Range(1, 1, detailRow - 1, detailHeaders.Length).CreateTable("DetalleDeudasExportado");
                table.Theme = XLTableTheme.TableStyleMedium2;
            }

            for (var col = 4; col <= 7; col++) summary.Column(col).Style.NumberFormat.Format = "#,##0.00";
            for (var col = 8; col <= 10; col++) detail.Column(col).Style.NumberFormat.Format = "#,##0.00";
            summary.Columns().AdjustToContents();
            detail.Columns().AdjustToContents();
            foreach (var ws in new[] { summary, detail })
                foreach (var col in ws.ColumnsUsed())
                    if (col.Width > 45) col.Width = 45;

            workbook.SaveAs(dialog.FileName);
            return dialog.FileName;
        }

        public static string? ExportDataTable(DataTable table, string baseName)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));

            using var dialog = new SaveFileDialog
            {
                Filter = "Excel (*.xlsx)|*.xlsx",
                FileName = $"{baseName}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
                Title = "Exportar a Excel"
            };

            if (dialog.ShowDialog() != DialogResult.OK)
                return null;

            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add(string.IsNullOrWhiteSpace(baseName) ? "Datos" : baseName);
            sheet.Cell(1, 1).InsertTable(table);
            sheet.Columns().AdjustToContents();
            workbook.SaveAs(dialog.FileName);
            return dialog.FileName;
        }
    public sealed record InventoryImportResult(int Inserted, int Updated, int Skipped, string FilePath);
    }
}
