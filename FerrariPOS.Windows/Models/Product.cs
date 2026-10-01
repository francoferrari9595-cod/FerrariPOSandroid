namespace FerrarisPOS.Models;
public record Product(int Id, string Barcode, string Description, double SalePrice, double WholesalePrice, double CostPrice, double Stock, double MinStock, string Category, string Unit, bool Active, bool IsBulk = false, bool UsesInventory = true);
