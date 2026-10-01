using FerrarisPOS.Data;
using FerrarisPOS.Models;

namespace FerrarisPOS.Services;
public static class FavoriteService
{
    public static List<Product> Get(){using var cn=Database.Open();using var cmd=cn.CreateCommand();cmd.CommandText="SELECT p.id,p.barcode,p.description,p.sale_price,p.wholesale_price,p.cost_price,p.stock,p.min_stock,p.category,p.unit,p.active,p.is_bulk,p.uses_inventory FROM favorite_products f JOIN products p ON p.id=f.product_id WHERE p.active=1 ORDER BY f.position,p.description";using var r=cmd.ExecuteReader();var list=new List<Product>();while(r.Read())list.Add(new(r.GetInt32(0),r.GetString(1),r.GetString(2),r.GetDouble(3),r.GetDouble(4),r.GetDouble(5),r.GetDouble(6),r.GetDouble(7),r.GetString(8),r.GetString(9),r.GetInt32(10)!=0,r.GetInt32(11)!=0,r.GetInt32(12)!=0));return list;}
    public static bool IsFavorite(int id){using var cn=Database.Open();using var cmd=cn.CreateCommand();cmd.CommandText="SELECT COUNT(*) FROM favorite_products WHERE product_id=$p";cmd.Parameters.AddWithValue("$p",id);return Convert.ToInt32(cmd.ExecuteScalar()??0)>0;}
    public static void Toggle(int id){using var cn=Database.Open();using var cmd=cn.CreateCommand();cmd.CommandText=IsFavorite(id)?"DELETE FROM favorite_products WHERE product_id=$p":"INSERT INTO favorite_products(product_id,position) VALUES($p,COALESCE((SELECT MAX(position)+1 FROM favorite_products),0))";cmd.Parameters.AddWithValue("$p",id);cmd.ExecuteNonQuery();}
}
