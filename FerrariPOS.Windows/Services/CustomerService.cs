using FerrarisPOS.Data;
using FerrarisPOS.Models;

namespace FerrarisPOS.Services;

public static class CustomerService
{
    public static List<Customer> All(bool includeInactive = false)
    {
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = includeInactive
            ? "SELECT id,name,document,phone,email,address,credit_limit,active FROM customers ORDER BY CASE WHEN id=1 THEN 0 ELSE 1 END, id"
            : "SELECT id,name,document,phone,email,address,credit_limit,active FROM customers WHERE active=1 ORDER BY CASE WHEN id=1 THEN 0 ELSE 1 END, id";
        using var r = cmd.ExecuteReader();
        var list = new List<Customer>();
        while (r.Read()) list.Add(new Customer(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetDouble(6), r.GetInt32(7) != 0));
        return list;
    }

    public static List<CustomerAccountSummary> AccountSummaries(bool includeInactive = false)
    {
        var customers = All(includeInactive);
        var result = new List<CustomerAccountSummary>(customers.Count);

        using var cn = Database.Open();
        foreach (var customer in customers)
        {
            using var balanceCmd = cn.CreateCommand();
            balanceCmd.CommandText = """
                SELECT COALESCE(SUM(
                    CASE
                        WHEN entry_type='SALE' THEN amount
                        WHEN entry_type='PAYMENT' THEN -amount
                        ELSE 0
                    END),0)
                FROM customer_accounts
                WHERE customer_id=$c
                """;
            balanceCmd.Parameters.AddWithValue("$c", customer.Id);
            var balance = Convert.ToDouble(balanceCmd.ExecuteScalar() ?? 0);

            using var lastCmd = cn.CreateCommand();
            lastCmd.CommandText = """
                SELECT COALESCE(amount,0)
                FROM customer_accounts
                WHERE customer_id=$c AND entry_type='SALE'
                ORDER BY id DESC LIMIT 1
                """;
            lastCmd.Parameters.AddWithValue("$c", customer.Id);
            var lastCredit = Convert.ToDouble(lastCmd.ExecuteScalar() ?? 0);

            var available = customer.CreditLimit <= 0
                ? double.PositiveInfinity
                : Math.Max(0, customer.CreditLimit - balance);

            result.Add(new CustomerAccountSummary(customer, balance, lastCredit, available));
        }

        return result;
    }

    public static double Balance(int customerId)
    {
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(SUM(CASE WHEN entry_type='SALE' THEN amount WHEN entry_type='PAYMENT' THEN -amount ELSE 0 END),0) FROM customer_accounts WHERE customer_id=$c";
        cmd.Parameters.AddWithValue("$c", customerId);
        return Convert.ToDouble(cmd.ExecuteScalar());
    }

    public static void Save(Customer c)
    {
        using var cn = Database.Open();

        // ID 1 es reservado para Público General. Se normaliza su identidad
        // sin tocar créditos, ventas ni movimientos de su cuenta corriente.
        if (c.Id == 1)
        {
            using var normalize = cn.CreateCommand();
            normalize.CommandText = "UPDATE customers SET name='Público General',document='',phone='',email='',address='',credit_limit=0,active=1 WHERE id=1";
            normalize.ExecuteNonQuery();
            return;
        }

        using var cmd = cn.CreateCommand();
        cmd.CommandText = c.Id == 0
            ? "INSERT INTO customers(name,document,phone,email,address,credit_limit,active) VALUES($n,$d,$p,$e,$a,$l,1)"
            : "UPDATE customers SET name=$n,document=$d,phone=$p,email=$e,address=$a,credit_limit=$l WHERE id=$id AND id>1";
        cmd.Parameters.AddWithValue("$n", c.Name.Trim());
        cmd.Parameters.AddWithValue("$d", c.Document.Trim());
        cmd.Parameters.AddWithValue("$p", c.Phone.Trim());
        cmd.Parameters.AddWithValue("$e", c.Email.Trim());
        cmd.Parameters.AddWithValue("$a", c.Address.Trim());
        cmd.Parameters.AddWithValue("$l", c.CreditLimit);
        if (c.Id != 0) cmd.Parameters.AddWithValue("$id", c.Id);
        cmd.ExecuteNonQuery();
        try { CustomerBackupService.Sync(); } catch { }
    }

    public static double LastCredit(int customerId)
    {
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(amount,0) FROM customer_accounts WHERE customer_id=$c AND entry_type='SALE' ORDER BY id DESC LIMIT 1";
        cmd.Parameters.AddWithValue("$c", customerId);
        return Convert.ToDouble(cmd.ExecuteScalar() ?? 0);
    }

    public static double AvailableCredit(int customerId)
    {
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT credit_limit FROM customers WHERE id=$c";
        cmd.Parameters.AddWithValue("$c", customerId);
        var limit = Convert.ToDouble(cmd.ExecuteScalar() ?? 0);
        if (limit <= 0) return double.PositiveInfinity;
        return Math.Max(0, limit - Balance(customerId));
    }

    public static void RegisterPayment(int customerId, double amount, string concept, int userId)
        => RegisterPayment(customerId, new List<PaymentLine> { new("EFECTIVO", amount, "") }, userId, concept);

    public static void RegisterPayment(int customerId, IReadOnlyList<PaymentLine> payments, int userId, string concept = "Pago de cuenta corriente")
    {
        if (customerId <= 1) throw new InvalidOperationException("Seleccioná un cliente válido.");
        payments = payments.Select(p => new PaymentLine(NormalizePaymentMethod(p.Method), p.Amount, p.Reference)).ToList();
        var total = Math.Round(payments.Sum(p => p.Amount), 2);
        if (total <= 0) throw new InvalidOperationException("El pago debe ser mayor a cero.");
        if (payments.Any(p => p.Amount <= 0)) throw new InvalidOperationException("Los importes de pago deben ser mayores a cero.");

        var balance = Balance(customerId);
        if (total > balance + 0.01) throw new InvalidOperationException($"El pago no puede superar la deuda pendiente de ${balance:N2}.");

        var cash = payments.Where(p => p.Method.Equals("EFECTIVO", StringComparison.OrdinalIgnoreCase)).Sum(p => p.Amount);
        var mercadoPago = payments.Where(p => p.Method.Equals("MERCADO PAGO", StringComparison.OrdinalIgnoreCase)).Sum(p => p.Amount);
        var tarjeta = payments.Where(p => p.Method.Equals("TARJETA", StringComparison.OrdinalIgnoreCase)).Sum(p => p.Amount);
        var transferencia = payments.Where(p => p.Method.Equals("TRANSFERENCIA", StringComparison.OrdinalIgnoreCase)).Sum(p => p.Amount);
        // En cuentas corrientes, todo cobro electrónico (MP, tarjeta o transferencia)
        // se refleja también en la caja paralela de Mercado Pago para que el saldo
        // esperado de esa caja coincida con el dinero digital que debe ingresar.
        var digitalToMercadoPago = mercadoPago + tarjeta + transferencia;
        if (cash > 0 && !CashService.IsOpen())
            throw new InvalidOperationException("La caja está cerrada. Abrí la caja antes de registrar un pago en efectivo.");
        if (digitalToMercadoPago > 0 && !CashService.IsMercadoPagoEnabled())
            throw new InvalidOperationException("Para registrar un abono electrónico (Mercado Pago, tarjeta o transferencia), primero activá la CAJA PARALELA DE MERCADO PAGO desde la apertura de caja.");

        using var cn = Database.Open();
        using var tx = cn.BeginTransaction();
        try
        {
            var method = payments.Count == 1 ? payments[0].Method : "MIXTO";
            using var cmd = cn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO customer_accounts(customer_id,entry_type,amount,concept,payment_method,user_id) VALUES($c,'PAYMENT',$a,$co,$pm,$u)";
            cmd.Parameters.AddWithValue("$c", customerId);
            cmd.Parameters.AddWithValue("$a", total);
            cmd.Parameters.AddWithValue("$co", concept);
            cmd.Parameters.AddWithValue("$pm", method);
            cmd.Parameters.AddWithValue("$u", userId);
            cmd.ExecuteNonQuery();
            long accountEntryId;
            using (var last = cn.CreateCommand())
            {
                last.Transaction = tx;
                last.CommandText = "SELECT last_insert_rowid()";
                accountEntryId = Convert.ToInt64(last.ExecuteScalar());
            }

            if (cash > 0 || digitalToMercadoPago > 0)
            {
                if (cash > 0)
                {
                    using var cashCmd = cn.CreateCommand();
                    cashCmd.Transaction = tx;
                    cashCmd.CommandText = "INSERT INTO cash_movements(session_id,user_id,movement_type,concept,amount,payment_method,reference_id) VALUES((SELECT id FROM cash_sessions WHERE status='OPEN' ORDER BY id DESC LIMIT 1),$u,'INCOME',$c,$a,'EFECTIVO',$ref)";
                    cashCmd.Parameters.AddWithValue("$u", userId);
                    cashCmd.Parameters.AddWithValue("$c", $"Pago cuenta cliente #{customerId} ({method})");
                    cashCmd.Parameters.AddWithValue("$a", cash);
                    cashCmd.Parameters.AddWithValue("$ref", accountEntryId);
                    cashCmd.ExecuteNonQuery();
                }
                if (digitalToMercadoPago > 0)
                {
                    using var mpCmd = cn.CreateCommand();
                    mpCmd.Transaction = tx;
                    mpCmd.CommandText = "INSERT INTO cash_movements(session_id,user_id,movement_type,concept,amount,payment_method,reference_id) VALUES((SELECT id FROM cash_sessions WHERE status='OPEN' ORDER BY id DESC LIMIT 1),$u,'INCOME',$c,$a,'MERCADO PAGO',$ref)";
                    mpCmd.Parameters.AddWithValue("$u", userId);
                    mpCmd.Parameters.AddWithValue("$c", $"Abono cliente #{customerId} · ingreso digital a Mercado Pago ({method})");
                    mpCmd.Parameters.AddWithValue("$a", digitalToMercadoPago);
                    mpCmd.Parameters.AddWithValue("$ref", accountEntryId);
                    mpCmd.ExecuteNonQuery();
                }
            }
            tx.Commit();
            try { CustomerBackupService.Sync(); } catch { }
        }
        catch { tx.Rollback(); throw; }
    }


    /// <summary>
    /// Reinicia únicamente el historial de cuenta corriente del cliente.
    /// No elimina al cliente ni sus ventas/tickets; solo borra los movimientos
    /// del libro de deuda (créditos y abonos), dejando el saldo en cero para
    /// comenzar un nuevo ciclo de cuenta corriente.
    /// </summary>
    public static void ClearDebtHistory(int customerId)
    {
        if (customerId <= 1) throw new InvalidOperationException("No se puede limpiar la cuenta de Público General.");
        var balance = Balance(customerId);
        if (Math.Abs(balance) > 0.005)
            throw new InvalidOperationException($"La cuenta todavía tiene una deuda de ${balance:N2}. Primero dejá el saldo en cero.");

        using var cn = Database.Open();
        using var tx = cn.BeginTransaction();
        try
        {
            using var cmd = cn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM customer_accounts WHERE customer_id=$c";
            cmd.Parameters.AddWithValue("$c", customerId);
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        try { CustomerBackupService.Sync(); } catch { }
    }

    public static List<CustomerDebtDetail> DebtDetails(int customerId)
    {
        if (customerId <= 1) return new List<CustomerDebtDetail>();

        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            SELECT ca.id, datetime(ca.created_at,'localtime'), ca.entry_type, ca.amount,
                   ca.concept, ca.payment_method, COALESCE(ca.sale_id,0), COALESCE(s.ticket_no,0)
            FROM customer_accounts ca
            LEFT JOIN sales s ON s.id = ca.sale_id
            WHERE ca.customer_id=$c
            ORDER BY ca.id DESC
            """;
        cmd.Parameters.AddWithValue("$c", customerId);

        var raw = new List<(long Id, string Date, string Type, double Amount, string Concept,
            string Method, long SaleId, long Ticket)>();
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read())
            {
                raw.Add((
                    r.GetInt64(0),
                    r.IsDBNull(1) ? "" : r.GetString(1),
                    r.IsDBNull(2) ? "" : r.GetString(2),
                    r.IsDBNull(3) ? 0 : r.GetDouble(3),
                    r.IsDBNull(4) ? "" : r.GetString(4),
                    r.IsDBNull(5) ? "" : r.GetString(5),
                    r.GetInt64(6),
                    r.GetInt64(7)));
            }
        }

        var result = new List<CustomerDebtDetail>(raw.Count);
        foreach (var entry in raw)
        {
            var products = string.Empty;
            if (entry.SaleId > 0)
            {
                using var items = cn.CreateCommand();
                items.CommandText = """
                    SELECT description, quantity, unit_price, total
                    FROM sale_items WHERE sale_id=$s ORDER BY id
                    """;
                items.Parameters.AddWithValue("$s", entry.SaleId);
                using var ir = items.ExecuteReader();
                var parts = new List<string>();
                while (ir.Read())
                {
                    var description = ir.IsDBNull(0) ? "Producto" : ir.GetString(0);
                    var quantity = ir.IsDBNull(1) ? 0 : ir.GetDouble(1);
                    var unitPrice = ir.IsDBNull(2) ? 0 : ir.GetDouble(2);
                    var total = ir.IsDBNull(3) ? 0 : ir.GetDouble(3);
                    parts.Add($"{description} x{quantity:N2} (${unitPrice:N2}) = ${total:N2}");
                }
                products = string.Join(Environment.NewLine, parts);
            }

            result.Add(new CustomerDebtDetail(
                entry.Id, entry.Date, entry.Type, entry.Amount, entry.Concept,
                entry.Method, entry.SaleId, entry.Ticket, products));
        }

        return result;
    }

    public static void Delete(int id)
        => Delete(id, "Desactivación solicitada desde la gestión de clientes.", Session.UserId);

    public static void Delete(int id, string reason, int userId)
    {
        if (id <= 1) throw new InvalidOperationException("Público General no se puede eliminar.");
        reason = (reason ?? "").Trim();
        if (reason.Length == 0) throw new InvalidOperationException("El motivo de eliminación es obligatorio.");

        using var cn = Database.Open();
        using var tx = cn.BeginTransaction();
        try
        {
            string customerName = "", document = "";
            using (var find = cn.CreateCommand())
            {
                find.Transaction = tx;
                find.CommandText = "SELECT name,COALESCE(document,'') FROM customers WHERE id=$id AND active=1";
                find.Parameters.AddWithValue("$id", id);
                using var r = find.ExecuteReader();
                if (!r.Read()) throw new InvalidOperationException("El cliente seleccionado ya no existe o ya fue eliminado.");
                customerName = r.GetString(0);
                document = r.GetString(1);
            }

            using (var log = cn.CreateCommand())
            {
                log.Transaction = tx;
                log.CommandText = "INSERT INTO customer_deletion_log(customer_id,customer_name,customer_document,reason,user_id) VALUES($id,$name,$doc,$reason,$user)";
                log.Parameters.AddWithValue("$id", id);
                log.Parameters.AddWithValue("$name", customerName);
                log.Parameters.AddWithValue("$doc", document);
                log.Parameters.AddWithValue("$reason", reason);
                log.Parameters.AddWithValue("$user", userId);
                log.ExecuteNonQuery();
            }

            using (var cmd = cn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = "UPDATE customers SET active=0 WHERE id=$id AND active=1";
                cmd.Parameters.AddWithValue("$id", id);
                if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("No se pudo eliminar el cliente seleccionado.");
            }
            tx.Commit();

            AuditService.Log(userId, "CUSTOMER_DELETED", "CLIENTES",
                $"Cliente #{id} · {customerName} · Documento: {document} · Motivo: {reason}");
            try { CustomerBackupService.Sync(); } catch { }
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }
    private static string NormalizePaymentMethod(string? method)
    {
        var value=(method??string.Empty).Trim();
        var decomposed=value.Normalize(System.Text.NormalizationForm.FormD);
        var sb=new System.Text.StringBuilder(decomposed.Length);
        foreach(var ch in decomposed)
            if(System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch)!=System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(ch);
        var key=sb.ToString().Replace("?",string.Empty,StringComparison.Ordinal).Replace("�",string.Empty,StringComparison.Ordinal).Replace(" ",string.Empty,StringComparison.Ordinal).ToUpperInvariant();
        if(key.StartsWith("CREDITO",StringComparison.Ordinal)||key.StartsWith("CRDITO",StringComparison.Ordinal)) return "CRÉDITO";
        if(key=="EFECTIVO") return "EFECTIVO";
        if(key=="TRANSFERENCIA") return "TRANSFERENCIA";
        if(key=="MERCADOPAGO") return "MERCADO PAGO";
        if(key=="TARJETA") return "TARJETA";
        if(key=="DOLARES") return "DÓLARES";
        return value.ToUpperInvariant();
    }
}


public sealed class CustomerDebtDetail
{
    public long Id { get; }
    public string DateTimeText { get; }
    public string EntryType { get; }
    public double Amount { get; }
    public string Concept { get; }
    public string PaymentMethod { get; }
    public long SaleId { get; }
    public long TicketNumber { get; }
    public string Products { get; }

    public bool IsSale => string.Equals(EntryType, "SALE", StringComparison.OrdinalIgnoreCase);
    public string TypeText => IsSale ? "DEUDA / CRÉDITO" : "PAGO";
    public string AmountText => IsSale ? $"+ $ {Amount:N2}" : $"- $ {Amount:N2}";
    public string ReferenceText => TicketNumber > 0 ? $"Ticket #{TicketNumber}" : "Movimiento de cuenta";

    public CustomerDebtDetail(long id, string dateTimeText, string entryType, double amount,
        string concept, string paymentMethod, long saleId, long ticketNumber, string products)
    {
        Id = id; DateTimeText = dateTimeText; EntryType = entryType; Amount = amount;
        Concept = concept; PaymentMethod = paymentMethod; SaleId = saleId;
        TicketNumber = ticketNumber; Products = products;
    }
}
