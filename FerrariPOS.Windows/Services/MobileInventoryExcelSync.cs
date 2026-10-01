using ClosedXML.Excel;
using FerrarisPOS.Data;
using System.Globalization;

namespace FerrarisPOS.Services;

/// <summary>Genera automáticamente una copia Excel del inventario después de operaciones móviles.</summary>
public static class MobileInventoryExcelSync
{
    public static string Export()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Ferrari'sPOS");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "FerrariPOS_Inventario_Actual.xlsx");
        var temp = path + ".tmp";

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("INVENTARIO");
        var headers = new[] { "CÓDIGO DE BARRAS", "PRODUCTO", "CATEGORÍA", "UNIDAD", "PRECIO DE COSTO", "PRECIO DE VENTA", "PRECIO MAYOREO", "STOCK", "STOCK MÍNIMO", "ACTIVO", "GRANEL", "USA INVENTARIO", "ID INTERNO" };
        for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
        ws.Row(1).Style.Font.Bold = true;
        ws.SheetView.FreezeRows(1);

        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = @"SELECT id,barcode,description,category,unit,cost_price,sale_price,wholesale_price,stock,min_stock,active,is_bulk,COALESCE(uses_inventory,1) FROM products WHERE barcode <> '__COMUN__' ORDER BY description COLLATE NOCASE";
        using var r = cmd.ExecuteReader();
        var row = 2;
        while (r.Read())
        {
            ws.Cell(row, 1).Value = r.GetString(1);
            ws.Cell(row, 2).Value = r.GetString(2);
            ws.Cell(row, 3).Value = r.GetString(3);
            ws.Cell(row, 4).Value = r.GetString(4);
            ws.Cell(row, 5).Value = r.GetDouble(5);
            ws.Cell(row, 6).Value = r.GetDouble(6);
            ws.Cell(row, 7).Value = r.GetDouble(7);
            ws.Cell(row, 8).Value = r.GetDouble(8);
            ws.Cell(row, 9).Value = r.GetDouble(9);
            ws.Cell(row, 10).Value = r.GetInt32(10) != 0 ? "SÍ" : "NO";
            ws.Cell(row, 11).Value = r.GetInt32(11) != 0 ? "SÍ" : "NO";
            ws.Cell(row, 12).Value = r.GetInt32(12) != 0 ? "SÍ" : "NO";
            ws.Cell(row, 13).Value = r.GetInt32(0);
            row++;
        }
        if (row > 2)
        {
            var table = ws.Range(1, 1, row - 1, headers.Length).CreateTable("InventarioActual");
            table.Theme = XLTableTheme.TableStyleMedium2;
        }
        for (var col = 5; col <= 9; col++) ws.Column(col).Style.NumberFormat.Format = "#,##0.00";
        ws.Column(13).Hide();
        ws.Columns().AdjustToContents();
        foreach (var col in ws.ColumnsUsed()) if (col.Width > 45) col.Width = 45;
        wb.SaveAs(temp);
        File.Move(temp, path, true);
        Database.SetSetting("mobile_inventory_excel_last_sync", DateTime.Now.ToString("O", CultureInfo.InvariantCulture));
        return path;
    }
}
