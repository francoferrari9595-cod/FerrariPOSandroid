using FerrarisPOS.Data;
using Microsoft.Data.Sqlite;

namespace FerrarisPOS.Services;

public sealed record RestaurantTable(int Id, string Name, int X, int Y, int Width, int Height, bool Enabled, int SalonId = 1, string Shape = "RECTANGLE", double Rotation = 0, int Capacity = 4, string Color = "");
public sealed record SalonDecoration(int Id, string Type, string Name, int X, int Y, int Width, int Height, int SalonId = 1);
public sealed record Salon(int Id, string Name, string Description, bool Active);

public static class TableService
{
    public static void EnsureTables()
    {
        using var cn = Database.Open();
        using var tx = cn.BeginTransaction();
        Exec(tx, cn, "CREATE TABLE IF NOT EXISTS salons(id INTEGER PRIMARY KEY AUTOINCREMENT,name TEXT NOT NULL UNIQUE,description TEXT NOT NULL DEFAULT '',active INTEGER NOT NULL DEFAULT 1)");
        Exec(tx, cn, "CREATE TABLE IF NOT EXISTS restaurant_tables(id INTEGER PRIMARY KEY AUTOINCREMENT,name TEXT NOT NULL,x INTEGER NOT NULL DEFAULT 20,y INTEGER NOT NULL DEFAULT 20,width INTEGER NOT NULL DEFAULT 120,height INTEGER NOT NULL DEFAULT 80,enabled INTEGER NOT NULL DEFAULT 1,salon_id INTEGER NOT NULL DEFAULT 1,shape TEXT NOT NULL DEFAULT 'RECTANGLE',rotation REAL NOT NULL DEFAULT 0,capacity INTEGER NOT NULL DEFAULT 4,color TEXT NOT NULL DEFAULT '')");
        Exec(tx, cn, "CREATE TABLE IF NOT EXISTS salon_decorations(id INTEGER PRIMARY KEY AUTOINCREMENT,type TEXT NOT NULL,name TEXT NOT NULL,x INTEGER NOT NULL DEFAULT 20,y INTEGER NOT NULL DEFAULT 20,width INTEGER NOT NULL DEFAULT 80,height INTEGER NOT NULL DEFAULT 50,salon_id INTEGER NOT NULL DEFAULT 1)");
        Exec(tx, cn, "INSERT OR IGNORE INTO salons(id,name,description,active) VALUES(1,'Salón principal','Diseño principal',1)");
        using var count = cn.CreateCommand(); count.Transaction = tx; count.CommandText = "SELECT COUNT(*) FROM restaurant_tables";
        if (Convert.ToInt32(count.ExecuteScalar() ?? 0) == 0)
        {
            for (var i = 0; i < 8; i++)
            {
                using var cmd = cn.CreateCommand(); cmd.Transaction = tx;
                cmd.CommandText = "INSERT INTO restaurant_tables(name,x,y,width,height,enabled,salon_id,shape,capacity) VALUES($n,$x,$y,120,80,1,1,$shape,4)";
                cmd.Parameters.AddWithValue("$n", $"Mesa {i + 1}");
                cmd.Parameters.AddWithValue("$x", 30 + (i % 4) * 170);
                cmd.Parameters.AddWithValue("$y", 30 + (i / 4) * 130);
                cmd.Parameters.AddWithValue("$shape", i % 3 == 0 ? "ROUND" : "RECTANGLE");
                cmd.ExecuteNonQuery();
            }
        }
        tx.Commit();
    }

    public static List<Salon> GetSalons(bool activeOnly = true)
    {
        EnsureTables();
        using var cn = Database.Open(); using var cmd = cn.CreateCommand();
        cmd.CommandText = activeOnly ? "SELECT id,name,description,active FROM salons WHERE active=1 ORDER BY id" : "SELECT id,name,description,active FROM salons ORDER BY id";
        using var r = cmd.ExecuteReader(); var result = new List<Salon>();
        while (r.Read()) result.Add(new Salon(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetInt32(3) != 0));
        return result;
    }

    public static int ActiveSalonId => int.TryParse(Database.GetSetting("active_salon_id", "1"), out var id) && id > 0 ? id : 1;
    public static void SetActiveSalon(int salonId) => Database.SetSetting("active_salon_id", salonId.ToString());

    public static int AddSalon(string name, string description = "")
    {
        if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Ingresá un nombre de salón.");
        using var cn = Database.Open(); using var cmd = cn.CreateCommand();
        cmd.CommandText = "INSERT INTO salons(name,description,active) VALUES($n,$d,1); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$n", name.Trim()); cmd.Parameters.AddWithValue("$d", description.Trim());
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public static void RenameSalon(int id, string name, string description = "")
    {
        using var cn = Database.Open(); using var cmd = cn.CreateCommand(); cmd.CommandText = "UPDATE salons SET name=$n,description=$d WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$n", name.Trim()); cmd.Parameters.AddWithValue("$d", description.Trim()); cmd.ExecuteNonQuery();
    }

    public static List<RestaurantTable> GetTables(bool includeDisabled = false)
    {
        EnsureTables(); using var cn = Database.Open(); using var cmd = cn.CreateCommand();
        cmd.CommandText = includeDisabled
            ? "SELECT id,name,x,y,width,height,enabled,salon_id,shape,rotation,capacity,color FROM restaurant_tables WHERE salon_id=$s ORDER BY id"
            : "SELECT id,name,x,y,width,height,enabled,salon_id,shape,rotation,capacity,color FROM restaurant_tables WHERE salon_id=$s AND enabled=1 ORDER BY id";
        cmd.Parameters.AddWithValue("$s", ActiveSalonId);
        using var r = cmd.ExecuteReader(); var result = new List<RestaurantTable>();
        while (r.Read()) result.Add(new RestaurantTable(r.GetInt32(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5), r.GetInt32(6) != 0, r.GetInt32(7), r.GetString(8), r.GetDouble(9), r.GetInt32(10), r.GetString(11)));
        return result;
    }

    public static int AddTable(string name = "Mesa nueva", string shape = "RECTANGLE", int capacity = 4)
    {
        EnsureTables(); using var cn = Database.Open(); using var cmd = cn.CreateCommand();
        cmd.CommandText = "INSERT INTO restaurant_tables(name,x,y,width,height,enabled,salon_id,shape,capacity) VALUES($n,40,140,120,80,1,$s,$shape,$cap); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$n", name); cmd.Parameters.AddWithValue("$s", ActiveSalonId); cmd.Parameters.AddWithValue("$shape", shape); cmd.Parameters.AddWithValue("$cap", Math.Max(1, capacity)); return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public static void UpdateTable(int id, string name, int x, int y, int width, int height, bool enabled = true, string? shape = null, double rotation = 0, int capacity = 4, string color = "")
    {
        using var cn = Database.Open(); using var cmd = cn.CreateCommand();
        cmd.CommandText = "UPDATE restaurant_tables SET name=$n,x=$x,y=$y,width=$w,height=$h,enabled=$e,shape=COALESCE($shape,shape),rotation=$rot,capacity=$cap,color=$color WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$n", name); cmd.Parameters.AddWithValue("$x", x); cmd.Parameters.AddWithValue("$y", y); cmd.Parameters.AddWithValue("$w", width); cmd.Parameters.AddWithValue("$h", height); cmd.Parameters.AddWithValue("$e", enabled ? 1 : 0); cmd.Parameters.AddWithValue("$shape", (object?)shape ?? DBNull.Value); cmd.Parameters.AddWithValue("$rot", rotation); cmd.Parameters.AddWithValue("$cap", Math.Max(1, capacity)); cmd.Parameters.AddWithValue("$color", color ?? ""); cmd.ExecuteNonQuery();
    }

    public static void DeleteTable(int id) { using var cn = Database.Open(); using var cmd = cn.CreateCommand(); cmd.CommandText = "UPDATE restaurant_tables SET enabled=0 WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id); cmd.ExecuteNonQuery(); }

    public static List<SalonDecoration> GetDecorations()
    {
        EnsureTables(); using var cn = Database.Open(); using var cmd = cn.CreateCommand(); cmd.CommandText = "SELECT id,type,name,x,y,width,height,salon_id FROM salon_decorations WHERE salon_id=$s ORDER BY id"; cmd.Parameters.AddWithValue("$s", ActiveSalonId);
        using var r = cmd.ExecuteReader(); var result = new List<SalonDecoration>(); while (r.Read()) result.Add(new SalonDecoration(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5), r.GetInt32(6), r.GetInt32(7))); return result;
    }

    public static int AddDecoration(string type, string name) { using var cn = Database.Open(); using var cmd = cn.CreateCommand(); cmd.CommandText = "INSERT INTO salon_decorations(type,name,x,y,width,height,salon_id) VALUES($t,$n,40,140,90,60,$s); SELECT last_insert_rowid();"; cmd.Parameters.AddWithValue("$t", type); cmd.Parameters.AddWithValue("$n", name); cmd.Parameters.AddWithValue("$s", ActiveSalonId); return Convert.ToInt32(cmd.ExecuteScalar()); }
    public static void UpdateDecoration(int id, int x, int y, int width, int height) { using var cn = Database.Open(); using var cmd = cn.CreateCommand(); cmd.CommandText = "UPDATE salon_decorations SET x=$x,y=$y,width=$w,height=$h WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$x", x); cmd.Parameters.AddWithValue("$y", y); cmd.Parameters.AddWithValue("$w", width); cmd.Parameters.AddWithValue("$h", height); cmd.ExecuteNonQuery(); }
    public static void DeleteDecoration(int id) { using var cn = Database.Open(); using var cmd = cn.CreateCommand(); cmd.CommandText = "DELETE FROM salon_decorations WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id); cmd.ExecuteNonQuery(); }
    private static void Exec(SqliteTransaction tx, SqliteConnection cn, string sql) { using var cmd = cn.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
}
