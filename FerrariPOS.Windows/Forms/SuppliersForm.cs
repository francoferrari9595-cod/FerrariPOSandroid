using FerrarisPOS.Services;
using FerrarisPOS.Data;

namespace FerrarisPOS.Forms;

public sealed class SuppliersForm : Form
{
    private readonly DataGridView grid = new();
    private readonly TextBox name = new(), doc = new(), phone = new(), email = new(), address = new();
    private readonly Button productsButton = new();
    private int id;

    public SuppliersForm()
    {
        Text = "FerrarisPOS - Proveedores";
        Width = 1150;
        Height = 650;
        MinimumSize = new Size(1120, 620);
        AutoScroll = true;
        StartPosition = FormStartPosition.CenterParent;
        Build();
        LoadGrid();
        ThemeService.Apply(this);
    }

    private void Build()
    {
        Controls.Add(new Label
        {
            Text = "PROVEEDORES",
            Location = new Point(20, 20),
            AutoSize = true,
            Font = new Font("Segoe UI", 18, FontStyle.Bold)
        });

        F("NOMBRE / RAZÓN SOCIAL", 60, name);
        F("DOCUMENTO", 105, doc);
        F("TELÉFONO", 150, phone);
        F("EMAIL", 195, email);
        F("DIRECCIÓN", 240, address);

        var save = new Button { Text = "GUARDAR", Location = new Point(20, 300), Width = 140, Height = 40 };
        save.Click += (_, _) => Save();
        Controls.Add(save);

        var n = new Button { Text = "NUEVO", Location = new Point(175, 300), Width = 140, Height = 40 };
        n.Click += (_, _) => Clear();
        Controls.Add(n);

        productsButton.Text = "PRODUCTOS DEL PROVEEDOR";
        productsButton.Location = new Point(330, 300);
        productsButton.Width = 200;
        productsButton.Height = 40;
        productsButton.Enabled = false;
        productsButton.Click += (_, _) => OpenProducts();
        Controls.Add(productsButton);

        var delete = new Button { Text = "ELIMINAR", Location = new Point(330, 350), Width = 200, Height = 40 };
        delete.Click += (_, _) => DeleteSelected();
        Controls.Add(delete);

        grid.Location = new Point(450, 35);
        grid.Size = new Size(650, 500);
        grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        grid.ReadOnly = true;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.RowHeadersVisible = false;
        grid.AutoGenerateColumns = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.MultiSelect = false;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.DataError += GridDataError;

        // Explicit text columns. This avoids DataGridView trying to cast a
        // database string into an automatically generated Image column.
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "ID", FillWeight = 8 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Nombre", HeaderText = "NOMBRE", FillWeight = 28 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Documento", HeaderText = "DOCUMENTO", FillWeight = 16 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Telefono", HeaderText = "TELÉFONO", FillWeight = 15 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Email", HeaderText = "EMAIL", FillWeight = 20 });
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Estado", HeaderText = "ESTADO", FillWeight = 13 });

        grid.SelectionChanged += (_, _) => Selected();
        Controls.Add(grid);
    }

    private void F(string label, int y, TextBox textBox)
    {
        Controls.Add(new Label
        {
            Text = label,
            Location = new Point(20, y + 5),
            AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        });
        textBox.Location = new Point(210, y);
        textBox.Width = 220;
        Controls.Add(textBox);
    }

    private void LoadGrid(int selectId = 0)
    {
        grid.Rows.Clear();

        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = @"
SELECT id, name, document, phone, email,
       CASE active WHEN 1 THEN 'ACTIVO' ELSE 'INACTIVO' END
FROM suppliers
ORDER BY name";

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var row = grid.Rows.Add(
                r.GetInt32(0),
                r.IsDBNull(1) ? "" : r.GetString(1),
                r.IsDBNull(2) ? "" : r.GetString(2),
                r.IsDBNull(3) ? "" : r.GetString(3),
                r.IsDBNull(4) ? "" : r.GetString(4),
                r.IsDBNull(5) ? "" : r.GetString(5)
            );
            grid.Rows[row].Tag = r.GetInt32(0);
        }

        if (selectId != 0)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (Convert.ToInt32(row.Cells["Id"].Value) == selectId)
                {
                    row.Selected = true;
                    grid.CurrentCell = row.Cells["Nombre"];
                    break;
                }
            }
        }
        else if (grid.Rows.Count > 0)
        {
            grid.Rows[0].Selected = true;
        }

        Selected();
    }

    private void Selected()
    {
        if (grid.CurrentRow?.Cells["Id"].Value is null)
        {
            id = 0;
            productsButton.Enabled = false;
            return;
        }

        id = Convert.ToInt32(grid.CurrentRow.Cells["Id"].Value);

        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT name, document, phone, email, address FROM suppliers WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);

        using var r = cmd.ExecuteReader();
        if (!r.Read())
        {
            id = 0;
            productsButton.Enabled = false;
            return;
        }

        name.Text = r.IsDBNull(0) ? "" : r.GetString(0);
        doc.Text = r.IsDBNull(1) ? "" : r.GetString(1);
        phone.Text = r.IsDBNull(2) ? "" : r.GetString(2);
        email.Text = r.IsDBNull(3) ? "" : r.GetString(3);
        address.Text = r.IsDBNull(4) ? "" : r.GetString(4);
        productsButton.Enabled = true;
    }

    private void Clear()
    {
        id = 0;
        name.Clear();
        doc.Clear();
        phone.Clear();
        email.Clear();
        address.Clear();
        grid.ClearSelection();
        productsButton.Enabled = false;
        name.Focus();
    }

    private void OpenProducts()
    {
        if (id == 0)
        {
            MessageBox.Show("Guardá y seleccioná primero un proveedor.", "Proveedores",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var f = new SupplierProductsForm(id, name.Text.Trim());
        f.ShowDialog(this);
    }

    private string PromptReason()
    {
        using var f = new Form { Text = "Motivo obligatorio · Eliminar proveedor", Width = 520, Height = 190, StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false };
        var label = new Label { Text = "Indicá el motivo por el que se elimina/desactiva el proveedor:", Left = 16, Top = 15, AutoSize = true };
        var box = new TextBox { Left = 16, Top = 48, Width = 470 };
        var ok = new Button { Text = "CONFIRMAR", Left = 300, Top = 90, Width = 110, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "CANCELAR", Left = 416, Top = 90, Width = 90, DialogResult = DialogResult.Cancel };
        f.Controls.AddRange(new Control[] { label, box, ok, cancel }); f.AcceptButton = ok; f.CancelButton = cancel;
        return f.ShowDialog(this) == DialogResult.OK ? box.Text.Trim() : "";
    }

    private void DeleteSelected()
    {
        if (id <= 0) return;
        var reason = PromptReason();
        if (string.IsNullOrWhiteSpace(reason)) { MessageBox.Show("La eliminación fue cancelada: el motivo es obligatorio.", "Proveedores", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (MessageBox.Show($"¿Desactivar el proveedor seleccionado?\n\nMotivo: {reason}", "Confirmar eliminación", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            using var cn = Database.Open(); using var tx = cn.BeginTransaction();
            string supplierName = "";
            using (var find = cn.CreateCommand()) { find.Transaction = tx; find.CommandText = "SELECT name FROM suppliers WHERE id=$id AND active=1"; find.Parameters.AddWithValue("$id", id); supplierName = Convert.ToString(find.ExecuteScalar()) ?? ""; }
            if (string.IsNullOrWhiteSpace(supplierName)) throw new InvalidOperationException("El proveedor seleccionado no existe o ya fue eliminado.");
            using (var log = cn.CreateCommand()) { log.Transaction = tx; log.CommandText = "INSERT INTO supplier_deletion_log(supplier_id,supplier_name,reason,user_id) VALUES($id,$n,$r,$u)"; log.Parameters.AddWithValue("$id", id); log.Parameters.AddWithValue("$n", supplierName); log.Parameters.AddWithValue("$r", reason); log.Parameters.AddWithValue("$u", Session.UserId); log.ExecuteNonQuery(); }
            using (var cmd = cn.CreateCommand()) { cmd.Transaction = tx; cmd.CommandText = "UPDATE suppliers SET active=0 WHERE id=$id AND active=1"; cmd.Parameters.AddWithValue("$id", id); if (cmd.ExecuteNonQuery()!=1) throw new InvalidOperationException("No se pudo eliminar el proveedor seleccionado."); }
            tx.Commit(); AuditService.Log(Session.UserId,"SUPPLIER_DELETE","PROVEEDORES",$"Proveedor {supplierName} · Motivo: {reason}"); LoadGrid(); Clear();
        }
        catch(Exception ex) { MessageBox.Show(ex.Message,"No se pudo eliminar el proveedor",MessageBoxButtons.OK,MessageBoxIcon.Error); }
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(name.Text))
        {
            MessageBox.Show("Ingresá el nombre del proveedor.", "Proveedores",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            int savedId;

            using (var cn = Database.Open())
            using (var cmd = cn.CreateCommand())
            {
                cmd.CommandText = id == 0
                    ? @"INSERT INTO suppliers(name,document,phone,email,address,active)
                        VALUES($n,$d,$p,$e,$a,1);
                        SELECT last_insert_rowid();"
                    : @"UPDATE suppliers
                        SET name=$n, document=$d, phone=$p, email=$e, address=$a
                        WHERE id=$id;
                        SELECT $id;";

                cmd.Parameters.AddWithValue("$n", name.Text.Trim());
                cmd.Parameters.AddWithValue("$d", doc.Text.Trim());
                cmd.Parameters.AddWithValue("$p", phone.Text.Trim());
                cmd.Parameters.AddWithValue("$e", email.Text.Trim());
                cmd.Parameters.AddWithValue("$a", address.Text.Trim());
                if (id != 0) cmd.Parameters.AddWithValue("$id", id);

                savedId = Convert.ToInt32(cmd.ExecuteScalar());
            }

            LoadGrid(savedId);

            MessageBox.Show(
                "Proveedor guardado correctamente.\n\nAhora podés elegir qué productos trae este proveedor.",
                "Proveedores",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            // Required workflow: immediately after saving, open the product
            // assignment screen for this exact supplier.
            OpenProducts();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "No se pudo guardar el proveedor.\n\n" + ex.Message,
                "Error de proveedores",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void GridDataError(object? sender, DataGridViewDataErrorEventArgs e)
    {
        e.ThrowException = false;
        e.Cancel = true;
    }
}
