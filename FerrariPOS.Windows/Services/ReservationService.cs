using FerrarisPOS.Data;

namespace FerrarisPOS.Services;

public sealed record Reservation(int Id, string CustomerName, string Phone, string Date, string Hour, int People, int? TableId, string TableName, string Status, string Notes);

public static class ReservationService
{
    public static List<Reservation> List(DateTime date)
    {
        using var cn=Database.Open(); using var cmd=cn.CreateCommand(); cmd.CommandText="SELECT r.id,r.customer_name,r.phone,r.reservation_date,r.hour,r.people,r.table_id,COALESCE(t.name,''),r.status,r.notes FROM reservations r LEFT JOIN restaurant_tables t ON t.id=r.table_id WHERE r.reservation_date=$d ORDER BY r.hour,r.id"; cmd.Parameters.AddWithValue("$d",date.ToString("yyyy-MM-dd")); using var r=cmd.ExecuteReader(); var list=new List<Reservation>(); while(r.Read()) list.Add(new Reservation(r.GetInt32(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetString(4),r.GetInt32(5),r.IsDBNull(6)?null:r.GetInt32(6),r.GetString(7),r.GetString(8),r.GetString(9))); return list;
    }
    public static int Save(string name,string phone,DateTime date, string hour,int people,int? tableId,string notes,int id=0,string status="CONFIRMED")
    {
        if(string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("Ingresá el nombre del cliente."); if(people<=0) throw new InvalidOperationException("La cantidad de personas debe ser mayor a cero.");
        using var cn=Database.Open();
        if (tableId.HasValue)
        {
            using var overlap=cn.CreateCommand(); overlap.CommandText="SELECT COUNT(*) FROM reservations WHERE reservation_date=$d AND hour=$h AND table_id=$t AND status='CONFIRMED' AND id<>$id"; overlap.Parameters.AddWithValue("$d",date.ToString("yyyy-MM-dd")); overlap.Parameters.AddWithValue("$h",hour); overlap.Parameters.AddWithValue("$t",tableId.Value); overlap.Parameters.AddWithValue("$id",id); if(Convert.ToInt32(overlap.ExecuteScalar()??0)>0) throw new InvalidOperationException("La mesa ya tiene una reserva confirmada para esa fecha y hora.");
        }
        using var cmd=cn.CreateCommand();
        cmd.CommandText=id==0?"INSERT INTO reservations(customer_name,phone,reservation_date,hour,people,table_id,status,notes,created_by) VALUES($n,$p,$d,$h,$pe,$t,$s,$no,$u); SELECT last_insert_rowid();":"UPDATE reservations SET customer_name=$n,phone=$p,reservation_date=$d,hour=$h,people=$pe,table_id=$t,status=$s,notes=$no WHERE id=$id; SELECT $id;";
        cmd.Parameters.AddWithValue("$n",name.Trim());cmd.Parameters.AddWithValue("$p",phone.Trim());cmd.Parameters.AddWithValue("$d",date.ToString("yyyy-MM-dd"));cmd.Parameters.AddWithValue("$h",hour);cmd.Parameters.AddWithValue("$pe",people);cmd.Parameters.AddWithValue("$t",tableId.HasValue?(object)tableId.Value:DBNull.Value);cmd.Parameters.AddWithValue("$s",status);cmd.Parameters.AddWithValue("$no",notes.Trim());cmd.Parameters.AddWithValue("$u",Session.UserId);if(id>0)cmd.Parameters.AddWithValue("$id",id);var newId=Convert.ToInt32(cmd.ExecuteScalar()); AuditService.Log(Session.UserId,id==0?"RESERVATION_CREATE":"RESERVATION_UPDATE","RESERVAS",$"{name} · {date:dd/MM/yyyy} {hour} · {people} personas"); return newId;
    }

    public static bool IsTableReserved(int tableId, DateTime date)
    {
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM reservations WHERE reservation_date=$d AND table_id=$t AND status='CONFIRMED'";
        cmd.Parameters.AddWithValue("$d", date.ToString("yyyy-MM-dd"));
        cmd.Parameters.AddWithValue("$t", tableId);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    public static Reservation? GetTableReservationWithinWindow(int tableId, DateTime now, int leadMinutes = 120)
    {
        var reservations = List(now.Date).Where(x => x.TableId == tableId && string.Equals(x.Status, "CONFIRMED", StringComparison.OrdinalIgnoreCase));
        Reservation? closest = null;
        DateTime closestTime = DateTime.MaxValue;
        foreach (var reservation in reservations)
        {
            if (!TryGetReservationDateTime(reservation, out var reservationTime)) continue;
            if (reservationTime.Date != now.Date || reservationTime < now || reservationTime > now.AddMinutes(leadMinutes)) continue;
            if (reservationTime < closestTime) { closest = reservation; closestTime = reservationTime; }
        }
        return closest;
    }

    public static bool IsTableBlockedForReservation(int tableId, DateTime now, int leadMinutes = 120)
        => GetTableReservationWithinWindow(tableId, now, leadMinutes) is not null;

    public static bool TryGetReservationDateTime(Reservation reservation, out DateTime value)
    {
        var text = $"{reservation.Date} {reservation.Hour}".Trim();
        return DateTime.TryParseExact(text, new[] { "yyyy-MM-dd HH:mm", "yyyy-MM-dd H:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd H:mm:ss" },
            System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out value);
    }

    public static void SetStatus(int id,string status){using var cn=Database.Open();using var cmd=cn.CreateCommand();cmd.CommandText="UPDATE reservations SET status=$s WHERE id=$id";cmd.Parameters.AddWithValue("$s",status);cmd.Parameters.AddWithValue("$id",id);cmd.ExecuteNonQuery();AuditService.Log(Session.UserId,"RESERVATION_STATUS","RESERVAS",$"Reserva {id}: {status}");}
}
