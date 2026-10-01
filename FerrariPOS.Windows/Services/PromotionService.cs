using FerrarisPOS.Data;
using FerrarisPOS.Models;

namespace FerrarisPOS.Services;

public sealed record PromotionComponent(int ProductId, string Description, string Barcode, double Quantity, double Stock, double SalePrice, bool UsesInventory);
public sealed record PromotionDefinition(long Id, string Name, string Description, double PromotionPrice, List<PromotionComponent> Items);

public static class PromotionService
{
    public static List<PromotionDefinition> GetActiveBundles()
    {
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = @"SELECT id,name,COALESCE(description,''),CASE WHEN COALESCE(promotion_price,0) > 0 THEN promotion_price ELSE COALESCE(amount,0) END
                            FROM promotions
                            WHERE active=1 AND type='PACK'
                              AND (start_at IS NULL OR start_at='' OR datetime(start_at)<=datetime('now'))
                              AND (end_at IS NULL OR end_at='' OR datetime(end_at)>=datetime('now'))
                            ORDER BY name";
        var rows = new List<(long Id, string Name, string Description, double Price)>();
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read()) rows.Add((r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetDouble(3)));
        }
        var result = new List<PromotionDefinition>();
        foreach (var row in rows) result.Add(new PromotionDefinition(row.Id, row.Name, row.Description, row.Price, LoadItems(cn, row.Id)));
        return result.Where(x => x.Items.Count > 0).ToList();
    }


    public static PromotionDefinition? GetById(long promotionId, bool onlyActive = false)
    {
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = @"SELECT id,name,COALESCE(description,''),
                                   CASE WHEN COALESCE(promotion_price,0) > 0 THEN promotion_price ELSE COALESCE(amount,0) END,
                                   COALESCE(active,1),start_at,end_at
                            FROM promotions WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", promotionId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        var id = r.GetInt64(0);
        var name = r.GetString(1);
        var description = r.GetString(2);
        var price = r.GetDouble(3);
        var active = r.GetInt32(4) != 0;
        DateTime? start = null;
        DateTime? end = null;
        if (!r.IsDBNull(5) && DateTime.TryParse(r.GetString(5), out var sd)) start = sd;
        if (!r.IsDBNull(6) && DateTime.TryParse(r.GetString(6), out var ed)) end = ed;
        r.Close();

        if (onlyActive && (!active || (start.HasValue && start.Value > DateTime.Now) || (end.HasValue && end.Value < DateTime.Now)))
            return null;

        return new PromotionDefinition(id, name, description, price, LoadItems(cn, promotionId));
    }

    public static List<PromotionComponent> LoadItems(Microsoft.Data.Sqlite.SqliteConnection cn, long promotionId)
    {
        using var cmd = cn.CreateCommand();
        cmd.CommandText = @"SELECT pi.product_id,p.description,p.barcode,pi.quantity,p.stock,p.sale_price,COALESCE(p.uses_inventory,1)
                            FROM promotion_items pi
                            JOIN products p ON p.id=pi.product_id
                            WHERE pi.promotion_id=$id AND p.active=1 ORDER BY p.description";
        cmd.Parameters.AddWithValue("$id", promotionId);
        using var r = cmd.ExecuteReader();
        var list = new List<PromotionComponent>();
        while (r.Read())
            list.Add(new PromotionComponent(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetDouble(3), r.GetDouble(4), r.GetDouble(5), InventoryControlService.IsGlobalEnabled && r.GetInt32(6) != 0));
        return list;
    }

    public static List<CartItem> BuildCartItems(PromotionDefinition promotion)
    {
        if (promotion.Items.Count == 0) throw new InvalidOperationException("La promoción no tiene productos.");
        var regularTotal = promotion.Items.Sum(x => x.SalePrice * x.Quantity);
        if (promotion.PromotionPrice <= 0) throw new InvalidOperationException("La promoción no tiene un precio promocional válido.");
        if (regularTotal <= 0) throw new InvalidOperationException("Los productos de la promoción no tienen precios de venta válidos.");

        foreach (var item in promotion.Items)
            if (item.UsesInventory && item.Quantity > item.Stock + 0.000001)
                throw new InvalidOperationException($"Stock insuficiente para {item.Description}. Disponible: {item.Stock:N3}.");

        // El precio guardado por el usuario es el precio FINAL del combo.
        // Antes se guardaba UnitPrice ya rebajado y además DiscountAmount, provocando
        // que el descuento se aplicara dos veces al insertar la promo en la venta.
        // Ahora UnitPrice conserva el precio normal y DiscountAmount representa solo
        // la rebaja, por lo que CartItem.Total coincide exactamente con PromotionPrice.
        var factor = promotion.PromotionPrice / regularTotal;
        var cart = new List<CartItem>();
        var runningTotal = 0d;
        for (int i = 0; i < promotion.Items.Count; i++)
        {
            var item = promotion.Items[i];
            var targetTotal = Math.Round(item.SalePrice * item.Quantity * factor, 2, MidpointRounding.AwayFromZero);
            if (i == promotion.Items.Count - 1)
                targetTotal = Math.Round(promotion.PromotionPrice - runningTotal, 2, MidpointRounding.AwayFromZero);
            targetTotal = Math.Max(0, targetTotal);

            var discount = Math.Max(0, Math.Round(item.SalePrice * item.Quantity - targetTotal, 2, MidpointRounding.AwayFromZero));
            var actualTotal = Math.Max(0, Math.Round(item.SalePrice * item.Quantity - discount, 2, MidpointRounding.AwayFromZero));
            runningTotal += actualTotal;

            cart.Add(new CartItem
            {
                ProductId = item.ProductId,
                Barcode = item.Barcode,
                Description = $"{item.Description} · PROMO {promotion.Name}",
                UnitPrice = item.SalePrice,
                RetailPrice = item.SalePrice,
                WholesalePrice = item.SalePrice,
                Quantity = item.Quantity,
                Stock = item.Stock,
                IsCommon = false,
                IsWholesale = false,
                IsBulk = false,
                UsesInventory = item.UsesInventory,
                DiscountAmount = discount,
                Modifiers = ""
            });
        }

        // Corrige cualquier centavo de redondeo sobre el último producto para que
        // el total del carrito sea exactamente el precio promocional configurado.
        var difference = Math.Round(promotion.PromotionPrice - cart.Sum(x => x.Total), 2, MidpointRounding.AwayFromZero);
        if (Math.Abs(difference) >= 0.01 && cart.Count > 0)
        {
            var last = cart[^1];
            last.DiscountAmount = Math.Max(0, last.DiscountAmount - difference);
        }
        return cart;
    }

}
