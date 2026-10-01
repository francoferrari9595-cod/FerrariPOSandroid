using FerrarisPOS.Services;
using FerrarisPOS.Data;
using System.Globalization;

namespace FerrarisPOS.Forms;

public sealed class IncomeExpenseForm : Form
{
    private readonly RadioButton income = new() { Text = "INGRESO", AutoSize = true, Checked = true };
    private readonly RadioButton expense = new() { Text = "EGRESO", AutoSize = true };
    private readonly TextBox concept = new();
    private readonly TextBox amount = new();
    private readonly ComboBox method = new();
    private readonly DataGridView history = new();

    public IncomeExpenseForm()
    {
        Text = "FerrarisPOS - Ingresos y Egresos";
        Width = 620;
        Height = 600;
        MinimumSize = new Size(580, 560);
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;
        BackColor = Color.FromArgb(18, 22, 28);

        Build();
        ThemeService.Apply(this);
        SyncMethods();
        LoadHistory();

        income.CheckedChanged += (_, _) => UpdateMovementCaption();
        expense.CheckedChanged += (_, _) => UpdateMovementCaption();
        Shown += (_, _) => concept.Focus();
    }

    private void Build()
    {
        var title = new Label
        {
            Text = "INGRESOS / EGRESOS",
            Location = new Point(22, 18),
            AutoSize = true,
            Font = new Font("Segoe UI", 17, FontStyle.Bold)
        };
        Controls.Add(title);

        var explanation = new Label
        {
            Text = "Registrá dinero que entra o sale durante el turno. Indicá qué movimiento es,\ncuánto dinero corresponde y por qué medio ingresó o salió.",
            Location = new Point(24, 52),
            AutoSize = true,
            Font = new Font("Segoe UI", 9),
            ForeColor = Color.Gainsboro
        };
        Controls.Add(explanation);

        var typeBox = new GroupBox
        {
            Text = "TIPO DE MOVIMIENTO",
            Location = new Point(22, 105),
            Size = new Size(555, 75),
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        income.Location = new Point(22, 31);
        expense.Location = new Point(150, 31);
        typeBox.Controls.Add(income);
        typeBox.Controls.Add(expense);
        Controls.Add(typeBox);

        AddLabel("CONCEPTO / EXPLICACIÓN", 205);
        concept.Location = new Point(230, 200);
        concept.Width = 345;
        concept.Height = 30;
        concept.PlaceholderText = "Ej.: compra de insumos, retiro de efectivo, vuelto...";
        concept.BackColor = Color.White;
        Controls.Add(concept);

        AddLabel("IMPORTE", 250);
        amount.Location = new Point(230, 245);
        amount.Width = 180;
        amount.Height = 30;
        amount.PlaceholderText = "0,00";
        amount.TextAlign = HorizontalAlignment.Right;
        amount.BackColor = Color.White;
        Controls.Add(amount);

        AddLabel("MEDIO", 295);
        method.Location = new Point(230, 290);
        method.Width = 220;
        method.DropDownStyle = ComboBoxStyle.DropDownList;
        Controls.Add(method);

        var help = new Label
        {
            Text = "EFECTIVO = caja física · TARJETA = movimiento por tarjeta ·\nMERCADO PAGO = caja virtual MP · TRANSFERENCIA = movimiento bancario.",
            Location = new Point(230, 327),
            AutoSize = true,
            Font = new Font("Segoe UI", 8.5f),
            ForeColor = Color.Gainsboro
        };
        Controls.Add(help);

        var save = new Button
        {
            Text = "REGISTRAR",
            Location = new Point(230, 382),
            Width = 160,
            Height = 42,
            BackColor = Color.White
        };
        save.Click += (_, _) => SaveMovement();
        Controls.Add(save);

        var close = new Button
        {
            Text = "CERRAR",
            Location = new Point(415, 382),
            Width = 160,
            Height = 42,
            BackColor = Color.White
        };
        close.Click += (_, _) => Close();
        Controls.Add(close);

        var historyLabel = new Label
        {
            Text = "ÚLTIMOS MOVIMIENTOS DEL TURNO",
            Location = new Point(22, 442),
            AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        Controls.Add(historyLabel);

        var viewAll = new Button
        {
            Text = "📋 VER TODOS LOS INGRESOS / EGRESOS",
            Location = new Point(22, 468),
            Width = 555,
            Height = 55,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            BackColor = Color.White,
            Cursor = Cursors.Hand
        };
        viewAll.Click += (_, _) => { using var f = new CashMovementsHistoryForm(); f.ShowDialog(this); LoadHistory(); };
        viewAll.AccessibleName = "Ver todos los ingresos y egresos del turno";
        viewAll.TabIndex = 100;
        Controls.Add(viewAll);

        history.Visible = false;
        history.Location = new Point(22, 530);
        history.Size = new Size(555, 80);
        history.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        history.ReadOnly = true;
        history.AllowUserToAddRows = false;
        history.AllowUserToDeleteRows = false;
        history.RowHeadersVisible = false;
        history.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        history.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        history.MultiSelect = false;
        history.MouseDown += HistoryMouseDown;
        history.ContextMenuStrip = BuildHistoryContextMenu();
        history.BackgroundColor = Color.FromArgb(25, 30, 38);
        var idColumn = new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "ID", Visible = false };
        history.Columns.Add(idColumn);
        history.Columns.Add("Hora", "HORA");
        history.Columns.Add("Tipo", "TIPO");
        history.Columns.Add("Concepto", "CONCEPTO");
        history.Columns.Add("Importe", "IMPORTE");
        history.Columns.Add("Medio", "MEDIO");
        Controls.Add(history);

        AcceptButton = save;
    }

    private void AddLabel(string text, int y)
    {
        Controls.Add(new Label
        {
            Text = text,
            Location = new Point(24, y + 5),
            AutoSize = true,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        });
    }

    private void SyncMethods()
    {
        var selected = method.SelectedItem?.ToString();
        method.Items.Clear();
        method.Items.Add("EFECTIVO");
        method.Items.Add("TARJETA");
        method.Items.Add("TRANSFERENCIA");
        if (CashService.IsMercadoPagoEnabled())
            method.Items.Add("MERCADO PAGO");
        var index = selected is null ? 0 : method.Items.IndexOf(selected);
        method.SelectedIndex = index >= 0 ? index : 0;
    }

    private void UpdateMovementCaption()
    {
        Text = income.Checked
            ? "FerrarisPOS - Ingreso de dinero"
            : "FerrarisPOS - Egreso de dinero";
    }

    private void SaveMovement()
    {
        if (CashService.CurrentSessionId() is null)
        {
            MessageBox.Show(
                "Primero abrí la caja. El ingreso o egreso se registra dentro del turno de caja activo.",
                "Ingresos / Egresos",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var raw = amount.Text.Trim().Replace(".", "").Replace(',', '.');
        if (!double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value) || value <= 0)
        {
            MessageBox.Show("Ingresá un importe válido mayor a cero.", "Ingresos / Egresos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            amount.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(concept.Text))
        {
            MessageBox.Show("Escribí una explicación del ingreso o egreso.", "Ingresos / Egresos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            concept.Focus();
            return;
        }

        try
        {
            var type = income.Checked ? "INCOME" : "EXPENSE";
            var selectedMethod = method.SelectedItem?.ToString() ?? "EFECTIVO";
            CashService.Movement(type, concept.Text.Trim(), value, Session.UserId, selectedMethod);

            MessageBox.Show(
                $"{(income.Checked ? "Ingreso" : "Egreso")} registrado correctamente.\n\nImporte: ${value:N2}\nMedio: {selectedMethod}\nConcepto: {concept.Text.Trim()}",
                "Ingresos / Egresos",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            concept.Clear();
            amount.Clear();
            LoadHistory();
            concept.Focus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Ingresos / Egresos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void HistoryMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        var hit = history.HitTest(e.X, e.Y);
        if (hit.RowIndex >= 0)
        {
            history.ClearSelection();
            history.Rows[hit.RowIndex].Selected = true;
            history.CurrentCell = history.Rows[hit.RowIndex].Cells["Concepto"];
        }
    }

    private ContextMenuStrip BuildHistoryContextMenu()
    {
        var menu = new ContextMenuStrip();
        var voidItem = new ToolStripMenuItem("↩ Anular / borrar movimiento");
        voidItem.Click += (_, _) => VoidSelectedMovement();
        menu.Opening += (_, e) =>
        {
            var hasSelection = history.SelectedRows.Count == 1 && history.SelectedRows[0].Cells["Id"].Value is not null;
            voidItem.Enabled = hasSelection;
            if (!hasSelection) e.Cancel = true;
        };
        menu.Items.Add(voidItem);
        return menu;
    }

    private void VoidSelectedMovement()
    {
        if (history.SelectedRows.Count != 1) return;
        var row = history.SelectedRows[0];
        if (!long.TryParse(Convert.ToString(row.Cells["Id"].Value), out var movementId)) return;

        var type = Convert.ToString(row.Cells["Tipo"].Value);
        var conceptText = Convert.ToString(row.Cells["Concepto"].Value);
        var amountText = Convert.ToString(row.Cells["Importe"].Value);
        using var prompt = new ReasonPromptForm("EXPLICACIÓN DE ANULACIÓN",
            $"¿Por qué se anula este {type?.ToLowerInvariant()}?\n\n{conceptText}\nImporte: {amountText}\n\nLa explicación quedará en el cierre y en el reporte enviado por mail.");
        if (prompt.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            CashService.VoidManualMovement(movementId, Session.UserId, prompt.Reason);
            LoadHistory();
            MessageBox.Show("Movimiento anulado correctamente.", "Ingresos / Egresos", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Ingresos / Egresos", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void LoadHistory()
    {
        history.Rows.Clear();
        var session = CashService.CurrentSessionId();
        if (session is null) return;

        using var cn = Database.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandText = """
            SELECT id, created_at, movement_type, concept, amount, payment_method
            FROM cash_movements
            WHERE session_id=$s AND movement_type IN ('INCOME','EXPENSE')
            ORDER BY id DESC
            LIMIT 5
            """;
        cmd.Parameters.AddWithValue("$s", session.Value);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var dateText = r.GetString(1);
            var date = DateTime.TryParse(dateText, out var dt) ? dt.ToString("HH:mm") : dateText;
            history.Rows.Add(
                r.GetInt64(0),
                date,
                r.GetString(2) == "INCOME" ? "INGRESO" : "EGRESO",
                r.GetString(3),
                $"${r.GetDouble(4):N2}",
                r.GetString(5));
        }
    }
}
