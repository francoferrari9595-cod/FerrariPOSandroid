using FerrarisPOS.Data;

namespace FerrarisPOS.Services;

public static class SaleReturnService
{
    public static void ReturnItem(long saleItemId, double quantity, int userId, string reason)
    {
        if (quantity <= 0)
            throw new InvalidOperationException("La cantidad a devolver debe ser mayor a cero.");

        reason = (reason ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("El motivo de la devolución es obligatorio.");
        if (reason.Length > 500)
            reason = reason[..500];

        using var cn = Database.Open();
        using var tx = cn.BeginTransaction();

        try
        {
            long saleId;
            int customerId;
            int productId;
            double soldQuantity;
            double returnedQuantity;
            double unitPrice;
            string description;
            string barcode;
            string status;
            bool usesInventory;

            using (var cmd = cn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = """
                    SELECT si.sale_id, s.customer_id, si.product_id, si.quantity, COALESCE(si.returned_quantity,0),
                           si.unit_price, si.description, si.barcode, s.status, COALESCE(p.uses_inventory,1)
                    FROM sale_items si
                    INNER JOIN sales s ON s.id=si.sale_id
                    LEFT JOIN products p ON p.id=si.product_id
                    WHERE si.id=$id
                    """;
                cmd.Parameters.AddWithValue("$id", saleItemId);

                using var r = cmd.ExecuteReader();
                if (!r.Read())
                    throw new InvalidOperationException("El producto del ticket ya no existe.");

                saleId = r.GetInt64(0);
                customerId = r.GetInt32(1);
                productId = r.GetInt32(2);
                soldQuantity = r.GetDouble(3);
                returnedQuantity = r.GetDouble(4);
                unitPrice = r.GetDouble(5);
                description = r.GetString(6);
                barcode = r.GetString(7);
                status = r.GetString(8);
                usesInventory = InventoryControlService.IsGlobalEnabled && r.GetInt32(9) != 0;
            }

            if (!string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Solo se pueden devolver productos de tickets completados.");

            if (string.Equals(barcode, "COMÚN", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("El producto común/redondeo no controla stock y no puede devolverse al inventario.");

            var available = Math.Max(0, soldQuantity - returnedQuantity);
            if (quantity > available + 0.000001)
                throw new InvalidOperationException(
                    $"No podés devolver {quantity:N3}. Disponible para devolución: {available:N3}.");

            // El reintegro monetario debe respetar el precio neto de la línea,
            // incluidos descuentos. Así una devolución parcial no descompagina
            // el importe del ticket.
            double lineTotal = 0;
            using (var line = cn.CreateCommand())
            {
                line.Transaction = tx;
                line.CommandText = "SELECT total FROM sale_items WHERE id=$id";
                line.Parameters.AddWithValue("$id", saleItemId);
                lineTotal = Convert.ToDouble(line.ExecuteScalar() ?? 0);
            }
            var netUnitPrice = soldQuantity > 0 ? lineTotal / soldQuantity : unitPrice;
            var amount = Math.Round(quantity * netUnitPrice, 2);

            using (var update = cn.CreateCommand())
            {
                update.Transaction = tx;
                update.CommandText = """
                    UPDATE sale_items
                    SET returned_quantity = COALESCE(returned_quantity,0) + $q
                    WHERE id=$id
                    """;
                update.Parameters.AddWithValue("$q", quantity);
                update.Parameters.AddWithValue("$id", saleItemId);
                update.ExecuteNonQuery();
            }

            if (usesInventory)
            {
                using (var stock = cn.CreateCommand())
                {
                    stock.Transaction = tx;
                    stock.CommandText = """
                        UPDATE products
                        SET stock=stock+$q, updated_at=CURRENT_TIMESTAMP
                        WHERE id=$pid
                        """;
                    stock.Parameters.AddWithValue("$q", quantity);
                    stock.Parameters.AddWithValue("$pid", productId);

                    if (stock.ExecuteNonQuery() == 0)
                        throw new InvalidOperationException(
                            $"No se pudo devolver el producto '{description}' al inventario.");
                }

                using (var movement = cn.CreateCommand())
                {
                    movement.Transaction = tx;
                    movement.CommandText = """
                        INSERT INTO stock_movements(product_id,movement_type,quantity,reference,user_id)
                        VALUES($pid,'RETURN',$q,$ref,$u)
                        """;
                    movement.Parameters.AddWithValue("$pid", productId);
                    movement.Parameters.AddWithValue("$q", quantity);
                    movement.Parameters.AddWithValue("$ref", $"DEVOLUCIÓN TICKET #{saleId}");
                    movement.Parameters.AddWithValue("$u", userId);
                    movement.ExecuteNonQuery();
                }
            }

            using (var ret = cn.CreateCommand())
            {
                ret.Transaction = tx;
                ret.CommandText = """
                    INSERT INTO sale_returns
                        (sale_id,sale_item_id,product_id,quantity,amount,user_id,reason)
                    VALUES($sid,$item,$pid,$q,$amount,$u,$reason)
                    """;
                ret.Parameters.AddWithValue("$sid", saleId);
                ret.Parameters.AddWithValue("$item", saleItemId);
                ret.Parameters.AddWithValue("$pid", productId);
                ret.Parameters.AddWithValue("$q", quantity);
                ret.Parameters.AddWithValue("$amount", amount);
                ret.Parameters.AddWithValue("$u", userId);
                ret.Parameters.AddWithValue("$reason", reason);
                ret.ExecuteNonQuery();
            }

            // Reintegro del dinero: se devuelve por el/los mismos medios con los que se pagó.
            // EFECTIVO además debe reducir físicamente la caja mediante un movimiento negativo.
            var refundLines = new List<(string Method, double Amount)>();
            using (var pay = cn.CreateCommand())
            {
                pay.Transaction = tx;
                pay.CommandText = "SELECT method,COALESCE(SUM(amount),0) FROM payments WHERE sale_id=$sid AND status='APPROVED' GROUP BY method";
                pay.Parameters.AddWithValue("$sid", saleId);
                using var pr = pay.ExecuteReader();
                var salePaid = 0.0;
                while (pr.Read()) { var m=pr.GetString(0); var a=pr.GetDouble(1); if(a>0) { refundLines.Add((m,a)); salePaid += a; } }
                if (salePaid > 0.000001)
                {
                    var factor = Math.Min(1.0, amount / salePaid);
                    refundLines = refundLines.Select(x => (x.Method, Math.Round(x.Amount * factor, 2))).Where(x=>x.Item2>0).ToList();
                }
            }
            foreach (var refund in refundLines)
            {
                using var rp = cn.CreateCommand();
                rp.Transaction = tx;
                rp.CommandText = "INSERT INTO payments(sale_id,method,amount,reference,status) VALUES($sid,$m,$a,$r,'APPROVED')";
                rp.Parameters.AddWithValue("$sid", saleId); rp.Parameters.AddWithValue("$m", refund.Method); rp.Parameters.AddWithValue("$a", -refund.Amount); rp.Parameters.AddWithValue("$r", $"DEVOLUCIÓN TICKET #{saleId}"); rp.ExecuteNonQuery();
                if (refund.Method.StartsWith("CRÉDIT", StringComparison.OrdinalIgnoreCase) || refund.Method.StartsWith("CREDITO", StringComparison.OrdinalIgnoreCase))
                {
                    using var ca=cn.CreateCommand(); ca.Transaction=tx; ca.CommandText="INSERT INTO customer_accounts(customer_id,sale_id,entry_type,amount,concept,payment_method,user_id) VALUES($c,$sid,'PAYMENT',$a,$co,'CRÉDITO',$u)"; ca.Parameters.AddWithValue("$c",customerId); ca.Parameters.AddWithValue("$sid",saleId); ca.Parameters.AddWithValue("$a",refund.Amount); ca.Parameters.AddWithValue("$co",$"Reintegro por devolución ticket #{saleId}"); ca.Parameters.AddWithValue("$u",userId); ca.ExecuteNonQuery();
                }
                if (refund.Method.Equals("EFECTIVO", StringComparison.OrdinalIgnoreCase))
                {
                    using var cm = cn.CreateCommand(); cm.Transaction=tx; cm.CommandText="INSERT INTO cash_movements(session_id,user_id,movement_type,concept,amount,payment_method,reference_id) VALUES((SELECT session_id FROM sales WHERE id=$sid),$u,'SALE',$c,$a,'EFECTIVO',$sid)"; cm.Parameters.AddWithValue("$sid",saleId); cm.Parameters.AddWithValue("$u",userId); cm.Parameters.AddWithValue("$c",$"Devolución ticket #{saleId}"); cm.Parameters.AddWithValue("$a",-refund.Amount); cm.ExecuteNonQuery();
                }
            }

            using (var audit = cn.CreateCommand())
            {
                audit.Transaction = tx;
                audit.CommandText = """
                    INSERT INTO audit_log(user_id,action,module,details)
                    VALUES($u,'RETURN','VENTAS',$d)
                    """;
                audit.Parameters.AddWithValue("$u", userId);
                audit.Parameters.AddWithValue(
                    "$d",
                    $"Devolución de {quantity:N3} de '{description}' del ticket #{saleId}. Importe: ${amount:N2}. Motivo: {reason}");
                audit.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
    public static void CancelSale(long saleId, int userId, string reason)
    {
        reason=(reason??string.Empty).Trim();
        if(string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("El motivo de cancelación es obligatorio.");
        using var cn=Database.Open(); using var tx=cn.BeginTransaction();
        try
        {
            string status; int customerId; long ticket;
            using(var q=cn.CreateCommand()){q.Transaction=tx;q.CommandText="SELECT status,customer_id,ticket_no FROM sales WHERE id=$id";q.Parameters.AddWithValue("$id",saleId);using var r=q.ExecuteReader();if(!r.Read())throw new InvalidOperationException("Ticket no encontrado.");status=r.GetString(0);customerId=r.GetInt32(1);ticket=r.GetInt64(2);}
            if(!status.Equals("COMPLETED",StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Solo se pueden cancelar tickets completados.");

            using(var q=cn.CreateCommand()){q.Transaction=tx;q.CommandText="SELECT si.id,si.product_id,si.quantity,COALESCE(si.returned_quantity,0),COALESCE(p.uses_inventory,1) FROM sale_items si LEFT JOIN products p ON p.id=si.product_id WHERE si.sale_id=$sid";q.Parameters.AddWithValue("$sid",saleId);using var r=q.ExecuteReader();var rows=new List<(long id,int pid,double qty,double returned,bool inv)>();while(r.Read())rows.Add((r.GetInt64(0),r.GetInt32(1),r.GetDouble(2),r.GetDouble(3),InventoryControlService.IsGlobalEnabled&&r.GetInt32(4)!=0));r.Close();foreach(var x in rows){var remaining=Math.Max(0,x.qty-x.returned);if(remaining<=0)continue; if(x.inv){using var st=cn.CreateCommand();st.Transaction=tx;st.CommandText="UPDATE products SET stock=stock+$q,updated_at=CURRENT_TIMESTAMP WHERE id=$id";st.Parameters.AddWithValue("$q",remaining);st.Parameters.AddWithValue("$id",x.pid);st.ExecuteNonQuery();using var mv=cn.CreateCommand();mv.Transaction=tx;mv.CommandText="INSERT INTO stock_movements(product_id,movement_type,quantity,reference,user_id) VALUES($id,'RETURN',$q,$ref,$u)";mv.Parameters.AddWithValue("$id",x.pid);mv.Parameters.AddWithValue("$q",remaining);mv.Parameters.AddWithValue("$ref",$"CANCELACIÓN TICKET #{ticket}");mv.Parameters.AddWithValue("$u",userId);mv.ExecuteNonQuery();}using var ui=cn.CreateCommand();ui.Transaction=tx;ui.CommandText="UPDATE sale_items SET returned_quantity=quantity WHERE id=$id";ui.Parameters.AddWithValue("$id",x.id);ui.ExecuteNonQuery();}}

            var pays=new List<(string Method,double Amount)>();
            using(var q=cn.CreateCommand()){q.Transaction=tx;q.CommandText="SELECT method,COALESCE(SUM(amount),0) FROM payments WHERE sale_id=$sid AND status='APPROVED' GROUP BY method";q.Parameters.AddWithValue("$sid",saleId);using var r=q.ExecuteReader();while(r.Read())if(r.GetDouble(1)>0)pays.Add((r.GetString(0),r.GetDouble(1)));}
            foreach(var pay in pays){using var rp=cn.CreateCommand();rp.Transaction=tx;rp.CommandText="INSERT INTO payments(sale_id,method,amount,reference,status) VALUES($sid,$m,$a,$r,'APPROVED')";rp.Parameters.AddWithValue("$sid",saleId);rp.Parameters.AddWithValue("$m",pay.Method);rp.Parameters.AddWithValue("$a",-pay.Amount);rp.Parameters.AddWithValue("$r",$"CANCELACIÓN TICKET #{ticket}");rp.ExecuteNonQuery();if(pay.Method.StartsWith("CRÉDIT",StringComparison.OrdinalIgnoreCase)||pay.Method.StartsWith("CREDITO",StringComparison.OrdinalIgnoreCase)){using var ca=cn.CreateCommand();ca.Transaction=tx;ca.CommandText="INSERT INTO customer_accounts(customer_id,sale_id,entry_type,amount,concept,payment_method,user_id) VALUES($c,$sid,'PAYMENT',$a,$co,'CRÉDITO',$u)";ca.Parameters.AddWithValue("$c",customerId);ca.Parameters.AddWithValue("$sid",saleId);ca.Parameters.AddWithValue("$a",pay.Amount);ca.Parameters.AddWithValue("$co",$"Cancelación ticket #{ticket}");ca.Parameters.AddWithValue("$u",userId);ca.ExecuteNonQuery();}if(pay.Method.Equals("EFECTIVO",StringComparison.OrdinalIgnoreCase)){using var cm=cn.CreateCommand();cm.Transaction=tx;cm.CommandText="INSERT INTO cash_movements(session_id,user_id,movement_type,concept,amount,payment_method,reference_id) VALUES((SELECT session_id FROM sales WHERE id=$sid),$u,'SALE',$c,$a,'EFECTIVO',$sid)";cm.Parameters.AddWithValue("$sid",saleId);cm.Parameters.AddWithValue("$u",userId);cm.Parameters.AddWithValue("$c",$"Cancelación ticket #{ticket}");cm.Parameters.AddWithValue("$a",-pay.Amount);cm.ExecuteNonQuery();}}
            using(var up=cn.CreateCommand()){up.Transaction=tx;up.CommandText="UPDATE sales SET status='CANCELLED',notes=CASE WHEN COALESCE(notes,'')='' THEN $n ELSE notes || ' | CANCELADO: ' || $n END WHERE id=$id";up.Parameters.AddWithValue("$n",reason);up.Parameters.AddWithValue("$id",saleId);up.ExecuteNonQuery();}
            using(var au=cn.CreateCommand()){au.Transaction=tx;au.CommandText="INSERT INTO audit_log(user_id,action,module,details) VALUES($u,'MOBILE_TICKET_CANCEL','VENTAS',$d)";au.Parameters.AddWithValue("$u",userId);au.Parameters.AddWithValue("$d",$"Ticket #{ticket} cancelado desde FerrariPOS Manager. Motivo: {reason}");au.ExecuteNonQuery();}
            tx.Commit();
        }catch{tx.Rollback();throw;}
    }

}
