using FerrarisPOS.Data;

namespace FerrarisPOS.Services;

public static class CategoryService
{
    /// <summary>
    /// Devuelve las categorías activas ordenadas alfabéticamente.
    /// La tabla de categorías es independiente del texto guardado en productos
    /// para mantener compatibilidad con bases existentes.
    /// </summary>
    public static List<string> GetAll()
    {
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT name FROM product_categories WHERE active=1 ORDER BY name COLLATE NOCASE";
        using var r = cmd.ExecuteReader();

        var result = new List<string>();
        while (r.Read())
            result.Add(r.GetString(0));
        return result;
    }

    /// <summary>
    /// Crea una categoría si no existe. La comparación es independiente de
    /// mayúsculas/minúsculas y de espacios exteriores.
    /// </summary>
    public static string Ensure(string name)
    {
        name = Normalize(name);
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        using var cn = Database.Open();
        using var find = cn.CreateCommand();
        find.CommandText = "SELECT name FROM product_categories WHERE active=1 AND name=$name COLLATE NOCASE LIMIT 1";
        find.Parameters.AddWithValue("$name", name);
        var existing = find.ExecuteScalar();
        if (existing is not null && existing != DBNull.Value)
            return Convert.ToString(existing) ?? name;

        using var insert = cn.CreateCommand();
        insert.CommandText = "INSERT INTO product_categories(name,active) VALUES($name,1)";
        insert.Parameters.AddWithValue("$name", name);

        try
        {
            insert.ExecuteNonQuery();
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex)
            when (ex.SqliteErrorCode == 19)
        {
            // Otra instancia pudo crearla entre SELECT e INSERT.
            using var retry = cn.CreateCommand();
            retry.CommandText = "SELECT name FROM product_categories WHERE name=$name COLLATE NOCASE LIMIT 1";
            retry.Parameters.AddWithValue("$name", name);
            return Convert.ToString(retry.ExecuteScalar()) ?? name;
        }

        AuditService.Log(Session.UserId, "CATEGORY_CREATE", "INVENTARIO", name);
        return name;
    }

    public static bool Exists(string name)
    {
        name = Normalize(name);
        if (string.IsNullOrWhiteSpace(name)) return false;

        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM product_categories WHERE active=1 AND name=$name COLLATE NOCASE LIMIT 1";
        cmd.Parameters.AddWithValue("$name", name);
        return cmd.ExecuteScalar() is not null;
    }

    public static string Normalize(string? name)
        => (name ?? string.Empty).Trim();
}
