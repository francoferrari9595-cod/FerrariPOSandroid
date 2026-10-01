using FerrarisPOS.Data;
using Microsoft.Data.Sqlite;
using System.Globalization;

namespace FerrarisPOS.Services;

/// <summary>
/// Consolida el dinero vendido por categoría/departamento y medio de pago.
/// En ventas mixtas distribuye cada pago proporcionalmente entre las líneas de la venta,
/// evitando duplicar importes cuando una venta contiene varios productos.
/// </summary>
public static class SalesCategoryReportService
{
    public sealed record CategorySales(
        string Category,
        double Cash,
        double MercadoPago,
        double Card,
        double Transfer,
        double Credit,
        double Total)
    {
        public double Digital => MercadoPago + Card + Transfer;
    }

    public static List<CategorySales> ForSession(SqliteConnection cn, long sessionId)
    {
        const string sql = """
            SELECT COALESCE(NULLIF(TRIM(si.category),''), NULLIF(TRIM(p.category),''), 'SIN CATEGORÍA') AS Categoria,
                   COALESCE(SUM(CASE WHEN UPPER(TRIM(pay.method))='EFECTIVO' THEN CASE WHEN s.total>0 THEN si.total*pay.amount/s.total ELSE 0 END ELSE 0 END),0) AS Efectivo,
                   COALESCE(SUM(CASE WHEN UPPER(TRIM(pay.method))='MERCADO PAGO' THEN CASE WHEN s.total>0 THEN si.total*pay.amount/s.total ELSE 0 END ELSE 0 END),0) AS MercadoPago,
                   COALESCE(SUM(CASE WHEN UPPER(TRIM(pay.method))='TARJETA' THEN CASE WHEN s.total>0 THEN si.total*pay.amount/s.total ELSE 0 END ELSE 0 END),0) AS Tarjeta,
                   COALESCE(SUM(CASE WHEN UPPER(TRIM(pay.method))='TRANSFERENCIA' THEN CASE WHEN s.total>0 THEN si.total*pay.amount/s.total ELSE 0 END ELSE 0 END),0) AS Transferencia,
                   COALESCE(SUM(CASE WHEN UPPER(TRIM(pay.method)) LIKE 'CRÉDITO%' OR UPPER(TRIM(pay.method)) LIKE 'CREDITO%' THEN CASE WHEN s.total>0 THEN si.total*pay.amount/s.total ELSE 0 END ELSE 0 END),0) AS Credito,
                   COALESCE(SUM(CASE WHEN s.total>0 THEN si.total*pay.amount/s.total ELSE 0 END),0) AS TotalVendido
            FROM sale_items si
            JOIN sales s ON s.id=si.sale_id
            JOIN products p ON p.id=si.product_id
            JOIN payments pay ON pay.sale_id=s.id AND pay.status='APPROVED'
            WHERE s.session_id=$s AND s.status='COMPLETED'
            GROUP BY Categoria
            HAVING ABS(TotalVendido)>0.005
            ORDER BY TotalVendido DESC, Categoria COLLATE NOCASE
            """;
        return Query(cn, sql, cmd => cmd.Parameters.AddWithValue("$s", sessionId));
    }

    public static List<CategorySales> ForDate(SqliteConnection cn, DateTime date)
    {
        const string sql = """
            SELECT COALESCE(NULLIF(TRIM(si.category),''), NULLIF(TRIM(p.category),''), 'SIN CATEGORÍA') AS Categoria,
                   COALESCE(SUM(CASE WHEN UPPER(TRIM(pay.method))='EFECTIVO' THEN CASE WHEN s.total>0 THEN si.total*pay.amount/s.total ELSE 0 END ELSE 0 END),0) AS Efectivo,
                   COALESCE(SUM(CASE WHEN UPPER(TRIM(pay.method))='MERCADO PAGO' THEN CASE WHEN s.total>0 THEN si.total*pay.amount/s.total ELSE 0 END ELSE 0 END),0) AS MercadoPago,
                   COALESCE(SUM(CASE WHEN UPPER(TRIM(pay.method))='TARJETA' THEN CASE WHEN s.total>0 THEN si.total*pay.amount/s.total ELSE 0 END ELSE 0 END),0) AS Tarjeta,
                   COALESCE(SUM(CASE WHEN UPPER(TRIM(pay.method))='TRANSFERENCIA' THEN CASE WHEN s.total>0 THEN si.total*pay.amount/s.total ELSE 0 END ELSE 0 END),0) AS Transferencia,
                   COALESCE(SUM(CASE WHEN UPPER(TRIM(pay.method)) LIKE 'CRÉDITO%' OR UPPER(TRIM(pay.method)) LIKE 'CREDITO%' THEN CASE WHEN s.total>0 THEN si.total*pay.amount/s.total ELSE 0 END ELSE 0 END),0) AS Credito,
                   COALESCE(SUM(CASE WHEN s.total>0 THEN si.total*pay.amount/s.total ELSE 0 END),0) AS TotalVendido
            FROM sale_items si
            JOIN sales s ON s.id=si.sale_id
            JOIN products p ON p.id=si.product_id
            JOIN payments pay ON pay.sale_id=s.id AND pay.status='APPROVED'
            WHERE date(s.created_at,'localtime')=$d AND s.status='COMPLETED'
            GROUP BY Categoria
            HAVING ABS(TotalVendido)>0.005
            ORDER BY TotalVendido DESC, Categoria COLLATE NOCASE
            """;
        var d = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return Query(cn, sql, cmd => cmd.Parameters.AddWithValue("$d", d));
    }

    private static List<CategorySales> Query(SqliteConnection cn, string sql, Action<SqliteCommand> parameters)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = sql;
        parameters(cmd);
        using var r = cmd.ExecuteReader();
        var result = new List<CategorySales>();
        while (r.Read())
        {
            result.Add(new CategorySales(
                r.IsDBNull(0) ? "SIN CATEGORÍA" : r.GetString(0),
                r.IsDBNull(1) ? 0 : r.GetDouble(1),
                r.IsDBNull(2) ? 0 : r.GetDouble(2),
                r.IsDBNull(3) ? 0 : r.GetDouble(3),
                r.IsDBNull(4) ? 0 : r.GetDouble(4),
                r.IsDBNull(5) ? 0 : r.GetDouble(5),
                r.IsDBNull(6) ? 0 : r.GetDouble(6)));
        }
        return result;
    }
}
