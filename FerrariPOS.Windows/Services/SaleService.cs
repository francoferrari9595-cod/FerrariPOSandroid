using FerrarisPOS.Data;
using FerrarisPOS.Models;

namespace FerrarisPOS.Services;

public static class SaleService
{
    // Serializa las operaciones de venta para que dos toques/solicitudes simultáneas
    // no puedan superar el límite de crédito con el mismo saldo leído.
    private static readonly object CompletionGate = new();

    public static long Complete(int userId, int customerId, IReadOnlyList<CartItem> items, IReadOnlyList<PaymentLine> payments, double received, double change, int? tableId = null, string saleChannel = "SALÓN", string notes = "", string deliveryAddress = "", string deliveryStatus = "N/A", string discountReason = "", bool allowInactiveProducts = false)
        => lockAndComplete(userId, customerId, items, payments, received, change, tableId, saleChannel, notes, deliveryAddress, deliveryStatus, discountReason, allowInactiveProducts);

    private static long lockAndComplete(int userId, int customerId, IReadOnlyList<CartItem> items, IReadOnlyList<PaymentLine> payments, double received, double change, int? tableId, string saleChannel, string notes, string deliveryAddress, string deliveryStatus, string discountReason, bool allowInactiveProducts)
    {
        lock (CompletionGate)
        {
            return CompleteCore(userId, customerId, items, payments, received, change, tableId, saleChannel, notes, deliveryAddress, deliveryStatus, discountReason, allowInactiveProducts);
        }
    }

    public static double TodayTotal()
    {
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            SELECT COALESCE(SUM(total),0)
            FROM sales
            WHERE status='COMPLETED'
              AND date(created_at)=date('now','localtime')
            """;
        return Convert.ToDouble(cmd.ExecuteScalar() ?? 0);
    }

    public static int TodayTicketCount()
    {
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*)
            FROM sales
            WHERE status='COMPLETED'
              AND date(created_at)=date('now','localtime')
            """;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    public static long TicketNumber(long saleId)
    {
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT ticket_no FROM sales WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", saleId);
        return Convert.ToInt64(cmd.ExecuteScalar() ?? saleId);
    }

    private static bool ProductUsesInventory(Microsoft.Data.Sqlite.SqliteConnection cn, Microsoft.Data.Sqlite.SqliteTransaction tx, int productId)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COALESCE(uses_inventory,1) FROM products WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", productId);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 1) != 0;
    }

    private static long CompleteCore(int userId, int customerId, IReadOnlyList<CartItem> items, IReadOnlyList<PaymentLine> payments, double received, double change, int? tableId = null, string saleChannel = "SALÓN", string notes = "", string deliveryAddress = "", string deliveryStatus = "N/A", string discountReason = "", bool allowInactiveProducts = false)
    {
        if (items.Count == 0) throw new InvalidOperationException("La venta no tiene productos.");
        // Las ventas que llegan desde Android/HTTP pueden traer el texto del medio de pago
        // dañado por una conversión de caracteres (por ejemplo "cre??dito").
        // Normalizamos ANTES de calcular crédito, validar límites y persistir payments.
        // Así nunca se pierde una cuenta corriente por una etiqueta corrupta.
        payments = payments.Select(p => new PaymentLine(NormalizePaymentMethod(p.Method), p.Amount, p.Reference)).ToList();

        var subtotal = Math.Round(items.Sum(x => x.GrossTotal), 2);
        var discount = Math.Round(items.Sum(x => x.DiscountAmount), 2);
        var total = Math.Round(items.Sum(x => x.Total), 2);
        var paid = Math.Round(payments.Sum(x => x.Amount), 2);
        if (paid + 0.01 < total) throw new InvalidOperationException("El importe recibido es insuficiente.");
        if (payments.Any(p => p.Amount < 0)) throw new InvalidOperationException("No se permiten importes negativos.");

        var cashAmount = payments.Where(p => p.Method.Equals("EFECTIVO", StringComparison.OrdinalIgnoreCase)).Sum(p => p.Amount);
        var mercadoPagoAmount = payments.Where(p => p.Method.Equals("MERCADO PAGO", StringComparison.OrdinalIgnoreCase)).Sum(p => p.Amount);
        if (cashAmount > 0 && !CashService.IsOpen()) throw new InvalidOperationException("La caja está cerrada. Abrí la caja antes de registrar una venta en efectivo.");
        if (mercadoPagoAmount > 0 && !CashService.IsMercadoPagoEnabled())
            throw new InvalidOperationException("Mercado Pago no está habilitado en esta sesión. Para cobrar con Mercado Pago, activalo en la apertura de caja e ingresá el saldo inicial.");
        var creditAmountRequested = payments
            .Where(p => p.Method.StartsWith("CRÉDIT", StringComparison.OrdinalIgnoreCase) || p.Method.StartsWith("CREDITO", StringComparison.OrdinalIgnoreCase))
            .Sum(p => p.Amount);

        // Solo una venta que realmente contiene una parte a crédito puede quedar
        // vinculada a un cliente. Esto evita que el cliente seleccionado para una
        // venta anterior se "pegue" a las ventas normales.
        var persistedCustomerId = creditAmountRequested > 0 && customerId > 1 ? customerId : 1;

        if (creditAmountRequested > 0)
        {
            if (customerId <= 1)
                throw new InvalidOperationException("Una venta a crédito necesita un cliente guardado distinto de Público General.");

            using var creditCn = Database.Open();
            using var creditCmd = creditCn.CreateCommand();
            creditCmd.CommandText = "SELECT credit_limit FROM customers WHERE id=$c AND active=1";
            creditCmd.Parameters.AddWithValue("$c", customerId);
            var limitValue = Convert.ToDouble(creditCmd.ExecuteScalar() ?? 0);

            using var balanceCmd = creditCn.CreateCommand();
            balanceCmd.CommandText = "SELECT COALESCE(SUM(CASE WHEN entry_type='SALE' THEN amount WHEN entry_type='PAYMENT' THEN -amount ELSE 0 END),0) FROM customer_accounts WHERE customer_id=$c";
            balanceCmd.Parameters.AddWithValue("$c", customerId);
            var currentBalance = Convert.ToDouble(balanceCmd.ExecuteScalar() ?? 0);

            if (limitValue > 0 && currentBalance + creditAmountRequested > limitValue + 0.01)
                throw new InvalidOperationException($"El crédito disponible del cliente es ${Math.Max(0, limitValue - currentBalance):N2}. La parte a crédito es ${creditAmountRequested:N2}.");
        }

        using var cn = Database.Open();
        using var tx = cn.BeginTransaction();
        try
        {
            long ticket;
            using (var next = cn.CreateCommand())
            {
                next.Transaction = tx;
                next.CommandText = "SELECT COALESCE(MAX(ticket_no),0)+1 FROM sales";
                ticket = Convert.ToInt64(next.ExecuteScalar());
            }

            long saleId;
            using (var cmd = cn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO sales(ticket_no,user_id,customer_id,session_id,subtotal,discount,total,payment_method,amount_received,change_amount,status,table_id,sale_channel,notes,delivery_address,delivery_status,discount_reason) VALUES($t,$u,$c,$sess,$s,$disc,$tot,$pm,$ar,$ch,'COMPLETED',$table,$channel,$notes,$address,$deliveryStatus,$discountReason); SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("$t", ticket); cmd.Parameters.AddWithValue("$u", userId); cmd.Parameters.AddWithValue("$disc", discount); cmd.Parameters.AddWithValue("$c", persistedCustomerId); cmd.Parameters.AddWithValue("$sess", CashService.CurrentSessionId() is long sid ? sid : DBNull.Value); cmd.Parameters.AddWithValue("$s", subtotal); cmd.Parameters.AddWithValue("$tot", total); cmd.Parameters.AddWithValue("$pm", payments.Count == 1 ? payments[0].Method : "MIXTO"); cmd.Parameters.AddWithValue("$ar", received); cmd.Parameters.AddWithValue("$ch", change); cmd.Parameters.AddWithValue("$table", tableId.HasValue ? (object)tableId.Value : DBNull.Value); cmd.Parameters.AddWithValue("$channel", string.IsNullOrWhiteSpace(saleChannel) ? "SALÓN" : saleChannel); cmd.Parameters.AddWithValue("$notes", notes ?? ""); cmd.Parameters.AddWithValue("$address", deliveryAddress ?? ""); cmd.Parameters.AddWithValue("$deliveryStatus", string.IsNullOrWhiteSpace(deliveryStatus) ? "N/A" : deliveryStatus); cmd.Parameters.AddWithValue("$discountReason", discountReason ?? "");
                saleId = Convert.ToInt64(cmd.ExecuteScalar());
            }

            foreach (var item in items)
            {
                string barcode; string desc; string category;
                if (item.IsCommon)
                {
                    barcode = "COMÚN";
                    desc = item.Description;
                    category = "SIN CATEGORÍA";
                }
                else
                {
                    using var check = cn.CreateCommand(); check.Transaction = tx; check.CommandText = "SELECT stock,description,barcode,COALESCE(uses_inventory,1),COALESCE(category,'') FROM products WHERE id=$id AND (active=1 OR $allowInactive=1)"; check.Parameters.AddWithValue("$id", item.ProductId); check.Parameters.AddWithValue("$allowInactive", allowInactiveProducts ? 1 : 0); using var r = check.ExecuteReader();
                    if (!r.Read()) throw new InvalidOperationException($"El producto {item.Description} ya no está disponible.");
                    var stock = r.GetDouble(0); desc = r.GetString(1); barcode = r.GetString(2); category = r.GetString(4).Trim();
                    if (string.IsNullOrWhiteSpace(category)) category = "SIN CATEGORÍA";
                    var usesInventory = InventoryControlService.IsGlobalEnabled && r.GetInt32(3) != 0;
                    if (usesInventory && item.Quantity > stock + 0.0001) throw new InvalidOperationException($"Stock insuficiente para {desc}. Disponible: {stock:N2}.");
                    r.Close();
                    PriceGuardService.EnsureSaleAllowed(cn, tx, item.ProductId, desc);
                }

                using var q = cn.CreateCommand(); q.Transaction = tx;
                q.CommandText = item.IsCommon || !ProductUsesInventory(cn, tx, item.ProductId)
                    ? "INSERT INTO sale_items(sale_id,product_id,barcode,description,quantity,unit_price,discount,total,category,discount_reason) VALUES($sid,$pid,$b,$d,$q,$p,$disc,$tot,$cat,$discountReason);"
                    : "INSERT INTO sale_items(sale_id,product_id,barcode,description,quantity,unit_price,discount,total,category,discount_reason) VALUES($sid,$pid,$b,$d,$q,$p,$disc,$tot,$cat,$discountReason); UPDATE products SET stock=stock-$q,updated_at=CURRENT_TIMESTAMP WHERE id=$pid; INSERT INTO stock_movements(product_id,movement_type,quantity,reference,user_id) VALUES($pid,'SALE',$q,$ref,$u);";
                q.Parameters.AddWithValue("$sid", saleId); q.Parameters.AddWithValue("$pid", item.ProductId); q.Parameters.AddWithValue("$disc", item.DiscountAmount); q.Parameters.AddWithValue("$discountReason", item.DiscountAmount > 0.005 ? (discountReason ?? "") : ""); q.Parameters.AddWithValue("$b", barcode); q.Parameters.AddWithValue("$d", desc); q.Parameters.AddWithValue("$q", item.Quantity); q.Parameters.AddWithValue("$p", item.UnitPrice); q.Parameters.AddWithValue("$tot", item.Total); q.Parameters.AddWithValue("$cat", category); q.Parameters.AddWithValue("$ref", $"VENTA #{ticket}"); q.Parameters.AddWithValue("$u", userId); q.ExecuteNonQuery();
            }

            foreach (var payment in payments)
            {
                using var q = cn.CreateCommand(); q.Transaction = tx; q.CommandText = "INSERT INTO payments(sale_id,method,amount,reference,status) VALUES($sid,$m,$a,$r,'APPROVED')"; q.Parameters.AddWithValue("$sid", saleId); q.Parameters.AddWithValue("$m", payment.Method); q.Parameters.AddWithValue("$a", payment.Amount); q.Parameters.AddWithValue("$r", payment.Reference); q.ExecuteNonQuery();
            }

            if (cashAmount > 0)
            {
                using var q = cn.CreateCommand(); q.Transaction = tx; q.CommandText = "INSERT INTO cash_movements(session_id,user_id,movement_type,concept,amount,payment_method,reference_id) VALUES((SELECT id FROM cash_sessions WHERE status='OPEN' ORDER BY id DESC LIMIT 1),$u,'SALE',$c,$a,'EFECTIVO',$sid)"; q.Parameters.AddWithValue("$u", userId); q.Parameters.AddWithValue("$c", $"Venta #{ticket}"); q.Parameters.AddWithValue("$a", cashAmount); q.Parameters.AddWithValue("$sid", saleId); q.ExecuteNonQuery();
            }

            if (creditAmountRequested > 0)
            {
                using var q = cn.CreateCommand();
                q.Transaction = tx;
                q.CommandText = "INSERT INTO customer_accounts(customer_id,sale_id,entry_type,amount,concept,payment_method,user_id) VALUES($c,$s,'SALE',$a,$co,'CRÉDITO',$u)";
                q.Parameters.AddWithValue("$c", customerId);
                q.Parameters.AddWithValue("$s", saleId);
                q.Parameters.AddWithValue("$a", creditAmountRequested);
                q.Parameters.AddWithValue("$co", $"Venta a crédito #{ticket}");
                q.Parameters.AddWithValue("$u", userId);
                q.ExecuteNonQuery();
            }

            using (var audit = cn.CreateCommand())
            {
                audit.Transaction = tx; audit.CommandText = "INSERT INTO audit_log(user_id,action,module,details) VALUES($u,'SALE','VENTAS',$d)"; audit.Parameters.AddWithValue("$u", userId); audit.Parameters.AddWithValue("$d", $"Ticket {ticket} - Subtotal {subtotal:N2} - Descuento {discount:N2} - Total {total:N2}"); audit.ExecuteNonQuery();
            }
            tx.Commit();
            if (creditAmountRequested > 0) { try { CustomerBackupService.Sync(); } catch { } }
            return saleId;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public static string NormalizePaymentMethod(string? method)
    {
        var value = (method ?? string.Empty).Trim();
        if (value.Length == 0) return "EFECTIVO";

        // FormD elimina acentos legítimos; quitar ?, � y espacios cubre los casos
        // conocidos de "CR??DITO", "CRE??DITO" y reemplazos Unicode dañados.
        var decomposed = value.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) !=
                System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(ch);
        }

        var key = sb.ToString()
            .Replace("?", string.Empty, StringComparison.Ordinal)
            .Replace("�", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();

        if (key.StartsWith("CREDITO", StringComparison.Ordinal) ||
            key.StartsWith("CRDITO", StringComparison.Ordinal))
            return "CRÉDITO";
        if (key == "EFECTIVO") return "EFECTIVO";
        if (key == "TRANSFERENCIA") return "TRANSFERENCIA";
        if (key == "DOLARES") return "DÓLARES";
        if (key == "TARJETA") return "TARJETA";
        if (key == "MERCADOPAGO") return "MERCADO PAGO";
        if (key == "MIXTO") return "MIXTO";

        return value;
    }
}
