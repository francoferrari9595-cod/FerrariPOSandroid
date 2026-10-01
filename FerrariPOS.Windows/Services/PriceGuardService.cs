using Microsoft.Data.Sqlite;

namespace FerrarisPOS.Services;

/// <summary>
/// Compatibilidad con llamadas existentes relacionadas con cambios de precio.
/// La regla de bloqueo de ventas por reducción de precio fue eliminada.
/// Estos métodos se mantienen para no romper versiones/código que todavía los invoque.
/// </summary>
public static class PriceGuardService
{
    public static void RecordProductPriceChange(SqliteConnection cn, int productId, double newPrice)
    {
        // Intencionalmente sin bloqueo de ventas por precio.
    }

    public static void RecordProductPriceChange(SqliteConnection cn, SqliteTransaction tx, int productId, double oldPrice, double newPrice)
    {
        // Intencionalmente sin bloqueo de ventas por precio.
    }

    public static void EnsureSaleAllowed(SqliteConnection cn, SqliteTransaction tx, int productId, string description)
    {
        // Intencionalmente sin bloqueo de ventas por precio.
    }
}
