using FerrarisPOS.Data;
using FerrarisPOS.Services;

namespace FerrarisPOS.Forms;

public sealed class PurchaseOrderForm : Form
{
    private readonly SafeComboBox supplier = new();
    private readonly DateTimePicker expected = new();
    private readonly DataGridView grid = new();
    private readonly Label total = new();
    private readonly Label info = new();
    private readonly Button fillRemaining = new();
    private readonly Button confirm = new();
    private readonly TextBox receptionNotes = new();
    private readonly CheckBox closeComplete = new();

    private readonly int orderId;
    private readonly bool receiving;
    private readonly bool differenceMode;

    // product_id -> ordered quantity, original unit cost
    private readonly Dictionary<int, (double qty, double cost)> original = new();

    // Identifica si una línea de recepción corresponde a la orden original
    // o fue agregada manualmente como producto adicional recibido.
    private sealed record ReceiveLineTag(int ProductId, bool Additional);

    public PurchaseOrderForm(int orderId = 0, bool receiving = false, bool differenceMode = false)
    {
        this.orderId = orderId;
        this.receiving = receiving;
        this.differenceMode = differenceMode;

        Text = receiving
            ? (differenceMode ? "FerrarisPOS - Recepción con diferencia" : "FerrarisPOS - Recibir mercadería")
            : "FerrarisPOS - Nueva compra";

        Width = 1180;
        Height = 720;
        StartPosition = FormStartPosition.CenterParent;

        Build();
        LoadSuppliers();

        if (orderId != 0)
            LoadOrder();

        ThemeService.Apply(this);
    }

    private void Build()
    {
        Controls.Add(new Label
        {
            Text = "PROVEEDOR",
            Location = new Point(20, 22),
            AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        });

        supplier.Location = new Point(105, 18);
        supplier.Width = 320;
        supplier.DropDownStyle = ComboBoxStyle.DropDownList;
        supplier.FormattingEnabled = true;
        supplier.IntegralHeight = true;
        supplier.MaxDropDownItems = 20;
        supplier.DropDownWidth = 420;
        // El selector de proveedor debe abrirse siempre como una lista real,
        // incluso en equipos donde el estilo visual de Windows/tema pueda
        // interferir con el botón de despliegue.
        supplier.MouseDown += (_, _) =>
        {
            if (supplier.Enabled && supplier.Items.Count > 1)
                supplier.DroppedDown = true;
        };
        Controls.Add(supplier);

        Controls.Add(new Label
        {
            Text = "ENTREGA",
            Location = new Point(450, 22),
            AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        });

        expected.Location = new Point(510, 18);
        expected.Width = 150;
        expected.Format = DateTimePickerFormat.Short;
        Controls.Add(expected);

        var add = Btn(receiving ? "AGREGAR PRODUCTOS ADICIONALES" : "AGREGAR PRODUCTO", 680, 16, receiving ? 215 : 155);
        add.Click += (_, _) => AddProduct();
        Controls.Add(add);

        info.Location = new Point(20, 55);
        info.Size = new Size(1040, 36);
        info.AutoSize = false;
        info.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        info.Text = receiving
            ? (differenceMode
                ? "RECEPCIÓN CON DIFERENCIA: podés recibir menos, igual o MÁS que lo pendiente. Si hay excedente, indicá el motivo."
                : "RECEPCIÓN: podés recibir la cantidad real. También podés agregar productos adicionales que trajo el proveedor y quedarán registrados en el ingreso.")
            : "Elegí únicamente productos previamente asignados al proveedor y cargá cantidad y costo.";
        Controls.Add(info);

        grid.Location = new Point(20, 100);
        grid.Size = new Size(1120, receiving ? 430 : 475);
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.RowHeadersVisible = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.AutoGenerateColumns = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.DataError += (_, e) =>
        {
            e.ThrowException = false;
            e.Cancel = true;
        };

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Id", HeaderText = "ID", Visible = false
        });

        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Producto", HeaderText = "PRODUCTO", ReadOnly = true, FillWeight = 28
        });

        if (!receiving)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Stock", HeaderText = "STOCK", ReadOnly = true, FillWeight = 11
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Cantidad", HeaderText = "PEDIR", FillWeight = 13
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Costo", HeaderText = "COSTO UNIT.", FillWeight = 14
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Total", HeaderText = "TOTAL", ReadOnly = true, FillWeight = 14
            });
        }
        else
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Pedido", HeaderText = "PEDIDO", ReadOnly = true, FillWeight = 10
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Recibido", HeaderText = "YA RECIBIDO", ReadOnly = true, FillWeight = 12
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Pendiente", HeaderText = "PENDIENTE", ReadOnly = true, FillWeight = 12
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "RecibirAhora", HeaderText = "RECIBIR AHORA", FillWeight = 15
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Costo", HeaderText = "COSTO UNIT.", FillWeight = 14
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Total", HeaderText = "TOTAL RECIBIDO", ReadOnly = true, FillWeight = 15
            });
        }

        grid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex >= 0)
                RecalcRow(e.RowIndex);
        };

        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty)
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };

        Controls.Add(grid);

        total.Location = new Point(780, 585);
        total.AutoSize = true;
        total.Font = new Font("Segoe UI", 13, FontStyle.Bold);
        Controls.Add(total);

        if (receiving)
        {
            var notesLabel = new Label
            {
                Text = "NOTA DE RECEPCIÓN / MOTIVO DE DIFERENCIA",
                Location = new Point(20, 545),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
            };
            Controls.Add(notesLabel);

            receptionNotes.Location = new Point(20, 568);
            receptionNotes.Size = new Size(650, 48);
            receptionNotes.Multiline = true;
            receptionNotes.ScrollBars = ScrollBars.Vertical;
            receptionNotes.PlaceholderText = "Ej.: proveedor entregó 3 unidades adicionales por promoción / bonificación...";
            Controls.Add(receptionNotes);

            closeComplete.Text = "CERRAR LA ORDEN COMO COMPLETA AUNQUE QUEDEN PENDIENTES";
            closeComplete.Location = new Point(690, 570);
            closeComplete.AutoSize = true;
            closeComplete.Visible = true;
            Controls.Add(closeComplete);

            fillRemaining.Text = "RECIBIR TODO LO PENDIENTE";
            fillRemaining.Location = new Point(20, 625);
            fillRemaining.Width = 205;
            fillRemaining.Height = 38;
            fillRemaining.Click += (_, _) => FillRemaining();
            Controls.Add(fillRemaining);
        }

        total.Location = receiving ? new Point(760, 545) : new Point(780, 585);

        confirm.Text = receiving ? "CONFIRMAR RECEPCIÓN" : "GUARDAR ORDEN";
        confirm.Location = receiving ? new Point(235, 625) : new Point(20, 625);
        confirm.Width = 190;
        confirm.Height = 38;
        confirm.Click += (_, _) => Save();
        Controls.Add(confirm);

        var close = Btn("CERRAR", receiving ? 435 : 220, 625, 110);
        close.Click += (_, _) => Close();
        Controls.Add(close);
    }

    private Button Btn(string text, int x, int y, int width) =>
        new()
        {
            Text = text,
            Location = new Point(x, y),
            Width = width,
            Height = 38
        };

    private void LoadSuppliers()
    {
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        // En una nueva orden se deben poder ver TODOS los proveedores
        // registrados. El estado activo/inactivo se gestiona en Proveedores;
        // aquí el objetivo es que nunca desaparezcan de la lista de selección.
        cmd.CommandText = "SELECT id, name FROM suppliers ORDER BY active DESC, name";

        var list = new List<SupplierItem>();
        using var r = cmd.ExecuteReader();

        while (r.Read())
            list.Add(new SupplierItem(r.GetInt32(0), r.IsDBNull(1) ? "" : r.GetString(1)));

        supplier.DataSource = list;
        supplier.DisplayMember = "Name";
        supplier.ValueMember = "Id";
        if (list.Count == 0)
            supplier.SelectedIndex = -1;
    }

    private sealed record SupplierItem(int Id, string Name)
    {
        public override string ToString() => Name;
    }

    private int SupplierId =>
        supplier.SelectedValue is int value
            ? value
            : int.TryParse(Convert.ToString(supplier.SelectedValue), out var id) ? id : 0;

    private int listIndexBySupplierId(int supplierId)
    {
        if (supplier.DataSource is not IEnumerable<SupplierItem> source) return -1;
        var list = source.ToList();
        return list.FindIndex(x => x.Id == supplierId);
    }

    private void AddProduct()
    {
        if (SupplierId == 0)
        {
            MessageBox.Show("Seleccioná un proveedor.", "Compras",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var f = new PurchaseProductPickerForm(SupplierId);
        if (f.ShowDialog(this) != DialogResult.OK)
            return;

        foreach (var p in f.Selected)
        {
            var existingRow = grid.Rows.Cast<DataGridViewRow>()
                .FirstOrDefault(r => Convert.ToInt32(r.Cells["Id"].Value) == p.Id);

            if (!receiving)
            {
                if (existingRow != null)
                    continue;

                var row = grid.Rows.Add(
                    p.Id, p.Description, p.Stock, p.Qty,
                    p.Cost.ToString("0.##"),
                    (p.Qty * p.Cost).ToString("0.##"));
                grid.Rows[row].Tag = new ReceiveLineTag(p.Id, false);
                continue;
            }

            if (existingRow != null)
            {
                // Si el producto ya forma parte de la OC, no duplicamos la línea:
                // acumulamos la cantidad adicional en "RECIBIR AHORA".
                var current = Parse(existingRow.Cells["RecibirAhora"].Value);
                var cost = Parse(existingRow.Cells["Costo"].Value);
                if (cost <= 0) cost = p.Cost;
                existingRow.Cells["RecibirAhora"].Value = FormatNumber(current + p.Qty);
                existingRow.Cells["Costo"].Value = FormatNumber(cost);
                RecalcRow(existingRow.Index);
                continue;
            }

            // Producto que NO estaba pedido: se agrega como línea adicional.
            // Pedido/pendiente quedan en cero y la cantidad real queda en
            // RECIBIR AHORA. Al confirmar se insertará también en
            // purchase_order_items para que aparezca en el reporte/ticket de ingreso.
            var extraRow = grid.Rows.Add(
                p.Id,
                "ADICIONAL · " + p.Description,
                FormatNumber(0),
                FormatNumber(0),
                FormatNumber(0),
                FormatNumber(p.Qty),
                FormatNumber(p.Cost),
                FormatNumber(p.Qty * p.Cost));
            grid.Rows[extraRow].Tag = new ReceiveLineTag(p.Id, true);
        }

        RecalcTotal();
    }

    private void LoadOrder()
    {
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();

        cmd.CommandText = @"
SELECT supplier_id, expected_date, status, notes
FROM purchase_orders
WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", orderId);

        using var r = cmd.ExecuteReader();

        if (!r.Read())
        {
            MessageBox.Show("No se encontró la orden de compra.", "Compras",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        var supplierId = r.GetInt32(0);
        var expectedDate = DateTime.Today;

        if (!r.IsDBNull(1))
            DateTime.TryParse(r.GetString(1), out expectedDate);

        var status = r.IsDBNull(2) ? "" : r.GetString(2);
        var savedNotes = r.IsDBNull(3) ? "" : r.GetString(3);

        if (status == "CANCELLED")
        {
            MessageBox.Show("La orden está cancelada y no puede recibirse.", "Compras",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        if (receiving && (status == "RECEIVED" || status == "RECEIVED_WITH_DIFFERENCE"))
        {
            MessageBox.Show("La orden ya fue recibida completamente.", "Compras",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        if (supplier.Items.Count > 0)
        {
            var supplierIndex = listIndexBySupplierId(supplierId);
            supplier.SelectedIndex = supplierIndex >= 0 && supplierIndex < supplier.Items.Count ? supplierIndex : -1;
        }
        expected.Value = expectedDate;
        if (receiving) receptionNotes.Text = savedNotes;
        r.Close();

        cmd.Parameters.Clear();
        cmd.CommandText = @"
SELECT
    poi.product_id,
    p.description,
    p.stock,
    poi.quantity,
    poi.unit_cost,
    COALESCE(poi.received_quantity,0)
FROM purchase_order_items poi
JOIN products p ON p.id=poi.product_id
WHERE poi.order_id=$id
ORDER BY p.description";
        cmd.Parameters.AddWithValue("$id", orderId);

        using var r2 = cmd.ExecuteReader();

        while (r2.Read())
        {
            var productId = r2.GetInt32(0);
            var description = r2.IsDBNull(1) ? "" : r2.GetString(1);
            var stock = r2.GetDouble(2);
            var ordered = r2.GetDouble(3);
            var cost = r2.GetDouble(4);
            var alreadyReceived = r2.GetDouble(5);
            var remaining = Math.Max(0, ordered - alreadyReceived);

            original[productId] = (ordered, cost);

            if (!receiving)
            {
                var row = grid.Rows.Add(
                    productId,
                    description,
                    stock,
                    FormatNumber(ordered),
                    FormatNumber(cost),
                    FormatNumber(ordered * cost));
                grid.Rows[row].Tag = new ReceiveLineTag(productId, false);
            }
            else
            {
                // IMPORTANT: the editable value is only what will arrive NOW.
                // We never put already-received quantity into this field again.
                var row = grid.Rows.Add(
                    productId,
                    description,
                    FormatNumber(ordered),
                    FormatNumber(alreadyReceived),
                    FormatNumber(remaining),
                    FormatNumber(differenceMode ? 0 : remaining),
                    FormatNumber(cost),
                    FormatNumber(remaining * cost));
                grid.Rows[row].Tag = new ReceiveLineTag(productId, false);
            }
        }

        supplier.Enabled = false;
        expected.Enabled = false;
        RecalcTotal();
    }

    private void FillRemaining()
    {
        if (!receiving) return;

        foreach (DataGridViewRow row in grid.Rows)
        {
            var remaining = Parse(row.Cells["Pendiente"].Value);
            row.Cells["RecibirAhora"].Value = FormatNumber(remaining);
        }

        RecalcTotal();
    }

    private void RecalcRow(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= grid.Rows.Count)
            return;

        if (receiving)
        {
            var remaining = Parse(grid.Rows[rowIndex].Cells["Pendiente"].Value);
            var receiveNow = Parse(grid.Rows[rowIndex].Cells["RecibirAhora"].Value);
            var cost = Parse(grid.Rows[rowIndex].Cells["Costo"].Value);

            if (receiveNow < 0) receiveNow = 0;

            // Se permite recibir más que lo pendiente. Si ocurre, al confirmar
            // se exige una nota y puede cerrarse la OC como completa con diferencia.
            grid.Rows[rowIndex].Cells["RecibirAhora"].Value = FormatNumber(receiveNow);
            grid.Rows[rowIndex].Cells["Total"].Value = FormatNumber(receiveNow * cost);
        }
        else
        {
            var qty = Parse(grid.Rows[rowIndex].Cells["Cantidad"].Value);
            var cost = Parse(grid.Rows[rowIndex].Cells["Costo"].Value);
            if (qty < 0) qty = 0;
            grid.Rows[rowIndex].Cells["Total"].Value = FormatNumber(qty * cost);
        }

        RecalcTotal();
    }

    private void RecalcTotal()
    {
        double value = 0;

        foreach (DataGridViewRow row in grid.Rows)
            value += Parse(row.Cells["Total"].Value);

        total.Text = receiving
            ? $"VALOR RECIBIDO AHORA: ${value:N2}"
            : $"TOTAL: ${value:N2}";
    }

    private static string FormatNumber(double value) =>
        value.ToString("0.###", System.Globalization.CultureInfo.CurrentCulture);

    private static double Parse(object? value)
    {
        var raw = Convert.ToString(value)?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(raw))
            return 0;

        if (double.TryParse(raw,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.CurrentCulture,
            out var current))
            return current;

        if (double.TryParse(raw,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out var invariant))
            return invariant;

        return 0;
    }

    private void Save()
    {
        if (SupplierId == 0)
        {
            MessageBox.Show("Seleccioná un proveedor.", "Compras",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (grid.Rows.Count == 0)
        {
            MessageBox.Show("Agregá al menos un producto.", "Compras",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            using var cn = Database.Open();
            using var tx = cn.BeginTransaction();

            if (!receiving)
            {
                var gridTotal = GridTotal();

                int id;
                using (var c = cn.CreateCommand())
                {
                    c.Transaction = tx;
                    c.CommandText = @"
INSERT INTO purchase_orders(
    order_no, supplier_id, status, order_date, expected_date,
    total, received_total)
VALUES(
    $no, $sid, 'DRAFT', CURRENT_TIMESTAMP, $date,
    $total, 0);
SELECT last_insert_rowid();";

                    c.Parameters.AddWithValue("$no", NextOrderNo(cn, tx));
                    c.Parameters.AddWithValue("$sid", SupplierId);
                    c.Parameters.AddWithValue("$date", expected.Value.ToString("yyyy-MM-dd"));
                    c.Parameters.AddWithValue("$total", gridTotal);

                    id = Convert.ToInt32(c.ExecuteScalar());
                }

                InsertItems(cn, tx, id);
                tx.Commit();

                MessageBox.Show(
                    "Orden de compra guardada correctamente.\n\nCuando llegue la mercadería, entrá en COMPRAS y elegí RECIBIR MERCADERÍA.",
                    "Compras",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                var result = Receive(cn, tx);
                tx.Commit();

                var print = MessageBox.Show(
                    result.Message + "\n\n¿Querés imprimir el ticket de recepción?",
                    "Recepción de mercadería",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information);

                if (print == DialogResult.Yes)
                    PurchaseReceiptTicketService.Print(orderId);

                DialogResult = DialogResult.OK;
                Close();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "No se pudo procesar la compra.\n\n" + ex.Message,
                "Error de compras",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private string NextOrderNo(
        Microsoft.Data.Sqlite.SqliteConnection cn,
        Microsoft.Data.Sqlite.SqliteTransaction tx)
    {
        using var c = cn.CreateCommand();
        c.Transaction = tx;
        c.CommandText = "SELECT COALESCE(MAX(id),0)+1 FROM purchase_orders";
        return "OC-" + Convert.ToInt32(c.ExecuteScalar()).ToString("000000");
    }

    private double GridTotal()
    {
        double value = 0;
        foreach (DataGridViewRow row in grid.Rows)
            value += Parse(row.Cells["Total"].Value);
        return value;
    }

    private void InsertItems(
        Microsoft.Data.Sqlite.SqliteConnection cn,
        Microsoft.Data.Sqlite.SqliteTransaction tx,
        int id)
    {
        foreach (DataGridViewRow row in grid.Rows)
        {
            var productId = Convert.ToInt32(row.Cells["Id"].Value);
            var quantity = Parse(row.Cells["Cantidad"].Value);
            var cost = Parse(row.Cells["Costo"].Value);

            if (quantity <= 0)
                continue;

            using var cmd = cn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
INSERT INTO purchase_order_items(
    order_id, product_id, quantity, unit_cost, received_quantity)
VALUES($oid,$pid,$q,$c,0)";

            cmd.Parameters.AddWithValue("$oid", id);
            cmd.Parameters.AddWithValue("$pid", productId);
            cmd.Parameters.AddWithValue("$q", quantity);
            cmd.Parameters.AddWithValue("$c", cost);
            cmd.ExecuteNonQuery();
        }
    }

    private sealed record ReceiveResult(string Message);

    private ReceiveResult Receive(
        Microsoft.Data.Sqlite.SqliteConnection cn,
        Microsoft.Data.Sqlite.SqliteTransaction tx)
    {
        double totalReceivedNow = 0;
        int linesReceived = 0;
        bool hasOverage = false;
        var overageLines = new List<string>();

        foreach (DataGridViewRow row in grid.Rows)
        {
            var productId = Convert.ToInt32(row.Cells["Id"].Value);
            var receiveNow = Parse(row.Cells["RecibirAhora"].Value);
            var cost = Parse(row.Cells["Costo"].Value);
            var remaining = Parse(row.Cells["Pendiente"].Value);

            if (receiveNow <= 0)
                continue;

            if (receiveNow > remaining + 0.000001)
            {
                hasOverage = true;
                overageLines.Add($"{row.Cells["Producto"].Value}: pedido/pendiente {remaining:0.###}, recibido {receiveNow:0.###}");
            }

            if (cost < 0)
                throw new Exception($"El costo no puede ser negativo para {row.Cells["Producto"].Value}.");

            if (InventoryControlService.IsGlobalEnabled)
            {
                using (var u = cn.CreateCommand())
                {
                    u.Transaction = tx;
                    u.CommandText = @"
UPDATE products
SET stock = stock + $q,
    cost_price = $c,
    updated_at = CURRENT_TIMESTAMP
WHERE id = $id";

                    u.Parameters.AddWithValue("$q", receiveNow);
                    u.Parameters.AddWithValue("$c", cost);
                    u.Parameters.AddWithValue("$id", productId);
                    u.ExecuteNonQuery();
                }

                using (var sm = cn.CreateCommand())
                {
                    sm.Transaction = tx;
                sm.CommandText = @"
INSERT INTO stock_movements(
    product_id, movement_type, quantity, reference, user_id)
VALUES($p,'COMPRA',$q,$ref,$u)";

                    sm.Parameters.AddWithValue("$p", productId);
                    sm.Parameters.AddWithValue("$q", receiveNow);
                    sm.Parameters.AddWithValue("$ref", "OC #" + orderId);
                    sm.Parameters.AddWithValue("$u", Session.UserId);
                    sm.ExecuteNonQuery();
                }
            }

            var lineTag = row.Tag as ReceiveLineTag;
            var isAdditional = lineTag?.Additional == true;

            using (var oi = cn.CreateCommand())
            {
                oi.Transaction = tx;
                oi.CommandText = @"
UPDATE purchase_order_items
SET received_quantity = received_quantity + $q,
    unit_cost = $c
WHERE order_id = $oid AND product_id = $pid";

                oi.Parameters.AddWithValue("$q", receiveNow);
                oi.Parameters.AddWithValue("$c", cost);
                oi.Parameters.AddWithValue("$oid", orderId);
                oi.Parameters.AddWithValue("$pid", productId);
                var affected = oi.ExecuteNonQuery();

                // Si no existía en la orden original, lo registramos como
                // producto adicional. quantity=0 indica que no fue pedido;
                // received_quantity conserva lo efectivamente ingresado.
                if (affected == 0 && isAdditional)
                {
                    using var insertExtra = cn.CreateCommand();
                    insertExtra.Transaction = tx;
                    insertExtra.CommandText = @"
INSERT INTO purchase_order_items(
    order_id, product_id, quantity, unit_cost, received_quantity, notes)
VALUES($oid,$pid,0,$c,$q,'PRODUCTO ADICIONAL RECIBIDO')";
                    insertExtra.Parameters.AddWithValue("$oid", orderId);
                    insertExtra.Parameters.AddWithValue("$pid", productId);
                    insertExtra.Parameters.AddWithValue("$c", cost);
                    insertExtra.Parameters.AddWithValue("$q", receiveNow);
                    insertExtra.ExecuteNonQuery();
                }
                else if (affected == 0)
                {
                    throw new Exception($"No se pudo registrar la recepción del producto {productId} en la orden.");
                }
            }

            totalReceivedNow += receiveNow * cost;
            linesReceived++;
        }

        if (linesReceived == 0)
            throw new Exception("Indicá al menos una cantidad REAL recibida. Si llegó todo, usá RECIBIR TODO LO PENDIENTE.");

        var note = receptionNotes.Text.Trim();

        bool pendingAfter;
        using (var pending = cn.CreateCommand())
        {
            pending.Transaction = tx;
            pending.CommandText = @"
SELECT EXISTS(
    SELECT 1 FROM purchase_order_items
    WHERE order_id=$id AND received_quantity < quantity)";
            pending.Parameters.AddWithValue("$id", orderId);
            pendingAfter = Convert.ToInt32(pending.ExecuteScalar()) != 0;
        }

        if ((hasOverage || (closeComplete.Checked && pendingAfter)) && string.IsNullOrWhiteSpace(note))
            throw new Exception("Debés indicar el motivo en NOTA DE RECEPCIÓN para registrar un excedente o cerrar la orden como completa con pendientes.");

        var finalStatus = (!pendingAfter)
            ? (hasOverage ? "RECEIVED_WITH_DIFFERENCE" : "RECEIVED")
            : (closeComplete.Checked ? "RECEIVED_WITH_DIFFERENCE" : "PARTIAL");

        var finalNotes = note;
        if (hasOverage)
            finalNotes = AppendNote(finalNotes, "EXCEDENTE RECIBIDO: " + string.Join(" | ", overageLines));
        if (closeComplete.Checked && pendingAfter)
            finalNotes = AppendNote(finalNotes, "ORDEN CERRADA COMO COMPLETA POR DECISIÓN DEL USUARIO, CON CANTIDADES PENDIENTES SIN RECIBIR.");

        using var st = cn.CreateCommand();
        st.Transaction = tx;
        st.CommandText = @"
UPDATE purchase_orders
SET status = $status,
    notes = CASE WHEN $notes='' THEN notes ELSE $notes END,
    received_total = COALESCE((
        SELECT SUM(received_quantity * unit_cost)
        FROM purchase_order_items
        WHERE order_id=$id
    ),0),
    received_date = CURRENT_TIMESTAMP,
    updated_at = CURRENT_TIMESTAMP
WHERE id=$id";

        st.Parameters.AddWithValue("$status", finalStatus);
        st.Parameters.AddWithValue("$notes", finalNotes);
        st.Parameters.AddWithValue("$id", orderId);
        st.ExecuteNonQuery();

        var statusText = finalStatus == "RECEIVED_WITH_DIFFERENCE"
            ? "La orden quedó CERRADA COMO COMPLETA CON DIFERENCIA."
            : finalStatus == "RECEIVED"
                ? "La orden quedó COMPLETA."
                : "La orden quedó PARCIAL y mantiene lo pendiente.";

        return new ReceiveResult(
            $"Recepción registrada correctamente.\n\n{statusText}\nValor recibido ahora: ${totalReceivedNow:N2}");
    }

    private static string AppendNote(string current, string addition)
    {
        if (string.IsNullOrWhiteSpace(current)) return addition;
        return current + "\n" + addition;
    }

}
