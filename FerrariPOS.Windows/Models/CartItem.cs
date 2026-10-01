namespace FerrarisPOS.Models;
public class CartItem
{
    public int ProductId { get; set; }
    public string Barcode { get; set; } = "";
    public string Description { get; set; } = "";
    public double UnitPrice { get; set; }
    public double RetailPrice { get; set; }
    public double WholesalePrice { get; set; }
    // Coste unitario conservado al reconstruir ventas/mesas desde Android.
    public double CostPrice { get; set; }
    public double Quantity { get; set; }
    public double Stock { get; set; }
    public bool IsCommon { get; set; }
    public bool IsWholesale { get; set; }
    public bool IsBulk { get; set; }
    public bool UsesInventory { get; set; } = true;
    public double DiscountAmount { get; set; }
    public string Modifiers { get; set; } = "";
    public double GrossTotal => UnitPrice * Quantity;
    public double Total => Math.Max(0, GrossTotal - DiscountAmount);
    public double DiscountPercent => GrossTotal <= 0 ? 0 : DiscountAmount * 100.0 / GrossTotal;
}
