using FerrarisPOS.Data;
using FerrarisPOS.Models;
using System.Globalization;

namespace FerrarisPOS.Services;

public static class ProductService
{
    public static List<Product> Search(string text = "")
    {
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT id,barcode,description,sale_price,wholesale_price,cost_price,stock,min_stock,category,unit,active,is_bulk,uses_inventory FROM products WHERE active=1 AND barcode <> '__COMUN__' AND ($q='' OR barcode LIKE $like OR description LIKE $like OR category LIKE $like) ORDER BY description";
        cmd.Parameters.AddWithValue("$q", text.Trim());
        cmd.Parameters.AddWithValue("$like", $"%{text.Trim()}%");
        using var r = cmd.ExecuteReader();
        var list = new List<Product>();
        while (r.Read())
            list.Add(new Product(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetDouble(3), r.GetDouble(4), r.GetDouble(5), r.GetDouble(6), r.GetDouble(7), r.GetString(8), r.GetString(9), r.GetInt32(10) != 0, r.GetInt32(11) != 0, InventoryControlService.IsGlobalEnabled && r.GetInt32(12) != 0));
        return list;
    }

    // En la pantalla principal la búsqueda de venta es EXCLUSIVAMENTE por código de barras.
    // Búsqueda para edición desde Productos: solo por descripción y por prefijo.
    // Ej.: "a" devuelve productos cuya descripción comienza con A; "an" devuelve los que comienzan con AN.
    public static List<Product> SearchByDescriptionPrefix(string text = "")
    {
        text = text.Trim();
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT id,barcode,description,sale_price,wholesale_price,cost_price,stock,min_stock,category,unit,active,is_bulk,uses_inventory FROM products WHERE active=1 AND barcode <> '__COMUN__' AND ($q='' OR description LIKE $prefix) ORDER BY description COLLATE NOCASE";
        cmd.Parameters.AddWithValue("$q", text);
        cmd.Parameters.AddWithValue("$prefix", $"{text}%");
        using var r = cmd.ExecuteReader();
        var list = new List<Product>();
        while (r.Read())
            list.Add(new Product(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetDouble(3), r.GetDouble(4), r.GetDouble(5), r.GetDouble(6), r.GetDouble(7), r.GetString(8), r.GetString(9), r.GetInt32(10) != 0, r.GetInt32(11) != 0, InventoryControlService.IsGlobalEnabled && r.GetInt32(12) != 0));
        return list;
    }

    public static Product? FindByBarcode(string barcode)
    {
        barcode = barcode.Trim();
        if (barcode.Length == 0) return null;
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        // Primero buscamos el código principal del producto.
        // Si no existe, también aceptamos un código interno generado que esté
        // asociado a ese producto. Esto permite escanear cualquiera de los
        // códigos vinculados y llevar directamente el producto al inventario.
        cmd.CommandText = @"SELECT p.id,p.barcode,p.description,p.sale_price,p.wholesale_price,p.cost_price,p.stock,p.min_stock,p.category,p.unit,p.active,p.is_bulk,p.uses_inventory
                            FROM products p
                            WHERE p.active=1 AND p.barcode=$barcode AND p.barcode <> '__COMUN__'
                            UNION ALL
                            SELECT p.id,p.barcode,p.description,p.sale_price,p.wholesale_price,p.cost_price,p.stock,p.min_stock,p.category,p.unit,p.active,p.is_bulk,p.uses_inventory
                            FROM generated_barcodes gb
                            JOIN products p ON p.id=gb.product_id
                            WHERE p.active=1 AND gb.barcode=$barcode AND p.barcode <> '__COMUN__'
                            LIMIT 1";
        cmd.Parameters.AddWithValue("$barcode", barcode);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new Product(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetDouble(3), r.GetDouble(4), r.GetDouble(5), r.GetDouble(6), r.GetDouble(7), r.GetString(8), r.GetString(9), r.GetInt32(10) != 0, r.GetInt32(11) != 0, InventoryControlService.IsGlobalEnabled && r.GetInt32(12) != 0) : null;
    }


    public static List<Product> SearchWithoutBarcode(string text = "")
    {
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT id,barcode,description,sale_price,wholesale_price,cost_price,stock,min_stock,category,unit,active,is_bulk,uses_inventory FROM products WHERE active=1 AND barcode <> '__COMUN__' AND (TRIM(barcode)='' OR UPPER(TRIM(barcode)) IN ('0','SIN CODIGO','SIN CÓDIGO','S/C','SC','N/A','NA','-')) AND ($q='' OR description LIKE $like OR category LIKE $like) ORDER BY description";
        cmd.Parameters.AddWithValue("$q", text.Trim());
        cmd.Parameters.AddWithValue("$like", $"%{text.Trim()}%");
        using var r = cmd.ExecuteReader();
        var list = new List<Product>();
        while (r.Read())
            list.Add(new Product(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetDouble(3), r.GetDouble(4), r.GetDouble(5), r.GetDouble(6), r.GetDouble(7), r.GetString(8), r.GetString(9), r.GetInt32(10) != 0, r.GetInt32(11) != 0, InventoryControlService.IsGlobalEnabled && r.GetInt32(12) != 0));
        return list;
    }

    public static void AssignBarcode(int productId, string barcode)
    {
        barcode = barcode.Trim();
        if (productId <= 0) throw new ArgumentException("Producto inválido.");
        if (string.IsNullOrWhiteSpace(barcode)) throw new ArgumentException("El código de barras no puede estar vacío.");
        if (barcode == "__COMUN__") throw new ArgumentException("Ese código está reservado para PRODUCTO COMÚN.");

        using var cn = Database.Open();
        using var tx = cn.BeginTransaction();

        using (var find = cn.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText = "SELECT id,description FROM products WHERE barcode=$b LIMIT 1";
            find.Parameters.AddWithValue("$b", barcode);
            using var r = find.ExecuteReader();
            if (r.Read() && r.GetInt32(0) != productId)
                throw new InvalidOperationException($"El código de barras {barcode} ya está asignado al producto '{r.GetString(1)}'.");
        }

        try
        {
            using var update = cn.CreateCommand();
            update.Transaction = tx;
            update.CommandText = "UPDATE products SET barcode=$b,updated_at=CURRENT_TIMESTAMP WHERE id=$id AND active=1";
            update.Parameters.AddWithValue("$b", barcode);
            update.Parameters.AddWithValue("$id", productId);
            if (update.ExecuteNonQuery() != 1)
                throw new InvalidOperationException("No se encontró el producto o está inactivo.");
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException(
                $"El código de barras {barcode} ya está siendo utilizado por otro registro. No se modificó el producto.", ex);
        }

        tx.Commit();
        AuditService.Log(Session.UserId, "PRODUCT_BARCODE_ASSIGN", "INVENTARIO", $"Producto ID {productId} asignado al código {barcode}");
    }

    public static Product CommonProduct()
    {
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT id,barcode,description,sale_price,wholesale_price,cost_price,stock,min_stock,category,unit,active,is_bulk,uses_inventory FROM products WHERE barcode='__COMUN__' LIMIT 1";
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new InvalidOperationException("No está configurado el producto común.");
        return new Product(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetDouble(3), r.GetDouble(4), r.GetDouble(5), r.GetDouble(6), r.GetDouble(7), r.GetString(8), r.GetString(9), r.GetInt32(10) != 0, r.GetInt32(11) != 0, InventoryControlService.IsGlobalEnabled && r.GetInt32(12) != 0);
    }

    private static void ReleaseInactiveBarcodeConflicts(Microsoft.Data.Sqlite.SqliteConnection cn, string barcode)
    {
        var ids = new List<(int Id, string Description, string StoredBarcode)>();
        using (var q = cn.CreateCommand())
        {
            q.CommandText = @"SELECT id,description,barcode FROM products
                              WHERE active=0 AND TRIM(barcode)=TRIM($b)
                              ORDER BY id";
            q.Parameters.AddWithValue("$b", barcode);
            using var r = q.ExecuteReader();
            while (r.Read())
                ids.Add((r.GetInt32(0), r.GetString(1), r.GetString(2)));
        }

        foreach (var item in ids)
        {
            // Si tiene referencias históricas, NO hacemos DELETE físico.
            // Liberamos el barcode con un identificador de archivo único.
            var hasHistory = false;
            string[] checks =
            [
                "SELECT 1 FROM sale_items WHERE product_id=$id LIMIT 1",
                "SELECT 1 FROM sale_returns WHERE product_id=$id LIMIT 1",
                "SELECT 1 FROM stock_movements WHERE product_id=$id LIMIT 1",
                "SELECT 1 FROM waste_records WHERE product_id=$id LIMIT 1",
                "SELECT 1 FROM purchase_order_items WHERE product_id=$id LIMIT 1",
                "SELECT 1 FROM supplier_products WHERE product_id=$id LIMIT 1",
                "SELECT 1 FROM product_modifiers WHERE product_id=$id LIMIT 1",
                "SELECT 1 FROM recipes WHERE product_id=$id OR ingredient_product_id=$id LIMIT 1",
                "SELECT 1 FROM promotion_items WHERE product_id=$id LIMIT 1",
                "SELECT 1 FROM favorite_products WHERE product_id=$id LIMIT 1"
            ];
            foreach (var sql in checks)
            {
                try
                {
                    using var check = cn.CreateCommand();
                    check.CommandText = sql;
                    check.Parameters.AddWithValue("$id", item.Id);
                    if (check.ExecuteScalar() is not null) { hasHistory = true; break; }
                }
                catch (Microsoft.Data.Sqlite.SqliteException)
                {
                    // Si una tabla opcional no existe en una base antigua, no
                    // impedimos liberar el código: la migración de esquema la
                    // creará en el arranque.
                }
            }

            if (!hasHistory)
            {
                try
                {
                    using var del = cn.CreateCommand();
                    del.CommandText = "DELETE FROM products WHERE id=$id AND active=0";
                    del.Parameters.AddWithValue("$id", item.Id);
                    del.ExecuteNonQuery();
                    AuditService.Log(Session.UserId, "PRODUCT_INACTIVE_PURGE", "INVENTARIO",
                        $"Producto inactivo eliminado al reutilizar código {item.StoredBarcode}: ID {item.Id} - {item.Description}");
                    continue;
                }
                catch (Microsoft.Data.Sqlite.SqliteException)
                {
                    // Si SQLite no permite el DELETE por una referencia no prevista,
                    // hacemos el archivado seguro de abajo.
                }
            }

            var archivedBarcode = $"__ARCHIVO__{item.Id}_{Guid.NewGuid():N}";
            using var archive = cn.CreateCommand();
            archive.CommandText = "UPDATE products SET barcode=$b,updated_at=CURRENT_TIMESTAMP WHERE id=$id AND active=0";
            archive.Parameters.AddWithValue("$b", archivedBarcode);
            archive.Parameters.AddWithValue("$id", item.Id);
            archive.ExecuteNonQuery();
            AuditService.Log(Session.UserId, "PRODUCT_INACTIVE_ARCHIVE_BARCODE", "INVENTARIO",
                $"Código {item.StoredBarcode} liberado del producto inactivo ID {item.Id} - {item.Description}");
        }

        // También liberamos un código interno huérfano o asociado a un producto
        // inactivo. Un código interno activo de otro producto sigue siendo un
        // conflicto real y se informará más abajo.
        using (var gb = cn.CreateCommand())
        {
            gb.CommandText = @"SELECT gb.id, gb.product_id
                               FROM generated_barcodes gb
                               LEFT JOIN products p ON p.id=gb.product_id
                               WHERE TRIM(gb.barcode)=TRIM($b)
                                 AND (gb.product_id IS NULL OR p.active=0)";
            gb.Parameters.AddWithValue("$b", barcode);
            var idsToDelete = new List<int>();
            using var r = gb.ExecuteReader();
            while (r.Read()) idsToDelete.Add(r.GetInt32(0));
            r.Close();
            foreach (var gid in idsToDelete)
            {
                using var del = cn.CreateCommand();
                del.CommandText = "DELETE FROM generated_barcodes WHERE id=$id";
                del.Parameters.AddWithValue("$id", gid);
                del.ExecuteNonQuery();
            }
        }
    }

    public static bool GetAddsIva21(int productId)
    {
        if (productId <= 0) return false;
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(adds_iva_21,0) FROM products WHERE id=$id LIMIT 1";
        cmd.Parameters.AddWithValue("$id", productId);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) != 0;
    }

    public static void SetAddsIva21(int productId, bool enabled)
    {
        if (productId <= 0) return;
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "UPDATE products SET adds_iva_21=$v,iva_cost_migrated=1,updated_at=CURRENT_TIMESTAMP WHERE id=$id";
        cmd.Parameters.AddWithValue("$v", enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.ExecuteNonQuery();
    }

    public static bool GetRoundSaleTo5(int productId)
    {
        if (productId <= 0) return false;
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(round_sale_to_5,0) FROM products WHERE id=$id LIMIT 1";
        cmd.Parameters.AddWithValue("$id", productId);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) != 0;
    }

    public static void SetRoundSaleTo5(int productId, bool enabled)
    {
        if (productId <= 0) return;
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "UPDATE products SET round_sale_to_5=$v,updated_at=CURRENT_TIMESTAMP WHERE id=$id";
        cmd.Parameters.AddWithValue("$v", enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", productId);
        cmd.ExecuteNonQuery();
    }

    public static void Save(Product p, string stockReductionReason = "")
    {
        var barcode = (p.Barcode ?? string.Empty).Trim();
        var description = (p.Description ?? string.Empty).Trim();
        var category = CategoryService.Normalize(p.Category);

        if (string.IsNullOrWhiteSpace(barcode))
            throw new ProductSaveException("El código de barras es obligatorio.");

        if (string.Equals(barcode, "__COMUN__", StringComparison.Ordinal))
            throw new ProductSaveException("Ese código está reservado para PRODUCTO COMÚN.");

        if (string.IsNullOrWhiteSpace(description))
            throw new ProductSaveException("La descripción del producto es obligatoria.");

        using var cn = Database.Open();

        // Si el código pertenece únicamente a un producto INACTIVO, liberamos
        // ese código antes de crear el nuevo producto. Esto resuelve el caso
        // típico en el que el producto viejo no aparece en la grilla pero sigue
        // existiendo en la base y la restricción UNIQUE todavía lo bloquea.
        // Si el registro viejo tiene historial, no lo borramos físicamente:
        // conservamos el historial y le asignamos un código interno de archivo.
        if (p.Id == 0)
            ReleaseInactiveBarcodeConflicts(cn, barcode);

        // Validación previa: evita llegar al UNIQUE de SQLite y, por lo tanto,
        // evita que el usuario vea el error técnico "products.barcode".
        // Se compara con TRIM porque versiones anteriores podían haber guardado
        // accidentalmente espacios exteriores.
        using (var conflict = cn.CreateCommand())
        {
            conflict.CommandText = @"
                SELECT id, description, barcode
                FROM products
                WHERE TRIM(barcode)=TRIM($b)
                  AND ($id=0 OR id<>$id)
                ORDER BY id
                LIMIT 1";
            conflict.Parameters.AddWithValue("$b", barcode);
            conflict.Parameters.AddWithValue("$id", p.Id);

            using var r = conflict.ExecuteReader();
            if (r.Read())
            {
                var otherId = r.GetInt32(0);
                var otherDescription = r.GetString(1);
                throw new ProductSaveException(
                    $"El código de barras {barcode} ya está asignado al producto \"{otherDescription}\" (ID {otherId}).\n\n" +
                    "No se modificó el producto actual. Elegí otro código o buscá ese producto para editarlo.");
            }
        }

        // Un código interno generado tampoco debe convertirse en el código
        // principal de otro producto: de lo contrario el lector podría encontrar
        // dos destinos distintos para el mismo código.
        using (var generated = cn.CreateCommand())
        {
            generated.CommandText = @"
                SELECT gb.name
                FROM generated_barcodes gb
                WHERE TRIM(gb.barcode)=TRIM($b)
                  AND (gb.product_id IS NULL OR gb.product_id<>$id)
                LIMIT 1";
            generated.Parameters.AddWithValue("$b", barcode);
            generated.Parameters.AddWithValue("$id", p.Id);

            var generatedName = generated.ExecuteScalar();
            if (generatedName is not null && generatedName != DBNull.Value)
            {
                throw new ProductSaveException(
                    $"El código {barcode} ya está registrado como código interno de \"{generatedName}\".\n\n" +
                    "Usá otro código o asigná ese código interno al producto correspondiente.");
            }
        }

        // Las categorías son persistentes. Si el usuario escribió una categoría
        // manualmente, también queda registrada para poder reutilizarla después.
        if (!string.IsNullOrWhiteSpace(category))
            category = CategoryService.Ensure(category);

        using var cmd = cn.CreateCommand();
        cmd.CommandText = p.Id == 0
            ? "INSERT INTO products(barcode,description,sale_price,wholesale_price,cost_price,stock,min_stock,category,unit,active,is_bulk,uses_inventory) VALUES($b,$d,$s,$w,$c,$st,$m,$cat,$u,1,$bulk,$inv)"
            : "UPDATE products SET barcode=$b,description=$d,sale_price=$s,wholesale_price=$w,cost_price=$c,stock=$st,min_stock=$m,category=$cat,unit=$u,is_bulk=$bulk,uses_inventory=$inv,updated_at=CURRENT_TIMESTAMP WHERE id=$id";

        cmd.Parameters.AddWithValue("$b", barcode);
        cmd.Parameters.AddWithValue("$d", description);
        cmd.Parameters.AddWithValue("$s", p.SalePrice);
        cmd.Parameters.AddWithValue("$w", p.WholesalePrice);
        cmd.Parameters.AddWithValue("$c", p.CostPrice);

        double stockToSave = p.Stock;
        double previousStock = p.Stock;
        double previousSalePrice = p.SalePrice;
        if (p.Id != 0)
        {
            using var current = cn.CreateCommand();
            current.CommandText = "SELECT stock,sale_price FROM products WHERE id=$id AND active=1 LIMIT 1";
            current.Parameters.AddWithValue("$id", p.Id);
            using var currentReader = current.ExecuteReader();
            if (!currentReader.Read()) throw new ProductSaveException("El producto seleccionado no existe o está inactivo.");
            previousStock = currentReader.GetDouble(0);
            previousSalePrice = currentReader.GetDouble(1);
        }
        if (!InventoryControlService.IsGlobalEnabled && p.Id != 0)
        {
            stockToSave = previousStock;
        }
        if (p.Id != 0 && stockToSave < previousStock - 0.0000001 && string.IsNullOrWhiteSpace(stockReductionReason))
            throw new ProductSaveException("La reducción de stock requiere un motivo.");
        stockReductionReason = (stockReductionReason ?? string.Empty).Trim();

        if (!p.UsesInventory)
        {
            stockToSave = 0d;
        }
        cmd.Parameters.AddWithValue("$st", stockToSave);
        cmd.Parameters.AddWithValue("$m", p.UsesInventory ? p.MinStock : 0d);
        cmd.Parameters.AddWithValue("$cat", category);
        cmd.Parameters.AddWithValue("$u", string.IsNullOrWhiteSpace(p.Unit) ? "UN" : p.Unit.Trim());
        cmd.Parameters.AddWithValue("$bulk", p.IsBulk ? 1 : 0);

        bool storedUsesInventory = p.UsesInventory;
        if (!InventoryControlService.IsGlobalEnabled)
        {
            if (p.Id == 0)
                storedUsesInventory = false;
            else
            {
                using var stored = cn.CreateCommand();
                stored.CommandText = "SELECT COALESCE(uses_inventory,1) FROM products WHERE id=$id LIMIT 1";
                stored.Parameters.AddWithValue("$id", p.Id);
                storedUsesInventory = Convert.ToInt32(stored.ExecuteScalar() ?? 1) != 0;
            }
        }

        cmd.Parameters.AddWithValue("$inv", storedUsesInventory ? 1 : 0);
        if (p.Id != 0)
            cmd.Parameters.AddWithValue("$id", p.Id);

        try
        {
            cmd.ExecuteNonQuery();
            if (p.Id != 0 && stockToSave < previousStock - 0.0000001)
            {
                using var movement = cn.CreateCommand();
                movement.CommandText = "INSERT INTO stock_movements(product_id,movement_type,quantity,reference,user_id) VALUES($p,'ADJUSTMENT_OUT',$q,$r,$u)";
                movement.Parameters.AddWithValue("$p", p.Id);
                movement.Parameters.AddWithValue("$q", previousStock - stockToSave);
                movement.Parameters.AddWithValue("$r", stockReductionReason);
                movement.Parameters.AddWithValue("$u", Session.UserId);
                movement.ExecuteNonQuery();
            }
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex)
            when (ex.SqliteErrorCode == 19)
        {
            // Protección final contra una condición de carrera: otra instancia
            // pudo guardar el mismo código justo después de nuestra validación.
            throw new ProductSaveException(
                $"No se pudo guardar el producto porque el código {barcode} acaba de ser utilizado por otro producto.\n\n" +
                "No se perdió la información. Elegí otro código y volvé a guardar.", ex);
        }

        AuditService.Log(
            Session.UserId,
            p.Id == 0 ? "PRODUCT_CREATE" : "PRODUCT_UPDATE",
            "INVENTARIO",
            $"{barcode} - {description} - Stock {p.Stock:N3} - Precio ${p.SalePrice:N2}");
    }

    public sealed class ProductSaveException : Exception
    {
        public ProductSaveException(string message) : base(message) { }
        public ProductSaveException(string message, Exception innerException) : base(message, innerException) { }
    }

    public static void AdjustStock(int productId, double quantity, string type, string reference, int userId)
    {
        InventoryControlService.RequireEnabled("un ajuste de stock");
        if (quantity == 0) return;
        reference = reference?.Trim() ?? "";
        if (quantity < 0 && string.IsNullOrWhiteSpace(reference))
            throw new InvalidOperationException("Toda salida negativa de inventario debe tener un motivo registrado.");
        if (userId <= 0)
            throw new InvalidOperationException("No hay un cajero/usuario activo para registrar el movimiento.");
        using var cn = Database.Open();
        using var tx = cn.BeginTransaction();
        using var q = cn.CreateCommand();
        q.Transaction = tx;
        q.CommandText = "UPDATE products SET stock=stock+$q,updated_at=CURRENT_TIMESTAMP WHERE id=$id AND active=1";
        q.Parameters.AddWithValue("$q", quantity);
        q.Parameters.AddWithValue("$id", productId);
        if (q.ExecuteNonQuery() != 1) throw new InvalidOperationException("No se encontró el producto.");
        using var m = cn.CreateCommand();
        m.Transaction = tx;
        m.CommandText = "INSERT INTO stock_movements(product_id,movement_type,quantity,reference,user_id) VALUES($p,$t,$q,$r,$u)";
        m.Parameters.AddWithValue("$p", productId); m.Parameters.AddWithValue("$t", type); m.Parameters.AddWithValue("$q", Math.Abs(quantity)); m.Parameters.AddWithValue("$r", reference); m.Parameters.AddWithValue("$u", userId);
        m.ExecuteNonQuery();
        tx.Commit();
    }

    public static void Delete(int id) => Delete(id, "Desactivación solicitada desde gestión de productos.", Session.UserId);

    public static void Delete(int id, string reason, int userId)
    {
        reason = (reason ?? "").Trim();
        if (reason.Length == 0) reason = "Eliminado desde gestión de productos.";
        using var cn = Database.Open();
        using var tx = cn.BeginTransaction();
        string barcode = "", description = ""; double stock = 0;
        using (var find = cn.CreateCommand())
        {
            find.Transaction = tx;
            find.CommandText = "SELECT barcode,description,stock FROM products WHERE id=$id AND active=1";
            find.Parameters.AddWithValue("$id", id);
            using var r = find.ExecuteReader();
            if (!r.Read()) throw new InvalidOperationException("El producto seleccionado no existe o ya fue eliminado.");
            barcode = r.IsDBNull(0) ? "" : r.GetString(0);
            description = r.IsDBNull(1) ? "Producto" : r.GetString(1);
            stock = r.IsDBNull(2) ? 0 : r.GetDouble(2);
        }
        using (var log = cn.CreateCommand())
        {
            log.Transaction = tx;
            log.CommandText = "INSERT INTO product_deletion_log(product_id,product_barcode,product_name,stock_before,reason,user_id) VALUES($id,$b,$n,$s,$r,$u)";
            log.Parameters.AddWithValue("$id", id); log.Parameters.AddWithValue("$b", barcode); log.Parameters.AddWithValue("$n", description);
            log.Parameters.AddWithValue("$s", stock); log.Parameters.AddWithValue("$r", reason); log.Parameters.AddWithValue("$u", userId); log.ExecuteNonQuery();
        }
        using (var cmd = cn.CreateCommand())
        {
            cmd.Transaction = tx; cmd.CommandText = "UPDATE products SET active=0,updated_at=CURRENT_TIMESTAMP WHERE id=$id AND active=1"; cmd.Parameters.AddWithValue("$id", id);
            if (cmd.ExecuteNonQuery() != 1) throw new InvalidOperationException("No se pudo eliminar el producto seleccionado.");
        }
        tx.Commit();
        AuditService.Log(userId, "PRODUCT_DELETE", "INVENTARIO", $"Producto {description} · Stock anterior {stock:N3} · Motivo: {reason}");
    }
}
