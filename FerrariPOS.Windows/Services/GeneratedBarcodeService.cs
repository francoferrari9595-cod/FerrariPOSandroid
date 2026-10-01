using FerrarisPOS.Data;
using FerrarisPOS.Models;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Printing;

namespace FerrarisPOS.Services;

public sealed record GeneratedBarcode(int Id, string Barcode, string Name, int? ProductId, DateTime CreatedAt);

public static class GeneratedBarcodeService
{
    private static string ImageDirectory
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FerrarisPOS", "CodigosBarras");

    public static List<GeneratedBarcode> Search(string text = "")
    {
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT id,barcode,name,product_id,created_at FROM generated_barcodes WHERE $q='' OR barcode LIKE $like OR name LIKE $like ORDER BY name, id DESC";
        cmd.Parameters.AddWithValue("$q", text.Trim());
        cmd.Parameters.AddWithValue("$like", $"%{text.Trim()}%");
        using var r = cmd.ExecuteReader();
        var list = new List<GeneratedBarcode>();
        while (r.Read())
        {
            DateTime.TryParse(r.GetString(4), out var created);
            list.Add(new GeneratedBarcode(r.GetInt32(0), r.GetString(1), r.GetString(2),
                r.IsDBNull(3) ? null : r.GetInt32(3), created));
        }
        return list;
    }

    public static string GenerateUniqueEan13()
    {
        var random = Random.Shared;
        for (int attempt = 0; attempt < 100; attempt++)
        {
            var digits = "20" + random.NextInt64(0, 10_000_000_000L).ToString("D10");
            var sum = 0;
            for (int i = 0; i < 12; i++)
                sum += (digits[i] - '0') * (i % 2 == 0 ? 1 : 3);

            var check = (10 - (sum % 10)) % 10;
            var code = digits + check;

            using var cn = Database.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM products WHERE barcode=$b UNION ALL SELECT 1 FROM generated_barcodes WHERE barcode=$b LIMIT 1";
            cmd.Parameters.AddWithValue("$b", code);
            if (cmd.ExecuteScalar() is null) return code;
        }

        throw new InvalidOperationException("No se pudo generar un código único. Intentá nuevamente.");
    }

    public static int Save(string name, string barcode, int? productId = null)
    {
        name = name.Trim();
        barcode = barcode.Trim();

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("El nombre del código es obligatorio.");

        if (!IsValidEan13(barcode))
            throw new ArgumentException("El código interno debe tener 13 dígitos EAN-13 válidos.");

        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "INSERT INTO generated_barcodes(barcode,name,product_id) VALUES($b,$n,$p)";
        cmd.Parameters.AddWithValue("$b", barcode);
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$p", productId.HasValue ? productId.Value : DBNull.Value);

        try { cmd.ExecuteNonQuery(); }
        catch (Exception ex) when (ex.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Ese código ya está guardado o ya pertenece a un producto.");
        }

        using var idCmd = cn.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid();";
        var id = Convert.ToInt32(idCmd.ExecuteScalar());

        // El gráfico queda guardado físicamente con el código exacto.
        SaveBarcodeImage(barcode, name);
        AuditService.Log(Session.UserId, "BARCODE_GENERATED", "INVENTARIO", $"Código {barcode} - {name}");
        return id;
    }

    /// <summary>
    /// Crea el producto normal del inventario a partir de un código interno guardado.
    /// Si ya existe un producto con exactamente el mismo nombre y no tiene código,
    /// se le asigna el código en lugar de crear un duplicado.
    /// </summary>
    public static Product CreateProductFromGenerated(int generatedId)
    {
        if (generatedId <= 0)
            throw new ArgumentException("Código interno inválido.");

        using var cn = Database.Open();
        using var tx = cn.BeginTransaction();

        string code;
        string name;
        int? linkedProductId;

        using (var q = cn.CreateCommand())
        {
            q.Transaction = tx;
            q.CommandText = "SELECT barcode,name,product_id FROM generated_barcodes WHERE id=$id LIMIT 1";
            q.Parameters.AddWithValue("$id", generatedId);
            using var r = q.ExecuteReader();
            if (!r.Read())
                throw new InvalidOperationException("No se encontró el código interno seleccionado.");

            code = r.GetString(0);
            name = r.GetString(1).Trim();
            linkedProductId = r.IsDBNull(2) ? null : r.GetInt32(2);
        }

        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("El código interno no tiene nombre de producto.");

        // Protección adicional: un código interno jamás puede convertirse en el
        // código principal de otro producto que ya lo tenga, incluso si ese
        // producto está inactivo y por eso no aparece en la grilla normal.
        using (var barcodeConflict = cn.CreateCommand())
        {
            barcodeConflict.Transaction = tx;
            barcodeConflict.CommandText = @"SELECT id,description FROM products WHERE TRIM(barcode)=TRIM($b) LIMIT 1";
            barcodeConflict.Parameters.AddWithValue("$b", code);
            using var conflictReader = barcodeConflict.ExecuteReader();
            if (conflictReader.Read() && (!linkedProductId.HasValue || conflictReader.GetInt32(0) != linkedProductId.Value))
                throw new InvalidOperationException($"El código {code} ya está asignado al producto '{conflictReader.GetString(1)}'. No se modificó ningún producto.");
        }

        // Si ya está vinculado, simplemente devolvemos el producto existente.
        if (linkedProductId.HasValue)
        {
            using var existing = cn.CreateCommand();
            existing.Transaction = tx;
            existing.CommandText = "SELECT id,barcode,description,sale_price,wholesale_price,cost_price,stock,min_stock,category,unit,active,is_bulk,COALESCE(uses_inventory,1) FROM products WHERE id=$id LIMIT 1";
            existing.Parameters.AddWithValue("$id", linkedProductId.Value);
            using var r = existing.ExecuteReader();
            if (r.Read())
            {
                var product = ReadProduct(r);
                r.Close();
                tx.Commit();
                return product;
            }
        }

        // Evita duplicar un producto que ya existe con el mismo nombre.
        using (var byName = cn.CreateCommand())
        {
            byName.Transaction = tx;
            byName.CommandText = "SELECT id,barcode,description,sale_price,wholesale_price,cost_price,stock,min_stock,category,unit,active,is_bulk,COALESCE(uses_inventory,1) FROM products WHERE active=1 AND LOWER(TRIM(description))=LOWER(TRIM($name)) ORDER BY id LIMIT 1";
            byName.Parameters.AddWithValue("$name", name);
            using var r = byName.ExecuteReader();
            if (r.Read())
            {
                var existingProduct = ReadProduct(r);
                r.Close();
                var oldBarcode = existingProduct.Barcode?.Trim() ?? "";
                var emptyBarcode = string.IsNullOrWhiteSpace(oldBarcode) ||
                    new[] { "0", "SIN CODIGO", "SIN CÓDIGO", "S/C", "SC", "N/A", "NA", "-" }.Contains(oldBarcode, StringComparer.OrdinalIgnoreCase);

                if (!emptyBarcode && !string.Equals(oldBarcode, code, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Ya existe un producto llamado '{existingProduct.Description}' con el código {oldBarcode}. Para reemplazarlo usá ASIGNAR CÓDIGO DE BARRAS.");

                try
                {
                    using var update = cn.CreateCommand();
                    update.Transaction = tx;
                    update.CommandText = "UPDATE products SET barcode=$b,updated_at=CURRENT_TIMESTAMP WHERE id=$id";
                    update.Parameters.AddWithValue("$b", code);
                    update.Parameters.AddWithValue("$id", existingProduct.Id);
                    update.ExecuteNonQuery();
                }
                catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19)
                {
                    throw new InvalidOperationException($"El código {code} ya está siendo utilizado por otro registro. No se modificó el producto.", ex);
                }

                using var link = cn.CreateCommand();
                link.Transaction = tx;
                link.CommandText = "UPDATE generated_barcodes SET product_id=$p WHERE id=$id";
                link.Parameters.AddWithValue("$p", existingProduct.Id);
                link.Parameters.AddWithValue("$id", generatedId);
                link.ExecuteNonQuery();

                tx.Commit();
                AuditService.Log(Session.UserId, "BARCODE_PRODUCT_LINK", "INVENTARIO", $"Código {code} vinculado al producto existente ID {existingProduct.Id} - {existingProduct.Description}");
                return existingProduct with { Barcode = code };
            }
        }

        // Producto nuevo: queda disponible inmediatamente en Productos/Ventas.
        using var insert = cn.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = @"INSERT INTO products
            (barcode,description,sale_price,wholesale_price,cost_price,stock,min_stock,category,unit,active,is_bulk,uses_inventory)
            VALUES($b,$d,0,0,0,0,0,'GENERAL','UN',1,0,1)";
        insert.Parameters.AddWithValue("$b", code);
        insert.Parameters.AddWithValue("$d", name);
        try
        {
            insert.ExecuteNonQuery();
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            throw new InvalidOperationException($"El código {code} ya está siendo utilizado por otro registro. No se creó el producto.", ex);
        }

        using var idCmd = cn.CreateCommand();
        idCmd.Transaction = tx;
        idCmd.CommandText = "SELECT last_insert_rowid();";
        var newId = Convert.ToInt32(idCmd.ExecuteScalar());

        using var generatedUpdate = cn.CreateCommand();
        generatedUpdate.Transaction = tx;
        generatedUpdate.CommandText = "UPDATE generated_barcodes SET product_id=$p WHERE id=$id";
        generatedUpdate.Parameters.AddWithValue("$p", newId);
        generatedUpdate.Parameters.AddWithValue("$id", generatedId);
        generatedUpdate.ExecuteNonQuery();

        tx.Commit();
        AuditService.Log(Session.UserId, "PRODUCT_CREATE_FROM_BARCODE", "INVENTARIO", $"Producto ID {newId} creado desde código interno {code} - {name}");

        return new Product(newId, code, name, 0, 0, 0, 0, 0, "GENERAL", "UN", true, false, InventoryControlService.DefaultUsesInventory);
    }

    private static Product ReadProduct(Microsoft.Data.Sqlite.SqliteDataReader r)
        => new(r.GetInt32(0), r.GetString(1), r.GetString(2), r.GetDouble(3), r.GetDouble(4), r.GetDouble(5),
            r.GetDouble(6), r.GetDouble(7), r.GetString(8), r.GetString(9), r.GetInt32(10) != 0,
            r.GetInt32(11) != 0, InventoryControlService.IsGlobalEnabled && r.GetInt32(12) != 0);

    public static void AssignToProduct(int generatedId, int productId)
    {
        if (generatedId <= 0 || productId <= 0)
            throw new ArgumentException("Código o producto inválido.");

        using var cn = Database.Open();
        using var q = cn.CreateCommand();
        q.CommandText = "SELECT barcode,name FROM generated_barcodes WHERE id=$id";
        q.Parameters.AddWithValue("$id", generatedId);

        using var r = q.ExecuteReader();
        if (!r.Read())
            throw new InvalidOperationException("No se encontró el código generado.");

        var code = r.GetString(0);
        var name = r.GetString(1);
        r.Close();

        ProductService.AssignBarcode(productId, code);

        using var u = cn.CreateCommand();
        u.CommandText = "UPDATE generated_barcodes SET product_id=$p WHERE id=$id";
        u.Parameters.AddWithValue("$p", productId);
        u.Parameters.AddWithValue("$id", generatedId);
        u.ExecuteNonQuery();

        SaveBarcodeImage(code, name);
        AuditService.Log(Session.UserId, "BARCODE_ASSIGN_GENERATED", "INVENTARIO",
            $"Código {code} ({name}) asignado al producto ID {productId}");
    }

    public static string GetImagePath(string barcode)
    {
        Directory.CreateDirectory(ImageDirectory);
        return Path.Combine(ImageDirectory, $"{barcode}.png");
    }

    public static string SaveBarcodeImage(string barcode, string name)
    {
        if (!IsValidEan13(barcode))
            throw new ArgumentException("Código EAN-13 inválido.");

        Directory.CreateDirectory(ImageDirectory);
        var path = GetImagePath(barcode);

        using var bmp = RenderEan13(barcode, name, 1200, 420);
        bmp.Save(path, ImageFormat.Png);
        return path;
    }

    public static Bitmap RenderEan13(string barcode, string name = "", int width = 1200, int height = 420)
    {
        if (!IsValidEan13(barcode))
            throw new ArgumentException("Código EAN-13 inválido.");

        var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.White);

        const int leftQuiet = 90;
        const int rightQuiet = 90;
        const int top = 55;
        const int barHeight = 245;
        var moduleWidth = Math.Max(2, (width - leftQuiet - rightQuiet) / 95f);

        string bits = Ean13Bits(barcode);
        float x = leftQuiet;

        using var black = new SolidBrush(Color.Black);
        for (int i = 0; i < bits.Length; i++)
        {
            if (bits[i] == '1')
            {
                var h = (i < 3 || (i >= 45 && i < 50) || i >= 92) ? barHeight + 28 : barHeight;
                g.FillRectangle(black, x, top, moduleWidth + 0.7f, h);
            }
            x += moduleWidth;
        }

        using var fontName = new Font("Segoe UI", 24, FontStyle.Bold);
        using var fontCode = new Font("Segoe UI", 28, FontStyle.Regular);

        var nameText = name ?? "";
        if (!string.IsNullOrWhiteSpace(nameText))
        {
            var nameSize = g.MeasureString(nameText, fontName);
            g.DrawString(nameText, fontName, black, (width - nameSize.Width) / 2f, 10);
        }

        var codeSize = g.MeasureString(barcode, fontCode);
        g.DrawString(barcode, fontCode, black, (width - codeSize.Width) / 2f, top + barHeight + 28);

        return bmp;
    }

    public static void PrintOne(GeneratedBarcode item)
    {
        PrintItems(new List<GeneratedBarcode> { item });
    }

    public static void PrintAll(IEnumerable<GeneratedBarcode> items)
    {
        var list = items.ToList();
        if (list.Count == 0)
            throw new InvalidOperationException("No hay códigos internos guardados para imprimir.");

        PrintItems(list);
    }

    private static void PrintItems(List<GeneratedBarcode> items)
    {
        using var dialog = new PrintDialog();
        using var document = new PrintDocument();
        dialog.Document = document;
        dialog.AllowSomePages = true;
        dialog.UseEXDialog = true;

        int index = 0;
        document.DefaultPageSettings.Landscape = false;
        document.PrintPage += (_, e) =>
        {
            const int margin = 40;
            const int labelWidth = 330;
            const int labelHeight = 175;
            const int columns = 2;
            const int rows = 4;

            using var nameFont = new Font("Segoe UI", 10, FontStyle.Bold);
            using var codeFont = new Font("Segoe UI", 9);
            using var pen = new Pen(Color.LightGray, 1);

            for (int row = 0; row < rows && index < items.Count; row++)
            {
                for (int col = 0; col < columns && index < items.Count; col++)
                {
                    var item = items[index++];
                    var x = margin + col * labelWidth;
                    var y = margin + row * labelHeight;
                    var rect = new Rectangle(x, y, labelWidth - 10, labelHeight - 10);

                    e.Graphics.DrawRectangle(pen, rect);
                    var title = item.Name.Length > 34 ? item.Name[..34] : item.Name;
                    e.Graphics.DrawString(title, nameFont, Brushes.Black, x + 10, y + 8);

                    using var bmp = RenderEan13(item.Barcode, "", 600, 210);
                    var barcodeRect = new Rectangle(x + 10, y + 35, labelWidth - 30, 105);
                    e.Graphics.DrawImage(bmp, barcodeRect);

                    var codeSize = e.Graphics.MeasureString(item.Barcode, codeFont);
                    e.Graphics.DrawString(item.Barcode, codeFont, Brushes.Black,
                        x + ((labelWidth - 10) - codeSize.Width) / 2f, y + 142);
                }
            }

            e.HasMorePages = index < items.Count;
        };

        if (dialog.ShowDialog() == DialogResult.OK)
            document.Print();
    }

    private static bool IsValidEan13(string code)
    {
        if (code.Length != 13 || !code.All(char.IsDigit))
            return false;

        var sum = 0;
        for (int i = 0; i < 12; i++)
            sum += (code[i] - '0') * (i % 2 == 0 ? 1 : 3);

        var check = (10 - sum % 10) % 10;
        return check == code[12] - '0';
    }

    private static string Ean13Bits(string code)
    {
        // EAN-13: paridades según primer dígito, 6 + 6 dígitos.
        string[] L =
        [
            "0001101","0011001","0010011","0111101","0100011",
            "0110001","0101111","0111011","0110111","0001011"
        ];
        string[] G =
        [
            "0100111","0110011","0011011","0100001","0011101",
            "0111001","0000101","0010001","0001001","0010111"
        ];
        string[] R =
        [
            "1110010","1100110","1101100","1000010","1011100",
            "1001110","1010000","1000100","1001000","1110100"
        ];
        string[] parity =
        [
            "LLLLLL","LLGLGG","LLGGLG","LLGGGL","LGLLGG",
            "LGGLLG","LGGGLL","LGLGLG","LGLGGL","LGGLGL"
        ];

        int first = code[0] - '0';
        var bits = "101";
        var p = parity[first];

        for (int i = 1; i <= 6; i++)
        {
            int d = code[i] - '0';
            bits += p[i - 1] == 'L' ? L[d] : G[d];
        }

        bits += "01010";

        for (int i = 7; i <= 12; i++)
            bits += R[code[i] - '0'];

        bits += "101";
        return bits;
    }
}
