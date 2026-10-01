-- FerrarisPOS C# / .NET 8
-- Esquema de referencia. La base real se crea en %APPDATA%\FerrarisPOS\FerrarisPOS.db.

-- products incluye wholesale_price para precio de mayoreo.
-- customers.credit_limit = 0 significa crédito infinito.
-- customer_accounts registra ventas a crédito y pagos de cuenta corriente.
-- customer_accounts.payment_method registra EFECTIVO, TARJETA o MIXTO.
-- cash_movements registra los pagos de cuenta corriente hechos en efectivo.

-- V73.1.49: los motivos de descuento se guardan en la venta y en cada item descontado.
-- La base real aplica estas columnas automaticamente desde Database.Initialize().

-- V73.1.52: categorías persistentes de productos.
CREATE TABLE IF NOT EXISTS product_categories(id INTEGER PRIMARY KEY AUTOINCREMENT,name TEXT NOT NULL COLLATE NOCASE UNIQUE,active INTEGER NOT NULL DEFAULT 1,created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP);
CREATE INDEX IF NOT EXISTS idx_product_categories_name ON product_categories(name COLLATE NOCASE);

-- V73.1.57: categoría/departamento queda guardada en cada línea de venta para que los reportes históricos no dependan exclusivamente de la categoría actual del producto.
CREATE TABLE IF NOT EXISTS customer_deletion_log(id INTEGER PRIMARY KEY AUTOINCREMENT,customer_id INTEGER NOT NULL,customer_name TEXT NOT NULL,customer_document TEXT NOT NULL DEFAULT '',reason TEXT NOT NULL,user_id INTEGER,created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,FOREIGN KEY(customer_id) REFERENCES customers(id),FOREIGN KEY(user_id) REFERENCES users(id));
CREATE TABLE IF NOT EXISTS product_deletion_log(id INTEGER PRIMARY KEY AUTOINCREMENT,product_id INTEGER NOT NULL,product_barcode TEXT NOT NULL DEFAULT '',product_name TEXT NOT NULL,stock_before REAL NOT NULL DEFAULT 0,reason TEXT NOT NULL,user_id INTEGER,created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,FOREIGN KEY(product_id) REFERENCES products(id),FOREIGN KEY(user_id) REFERENCES users(id));
CREATE TABLE IF NOT EXISTS supplier_deletion_log(id INTEGER PRIMARY KEY AUTOINCREMENT,supplier_id INTEGER NOT NULL,supplier_name TEXT NOT NULL,reason TEXT NOT NULL,user_id INTEGER,created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,FOREIGN KEY(supplier_id) REFERENCES suppliers(id),FOREIGN KEY(user_id) REFERENCES users(id));

CREATE TABLE IF NOT EXISTS price_change_log(id INTEGER PRIMARY KEY AUTOINCREMENT,product_id INTEGER NOT NULL,product_barcode TEXT NOT NULL DEFAULT '',product_name TEXT NOT NULL,old_price REAL NOT NULL DEFAULT 0,new_price REAL NOT NULL DEFAULT 0,reason TEXT NOT NULL,user_id INTEGER,created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,FOREIGN KEY(product_id) REFERENCES products(id),FOREIGN KEY(user_id) REFERENCES users(id));
