using FerrarisPOS.Data;

namespace FerrarisPOS.Services;

public static class CashService
{
    public static bool IsOpen()
    {
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM cash_sessions WHERE status='OPEN'";
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    public static long? CurrentSessionId()
    {
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT id FROM cash_sessions WHERE status='OPEN' ORDER BY id DESC LIMIT 1";
        var v = cmd.ExecuteScalar();
        return v is null ? null : Convert.ToInt64(v);
    }

    public static bool IsMercadoPagoEnabled()
    {
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(mercado_pago_enabled,0) FROM cash_sessions WHERE status='OPEN' ORDER BY id DESC LIMIT 1";
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) != 0;
    }

    public static double MercadoPagoRetentionPercent()
    {
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(mercado_pago_retention_percent,0) FROM cash_sessions WHERE status='OPEN' ORDER BY id DESC LIMIT 1";
        return Convert.ToDouble(cmd.ExecuteScalar() ?? 0);
    }

    public static double ConfiguredMercadoPagoRetentionPercent()
    {
        var raw = Database.GetSetting("mercado_pago_retention_percent", "0");
        return double.TryParse(raw.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, 0, 100) : 0;
    }

    public static void SaveMercadoPagoRetentionPercent(double percent)
    {
        if (double.IsNaN(percent) || percent < 0 || percent > 100) throw new InvalidOperationException("La retención de Mercado Pago debe estar entre 0% y 100%.");
        Database.SetSetting("mercado_pago_retention_percent", percent.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));
        var session = CurrentSessionId();
        if (session is not null)
        {
            using var cn = Database.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "UPDATE cash_sessions SET mercado_pago_retention_percent=$p WHERE id=$s";
            cmd.Parameters.AddWithValue("$p", percent); cmd.Parameters.AddWithValue("$s", session.Value);
            cmd.ExecuteNonQuery();
        }
    }

    public static double MercadoPagoOpeningAmount()
    {
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(mercado_pago_opening_amount,0) FROM cash_sessions WHERE status='OPEN' ORDER BY id DESC LIMIT 1";
        return Convert.ToDouble(cmd.ExecuteScalar() ?? 0);
    }

    public static void Open(double amount, int userId, bool mercadoPagoEnabled = false, double mercadoPagoOpening = 0, double? mercadoPagoRetentionPercent = null)
    {
        if (amount < 0) throw new InvalidOperationException("El fondo inicial no puede ser negativo.");
        if (mercadoPagoOpening < 0) throw new InvalidOperationException("El saldo inicial de Mercado Pago no puede ser negativo.");
        var retention = mercadoPagoRetentionPercent ?? ConfiguredMercadoPagoRetentionPercent();
        if (retention < 0 || retention > 100) throw new InvalidOperationException("La retención de Mercado Pago debe estar entre 0% y 100%.");
        if (!mercadoPagoEnabled) { mercadoPagoOpening = 0; retention = 0; }
        if (mercadoPagoEnabled && mercadoPagoOpening <= 0)
            throw new InvalidOperationException("Para habilitar Mercado Pago ingresá un saldo inicial mayor a cero.");
        if (IsOpen()) throw new InvalidOperationException("Ya hay una caja abierta.");

        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "INSERT INTO cash_sessions(user_id,opening_amount,mercado_pago_enabled,mercado_pago_opening_amount,mercado_pago_retention_percent,status) VALUES($u,$a,$mp,$mpa,$mpr,'OPEN')";
        cmd.Parameters.AddWithValue("$u", userId);
        cmd.Parameters.AddWithValue("$a", amount);
        cmd.Parameters.AddWithValue("$mp", mercadoPagoEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$mpa", mercadoPagoOpening);
        cmd.Parameters.AddWithValue("$mpr", retention);
        cmd.ExecuteNonQuery();

    }

    public static void Movement(string type, string concept, double amount, int userId, string paymentMethod = "EFECTIVO")
    {
        if (type is not ("INCOME" or "EXPENSE"))
            throw new InvalidOperationException("Tipo de movimiento de caja inválido.");
        if (amount <= 0)
            throw new InvalidOperationException("El importe debe ser mayor a cero.");
        if (string.IsNullOrWhiteSpace(concept))
            throw new InvalidOperationException("Ingresá un concepto.");
        if (paymentMethod is not ("EFECTIVO" or "TARJETA" or "MERCADO PAGO" or "TRANSFERENCIA"))
            throw new InvalidOperationException("Medio de ingreso/egreso no válido.");
        if (paymentMethod == "MERCADO PAGO" && !IsMercadoPagoEnabled())
            throw new InvalidOperationException("Mercado Pago no está habilitado en esta sesión. Para usarlo, activalo en la apertura de caja e ingresá el saldo inicial.");

        var session = CurrentSessionId() ?? throw new InvalidOperationException("Primero abrí la caja.");

        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO cash_movements(session_id,user_id,movement_type,concept,amount,payment_method,reference_id)
            VALUES($s,$u,$t,$c,$a,$pm,NULL)
            """;
        cmd.Parameters.AddWithValue("$s", session);
        cmd.Parameters.AddWithValue("$u", userId);
        cmd.Parameters.AddWithValue("$t", type);
        cmd.Parameters.AddWithValue("$c", concept.Trim());
        cmd.Parameters.AddWithValue("$a", amount);
        cmd.Parameters.AddWithValue("$pm", paymentMethod);
        cmd.ExecuteNonQuery();
    }

    public static void VoidManualMovement(long movementId, int userId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Ingresá la explicación de por qué se anula el movimiento.");

        var session = CurrentSessionId() ?? throw new InvalidOperationException("No hay una caja abierta.");
        using var cn = Database.Open();
        using var tx = cn.BeginTransaction();

        using var find = cn.CreateCommand();
        find.Transaction = tx;
        find.CommandText = "SELECT movement_type,concept,amount,payment_method,COALESCE(voided,0) FROM cash_movements WHERE id=$id AND session_id=$s AND movement_type IN ('INCOME','EXPENSE')";
        find.Parameters.AddWithValue("$id", movementId);
        find.Parameters.AddWithValue("$s", session);
        using var r = find.ExecuteReader();
        if (!r.Read())
            throw new InvalidOperationException("El ingreso o egreso seleccionado ya no existe o no pertenece a la caja abierta.");

        var type = r.GetString(0);
        var concept = r.GetString(1);
        var amount = r.GetDouble(2);
        var paymentMethod = r.GetString(3);
        var alreadyVoided = r.GetInt32(4) != 0;
        r.Close();
        if (alreadyVoided) throw new InvalidOperationException("Ese movimiento ya está anulado.");

        using var upd = cn.CreateCommand();
        upd.Transaction = tx;
        upd.CommandText = "UPDATE cash_movements SET voided=1, void_reason=$reason, voided_by=$user, voided_at=CURRENT_TIMESTAMP WHERE id=$id AND session_id=$s AND movement_type IN ('INCOME','EXPENSE') AND COALESCE(voided,0)=0";
        upd.Parameters.AddWithValue("$reason", reason.Trim());
        upd.Parameters.AddWithValue("$user", userId);
        upd.Parameters.AddWithValue("$id", movementId);
        upd.Parameters.AddWithValue("$s", session);
        if (upd.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("No se pudo anular el movimiento.");

        tx.Commit();
        AuditService.Log(userId, "CASH_MOVEMENT_VOID", "INGRESOS_EGRESOS",
            $"Movimiento anulado: {(type == "INCOME" ? "Ingreso" : "Egreso")} · ${amount:N2} · {paymentMethod} · {concept} · Motivo: {reason.Trim()}");
    }

    // Compatibilidad con versiones anteriores.
    public static void DeleteManualMovement(long movementId, int userId) => VoidManualMovement(movementId, userId, "Anulación solicitada desde una versión anterior.");

    public static IReadOnlyDictionary<string, double> SalesPaymentSummary(long sessionId)
    {
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            SELECT p.method, COALESCE(SUM(p.amount),0)
            FROM payments p
            JOIN sales s ON s.id=p.sale_id
            WHERE s.session_id=$s AND s.status='COMPLETED' AND p.status='APPROVED'
            GROUP BY p.method
            """;
        cmd.Parameters.AddWithValue("$s", sessionId);
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        using var r = cmd.ExecuteReader();
        while (r.Read()) result[r.GetString(0)] = r.GetDouble(1);
        return result;
    }

    public static (double opening, double sales, double income, double expenses, double cardIncome, double cardExpenses,
        double mercadoPagoOpening, double mercadoPagoSales, double mercadoPagoIncome, double mercadoPagoExpenses,
        double mercadoPagoRetentionPercent, double mercadoPagoRetentionAmount, double mercadoPagoExpected, double expected) Summary()
    {
        var session = CurrentSessionId();
        if (session is null) return (0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        using var cn = Database.Open();

        double Scalar(string sql)
        {
            using var c = cn.CreateCommand();
            c.CommandText = sql;
            c.Parameters.AddWithValue("$s", session.Value);
            return Convert.ToDouble(c.ExecuteScalar() ?? 0);
        }

        var opening = Scalar("SELECT opening_amount FROM cash_sessions WHERE id=$s");
        var mpOpening = Scalar("SELECT COALESCE(mercado_pago_opening_amount,0) FROM cash_sessions WHERE id=$s");
        var mpRetention = Scalar("SELECT COALESCE(mercado_pago_retention_percent,0) FROM cash_sessions WHERE id=$s");
        var salesCash = Scalar("SELECT COALESCE(SUM(amount),0) FROM cash_movements WHERE session_id=$s AND movement_type='SALE' AND payment_method='EFECTIVO' AND COALESCE(voided,0)=0");
        var incomeCash = Scalar("SELECT COALESCE(SUM(amount),0) FROM cash_movements WHERE session_id=$s AND movement_type='INCOME' AND payment_method='EFECTIVO' AND COALESCE(voided,0)=0");
        var expenseCash = Scalar("SELECT COALESCE(SUM(amount),0) FROM cash_movements WHERE session_id=$s AND movement_type='EXPENSE' AND payment_method='EFECTIVO' AND COALESCE(voided,0)=0");
        var cardIncome = Scalar("SELECT COALESCE(SUM(amount),0) FROM cash_movements WHERE session_id=$s AND movement_type='INCOME' AND payment_method='TARJETA' AND COALESCE(voided,0)=0");
        var cardExpenses = Scalar("SELECT COALESCE(SUM(amount),0) FROM cash_movements WHERE session_id=$s AND movement_type='EXPENSE' AND payment_method='TARJETA' AND COALESCE(voided,0)=0");
        var mpIncome = Scalar("SELECT COALESCE(SUM(amount),0) FROM cash_movements WHERE session_id=$s AND movement_type='INCOME' AND payment_method='MERCADO PAGO' AND COALESCE(voided,0)=0");
        var mpExpenses = Scalar("SELECT COALESCE(SUM(amount),0) FROM cash_movements WHERE session_id=$s AND movement_type='EXPENSE' AND payment_method='MERCADO PAGO' AND COALESCE(voided,0)=0");

        using var payments = cn.CreateCommand();
        payments.CommandText = "SELECT COALESCE(SUM(p.amount),0) FROM payments p JOIN sales s ON s.id=p.sale_id WHERE s.session_id=$s AND s.status='COMPLETED' AND p.status='APPROVED' AND p.method='MERCADO PAGO'";
        payments.Parameters.AddWithValue("$s", session.Value);
        var mpSales = Convert.ToDouble(payments.ExecuteScalar() ?? 0);

        var expectedCash = opening + salesCash + incomeCash - expenseCash;
        var mpRetentionAmount = Math.Round(mpSales * mpRetention / 100.0, 2, MidpointRounding.AwayFromZero);
        var expectedMp = mpOpening + mpSales + mpIncome - mpExpenses - mpRetentionAmount;
        return (opening, salesCash, incomeCash, expenseCash, cardIncome, cardExpenses,
            mpOpening, mpSales, mpIncome, mpExpenses, mpRetention, mpRetentionAmount, expectedMp, expectedCash);
    }

    public static long Close(double counted, int userId, string differenceReason = "", double? countedMercadoPago = null)
    {
        var session = CurrentSessionId() ?? throw new InvalidOperationException("No hay una caja abierta.");
        var s = Summary();
        var mpEnabled = IsMercadoPagoEnabled();
        var mpCounted = mpEnabled ? (countedMercadoPago ?? s.mercadoPagoExpected) : 0;
        var mpDifference = mpEnabled ? mpCounted - s.mercadoPagoExpected : 0;

        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "UPDATE cash_sessions SET closed_at=CURRENT_TIMESTAMP,closing_amount=$c,expected_amount=$e,difference=$d,mercado_pago_closing_amount=$mpc,mercado_pago_expected_amount=$mpe,mercado_pago_difference=$mpd,status='CLOSED' WHERE id=$s";
        cmd.Parameters.AddWithValue("$c", counted);
        cmd.Parameters.AddWithValue("$e", s.expected);
        cmd.Parameters.AddWithValue("$d", counted - s.expected);
        cmd.Parameters.AddWithValue("$mpc", mpEnabled ? mpCounted : 0);
        cmd.Parameters.AddWithValue("$mpe", mpEnabled ? s.mercadoPagoExpected : 0);
        cmd.Parameters.AddWithValue("$mpd", mpEnabled ? mpDifference : 0);
        cmd.Parameters.AddWithValue("$s", session);
        cmd.ExecuteNonQuery();
        AuditService.Log(userId, "CASH_CLOSE", "CAJA", $"Efectivo esperado ${s.expected:N2} · Contado ${counted:N2} · Diferencia ${counted - s.expected:N2}" +
            (mpEnabled ? $" · Mercado Pago esperado ${s.mercadoPagoExpected:N2} · Retención {s.mercadoPagoRetentionPercent:N2}% (${s.mercadoPagoRetentionAmount:N2}) · Saldo informado ${mpCounted:N2} · Diferencia ${mpDifference:N2}" : ""));
        return session;
    }

    public static (int userId, string username, string fullName, DateTime openedAt) CurrentSessionInfo()
    {
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            SELECT cs.user_id, COALESCE(u.username,''), COALESCE(u.full_name,''), cs.opened_at
            FROM cash_sessions cs LEFT JOIN users u ON u.id=cs.user_id
            WHERE cs.status='OPEN' ORDER BY cs.id DESC LIMIT 1
            """;
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new InvalidOperationException("No hay una caja abierta.");
        return (r.IsDBNull(0) ? 0 : r.GetInt32(0), r.GetString(1), r.GetString(2),
            DateTime.TryParse(r.GetString(3), out var dt) ? dt : DateTime.MinValue);
    }
}
