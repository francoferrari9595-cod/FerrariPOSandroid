using FerrarisPOS.Data;
using FerrarisPOS.Services;
using System.Security.Cryptography;
using System.Text;

namespace FerrarisPOS.Forms;

public class UsersForm : Form
{
    private readonly DataGridView grid = new();
    private readonly TextBox username = new(), fullName = new(), password = new();
    private readonly SafeComboBox role = new();
    private readonly CheckedListBox permissions = new();
    private readonly CheckBox controlTotal = new();
    private int id;

    public UsersForm()
    {
        if (!Session.IsAdmin)
        {
            MessageBox.Show("Solo el usuario ADMIN puede administrar usuarios.", "Permisos",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            Close();
            return;
        }

        Text = "FerrarisPOS - Usuarios y Cajeros";
        Width = 1050; Height = 650; MinimumSize = new Size(1050, 650); AutoScroll = true; BackColor = Color.Gainsboro;
        Build(); LoadGrid();
    }

    private void Build()
    {
        Controls.Add(new Label
        {
            Text = "USUARIOS / CAJEROS",
            Location = new Point(20, 20), AutoSize = true,
            Font = new Font("Segoe UI", 18, FontStyle.Bold)
        });
        Controls.Add(new Label
        {
            Text = "Solo ADMIN puede crear, modificar o desactivar usuarios.",
            Location = new Point(20, 55), AutoSize = true, ForeColor = Color.DimGray
        });
        Controls.Add(new Label
        {
            Text = "Los usuarios nuevos ingresan como CAJERO.",
            Location = new Point(20, 76), AutoSize = true, ForeColor = Color.DimGray
        });

        Field("USUARIO", 95, username);
        Field("NOMBRE", 145, fullName);
        Field("CONTRASEÑA", 195, password);
        Controls.Add(new Label { Text = "ROL", Location = new Point(20, 235), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        role.Location = new Point(140, 230); role.Width = 300; role.DropDownStyle = ComboBoxStyle.DropDownList; role.Items.AddRange(new object[] { "ADMIN", "ENCARGADO", "CAJERO", "MOZO" }); if (role.Items.Count > 2) role.SelectedIndex = 2; else if (role.Items.Count > 0) role.SelectedIndex = 0; Controls.Add(role);
        Controls.Add(new Label
        {
            Text = "PERMISOS\r\nESPECIALES",
            Location = new Point(20, 274),
            Size = new Size(105, 42),
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        });
        permissions.Location = new Point(140, 275); permissions.Size = new Size(300, 225);
        permissions.MultiColumn = true; permissions.ColumnWidth = 145;
        permissions.Items.AddRange(new object[] {
            "VENTAS", "CLIENTES", "PRODUCTOS", "INVENTARIO", "CAJA", "REPORTES",
            "CONFIGURACION", "USUARIOS", "PROVEEDORES", "RESPALDO", "EDITAR_PRECIOS",
            "RESERVAS", "MERMA", "COMPRAS", "PROMOCIONES", "MESAS", "SALON",
            "DEVOLUCIONES", "ARQUEOS", "EXPORTAR", "IMPORTAR_INVENTARIO",
            "MOVIMIENTOS_STOCK", "CONTEO_FISICO", "CAMBIAR_PRECIOS", "COBRAR", "BUSCAR_PRODUCTOS",
            "BACKUP", "CONTROL_TOTAL"
        });
        permissions.CheckOnClick = true; Controls.Add(permissions);

        controlTotal.Text = "CONTROL TOTAL (todas las funciones)";
        controlTotal.Location = new Point(140, 505); controlTotal.AutoSize = true;
        controlTotal.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        controlTotal.CheckedChanged += (_, _) =>
        {
            if (!controlTotal.Focused) return;
            for (int i = 0; i < permissions.Items.Count; i++)
                permissions.SetItemChecked(i, controlTotal.Checked);
        };
        Controls.Add(controlTotal);

        var save = new Button { Text = "GUARDAR", Location = new Point(20, 535), Width = 130, Height = 40 };
        save.Click += (_, _) => Save(); Controls.Add(save);

        var n = new Button { Text = "NUEVO", Location = new Point(165, 535), Width = 130, Height = 40 };
        n.Click += (_, _) => Clear(); Controls.Add(n);

        var deactivate = new Button { Text = "DESACTIVAR", Location = new Point(310, 535), Width = 130, Height = 40 };
        deactivate.Click += (_, _) => ToggleActive(); Controls.Add(deactivate);

        grid.Location = new Point(470, 40);
        grid.Size = new Size(530, 535);
        grid.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        grid.ReadOnly = true;
        grid.AutoGenerateColumns = true;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.BackgroundColor = Color.White;
        grid.SelectionChanged += Selected;
        Controls.Add(grid);
    }

    private void Field(string label, int y, TextBox t)
    {
        Controls.Add(new Label
        {
            Text = label, Location = new Point(20, y + 5), AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        });
        t.Location = new Point(140, y);
        t.Width = 300;
        t.UseSystemPasswordChar = label == "CONTRASEÑA";
        Controls.Add(t);
    }

    private void LoadGrid()
    {
        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT id,username,full_name,role,permissions,CASE active WHEN 1 THEN 'ACTIVO' ELSE 'INACTIVO' END AS estado FROM users ORDER BY username";
        using var r = cmd.ExecuteReader();
        var dt = new System.Data.DataTable();
        dt.Load(r);
        grid.DataSource = dt;
    }

    private void ApplyAdminEditLock()
    {
        var isAdminAccount = string.Equals(username.Text.Trim(), "admin", StringComparison.OrdinalIgnoreCase);
        username.ReadOnly = isAdminAccount;
        fullName.ReadOnly = isAdminAccount;
        role.Enabled = !isAdminAccount;
        permissions.Enabled = !isAdminAccount;
        controlTotal.Enabled = !isAdminAccount;
        if (isAdminAccount)
            password.PlaceholderText = "Solo se modifica la contraseña del ADMIN";
        else
            password.PlaceholderText = "Nueva contraseña (opcional)";
    }

    private void Selected(object? s, EventArgs e)
    {
        if (grid.CurrentRow?.Cells.Count >= 3 && grid.CurrentRow.Cells[0].Value is not null)
        {
            id = Convert.ToInt32(grid.CurrentRow.Cells[0].Value);
            username.Text = Convert.ToString(grid.CurrentRow.Cells[1].Value) ?? "";
            fullName.Text = Convert.ToString(grid.CurrentRow.Cells[2].Value) ?? "";
            password.Clear();
            if (grid.CurrentRow.Cells.Count > 3) role.SelectedItem = Convert.ToString(grid.CurrentRow.Cells[3].Value) ?? "CAJERO";
            for (int i = 0; i < permissions.Items.Count; i++) permissions.SetItemChecked(i, false);
            var raw = grid.CurrentRow.Cells.Count > 4 ? Convert.ToString(grid.CurrentRow.Cells[4].Value) ?? "" : "";
            var hasControlTotal = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(x => string.Equals(x, "CONTROL_TOTAL", StringComparison.OrdinalIgnoreCase));
            controlTotal.Checked = hasControlTotal;
            if (hasControlTotal)
                for (int i = 0; i < permissions.Items.Count; i++) permissions.SetItemChecked(i, true);
            foreach (var item in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                for (int i = 0; i < permissions.Items.Count; i++)
                    if (string.Equals(permissions.Items[i]?.ToString(), item, StringComparison.OrdinalIgnoreCase))
                        permissions.SetItemChecked(i, true);
            ApplyAdminEditLock();
        }
    }

    private void Clear()
    {
        id = 0;
        username.Clear();
        fullName.Clear();
        password.Clear();
        role.SelectedItem = "CAJERO";
        for (int i = 0; i < permissions.Items.Count; i++) permissions.SetItemChecked(i, false);
        controlTotal.Checked = false;
        username.ReadOnly = false;
        fullName.ReadOnly = false;
        role.Enabled = true;
        permissions.Enabled = true;
        controlTotal.Enabled = true;
        password.PlaceholderText = "Nueva contraseña (opcional)";
        username.Focus();
    }

    private void Save()
    {
        if (!Session.IsAdmin) return;
        if (string.IsNullOrWhiteSpace(username.Text) || string.IsNullOrWhiteSpace(fullName.Text))
        {
            MessageBox.Show("Completá usuario y nombre."); return;
        }

        try
        {
            using var cn = Database.Open();
            using var cmd = cn.CreateCommand();

            if (id == 0)
            {
                if (string.IsNullOrWhiteSpace(password.Text))
                {
                    MessageBox.Show("Ingresá una contraseña."); return;
                }

                cmd.CommandText = "INSERT INTO users(username,full_name,password_hash,role,permissions,active) VALUES($u,$n,$p,$role,$perms,1)";
                cmd.Parameters.AddWithValue("$p", Hash(password.Text));
                cmd.Parameters.AddWithValue("$u", username.Text.Trim());
                cmd.Parameters.AddWithValue("$role", role.Text);
                cmd.Parameters.AddWithValue("$n", fullName.Text.Trim());
                var selectedPermissionsNew = permissions.CheckedItems.Cast<object>().Select(x => x.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                if (controlTotal.Checked && !selectedPermissionsNew.Contains("CONTROL_TOTAL", StringComparer.OrdinalIgnoreCase))
                    selectedPermissionsNew.Add("CONTROL_TOTAL");
                cmd.Parameters.AddWithValue("$perms", string.Join(",", selectedPermissionsNew));
            }
            else if (string.Equals(username.Text.Trim(), "admin", StringComparison.OrdinalIgnoreCase))
            {
                // La cuenta ADMIN es estructural: su usuario, nombre, rol y permisos
                // no se pueden modificar desde la gestión de usuarios. Solo la contraseña.
                if (string.IsNullOrWhiteSpace(password.Text))
                {
                    MessageBox.Show("Para ADMIN solamente se puede modificar la contraseña. Ingresá la nueva contraseña.", "Usuarios", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                cmd.CommandText = "UPDATE users SET password_hash=$p WHERE id=$id";
                cmd.Parameters.AddWithValue("$p", Hash(password.Text));
                cmd.Parameters.AddWithValue("$id", id);
            }
            else
            {
                cmd.CommandText = string.IsNullOrWhiteSpace(password.Text)
                    ? "UPDATE users SET username=$u,full_name=$n,role=$role,permissions=$perms WHERE id=$id"
                    : "UPDATE users SET username=$u,full_name=$n,role=$role,password_hash=$p,permissions=$perms WHERE id=$id";

                if (!string.IsNullOrWhiteSpace(password.Text))
                    cmd.Parameters.AddWithValue("$p", Hash(password.Text));

                cmd.Parameters.AddWithValue("$id", id);
                cmd.Parameters.AddWithValue("$u", username.Text.Trim());
                cmd.Parameters.AddWithValue("$role", role.Text);
                cmd.Parameters.AddWithValue("$n", fullName.Text.Trim());
                var selectedPermissions = permissions.CheckedItems.Cast<object>().Select(x => x.ToString()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                if (controlTotal.Checked && !selectedPermissions.Contains("CONTROL_TOTAL", StringComparer.OrdinalIgnoreCase))
                    selectedPermissions.Add("CONTROL_TOTAL");
                cmd.Parameters.AddWithValue("$perms", string.Join(",", selectedPermissions));
            }
            cmd.ExecuteNonQuery();

            LoadGrid();
            Clear();
            MessageBox.Show("Usuario guardado correctamente.");
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo guardar el usuario:\n" + ex.Message,
                "Usuarios", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ToggleActive()
    {
        if (id <= 0) return;
        if (id == Session.UserId)
        {
            MessageBox.Show("No podés desactivar el usuario con el que estás conectado.",
                "Usuarios", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            using var cn = Database.Open();
            using var cmd = cn.CreateCommand();
            cmd.CommandText = "UPDATE users SET active=CASE active WHEN 1 THEN 0 ELSE 1 END WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
            LoadGrid();
            Clear();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Usuarios", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
