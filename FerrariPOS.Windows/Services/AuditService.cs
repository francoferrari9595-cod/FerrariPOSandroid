using FerrarisPOS.Data;

namespace FerrarisPOS.Services;

public static class AuditService
{
    public static void Log(int userId, string action, string module, string details = "")
    {
        try
        {
            using var cn = Database.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "INSERT INTO audit_log(user_id,action,module,details) VALUES($u,$a,$m,$d)";
            cmd.Parameters.AddWithValue("$u", userId);
            cmd.Parameters.AddWithValue("$a", action);
            cmd.Parameters.AddWithValue("$m", module);
            cmd.Parameters.AddWithValue("$d", details ?? "");
            cmd.ExecuteNonQuery();
        }
        catch { /* La auditoría nunca debe bloquear una venta. */ }
    }
}
