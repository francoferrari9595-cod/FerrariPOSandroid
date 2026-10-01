using Microsoft.Data.Sqlite;
using System.Globalization;

namespace FerrarisPOS.Data;

public static class Database
{
    private static readonly string DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FerrarisPOS");
    public static string DbPath => Path.Combine(DataDirectory, "FerrarisPOS.db");
    public static string BackupDirectory => Path.Combine(DataDirectory, "Backups");
    public static string ConnectionString => $"Data Source={DbPath};Foreign Keys=True;Mode=ReadWriteCreate;";

    public static void Initialize()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(BackupDirectory);
        using var cn = Open();
        using (var pragma = cn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys=ON;";
            pragma.ExecuteNonQuery();
        }
        EnsureSchema(cn);
        EnsureLicense(cn);
    }

    public static SqliteConnection Open()
    {
        var cn = new SqliteConnection(ConnectionString);
        cn.Open();
        // Android y Windows leen/escriben SQLite al mismo tiempo. WAL permite
        // lecturas concurrentes y busy_timeout evita que una contención puntual
        // se convierta en una pantalla congelada o en "database is locked".
        using (var pragma = cn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA busy_timeout=5000; PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";
            pragma.ExecuteNonQuery();
        }
        return cn;
    }

    private static void EnsureSchema(SqliteConnection cn)
    {
        string[] sql =
        [
            "CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT NOT NULL DEFAULT '')",
            "CREATE TABLE IF NOT EXISTS users(id INTEGER PRIMARY KEY AUTOINCREMENT, username TEXT NOT NULL UNIQUE, full_name TEXT NOT NULL, password_hash TEXT NOT NULL, role TEXT NOT NULL DEFAULT 'CAJERO', permissions TEXT NOT NULL DEFAULT '', active INTEGER NOT NULL DEFAULT 1, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP)",
            "CREATE TABLE IF NOT EXISTS products(id INTEGER PRIMARY KEY AUTOINCREMENT, barcode TEXT NOT NULL UNIQUE, description TEXT NOT NULL, sale_price REAL NOT NULL DEFAULT 0, wholesale_price REAL NOT NULL DEFAULT 0, cost_price REAL NOT NULL DEFAULT 0, stock REAL NOT NULL DEFAULT 0, min_stock REAL NOT NULL DEFAULT 0, category TEXT NOT NULL DEFAULT '', unit TEXT NOT NULL DEFAULT 'UN', is_bulk INTEGER NOT NULL DEFAULT 0, active INTEGER NOT NULL DEFAULT 1, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP)",
            "CREATE TABLE IF NOT EXISTS product_deletion_log(id INTEGER PRIMARY KEY AUTOINCREMENT,product_id INTEGER NOT NULL,product_barcode TEXT NOT NULL DEFAULT '',product_name TEXT NOT NULL,stock_before REAL NOT NULL DEFAULT 0,reason TEXT NOT NULL,user_id INTEGER,created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,FOREIGN KEY(product_id) REFERENCES products(id),FOREIGN KEY(user_id) REFERENCES users(id))",
            "CREATE TABLE IF NOT EXISTS price_change_log(id INTEGER PRIMARY KEY AUTOINCREMENT,product_id INTEGER NOT NULL,product_barcode TEXT NOT NULL DEFAULT '',product_name TEXT NOT NULL,old_price REAL NOT NULL DEFAULT 0,new_price REAL NOT NULL DEFAULT 0,reason TEXT NOT NULL,user_id INTEGER,created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,FOREIGN KEY(product_id) REFERENCES products(id),FOREIGN KEY(user_id) REFERENCES users(id))",
            "CREATE TABLE IF NOT EXISTS price_sale_blocks(product_id INTEGER PRIMARY KEY,blocked_until_utc TEXT NOT NULL,old_price REAL NOT NULL DEFAULT 0,new_price REAL NOT NULL DEFAULT 0,created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,FOREIGN KEY(product_id) REFERENCES products(id) ON DELETE CASCADE)",
            "CREATE TABLE IF NOT EXISTS supplier_deletion_log(id INTEGER PRIMARY KEY AUTOINCREMENT,supplier_id INTEGER NOT NULL,supplier_name TEXT NOT NULL,reason TEXT NOT NULL,user_id INTEGER,created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,FOREIGN KEY(supplier_id) REFERENCES suppliers(id),FOREIGN KEY(user_id) REFERENCES users(id))",
            "CREATE TABLE IF NOT EXISTS product_categories(id INTEGER PRIMARY KEY AUTOINCREMENT,name TEXT NOT NULL COLLATE NOCASE UNIQUE,active INTEGER NOT NULL DEFAULT 1,created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP)",
            "CREATE INDEX IF NOT EXISTS idx_product_categories_name ON product_categories(name COLLATE NOCASE)",
            "CREATE TABLE IF NOT EXISTS generated_barcodes(id INTEGER PRIMARY KEY AUTOINCREMENT, barcode TEXT NOT NULL UNIQUE, name TEXT NOT NULL, product_id INTEGER, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, FOREIGN KEY(product_id) REFERENCES products(id) ON DELETE SET NULL)",
            "CREATE INDEX IF NOT EXISTS idx_generated_barcodes_barcode ON generated_barcodes(barcode)",
            "CREATE INDEX IF NOT EXISTS idx_generated_barcodes_product ON generated_barcodes(product_id)",
            "CREATE TABLE IF NOT EXISTS customers(id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, document TEXT NOT NULL DEFAULT '', phone TEXT NOT NULL DEFAULT '', email TEXT NOT NULL DEFAULT '', address TEXT NOT NULL DEFAULT '', credit_limit REAL NOT NULL DEFAULT 0, active INTEGER NOT NULL DEFAULT 1, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP)",
            "CREATE TABLE IF NOT EXISTS suppliers(id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, document TEXT NOT NULL DEFAULT '', phone TEXT NOT NULL DEFAULT '', email TEXT NOT NULL DEFAULT '', address TEXT NOT NULL DEFAULT '', active INTEGER NOT NULL DEFAULT 1, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP)",
            "CREATE TABLE IF NOT EXISTS supplier_products(id INTEGER PRIMARY KEY AUTOINCREMENT, supplier_id INTEGER NOT NULL, product_id INTEGER NOT NULL, unit_cost REAL NOT NULL DEFAULT 0, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, UNIQUE(supplier_id,product_id), FOREIGN KEY(supplier_id) REFERENCES suppliers(id) ON DELETE CASCADE, FOREIGN KEY(product_id) REFERENCES products(id) ON DELETE CASCADE)",
            "CREATE INDEX IF NOT EXISTS idx_supplier_products_supplier ON supplier_products(supplier_id)",
            "CREATE INDEX IF NOT EXISTS idx_supplier_products_product ON supplier_products(product_id)",
            "CREATE TABLE IF NOT EXISTS sales(id INTEGER PRIMARY KEY AUTOINCREMENT, ticket_no INTEGER NOT NULL UNIQUE, user_id INTEGER, customer_id INTEGER, session_id INTEGER, subtotal REAL NOT NULL DEFAULT 0, discount REAL NOT NULL DEFAULT 0, tax REAL NOT NULL DEFAULT 0, total REAL NOT NULL DEFAULT 0, payment_method TEXT NOT NULL DEFAULT 'EFECTIVO', amount_received REAL NOT NULL DEFAULT 0, change_amount REAL NOT NULL DEFAULT 0, status TEXT NOT NULL DEFAULT 'COMPLETED', sale_channel TEXT NOT NULL DEFAULT 'SALÓN', notes TEXT NOT NULL DEFAULT '', created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, FOREIGN KEY(user_id) REFERENCES users(id), FOREIGN KEY(customer_id) REFERENCES customers(id), FOREIGN KEY(session_id) REFERENCES cash_sessions(id))",
            "CREATE TABLE IF NOT EXISTS sale_items(id INTEGER PRIMARY KEY AUTOINCREMENT, sale_id INTEGER NOT NULL, product_id INTEGER NOT NULL, barcode TEXT NOT NULL, description TEXT NOT NULL, quantity REAL NOT NULL DEFAULT 1, unit_price REAL NOT NULL DEFAULT 0, discount REAL NOT NULL DEFAULT 0, total REAL NOT NULL DEFAULT 0, FOREIGN KEY(sale_id) REFERENCES sales(id) ON DELETE CASCADE, FOREIGN KEY(product_id) REFERENCES products(id))",
            "CREATE TABLE IF NOT EXISTS sale_returns(id INTEGER PRIMARY KEY AUTOINCREMENT, sale_id INTEGER NOT NULL, sale_item_id INTEGER NOT NULL, product_id INTEGER NOT NULL, quantity REAL NOT NULL DEFAULT 0, amount REAL NOT NULL DEFAULT 0, user_id INTEGER, reason TEXT NOT NULL DEFAULT 'DEVOLUCIÓN', created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, FOREIGN KEY(sale_id) REFERENCES sales(id), FOREIGN KEY(sale_item_id) REFERENCES sale_items(id), FOREIGN KEY(product_id) REFERENCES products(id), FOREIGN KEY(user_id) REFERENCES users(id))",
            "CREATE TABLE IF NOT EXISTS payments(id INTEGER PRIMARY KEY AUTOINCREMENT, sale_id INTEGER NOT NULL, method TEXT NOT NULL, amount REAL NOT NULL DEFAULT 0, reference TEXT NOT NULL DEFAULT '', status TEXT NOT NULL DEFAULT 'APPROVED', created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, FOREIGN KEY(sale_id) REFERENCES sales(id) ON DELETE CASCADE)",
            "CREATE TABLE IF NOT EXISTS cash_sessions(id INTEGER PRIMARY KEY AUTOINCREMENT, user_id INTEGER, opened_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, closed_at TEXT, opening_amount REAL NOT NULL DEFAULT 0, closing_amount REAL, expected_amount REAL, difference REAL, mercado_pago_enabled INTEGER NOT NULL DEFAULT 0, mercado_pago_opening_amount REAL NOT NULL DEFAULT 0, mercado_pago_retention_percent REAL NOT NULL DEFAULT 0, mercado_pago_closing_amount REAL, mercado_pago_expected_amount REAL, mercado_pago_difference REAL, status TEXT NOT NULL DEFAULT 'OPEN', FOREIGN KEY(user_id) REFERENCES users(id))",
            "CREATE TABLE IF NOT EXISTS cash_movements(id INTEGER PRIMARY KEY AUTOINCREMENT, session_id INTEGER, user_id INTEGER, movement_type TEXT NOT NULL, concept TEXT NOT NULL, amount REAL NOT NULL DEFAULT 0, payment_method TEXT NOT NULL DEFAULT 'EFECTIVO', reference_id INTEGER, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, FOREIGN KEY(session_id) REFERENCES cash_sessions(id), FOREIGN KEY(user_id) REFERENCES users(id))",
            "CREATE TABLE IF NOT EXISTS customer_accounts(id INTEGER PRIMARY KEY AUTOINCREMENT, customer_id INTEGER NOT NULL, sale_id INTEGER, entry_type TEXT NOT NULL, amount REAL NOT NULL DEFAULT 0, concept TEXT NOT NULL DEFAULT '', payment_method TEXT NOT NULL DEFAULT '', user_id INTEGER, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, FOREIGN KEY(customer_id) REFERENCES customers(id), FOREIGN KEY(sale_id) REFERENCES sales(id), FOREIGN KEY(user_id) REFERENCES users(id))",
            "CREATE TABLE IF NOT EXISTS stock_movements(id INTEGER PRIMARY KEY AUTOINCREMENT, product_id INTEGER NOT NULL, movement_type TEXT NOT NULL, quantity REAL NOT NULL DEFAULT 0, reference TEXT NOT NULL DEFAULT '', user_id INTEGER, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, FOREIGN KEY(product_id) REFERENCES products(id), FOREIGN KEY(user_id) REFERENCES users(id))",
            "CREATE TABLE IF NOT EXISTS license(id INTEGER PRIMARY KEY CHECK(id=1), license_key TEXT NOT NULL DEFAULT '', installed_at TEXT NOT NULL, expires_at TEXT NOT NULL, status TEXT NOT NULL DEFAULT 'TRIAL')",
            "CREATE TABLE IF NOT EXISTS audit_log(id INTEGER PRIMARY KEY AUTOINCREMENT, user_id INTEGER, action TEXT NOT NULL, module TEXT NOT NULL, details TEXT NOT NULL DEFAULT '', created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, FOREIGN KEY(user_id) REFERENCES users(id))",
            "CREATE TABLE IF NOT EXISTS customer_deletion_log(id INTEGER PRIMARY KEY AUTOINCREMENT, customer_id INTEGER NOT NULL, customer_name TEXT NOT NULL, customer_document TEXT NOT NULL DEFAULT '', reason TEXT NOT NULL, user_id INTEGER, created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP, FOREIGN KEY(customer_id) REFERENCES customers(id), FOREIGN KEY(user_id) REFERENCES users(id))",
            "CREATE INDEX IF NOT EXISTS idx_products_barcode ON products(barcode)",
            "CREATE INDEX IF NOT EXISTS idx_sales_created ON sales(created_at)",
            "CREATE INDEX IF NOT EXISTS idx_sale_items_sale ON sale_items(sale_id)",
            "CREATE INDEX IF NOT EXISTS idx_sale_returns_sale ON sale_returns(sale_id)",
            "CREATE INDEX IF NOT EXISTS idx_cash_movements_session ON cash_movements(session_id)",
            "CREATE INDEX IF NOT EXISTS idx_customer_accounts_customer ON customer_accounts(customer_id)"
            ,"CREATE TABLE IF NOT EXISTS salons(id INTEGER PRIMARY KEY AUTOINCREMENT,name TEXT NOT NULL UNIQUE,description TEXT NOT NULL DEFAULT '',active INTEGER NOT NULL DEFAULT 1,created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP)",
            "CREATE TABLE IF NOT EXISTS restaurant_tables(id INTEGER PRIMARY KEY AUTOINCREMENT,name TEXT NOT NULL,x INTEGER NOT NULL DEFAULT 20,y INTEGER NOT NULL DEFAULT 20,width INTEGER NOT NULL DEFAULT 120,height INTEGER NOT NULL DEFAULT 80,enabled INTEGER NOT NULL DEFAULT 1,salon_id INTEGER NOT NULL DEFAULT 1,shape TEXT NOT NULL DEFAULT 'RECTANGLE',rotation REAL NOT NULL DEFAULT 0,capacity INTEGER NOT NULL DEFAULT 4,color TEXT NOT NULL DEFAULT '')"
            ,"CREATE TABLE IF NOT EXISTS salon_decorations(id INTEGER PRIMARY KEY AUTOINCREMENT,type TEXT NOT NULL,name TEXT NOT NULL,x INTEGER NOT NULL DEFAULT 20,y INTEGER NOT NULL DEFAULT 20,width INTEGER NOT NULL DEFAULT 90,height INTEGER NOT NULL DEFAULT 60,salon_id INTEGER NOT NULL DEFAULT 1)"
            ,"CREATE TABLE IF NOT EXISTS purchase_orders(id INTEGER PRIMARY KEY AUTOINCREMENT,order_no TEXT NOT NULL UNIQUE,supplier_id INTEGER NOT NULL,status TEXT NOT NULL DEFAULT 'DRAFT',order_date TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,expected_date TEXT,received_date TEXT,total REAL NOT NULL DEFAULT 0,received_total REAL NOT NULL DEFAULT 0,notes TEXT NOT NULL DEFAULT '',created_by INTEGER,created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,FOREIGN KEY(supplier_id) REFERENCES suppliers(id),FOREIGN KEY(created_by) REFERENCES users(id))"
            ,"CREATE TABLE IF NOT EXISTS purchase_order_items(id INTEGER PRIMARY KEY AUTOINCREMENT,order_id INTEGER NOT NULL,product_id INTEGER NOT NULL,quantity REAL NOT NULL DEFAULT 0,unit_cost REAL NOT NULL DEFAULT 0,received_quantity REAL NOT NULL DEFAULT 0,notes TEXT NOT NULL DEFAULT '',FOREIGN KEY(order_id) REFERENCES purchase_orders(id) ON DELETE CASCADE,FOREIGN KEY(product_id) REFERENCES products(id),UNIQUE(order_id,product_id))"
            ,"CREATE INDEX IF NOT EXISTS idx_purchase_orders_supplier ON purchase_orders(supplier_id)"
            ,"CREATE INDEX IF NOT EXISTS idx_purchase_orders_status ON purchase_orders(status)"
            ,"CREATE INDEX IF NOT EXISTS idx_purchase_items_order ON purchase_order_items(order_id)",
            "CREATE TABLE IF NOT EXISTS supplier_invoices(id INTEGER PRIMARY KEY AUTOINCREMENT,supplier_id INTEGER NOT NULL,invoice_no TEXT NOT NULL,invoice_date TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,due_date TEXT,total REAL NOT NULL DEFAULT 0,paid REAL NOT NULL DEFAULT 0,status TEXT NOT NULL DEFAULT 'PENDING',notes TEXT NOT NULL DEFAULT '',purchase_order_id INTEGER,created_by INTEGER,created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,FOREIGN KEY(supplier_id) REFERENCES suppliers(id),FOREIGN KEY(purchase_order_id) REFERENCES purchase_orders(id),FOREIGN KEY(created_by) REFERENCES users(id))",
            "CREATE TABLE IF NOT EXISTS supplier_payments(id INTEGER PRIMARY KEY AUTOINCREMENT,supplier_id INTEGER NOT NULL,invoice_id INTEGER,amount REAL NOT NULL DEFAULT 0,payment_method TEXT NOT NULL DEFAULT 'EFECTIVO',reference TEXT NOT NULL DEFAULT '',payment_date TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,notes TEXT NOT NULL DEFAULT '',user_id INTEGER,FOREIGN KEY(supplier_id) REFERENCES suppliers(id),FOREIGN KEY(invoice_id) REFERENCES supplier_invoices(id),FOREIGN KEY(user_id) REFERENCES users(id))",
            "CREATE INDEX IF NOT EXISTS idx_supplier_invoices_supplier ON supplier_invoices(supplier_id)",
            "CREATE INDEX IF NOT EXISTS idx_supplier_payments_supplier ON supplier_payments(supplier_id)"
            ,"CREATE TABLE IF NOT EXISTS reservations(id INTEGER PRIMARY KEY AUTOINCREMENT,customer_id INTEGER,customer_name TEXT NOT NULL DEFAULT '',phone TEXT NOT NULL DEFAULT '',reservation_date TEXT NOT NULL,hour TEXT NOT NULL,people INTEGER NOT NULL DEFAULT 2,table_id INTEGER,status TEXT NOT NULL DEFAULT 'CONFIRMED',notes TEXT NOT NULL DEFAULT '',created_by INTEGER,created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,FOREIGN KEY(customer_id) REFERENCES customers(id),FOREIGN KEY(table_id) REFERENCES restaurant_tables(id),FOREIGN KEY(created_by) REFERENCES users(id))"
            ,"CREATE INDEX IF NOT EXISTS idx_reservations_date ON reservations(reservation_date)"
            ,"CREATE TABLE IF NOT EXISTS waste_records(id INTEGER PRIMARY KEY AUTOINCREMENT,product_id INTEGER NOT NULL,quantity REAL NOT NULL,reason TEXT NOT NULL,notes TEXT NOT NULL DEFAULT '',user_id INTEGER,created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,FOREIGN KEY(product_id) REFERENCES products(id),FOREIGN KEY(user_id) REFERENCES users(id))"
            ,"CREATE TABLE IF NOT EXISTS product_modifiers(id INTEGER PRIMARY KEY AUTOINCREMENT,product_id INTEGER NOT NULL,name TEXT NOT NULL,price_delta REAL NOT NULL DEFAULT 0,active INTEGER NOT NULL DEFAULT 1,FOREIGN KEY(product_id) REFERENCES products(id) ON DELETE CASCADE)"
            ,"CREATE TABLE IF NOT EXISTS recipes(id INTEGER PRIMARY KEY AUTOINCREMENT,product_id INTEGER NOT NULL,ingredient_product_id INTEGER NOT NULL,quantity REAL NOT NULL DEFAULT 1,FOREIGN KEY(product_id) REFERENCES products(id) ON DELETE CASCADE,FOREIGN KEY(ingredient_product_id) REFERENCES products(id))"
            ,"CREATE TABLE IF NOT EXISTS split_accounts(id INTEGER PRIMARY KEY AUTOINCREMENT,sale_id INTEGER NOT NULL,part_no INTEGER NOT NULL,amount REAL NOT NULL,method TEXT NOT NULL DEFAULT '',paid INTEGER NOT NULL DEFAULT 0,paid_at TEXT,FOREIGN KEY(sale_id) REFERENCES sales(id) ON DELETE CASCADE)"
            ,"CREATE TABLE IF NOT EXISTS promotions(id INTEGER PRIMARY KEY AUTOINCREMENT,name TEXT NOT NULL,description TEXT NOT NULL DEFAULT '',type TEXT NOT NULL,amount REAL NOT NULL DEFAULT 0,promotion_price REAL NOT NULL DEFAULT 0,active INTEGER NOT NULL DEFAULT 1,start_at TEXT,end_at TEXT)"
            ,"CREATE TABLE IF NOT EXISTS promotion_items(id INTEGER PRIMARY KEY AUTOINCREMENT,promotion_id INTEGER NOT NULL,product_id INTEGER NOT NULL,quantity REAL NOT NULL DEFAULT 1,FOREIGN KEY(promotion_id) REFERENCES promotions(id) ON DELETE CASCADE,FOREIGN KEY(product_id) REFERENCES products(id),UNIQUE(promotion_id,product_id))"
            ,"CREATE INDEX IF NOT EXISTS idx_promotion_items_promotion ON promotion_items(promotion_id)"
            ,"CREATE TABLE IF NOT EXISTS favorite_products(product_id INTEGER PRIMARY KEY,position INTEGER NOT NULL DEFAULT 0,FOREIGN KEY(product_id) REFERENCES products(id) ON DELETE CASCADE)"
        ];

        using var tx = cn.BeginTransaction();
        foreach (var statement in sql)
        {
            using var cmd = cn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = statement;
            cmd.ExecuteNonQuery();
        }
        EnsureColumn(cn, tx, "customers", "credit_limit", "REAL NOT NULL DEFAULT 0");
        EnsureColumn(cn, tx, "users", "permissions", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(cn, tx, "products", "wholesale_price", "REAL NOT NULL DEFAULT 0");
        EnsureColumn(cn, tx, "products", "is_bulk", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(cn, tx, "products", "uses_inventory", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(cn, tx, "products", "adds_iva_21", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(cn, tx, "products", "iva_cost_migrated", "INTEGER NOT NULL DEFAULT 0");

        // Inventario: las versiones anteriores podían crear productos con uses_inventory=0
        // porque el interruptor global nacía deshabilitado. Para instalaciones existentes
        // hacemos una sola migración: habilitamos inventario y convertimos los productos
        // vendibles existentes en productos controlados por stock. Después de esta migración
        // el usuario puede volver a desactivar el control global o un producto concreto.
        using (var inventoryMigration = cn.CreateCommand())
        {
            inventoryMigration.Transaction = tx;
            inventoryMigration.CommandText = @"
                INSERT OR IGNORE INTO settings(key,value) VALUES('inventory_live_migrated_v1','0');
                UPDATE settings SET value='1' WHERE key='inventory_global_enabled' AND value='0'
                  AND EXISTS (SELECT 1 FROM settings WHERE key='inventory_live_migrated_v1' AND value='0');
                UPDATE products SET uses_inventory=1, updated_at=CURRENT_TIMESTAMP
                 WHERE active=1 AND barcode <> '__COMUN__'
                   AND EXISTS (SELECT 1 FROM settings WHERE key='inventory_live_migrated_v1' AND value='0');
                UPDATE settings SET value='1' WHERE key='inventory_live_migrated_v1' AND value='0';";
            inventoryMigration.ExecuteNonQuery();
        }

        // V73.1.55: la semántica correcta de IVA es sobre el COSTO.
        // Las versiones anteriores guardaban adds_iva_21=1 pero aplicaban el IVA
        // al precio de venta. Migramos una sola vez esos productos: el costo pasa
        // a incluir 21% y el precio de venta numérico se conserva, manteniendo así
        // el mismo margen económico que tenía antes (venta / (costo*1,21)).
        using (var ivaMigration = cn.CreateCommand())
        {
            ivaMigration.Transaction = tx;
            ivaMigration.CommandText = @"
                UPDATE products
                   SET cost_price = ROUND(cost_price * 1.21, 2),
                       iva_cost_migrated = 1,
                       updated_at = CURRENT_TIMESTAMP
                 WHERE COALESCE(adds_iva_21,0)=1
                   AND COALESCE(iva_cost_migrated,0)=0";
            ivaMigration.ExecuteNonQuery();
        }
        EnsureColumn(cn, tx, "products", "round_sale_to_5", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(cn, tx, "customer_accounts", "payment_method", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(cn, tx, "cash_sessions", "expected_amount", "REAL");
        EnsureColumn(cn, tx, "cash_sessions", "difference", "REAL");
        EnsureColumn(cn, tx, "cash_sessions", "mercado_pago_enabled", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(cn, tx, "cash_sessions", "mercado_pago_opening_amount", "REAL NOT NULL DEFAULT 0");
        EnsureColumn(cn, tx, "cash_sessions", "mercado_pago_retention_percent", "REAL NOT NULL DEFAULT 0");
        EnsureColumn(cn, tx, "cash_sessions", "mercado_pago_closing_amount", "REAL");
        EnsureColumn(cn, tx, "cash_sessions", "mercado_pago_expected_amount", "REAL");
        EnsureColumn(cn, tx, "cash_sessions", "mercado_pago_difference", "REAL");
        EnsureColumn(cn, tx, "sales", "session_id", "INTEGER");
        EnsureColumn(cn, tx, "cash_movements", "reference_id", "INTEGER");
        EnsureColumn(cn, tx, "cash_movements", "payment_method", "TEXT NOT NULL DEFAULT 'EFECTIVO'");
        EnsureColumn(cn, tx, "cash_movements", "voided", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(cn, tx, "cash_movements", "void_reason", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(cn, tx, "cash_movements", "voided_by", "INTEGER");
        EnsureColumn(cn, tx, "cash_movements", "voided_at", "TEXT");
        EnsureColumn(cn, tx, "sale_items", "returned_quantity", "REAL NOT NULL DEFAULT 0");
        EnsureColumn(cn, tx, "sale_items", "category", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(cn, tx, "sales", "table_id", "INTEGER");
        EnsureColumn(cn, tx, "suppliers", "payment_terms", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(cn, tx, "suppliers", "credit_days", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(cn, tx, "purchase_orders", "invoice_no", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(cn, tx, "sales", "sale_channel", "TEXT NOT NULL DEFAULT 'SALÓN'");
        EnsureColumn(cn, tx, "sales", "notes", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(cn, tx, "sales", "delivery_address", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(cn, tx, "sales", "delivery_status", "TEXT NOT NULL DEFAULT 'N/A'");
        EnsureColumn(cn, tx, "sales", "discount_reason", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(cn, tx, "sale_items", "discount_reason", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(cn, tx, "restaurant_tables", "salon_id", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(cn, tx, "restaurant_tables", "shape", "TEXT NOT NULL DEFAULT 'RECTANGLE'");
        EnsureColumn(cn, tx, "restaurant_tables", "rotation", "REAL NOT NULL DEFAULT 0");
        EnsureColumn(cn, tx, "restaurant_tables", "capacity", "INTEGER NOT NULL DEFAULT 4");
        EnsureColumn(cn, tx, "restaurant_tables", "color", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(cn, tx, "salon_decorations", "salon_id", "INTEGER NOT NULL DEFAULT 1");
        // Migración defensiva de promociones: versiones anteriores podían tener una tabla
        // promotions con solo algunas columnas (por ejemplo, sin type).
        // CREATE TABLE IF NOT EXISTS no modifica una tabla existente, por eso cada columna
        // utilizada por el módulo se asegura explícitamente antes de cualquier SELECT/INSERT.
        EnsureColumn(cn, tx, "promotions", "name", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(cn, tx, "promotions", "description", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(cn, tx, "promotions", "type", "TEXT NOT NULL DEFAULT 'PACK'");
        EnsureColumn(cn, tx, "promotions", "amount", "REAL NOT NULL DEFAULT 0");
        EnsureColumn(cn, tx, "promotions", "promotion_price", "REAL NOT NULL DEFAULT 0");
        EnsureColumn(cn, tx, "promotions", "active", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(cn, tx, "promotions", "start_at", "TEXT");
        EnsureColumn(cn, tx, "promotions", "end_at", "TEXT");
        EnsureColumn(cn, tx, "promotion_items", "promotion_id", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(cn, tx, "promotion_items", "product_id", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(cn, tx, "promotion_items", "quantity", "REAL NOT NULL DEFAULT 1");
        // V73.1.52: categorías persistentes. Se migran automáticamente las categorías
        // que ya estaban escritas como texto dentro de products.
        EnsureProductCategories(cn, tx);
        Seed(cn, tx);
        EnsurePublicGeneral(cn, tx);
        EnsureTypographyDefaults(cn, tx);
        EnsureThemeDefaults(cn, tx);
        tx.Commit();
    }

    private static void EnsurePublicGeneral(SqliteConnection cn, SqliteTransaction tx)
    {
        // ID 1 queda reservado para Público General. Solo se normaliza ese registro;
        // nunca se recrean ni se reordenan los demás clientes y no se toca su crédito/historial.
        Exec(cn, tx, "INSERT OR IGNORE INTO customers(id,name,document,phone,email,address,credit_limit,active) VALUES(1,'Público General','','','','',0,1);");
        Exec(cn, tx, "UPDATE customers SET name='Público General',document='',phone='',email='',address='',credit_limit=0,active=1 WHERE id=1;");
    }

    private static void EnsureProductCategories(SqliteConnection cn, SqliteTransaction tx)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT OR IGNORE INTO product_categories(name,active)
            SELECT DISTINCT TRIM(category),1
            FROM products
            WHERE TRIM(COALESCE(category,'')) <> '';";
        cmd.ExecuteNonQuery();
    }

    private static void EnsureColumn(SqliteConnection cn, SqliteTransaction tx, string table, string column, string definition)
    {
        using var check = cn.CreateCommand();
        check.Transaction = tx;
        check.CommandText = $"PRAGMA table_info({table})";
        using var reader = check.ExecuteReader();
        while (reader.Read())
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase)) return;
        reader.Close();
        using var add = cn.CreateCommand();
        add.Transaction = tx;
        add.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        add.ExecuteNonQuery();
    }

    // V8: corrected seed data. The products INSERT has matching columns and values.
    private static void Seed(SqliteConnection cn, SqliteTransaction tx)
    {
        Exec(cn, tx, "INSERT OR IGNORE INTO settings(key,value) VALUES ('business_name','Ferrari''s Punto de Venta'),('ticket_business_address',''),('ticket_business_phone',''),('language','es'),('language_initialized','0'),('theme','Graphite'),('font_family','Segoe UI'),('font_size','9.0'),('currency','$'),('ticket_footer','Gracias por su compra'),('ticket_width','80'),('exchange_rate','1'),('report_email_enabled','0'),('report_email_to',''),('smtp_host',''),('smtp_port','587'),('smtp_user',''),('smtp_password',''),('smtp_ssl','1'),('report_email2_enabled','0'),('report_email_to2',''),('smtp_host2',''),('smtp_port2','587'),('smtp_user2',''),('smtp_password2',''),('smtp_ssl2','1'),('resolution','1366x768'),('fullscreen','0'),('ui_brightness','140'),('providers_enabled','1'),('purchase_orders_enabled','1'),('tables_enabled','1'),('tables_in_main','1'),('tables_edit_enabled','1'),('active_salon_id','1'),('inventory_global_enabled','0'),('mercado_pago_retention_percent','0')");
        Exec(cn, tx, "INSERT OR IGNORE INTO salons(id,name,description,active) VALUES(1,'Salón principal','Diseño principal',1); INSERT OR IGNORE INTO users(id,username,full_name,password_hash,role,active) VALUES(1,'admin','Administrador','21232F297A57A5A743894A0E4A801FC3','ADMIN',1)");
        Exec(cn, tx, "INSERT OR IGNORE INTO customers(id,name) VALUES(1,'Público General')");
        Exec(cn, tx, "INSERT OR IGNORE INTO products(barcode,description,sale_price,wholesale_price,cost_price,stock,min_stock,category,unit) VALUES('7501000103256','TUINKY VAINILLA 152 GR',10,6,3,1,0,'ABARROTES','UN'),('9002490100070','Red Bull 250ml',22,14,3,1,0,'BEBIDAS','UN'),('7501055300952','Coca Cola 1 1/4lt',9,5,44,5,0,'BEBIDAS','UN'),('7501030497639','Pan Blanco Classic 454g',16,9,3,1,0,'PANIFICADOS','UN'),('7501030479499','Gansito Doble Chocolate 52 Gr.',5,3,5,1,0,'ABARROTES','UN')");
        Exec(cn, tx, "INSERT OR IGNORE INTO products(barcode,description,sale_price,cost_price,stock,min_stock,category,unit,active) VALUES('__COMUN__','PRODUCTO COMÚN',0,0,0,0,'ESPECIAL','UN',1)");
    }


    private static void EnsureThemeDefaults(SqliteConnection cn, SqliteTransaction tx)
    {
        // Tema de fábrica: Grafito. Solo se establece si todavía no existe
        // una preferencia guardada, por lo que no pisa la elección del usuario.
        Exec(cn, tx, "INSERT OR IGNORE INTO settings(key,value) VALUES ('theme','Graphite');");
    }

    private static void EnsureTypographyDefaults(SqliteConnection cn, SqliteTransaction tx)
    {
        // V47: Segoe UI fija y tamaño de fábrica 9,0.
        // Se normaliza una sola vez al actualizar desde versiones anteriores; después el usuario
        // puede modificar únicamente el tamaño desde Configuración.
        var migrated = GetSettingInTransaction(cn, tx, "typography_v47_factory9", "0");
        if (migrated == "1") return;
        Exec(cn, tx, "INSERT INTO settings(key,value) VALUES ('font_family','Segoe UI') ON CONFLICT(key) DO UPDATE SET value='Segoe UI';");
        Exec(cn, tx, "INSERT INTO settings(key,value) VALUES ('font_size','9.0') ON CONFLICT(key) DO UPDATE SET value='9.0';");
        Exec(cn, tx, "INSERT INTO settings(key,value) VALUES ('typography_v47_factory9','1') ON CONFLICT(key) DO UPDATE SET value='1';");
    }

    private static string GetSettingInTransaction(SqliteConnection cn, SqliteTransaction tx, string key, string fallback)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT value FROM settings WHERE key=$k LIMIT 1";
        cmd.Parameters.AddWithValue("$k", key);
        var value = cmd.ExecuteScalar()?.ToString();
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
    private static void Exec(SqliteConnection cn, SqliteTransaction tx, string statement)
    {
        using var cmd = cn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = statement;
        cmd.ExecuteNonQuery();
    }
    private static void EnsureColumn(SqliteConnection cn, string table, string column, string definition)
    {
        using var check = cn.CreateCommand();
        check.CommandText = $"PRAGMA table_info({table})";
        using var reader = check.ExecuteReader();
        while (reader.Read())
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return;

        using var alter = cn.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
        alter.ExecuteNonQuery();
    }

    private static void EnsureLicense(SqliteConnection cn)
    {
        using var q = cn.CreateCommand();
        q.CommandText = "SELECT status,expires_at FROM license WHERE id=1";
        using var r = q.ExecuteReader();
        if (r.Read())
        {
            var status = r.GetString(0);
            var expires = r.GetString(1);
            r.Close();
            if (string.Equals(status, "PERMANENT", StringComparison.OrdinalIgnoreCase)) return;
            if (expires.StartsWith("9999", StringComparison.Ordinal))
            {
                using var migrate = cn.CreateCommand();
                migrate.CommandText = "UPDATE license SET installed_at=$i,expires_at=$e,status='TRIAL' WHERE id=1";
                migrate.Parameters.AddWithValue("$i", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
                migrate.Parameters.AddWithValue("$e", DateTime.Today.AddDays(90).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                migrate.ExecuteNonQuery();
            }
            return;
        }
        r.Close();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "INSERT INTO license(id,license_key,installed_at,expires_at,status) VALUES(1,'',$i,$e,'TRIAL')";
        cmd.Parameters.AddWithValue("$i", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$e", DateTime.Today.AddDays(90).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    public static int LicenseDaysRemaining()
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT status,expires_at FROM license WHERE id=1";
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return 0;
        if (string.Equals(r.GetString(0), "PERMANENT", StringComparison.OrdinalIgnoreCase)) return -1;
        if (!DateTime.TryParse(r.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.None, out var expiry)) return 0;
        return Math.Max(0, (int)Math.Ceiling((expiry.Date - DateTime.Today).TotalDays));
    }

    public static void Backup()
    {
        Directory.CreateDirectory(BackupDirectory);
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "PRAGMA wal_checkpoint(FULL);";
        cmd.ExecuteNonQuery();
        var target = Path.Combine(BackupDirectory, $"FerrarisPOS_{DateTime.Now:yyyyMMdd_HHmmss}.db");
        File.Copy(DbPath, target, true);
    }

    public static string GetSetting(string key, string fallback = "")
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key=$k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar()?.ToString() ?? fallback;
    }

    public static void SetSetting(string key, string value)
    {
        using var cn = Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "INSERT INTO settings(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }
}
