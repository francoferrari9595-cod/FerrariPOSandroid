using ClosedXML.Excel;
using FerrarisPOS.Data;
using System.Globalization;

namespace FerrarisPOS.Services;

/// <summary>
/// Espejo de seguridad de clientes y cuentas corrientes.
/// SQLite sigue siendo la base operativa. Este Excel se actualiza automáticamente
/// después de cada alta/modificación/desactivación de cliente, crédito o abono.
/// El archivo se guarda oculto dentro de la carpeta de instalación (con fallback
/// a AppData si Windows no permite escribir en la instalación).
/// </summary>
public static class CustomerBackupService
{
    private const string FolderName = "RespaldoClientes";
    private const string FileName = "FerrariPOS_Clientes_Cuentas.xlsx";
    private static readonly object SyncLock = new();

    public static string InstallationBackupDirectory => Path.Combine(AppContext.BaseDirectory, FolderName);
    public static string InstallationBackupFile => Path.Combine(InstallationBackupDirectory, FileName);
    public static string FallbackBackupDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FerrarisPOS", FolderName);
    public static string FallbackBackupFile => Path.Combine(FallbackBackupDirectory, FileName);

    public static string CurrentBackupFile => File.Exists(InstallationBackupFile) ? InstallationBackupFile : FallbackBackupFile;

    public static string Sync()
    {
        lock (SyncLock)
        {
            var customers = ReadCustomers();
            var accounts = ReadAccounts();
            var targetDirectory = InstallationBackupDirectory;
            var target = InstallationBackupFile;

            try
            {
                Directory.CreateDirectory(targetDirectory);
                WriteWorkbookAtomically(target, customers, accounts);
                HidePath(targetDirectory);
                return target;
            }
            catch
            {
                // Program Files puede estar protegido en algunas instalaciones.
                // Nunca hacemos fallar una venta/cliente por el espejo Excel.
                targetDirectory = FallbackBackupDirectory;
                target = FallbackBackupFile;
                Directory.CreateDirectory(targetDirectory);
                WriteWorkbookAtomically(target, customers, accounts);
                HidePath(targetDirectory);
                return target;
            }
        }
    }

    public static bool Exists() => File.Exists(InstallationBackupFile) || File.Exists(FallbackBackupFile);

    public static string Status()
    {
        var path = CurrentBackupFile;
        if (!File.Exists(path)) return "Todavía no existe el respaldo Excel de clientes y cuentas corrientes.";
        var info = new FileInfo(path);
        return $"Respaldo automático activo.\nArchivo: {path}\nÚltima actualización: {info.LastWriteTime:dd/MM/yyyy HH:mm:ss}\nTamaño: {info.Length / 1024.0:N1} KB";
    }

    private sealed record CustomerRow(int Id, string Name, string Document, string Phone, string Email, string Address, double CreditLimit, bool Active, string CreatedAt);
    private sealed record AccountRow(long Id, int CustomerId, long SaleId, string EntryType, double Amount, string Concept, string PaymentMethod, int UserId, string CreatedAt);

    private static List<CustomerRow> ReadCustomers()
    {
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT id,name,document,phone,email,address,credit_limit,active,created_at FROM customers ORDER BY id";
        using var r = cmd.ExecuteReader();
        var list = new List<CustomerRow>();
        while (r.Read())
        {
            list.Add(new CustomerRow(
                r.GetInt32(0), r.IsDBNull(1) ? "" : r.GetString(1), r.IsDBNull(2) ? "" : r.GetString(2),
                r.IsDBNull(3) ? "" : r.GetString(3), r.IsDBNull(4) ? "" : r.GetString(4),
                r.IsDBNull(5) ? "" : r.GetString(5), r.IsDBNull(6) ? 0 : r.GetDouble(6),
                !r.IsDBNull(7) && r.GetInt32(7) != 0, r.IsDBNull(8) ? "" : r.GetString(8)));
        }
        return list;
    }

    private static List<AccountRow> ReadAccounts()
    {
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT id,customer_id,COALESCE(sale_id,0),entry_type,amount,concept,payment_method,COALESCE(user_id,0),created_at FROM customer_accounts ORDER BY id";
        using var r = cmd.ExecuteReader();
        var list = new List<AccountRow>();
        while (r.Read())
        {
            list.Add(new AccountRow(
                r.GetInt64(0), r.GetInt32(1), r.IsDBNull(2) ? 0 : r.GetInt64(2),
                r.IsDBNull(3) ? "" : r.GetString(3), r.IsDBNull(4) ? 0 : r.GetDouble(4),
                r.IsDBNull(5) ? "" : r.GetString(5), r.IsDBNull(6) ? "" : r.GetString(6),
                r.IsDBNull(7) ? 0 : r.GetInt32(7), r.IsDBNull(8) ? "" : r.GetString(8)));
        }
        return list;
    }

    private static void WriteWorkbookAtomically(string target, List<CustomerRow> customers, List<AccountRow> accounts)
    {
        var directory = Path.GetDirectoryName(target)!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{FileName}.{Environment.ProcessId}.tmp.xlsx");
        var previous = target + ".previous";

        try
        {
            using (var wb = new XLWorkbook())
            {
                var control = wb.Worksheets.Add("CONTROL");
                control.Cell(1, 1).Value = "Ferrari'sPOS · Respaldo automático de clientes y cuentas corrientes";
                control.Cell(2, 1).Value = "Generado";
                control.Cell(2, 2).Value = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
                control.Cell(3, 1).Value = "Clientes";
                control.Cell(3, 2).Value = customers.Count;
                control.Cell(4, 1).Value = "Movimientos de cuenta corriente";
                control.Cell(4, 2).Value = accounts.Count;
                control.Cell(5, 1).Value = "Origen";
                control.Cell(5, 2).Value = Database.DbPath;
                control.Row(1).Style.Font.Bold = true;
                control.Column(1).Width = 55;
                control.Column(2).Width = 65;

                var ws = wb.Worksheets.Add("CLIENTES");
                string[] headers = { "ID", "NOMBRE / RAZÓN SOCIAL", "DOCUMENTO / CUIT", "TELÉFONO", "EMAIL", "DIRECCIÓN", "LÍMITE DE CRÉDITO", "ACTIVO", "FECHA DE ALTA" };
                for (var i = 0; i < headers.Length; i++) ws.Cell(1, i + 1).Value = headers[i];
                for (var i = 0; i < customers.Count; i++)
                {
                    var c = customers[i]; var row = i + 2;
                    ws.Cell(row, 1).Value = c.Id;
                    ws.Cell(row, 2).Value = c.Name;
                    ws.Cell(row, 3).Value = c.Document;
                    ws.Cell(row, 4).Value = c.Phone;
                    ws.Cell(row, 5).Value = c.Email;
                    ws.Cell(row, 6).Value = c.Address;
                    ws.Cell(row, 7).Value = c.CreditLimit;
                    ws.Cell(row, 8).Value = c.Active ? "SÍ" : "NO";
                    ws.Cell(row, 9).Value = c.CreatedAt;
                }
                FormatSheet(ws, headers.Length);
                ws.Column(7).Style.NumberFormat.Format = "$ #,##0.00";

                var ca = wb.Worksheets.Add("CUENTAS_CORRIENTES");
                string[] accountHeaders = { "ID MOVIMIENTO", "ID CLIENTE", "ID VENTA", "TIPO", "IMPORTE", "CONCEPTO", "MEDIO DE PAGO", "ID USUARIO", "FECHA / HORA" };
                for (var i = 0; i < accountHeaders.Length; i++) ca.Cell(1, i + 1).Value = accountHeaders[i];
                for (var i = 0; i < accounts.Count; i++)
                {
                    var a = accounts[i]; var row = i + 2;
                    ca.Cell(row, 1).Value = a.Id;
                    ca.Cell(row, 2).Value = a.CustomerId;
                    ca.Cell(row, 3).Value = a.SaleId;
                    ca.Cell(row, 4).Value = a.EntryType;
                    ca.Cell(row, 5).Value = a.Amount;
                    ca.Cell(row, 6).Value = a.Concept;
                    ca.Cell(row, 7).Value = a.PaymentMethod;
                    ca.Cell(row, 8).Value = a.UserId;
                    ca.Cell(row, 9).Value = a.CreatedAt;
                }
                FormatSheet(ca, accountHeaders.Length);
                ca.Column(5).Style.NumberFormat.Format = "$ #,##0.00";

                // Hoja de resumen por cliente: deuda = créditos - abonos.
                var summary = wb.Worksheets.Add("RESUMEN_DEUDAS");
                string[] sumHeaders = { "ID CLIENTE", "CLIENTE", "LÍMITE", "CRÉDITOS", "ABONOS", "DEUDA ACTUAL", "CRÉDITO DISPONIBLE" };
                for (var i = 0; i < sumHeaders.Length; i++) summary.Cell(1, i + 1).Value = sumHeaders[i];
                for (var i = 0; i < customers.Count; i++)
                {
                    var c = customers[i]; var row = i + 2;
                    var credits = accounts.Where(a => a.CustomerId == c.Id && a.EntryType.Equals("SALE", StringComparison.OrdinalIgnoreCase)).Sum(a => a.Amount);
                    var payments = accounts.Where(a => a.CustomerId == c.Id && a.EntryType.Equals("PAYMENT", StringComparison.OrdinalIgnoreCase)).Sum(a => a.Amount);
                    var debt = Math.Round(credits - payments, 2);
                    var available = c.CreditLimit <= 0 ? double.PositiveInfinity : Math.Max(0, c.CreditLimit - debt);
                    summary.Cell(row, 1).Value = c.Id;
                    summary.Cell(row, 2).Value = c.Name;
                    summary.Cell(row, 3).Value = c.CreditLimit;
                    summary.Cell(row, 4).Value = credits;
                    summary.Cell(row, 5).Value = payments;
                    summary.Cell(row, 6).Value = debt;
                    summary.Cell(row, 7).Value = double.IsPositiveInfinity(available) ? "INFINITO" : available;
                }
                FormatSheet(summary, sumHeaders.Length);
                summary.Columns(3, 6).Style.NumberFormat.Format = "$ #,##0.00";

                wb.SaveAs(temp);
            }

            if (File.Exists(previous)) File.Delete(previous);
            if (File.Exists(target)) File.Move(target, previous);
            File.Move(temp, target);
            TryDelete(previous);
            HidePath(target);
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static void FormatSheet(IXLWorksheet ws, int columnCount)
    {
        ws.Row(1).Style.Font.Bold = true;
        ws.Row(1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.SheetView.FreezeRows(1);
        ws.Range(1, 1, Math.Max(1, ws.LastRowUsed()?.RowNumber() ?? 1), columnCount).SetAutoFilter();
        ws.Columns().AdjustToContents();
        foreach (var col in ws.ColumnsUsed()) if (col.Width > 55) col.Width = 55;
    }

    private static void HidePath(string path)
    {
        try
        {
            if (File.Exists(path)) File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
                File.SetAttributes(directory, File.GetAttributes(directory) | FileAttributes.Hidden);
        }
        catch { }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
