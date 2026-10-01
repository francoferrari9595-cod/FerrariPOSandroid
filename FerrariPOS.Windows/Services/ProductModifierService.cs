using FerrarisPOS.Data;

namespace FerrarisPOS.Services;

public sealed record ProductModifier(int Id,int ProductId,string Name,double PriceDelta,bool Active);
public static class ProductModifierService
{
    public static List<ProductModifier> Get(int productId){using var cn=Database.Open();using var cmd=cn.CreateCommand();cmd.CommandText="SELECT id,product_id,name,price_delta,active FROM product_modifiers WHERE product_id=$p AND active=1 ORDER BY name";cmd.Parameters.AddWithValue("$p",productId);using var r=cmd.ExecuteReader();var list=new List<ProductModifier>();while(r.Read())list.Add(new(r.GetInt32(0),r.GetInt32(1),r.GetString(2),r.GetDouble(3),r.GetInt32(4)!=0));return list;}
    public static void Save(int id,int productId,string name,double delta){using var cn=Database.Open();using var cmd=cn.CreateCommand();cmd.CommandText=id==0?"INSERT INTO product_modifiers(product_id,name,price_delta,active) VALUES($p,$n,$d,1)":"UPDATE product_modifiers SET name=$n,price_delta=$d WHERE id=$id";cmd.Parameters.AddWithValue("$p",productId);cmd.Parameters.AddWithValue("$n",name.Trim());cmd.Parameters.AddWithValue("$d",delta);if(id>0)cmd.Parameters.AddWithValue("$id",id);cmd.ExecuteNonQuery();}
    public static void Delete(int id){using var cn=Database.Open();using var cmd=cn.CreateCommand();cmd.CommandText="UPDATE product_modifiers SET active=0 WHERE id=$id";cmd.Parameters.AddWithValue("$id",id);cmd.ExecuteNonQuery();}
}
