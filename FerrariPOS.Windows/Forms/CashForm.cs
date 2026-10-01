using FerrarisPOS.Services;
using System.Globalization;

namespace FerrarisPOS.Forms;

public class CashForm : Form
{
    private readonly RichTextBox cashSummary = new(), mercadoPagoSummary = new();
    private readonly Label mercadoPagoExpectedLabel = new();
    private readonly TextBox opening = new(), mercadoPagoOpening = new(), mercadoPagoRetention = new(), concept = new(), amount = new(), counted = new(), mercadoPagoCounted = new();
    private readonly CheckBox enableMercadoPago = new();
    private readonly SafeComboBox movementMethod = new();

    public CashForm()
    {
        Text = "FerrarisPOS - Caja y Arqueo";
        Width = 1180; Height = 800;
        BackColor = Color.Gainsboro;
        Build();
        ThemeService.Apply(this);
        ApplyCashUiAccent();
        RefreshData();
    }

    private readonly ToolTip hints = new();

    private void Build()
    {
        AutoScroll = false;
        MinimumSize = new Size(900, 650);
        Width = 1180;
        Height = 800;
        StartPosition = FormStartPosition.CenterParent;
        Padding = new Padding(14);

        hints.AutoPopDelay = 9000;
        hints.InitialDelay = 350;
        hints.ReshowDelay = 150;
        hints.ShowAlways = true;
        hints.IsBalloon = false;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Color.FromArgb(18, 22, 28),
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 18));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 18));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        Controls.Add(root);

        // ------------------------------------------------------------
        // ENCABEZADO
        // ------------------------------------------------------------
        var title = new Label
        {
            Text = "CAJA · TURNO Y ARQUEO",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 17, FontStyle.Bold),
            Margin = Padding.Empty,
            Padding = new Padding(8, 0, 0, 0)
        };
        root.Controls.Add(title, 0, 0);
        hints.SetToolTip(title, "Panel de caja: apertura, movimientos, Mercado Pago y cierre del turno.");

        // ------------------------------------------------------------
        // RESÚMENES: DOS BLOQUES, SIN TEXTOS SUPERPUESTOS
        // ------------------------------------------------------------
        var summaries = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 4, 0, 4),
            Padding = Padding.Empty,
            BackColor = Color.FromArgb(18, 22, 28)
        };
        summaries.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        summaries.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        root.Controls.Add(summaries, 0, 1);

        var cashBox = new GroupBox
        {
            Text = "CAJA FÍSICA",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 6, 0),
            Padding = new Padding(10, 20, 10, 10),
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        cashSummary.Dock = DockStyle.Fill;
        cashSummary.ReadOnly = true;
        cashSummary.BorderStyle = BorderStyle.None;
        cashSummary.BackColor = Color.FromArgb(18, 22, 28);
        cashSummary.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        cashSummary.ScrollBars = RichTextBoxScrollBars.Vertical;
        cashSummary.Margin = Padding.Empty;
        cashBox.Controls.Add(cashSummary);
        summaries.Controls.Add(cashBox, 0, 0);
        hints.SetToolTip(cashSummary, "Resumen de apertura, ventas, ingresos, egresos y efectivo esperado del turno.");

        var mpBox = new GroupBox
        {
            Text = "MERCADO PAGO",
            Dock = DockStyle.Fill,
            Margin = new Padding(6, 0, 0, 0),
            Padding = new Padding(10, 20, 10, 10),
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        var mpInner = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };
        mpInner.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mpInner.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        mercadoPagoSummary.Dock = DockStyle.Fill;
        mercadoPagoSummary.ReadOnly = true;
        mercadoPagoSummary.BorderStyle = BorderStyle.None;
        mercadoPagoSummary.BackColor = Color.FromArgb(18, 22, 28);
        mercadoPagoSummary.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        mercadoPagoSummary.ScrollBars = RichTextBoxScrollBars.Vertical;
        mercadoPagoSummary.Margin = Padding.Empty;
        mpInner.Controls.Add(mercadoPagoSummary, 0, 0);

        var retentionPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };
        retentionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        retentionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        retentionPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var retentionTitle = new Label
        {
            Text = "RETENCIÓN MP (%)",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            Margin = Padding.Empty
        };
        mercadoPagoRetention.Dock = DockStyle.Fill;
        mercadoPagoRetention.Margin = new Padding(3, 4, 3, 4);
        mercadoPagoRetention.PlaceholderText = "0";
        mercadoPagoRetention.TextAlign = HorizontalAlignment.Right;
        mercadoPagoRetention.ReadOnly = !Session.IsAdmin;
        mercadoPagoRetention.Enabled = Session.IsAdmin;
        var saveRetention = new Button
        {
            Text = "GUARDAR",
            Dock = DockStyle.Fill,
            Margin = new Padding(3, 2, 0, 2),
            TabStop = true
        };
        saveRetention.Enabled = Session.IsAdmin;
        saveRetention.Click += (_, _) => SaveMercadoPagoRetention();
        retentionPanel.Controls.Add(retentionTitle, 0, 0);
        retentionPanel.Controls.Add(mercadoPagoRetention, 1, 0);
        retentionPanel.Controls.Add(saveRetention, 2, 0);
        mpInner.Controls.Add(retentionPanel, 0, 1);
        mpBox.Controls.Add(mpInner);
        summaries.Controls.Add(mpBox, 1, 0);

        hints.SetToolTip(mercadoPagoSummary, "Resumen de las ventas y saldo esperado de Mercado Pago del turno.");
        hints.SetToolTip(mercadoPagoRetention, "Porcentaje que ADMINISTRADOR puede configurar para descontarlo del saldo esperado de Mercado Pago.");
        hints.SetToolTip(saveRetention, "Guarda el porcentaje de retención de Mercado Pago.");

        // ------------------------------------------------------------
        // APERTURA
        // ------------------------------------------------------------
        var openingPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            Margin = new Padding(0, 4, 0, 4),
            Padding = new Padding(4),
            BackColor = Color.FromArgb(18, 22, 28)
        };
        openingPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        openingPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 39));
        openingPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16));
        openingPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 17));
        openingPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        openingPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(openingPanel, 0, 2);

        var openingLabel = new Label
        {
            Text = "FONDO INICIAL EFECTIVO",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Margin = Padding.Empty
        };
        openingPanel.Controls.Add(openingLabel, 0, 0);
        opening.Dock = DockStyle.Fill;
        opening.Margin = new Padding(0, 3, 10, 2);
        opening.PlaceholderText = "Ej.: 50000";
        openingPanel.Controls.Add(opening, 0, 1);

        var mpOpenLabel = new Label
        {
            Text = "MERCADO PAGO · SALDO INICIAL",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Margin = Padding.Empty
        };
        openingPanel.Controls.Add(mpOpenLabel, 1, 0);
        var mpOpenArea = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        mpOpenArea.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        mpOpenArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        enableMercadoPago.Text = "ACTIVAR CAJA PARALELA";
        enableMercadoPago.Dock = DockStyle.Fill;
        enableMercadoPago.Margin = new Padding(0, 2, 8, 2);
        enableMercadoPago.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        enableMercadoPago.AutoSize = false;
        enableMercadoPago.TextAlign = ContentAlignment.MiddleLeft;
        enableMercadoPago.CheckedChanged += (_, _) =>
        {
            mercadoPagoOpening.Enabled = enableMercadoPago.Checked;
            mercadoPagoCounted.Enabled = enableMercadoPago.Checked;
            mercadoPagoRetention.Enabled = Session.IsAdmin;
            mercadoPagoRetention.ReadOnly = !Session.IsAdmin;
        };
        mpOpenArea.Controls.Add(enableMercadoPago, 0, 0);
        mercadoPagoOpening.Dock = DockStyle.Fill;
        mercadoPagoOpening.Margin = new Padding(0, 2, 10, 2);
        mercadoPagoOpening.PlaceholderText = "0,00";
        mercadoPagoOpening.Enabled = false;
        mpOpenArea.Controls.Add(mercadoPagoOpening, 1, 0);
        openingPanel.Controls.Add(mpOpenArea, 1, 1);

        var open = new Button
        {
            Text = "ABRIR CAJA",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 2, 10, 2),
            Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        open.Click += (_, _) => OpenCash();
        openingPanel.Controls.Add(open, 2, 1);

        var openHint = new Label
        {
            Text = "Turno actual",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            Margin = Padding.Empty
        };
        openingPanel.Controls.Add(openHint, 3, 0);
        var openInfo = new Label
        {
            Text = "1) Ingresá el fondo.  2) Activá MP si la vas a usar.  3) Abrí la caja.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8.5f),
            Margin = Padding.Empty,
            AutoEllipsis = true
        };
        openingPanel.Controls.Add(openInfo, 3, 1);

        hints.SetToolTip(opening, "Monto de efectivo físico con el que comienza el turno.");
        hints.SetToolTip(enableMercadoPago, "Activa una caja paralela para registrar el saldo de Mercado Pago.");
        hints.SetToolTip(mercadoPagoOpening, "Saldo de Mercado Pago disponible al abrir el turno.");
        hints.SetToolTip(open, "Abre el turno y registra el fondo inicial.");

        // ------------------------------------------------------------
        // MOVIMIENTOS
        // ------------------------------------------------------------
        var movementPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 3,
            Margin = new Padding(0, 4, 0, 4),
            Padding = new Padding(4),
            BackColor = Color.FromArgb(18, 22, 28)
        };
        movementPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        movementPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14));
        movementPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16));
        movementPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14));
        movementPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 14));
        movementPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12));
        movementPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        movementPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        movementPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(movementPanel, 0, 3);

        var movementInfo = new Label
        {
            Text = "AQUÍ SE REGISTRAN LOS INGRESOS Y EGRESOS DE DINERO",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(255, 193, 7),
            Padding = new Padding(2, 0, 0, 0),
            Margin = Padding.Empty
        };
        movementPanel.Controls.Add(movementInfo, 0, 0);
        movementPanel.SetColumnSpan(movementInfo, 6);
        hints.SetToolTip(movementInfo, "Usá esta sección para registrar manualmente dinero que entra o sale de la caja. Elegí el medio correspondiente.");

        AddMovementField(movementPanel, 0, "CONCEPTO", concept, "Escribí qué ingreso o egreso de dinero estás registrando (ej.: retiro, gasto, devolución).");
        AddMovementField(movementPanel, 1, "IMPORTE", amount, "Importe del movimiento.");

        var medioLabel = new Label
        {
            Text = "MEDIO",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Margin = Padding.Empty
        };
        movementPanel.Controls.Add(medioLabel, 2, 1);
        movementMethod.Dock = DockStyle.Fill;
        movementMethod.Margin = new Padding(0, 3, 10, 2);
        movementMethod.DropDownStyle = ComboBoxStyle.DropDownList;
        movementMethod.Items.AddRange(new object[] { "EFECTIVO", "TARJETA", "TRANSFERENCIA" });
        if (movementMethod.Items.Count > 0) movementMethod.SelectedIndex = 0;
        movementPanel.Controls.Add(movementMethod, 2, 2);

        var inc = new Button
        {
            Text = "INGRESO",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 2, 6, 2),
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        inc.Click += (_, _) => Move("INCOME");
        movementPanel.Controls.Add(inc, 3, 2);

        var exp = new Button
        {
            Text = "EGRESO",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 2, 0, 2),
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        exp.Click += (_, _) => Move("EXPENSE");
        movementPanel.Controls.Add(exp, 4, 2);

        var viewMovements = new Button
        {
            Text = "VER INGRESOS\n/ EGRESOS",
            Dock = DockStyle.Fill,
            Margin = new Padding(4, 2, 0, 2),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
        };
        viewMovements.Click += (_, _) => { using var f = new CashMovementsHistoryForm(); f.ShowDialog(this); RefreshData(); };
        movementPanel.Controls.Add(viewMovements, 5, 2);

        hints.SetToolTip(movementMethod, "Medio utilizado para el ingreso o egreso.");
        hints.SetToolTip(inc, "Registra un ingreso manual en la caja.");
        hints.SetToolTip(exp, "Registra un egreso manual en la caja.");
        hints.SetToolTip(viewMovements, "Abre el historial completo de ingresos y egresos del turno. Desde ahí podés anular un movimiento y explicar el motivo.");

        // ------------------------------------------------------------
        // ARQUEO + ACCIONES
        // ------------------------------------------------------------
        var bottom = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0, 4, 0, 4),
            Padding = Padding.Empty,
            BackColor = Color.FromArgb(18, 22, 28)
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        root.Controls.Add(bottom, 0, 4);

        var cashArqueo = new GroupBox
        {
            Text = "ARQUEO · EFECTIVO",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 6, 0),
            Padding = new Padding(10, 20, 10, 10),
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        var cashCountPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };
        cashCountPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        cashCountPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        cashCountPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        cashCountPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var countedLabel = new Label
        {
            Text = "DINERO CONTADO",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            Margin = Padding.Empty
        };
        cashCountPanel.Controls.Add(countedLabel, 0, 0);
        counted.Dock = DockStyle.Fill;
        counted.Margin = new Padding(0, 2, 8, 2);
        counted.PlaceholderText = "Importe físico";
        cashCountPanel.Controls.Add(counted, 0, 1);
        var countMoney = new Button
        {
            Text = "CONTAR\nBILLETES",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 2, 0, 2),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
        };
        countMoney.Click += (_, _) => { using var f = new CashCountForm(); if (f.ShowDialog(this) == DialogResult.OK) counted.Text = f.Total.ToString("0.00"); };
        cashCountPanel.Controls.Add(countMoney, 1, 1);
        cashArqueo.Controls.Add(cashCountPanel);
        bottom.Controls.Add(cashArqueo, 0, 0);

        var mpArqueo = new GroupBox
        {
            Text = "ARQUEO · MERCADO PAGO",
            Dock = DockStyle.Fill,
            Margin = new Padding(6, 0, 6, 0),
            Padding = new Padding(10, 20, 10, 10),
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        var mpCountPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };
        mpCountPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        mpCountPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        mpCountPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        mpCountPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var mpCountLabel = new Label
        {
            Text = "SALDO INFORMADO",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
            Margin = Padding.Empty
        };
        mpCountPanel.Controls.Add(mpCountLabel, 0, 0);
        mercadoPagoCounted.Dock = DockStyle.Fill;
        mercadoPagoCounted.Margin = new Padding(0, 2, 8, 2);
        mercadoPagoCounted.PlaceholderText = "Saldo de MP";
        mercadoPagoCounted.Enabled = false;
        mpCountPanel.Controls.Add(mercadoPagoCounted, 0, 1);
        mercadoPagoExpectedLabel.Dock = DockStyle.Fill;
        mercadoPagoExpectedLabel.TextAlign = ContentAlignment.MiddleCenter;
        mercadoPagoExpectedLabel.Font = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        mercadoPagoExpectedLabel.Margin = Padding.Empty;
        mpCountPanel.Controls.Add(mercadoPagoExpectedLabel, 1, 1);
        mpArqueo.Controls.Add(mpCountPanel);
        bottom.Controls.Add(mpArqueo, 1, 0);

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(6, 0, 0, 0),
            Padding = Padding.Empty
        };
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 29));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 29));

        // CIERRE DE TURNO + CORTE Z GENERAL quedan visualmente separados para no confundirlos.
        var closeSplit = new TableLayoutPanel
        { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0,0,0,4), Padding = Padding.Empty };
        closeSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        closeSplit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var close = new Button
        {
            Text = "CERRAR CAJA\n(TURNO)",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 4, 0),
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        close.Click += (_, _) => CloseCash();
        closeSplit.Controls.Add(close, 0, 0);
        var zClose = new Button
        {
            Text = "CORTE Z\nGENERAL DEL DÍA",
            Dock = DockStyle.Fill,
            Margin = new Padding(4, 0, 0, 0),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
        };
        zClose.Click += (_, _) => GeneralDailyClosing();
        closeSplit.Controls.Add(zClose, 1, 0);
        actions.Controls.Add(closeSplit, 0, 0);
        var refresh = new Button
        {
            Text = "ACTUALIZAR",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 2, 0, 4),
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        refresh.Click += (_, _) => RefreshData();
        actions.Controls.Add(refresh, 0, 1);
        var switchUser = new Button
        {
            Text = "CAMBIAR USUARIO",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 2, 0, 0),
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
        };
        switchUser.Click += (_, _) => ChangeUser();
        actions.Controls.Add(switchUser, 0, 2);
        bottom.Controls.Add(actions, 2, 0);

        hints.SetToolTip(counted, "PASO 1 · Contá físicamente billetes y monedas y escribí aquí el total real de efectivo que tenés en la caja.");
        hints.SetToolTip(countMoney, "Abre el contador de billetes y monedas y coloca el total en el campo.");
        hints.SetToolTip(mercadoPagoCounted, "PASO 2 · Consultá el saldo disponible de Mercado Pago e ingresalo aquí para compararlo con el saldo esperado.");
        hints.SetToolTip(mercadoPagoExpectedLabel, "Saldo de Mercado Pago que el sistema espera según las operaciones del turno.");
        hints.SetToolTip(close, "Cierra únicamente el turno de la caja actual, realiza el arqueo y genera sus reportes.");
        hints.SetToolTip(zClose, "CORTE Z GENERAL: consolida las ventas, medios de pago, movimientos y turnos de TODOS los cajeros del día. No cierra ninguna caja.");
        hints.SetToolTip(refresh, "Actualiza todos los importes mostrados sin cerrar la caja.");
        hints.SetToolTip(switchUser, "Permite cambiar de usuario cuando la caja está cerrada.");

        void AddMovementField(TableLayoutPanel panel, int column, string caption, TextBox field, string hint)
        {
            var label = new Label
            {
                Text = caption,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.BottomLeft,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                Margin = Padding.Empty
            };
            panel.Controls.Add(label, column, 1);
            field.Dock = DockStyle.Fill;
            field.Margin = new Padding(0, 3, 10, 2);
            field.PlaceholderText = column == 0 ? "Ej.: retiro / gasto / ingreso de dinero" : "0,00";
            panel.Controls.Add(field, column, 2);
            hints.SetToolTip(field, hint);
        }
    }

    private static IEnumerable<Control> EnumerateControls(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (var descendant in EnumerateControls(child))
                yield return descendant;
        }
    }

    private void SyncMovementMethods()
    {
        var selected = movementMethod.SelectedItem?.ToString();
        var desired = new List<string> { "EFECTIVO", "TARJETA", "TRANSFERENCIA" };
        if (CashService.IsMercadoPagoEnabled()) desired.Add("MERCADO PAGO");

        movementMethod.BeginUpdate();
        try
        {
            movementMethod.Items.Clear();
            movementMethod.Items.AddRange(desired.Cast<object>().ToArray());
            var index = selected is null ? -1 : desired.FindIndex(x => x.Equals(selected, StringComparison.OrdinalIgnoreCase));
            movementMethod.SelectedIndex = index >= 0 ? index : 0;
        }
        finally
        {
            movementMethod.EndUpdate();
        }
    }

    private void ApplyCashUiAccent()
    {
        var yellow = Color.FromArgb(255, 193, 7);
        var green = Color.FromArgb(80, 220, 120);
        var cyan = Color.FromArgb(85, 205, 255);
        var dark = Color.FromArgb(12, 16, 22);

        foreach (Control control in EnumerateControls(this))
        {
            if (control is GroupBox gb)
            {
                gb.ForeColor = Color.White;
                gb.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
                if (gb.Text.Contains("ARQUEO", StringComparison.OrdinalIgnoreCase))
                    gb.ForeColor = cyan;
                else
                    gb.ForeColor = Color.White;
            }
            else if (control is Label label && label.Text.Length > 0)
            {
                var text = label.Text;
                if (text.Contains("FONDO INICIAL", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("SALDO INICIAL", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("DINERO CONTADO", StringComparison.OrdinalIgnoreCase) ||
                    text.Contains("SALDO INFORMADO", StringComparison.OrdinalIgnoreCase))
                    label.ForeColor = green;
                else if (text is "CONCEPTO" or "IMPORTE" or "MEDIO" ||
                         text.Contains("AQUÍ SE REGISTRAN", StringComparison.OrdinalIgnoreCase))
                    label.ForeColor = yellow;
                else if (text.Contains("ARQUEO", StringComparison.OrdinalIgnoreCase))
                    label.ForeColor = cyan;
                else
                    label.ForeColor = Color.WhiteSmoke;
            }
            else if (control is CheckBox check)
            {
                check.ForeColor = Color.White;
            }
            else if (control is Button button)
            {
                bool isMovement = string.Equals(button.Text, "INGRESO", StringComparison.OrdinalIgnoreCase)
                                  || string.Equals(button.Text, "EGRESO", StringComparison.OrdinalIgnoreCase);
                bool isGeneralZ = button.Text.Contains("CORTE Z", StringComparison.OrdinalIgnoreCase);
                if (isGeneralZ)
                {
                    var zCyan = Color.FromArgb(85, 205, 255);
                    button.BackColor = Color.FromArgb(8, 28, 38);
                    button.ForeColor = zCyan;
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = zCyan;
                    button.FlatAppearance.BorderSize = 2;
                    button.FlatAppearance.MouseOverBackColor = Color.FromArgb(15, 58, 75);
                }
                else if (isMovement)
                {
                    var neonGreen = Color.FromArgb(57, 255, 20);
                    button.BackColor = Color.FromArgb(8, 28, 12);
                    button.ForeColor = neonGreen;
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = neonGreen;
                    button.FlatAppearance.BorderSize = 2;
                    button.FlatAppearance.MouseOverBackColor = Color.FromArgb(18, 65, 24);
                }
                else
                {
                    button.BackColor = dark;
                    button.ForeColor = yellow;
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = yellow;
                    button.FlatAppearance.BorderSize = 1;
                    button.FlatAppearance.MouseOverBackColor = Color.FromArgb(42, 35, 10);
                }
            }
        }

        // Los dos resúmenes son información principal: blanco, sin texto oculto
        // detrás de la tarjeta y con alto contraste para el importe esperado.
        cashSummary.ForeColor = Color.White;
        mercadoPagoSummary.ForeColor = Color.White;
        mercadoPagoExpectedLabel.ForeColor = cyan;
        enableMercadoPago.ForeColor = Color.White;
    }

    private void RefreshData()
    {
        var x = CashService.Summary();
        SyncMovementMethods();
        if (!CashService.IsOpen())
        {
            cashSummary.Text = "CAJA CERRADA\n\nAbrí la caja para iniciar el turno.";
            mercadoPagoSummary.Text = "MERCADO PAGO\n\nNo hay una caja abierta.\n\nRETENCIÓN CONFIGURADA: " + CashService.ConfiguredMercadoPagoRetentionPercent().ToString("0.##", CultureInfo.InvariantCulture) + "%";
            mercadoPagoRetention.Text = CashService.ConfiguredMercadoPagoRetentionPercent().ToString("0.##", CultureInfo.InvariantCulture);
            mercadoPagoRetention.Enabled = Session.IsAdmin;
            mercadoPagoRetention.ReadOnly = !Session.IsAdmin;
            mercadoPagoExpectedLabel.Text = "SALDO ESPERADO: $0,00";
            return;
        }

        var session = CashService.CurrentSessionInfo();
        var mpEnabled = CashService.IsMercadoPagoEnabled();
        enableMercadoPago.Checked = mpEnabled;
        mercadoPagoOpening.Text = CashService.MercadoPagoOpeningAmount().ToString("N2");
        mercadoPagoOpening.Enabled = mpEnabled;
        mercadoPagoCounted.Enabled = mpEnabled;
        mercadoPagoRetention.Text = CashService.MercadoPagoRetentionPercent().ToString("0.##", CultureInfo.InvariantCulture);
        // Siempre bloqueado para usuarios que no sean ADMINISTRADOR.
        mercadoPagoRetention.Enabled = Session.IsAdmin;
        mercadoPagoRetention.ReadOnly = !Session.IsAdmin;
        var payments = CashService.SalesPaymentSummary(CashService.CurrentSessionId() ?? 0);
        double Payment(string name) => payments.TryGetValue(name, out var value) ? value : 0;

        cashSummary.Text =
            $"CAJA ABIERTA POR: {session.fullName} ({session.username})\n" +
            $"USUARIO ACTUAL: {Session.FullName} ({Session.Username})\n" +
            $"APERTURA: ${x.opening:N2}\n" +
            $"VENTAS EFECTIVO: ${Payment("EFECTIVO"):N2}\n" +
            $"TRANSFERENCIA: ${Payment("TRANSFERENCIA"):N2}\n" +
            $"CRÉDITO: ${Payment("CRÉDITO"):N2}\n" +
            $"DÓLARES: ${Payment("DÓLARES"):N2}\n" +
            $"INGRESOS EFECTIVO: ${x.income:N2}\n" +
            $"EGRESOS EFECTIVO: ${x.expenses:N2}\n" +
            $"EFECTIVO ESPERADO: ${x.expected:N2}";

        if (mpEnabled)
        {
            mercadoPagoSummary.Text =
                "MERCADO PAGO HABILITADO\n" +
                $"APERTURA: ${x.mercadoPagoOpening:N2}\n" +
                $"VENTAS MERCADO PAGO: ${x.mercadoPagoSales:N2}\n" +
                $"INGRESOS: ${x.mercadoPagoIncome:N2}\n" +
                $"EGRESOS: ${x.mercadoPagoExpenses:N2}\n" +
                $"RETENCIÓN: {x.mercadoPagoRetentionPercent:N2}% = ${x.mercadoPagoRetentionAmount:N2}\n" +
                $"SALDO MERCADO PAGO ESPERADO: ${x.mercadoPagoExpected:N2}";
            mercadoPagoSummary.SelectAll();
            mercadoPagoSummary.SelectionColor = Color.White;
            mercadoPagoSummary.Select(mercadoPagoSummary.Text.LastIndexOf("SALDO MERCADO PAGO ESPERADO", StringComparison.Ordinal), mercadoPagoSummary.Text.Length - mercadoPagoSummary.Text.LastIndexOf("SALDO MERCADO PAGO ESPERADO", StringComparison.Ordinal));
            mercadoPagoSummary.SelectionFont = new Font("Segoe UI", 10, FontStyle.Bold);
            mercadoPagoSummary.SelectionLength = 0;
            mercadoPagoExpectedLabel.Text = $"ESPERADO\n${x.mercadoPagoExpected:N2}";
        }
        else
        {
            mercadoPagoSummary.Text = "MERCADO PAGO NO HABILITADO\n\nEsta caja no tiene una caja paralela de Mercado Pago.\n\nPara utilizarla, activala al abrir la próxima caja e ingresá el saldo inicial.";
            mercadoPagoSummary.SelectAll();
            mercadoPagoSummary.SelectionColor = Color.WhiteSmoke;
            mercadoPagoSummary.SelectionLength = 0;
            mercadoPagoExpectedLabel.Text = "NO HABILITADO";
        }
    }

    private void SaveMercadoPagoRetention()
    {
        if (!Session.IsAdmin)
        {
            MessageBox.Show("Solo el usuario ADMINISTRADOR puede configurar la retención de Mercado Pago.", "Permiso restringido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            RefreshData();
            return;
        }
        try
        {
            var value = Num(mercadoPagoRetention);
            CashService.SaveMercadoPagoRetentionPercent(value);
            mercadoPagoRetention.Text = value.ToString("0.##", CultureInfo.InvariantCulture);
            RefreshData();
            MessageBox.Show($"Retención de Mercado Pago guardada: {value:N2}%\n\nAl cerrar el turno, ese porcentaje se descontará de las ventas cobradas por Mercado Pago para calcular el saldo esperado.", "Mercado Pago", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Mercado Pago", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }


    private static double Num(TextBox field)
    {
        var raw = (field.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw)) return 0;
        raw = raw.Replace(" ", string.Empty);
        if (raw.Contains(',')) raw = raw.Replace(".", string.Empty).Replace(',', '.');
        if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
            return value;
        throw new InvalidOperationException($"Ingresá un importe válido en '{field.Name}'.");
    }

    private void OpenCash()
    {
        try
        {
            if (Session.UserId <= 0)
            {
                MessageBox.Show("No hay un usuario válido en la sesión.", "Apertura de caja", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var cash = Num(opening);
            if (cash < 0)
            {
                MessageBox.Show("El fondo inicial de efectivo no puede ser negativo.", "Apertura de caja", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var mpEnabled = enableMercadoPago.Checked;
            var mp = mpEnabled ? Num(mercadoPagoOpening) : 0;
            if (mpEnabled && mp <= 0)
            {
                MessageBox.Show("Para habilitar Mercado Pago ingresá un saldo inicial mayor a cero.", "Apertura de caja", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                mercadoPagoOpening.Focus();
                mercadoPagoOpening.SelectAll();
                return;
            }

            CashService.Open(cash, Session.UserId, mpEnabled, mp, CashService.ConfiguredMercadoPagoRetentionPercent());
            RefreshData();
            MessageBox.Show(
                $"Caja abierta correctamente con un fondo inicial de ${cash:N2}." +
                (mpEnabled ? $"\nSaldo inicial Mercado Pago: ${mp:N2}." : ""),
                "Apertura de caja", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo abrir la caja:\n\n" + ex.Message, "Apertura de caja", MessageBoxButtons.OK, MessageBoxIcon.Error);
            RefreshData();
        }
    }

    private void Move(string type)
    {
        try
        {
            if (!CashService.IsOpen())
            {
                MessageBox.Show("Primero abrí la caja.", "Movimiento de caja", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var value = Num(amount);
            var method = movementMethod.SelectedItem?.ToString() ?? "EFECTIVO";
            CashService.Movement(type, concept.Text, value, Session.UserId, method);

            concept.Clear();
            amount.Clear();
            RefreshData();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Movimiento de caja", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ChangeUser()
    {
        try
        {
            using var login = new LoginForm();
            if (login.ShowDialog(this) == DialogResult.OK)
                RefreshData();
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo cambiar el usuario:\n\n" + ex.Message, "Cambiar usuario", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void GeneralDailyClosing()
    {
        try
        {
            var date = DateTime.Today;
            if (MessageBox.Show(
                $"Se generará el CORTE Z GENERAL del {date:dd/MM/yyyy}.\n\n" +
                "Incluirá TODOS los cajeros y turnos del día, ventas, tickets, medios de pago, ingresos, egresos y efectivo esperado.\n\n" +
                "Este proceso NO cierra ninguna caja.\n\n¿Generar corte general?",
                "CORTE Z · GENERAL DEL DÍA", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            var result = GeneralCashClosingService.Generate(date);
            using var report = new CashReportForm(result.text, $"CORTE Z GENERAL · {date:dd/MM/yyyy}\n\n" + result.text, (0, result.totalSales, 0, 0, 0, 0, result.totalExpectedCash));
            ThemeService.Apply(report);
            report.ShowDialog(this);

            MessageBox.Show(
                $"CORTE Z GENERAL generado correctamente.\n\n" +
                $"Cajas/turnos: {result.sessions}\n" +
                $"Cajeros: {result.cashiers}\n" +
                $"Tickets: {result.tickets}\n" +
                $"Ventas totales: ${result.totalSales:N2}\n\n" +
                $"Excel: {result.excelFile}",
                "CORTE Z GENERAL", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show("No se pudo generar el CORTE Z GENERAL:\n\n" + ex.Message, "Corte Z general", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CloseCash()
    {
        try
        {
            if (!CashService.IsOpen())
            {
                MessageBox.Show("No hay una caja abierta.");
                return;
            }

            var x = CashService.Summary();
            var c = Num(counted);
            var diff = c - x.expected;
            var mpEnabled = CashService.IsMercadoPagoEnabled();
            var mpCounted = mpEnabled ? Num(mercadoPagoCounted) : 0;
            var mpDiff = mpEnabled ? mpCounted - x.mercadoPagoExpected : 0;

            if (MessageBox.Show(
                $"EFECTIVO ESPERADO: ${x.expected:N2}\n" +
                $"DINERO CONTADO: ${c:N2}\n" +
                $"DIFERENCIA EFECTIVO: ${diff:N2}" +
                (mpEnabled ? $"\n\nMERCADO PAGO ESPERADO: ${x.mercadoPagoExpected:N2}\nSALDO INFORMADO MERCADO PAGO: ${mpCounted:N2}\nDIFERENCIA MERCADO PAGO: ${mpDiff:N2}" : "") +
                "\n\n¿Cerrar el turno?",
                "Arqueo de caja",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes)
            {
                var reason = "SIN DIFERENCIA";
                if (Math.Abs(diff) > 0.01 || (mpEnabled && Math.Abs(mpDiff) > 0.01))
                {
                    using var rf = new InputBoxForm("Motivo de la diferencia de efectivo / Mercado Pago", "");
                    if (rf.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(rf.Value))
                    { MessageBox.Show("Debés indicar el motivo de la diferencia antes de cerrar la caja.", "Arqueo", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
                    reason = rf.Value;
                }
                var closedSessionId = CashService.Close(c, Session.UserId, reason, mpEnabled ? mpCounted : null);
                SoundService.PlayCashRegister();
                var excelFile = CashExcelService.ExportClosedSession(closedSessionId);
                var shiftReport = ReportService.CashSessionDetailed(closedSessionId);
                var dailyReport = ReportService.DailySummary(DateTime.Today);
                string? pdfFile = null;
                try
                {
                    pdfFile = CashClosingPdfService.Generate(closedSessionId);
                }
                catch (Exception pdfEx)
                {
                    MessageBox.Show($"El cierre y el Excel fueron generados, pero no se pudo crear el PDF ejecutivo:\n\n{pdfEx.Message}", "Reporte PDF", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                try
                {
                    if (EmailReportService.IsConfigured)
                    {
                        var emailBody = CashClosingPdfService.BuildEmailSummary(closedSessionId);
                        var attachments = new List<string> { excelFile };
                        if (!string.IsNullOrWhiteSpace(pdfFile) && File.Exists(pdfFile)) attachments.Add(pdfFile);
                        EmailReportService.Send($"FerrarisPOS® - Reporte ejecutivo de cierre {DateTime.Now:dd/MM/yyyy HH:mm}", emailBody, attachments);
                    }
                }
                catch (Exception mailEx)
                {
                    MessageBox.Show($"El cierre quedó registrado y el Excel fue guardado, pero no se pudo enviar el correo:\n\n{mailEx.Message}", "Correo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                if (WhatsAppReportService.IsConfigured)
                {
                    try
                    {
                        var answer = MessageBox.Show(
                            "El cierre fue guardado. ¿Querés abrir WhatsApp con el resumen ejecutivo preparado para enviar?\n\nEl Excel queda guardado en la PC para adjuntarlo al chat.",
                            "WhatsApp · Informe de cierre", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                        if (answer == DialogResult.Yes)
                            WhatsAppReportService.OpenWhatsApp($"FerrarisPOS · Cierre de caja {DateTime.Now:dd/MM/yyyy HH:mm}", shiftReport, excelFile);
                    }
                    catch (Exception waEx)
                    {
                        MessageBox.Show("No se pudo abrir WhatsApp:\n\n" + waEx.Message, "WhatsApp", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }

                using var report = new CashReportForm(shiftReport, dailyReport, (x.opening, x.sales, x.income, x.expenses, x.cardIncome, x.cardExpenses, x.expected));
                ThemeService.Apply(report);
                report.ShowDialog(this);
                MessageBox.Show($"Cierre guardado automáticamente.\n\nExcel detallado:\n{excelFile}\n\nTXT detallado:\n{CashExcelService.DetailedTextFilePath}", "Arqueo", MessageBoxButtons.OK, MessageBoxIcon.Information);
                RefreshData();
            }
        }
        catch (Exception ex) { MessageBox.Show(ex.Message); }
    }
}


internal sealed class CashReportForm : Form
{
    private readonly (double opening, double sales, double income, double expenses, double cardIncome, double cardExpenses, double expected) summary;

    public CashReportForm(
        string shift,
        string daily,
        (double opening, double sales, double income, double expenses, double cardIncome, double cardExpenses, double expected) summary)
    {
        this.summary = summary;

        Text = "FerrarisPOS · Reporte de cierre";
        Width = 1180;
        Height = 800;
        StartPosition = FormStartPosition.CenterParent;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(Page("RESUMEN DE CIERRE", shift));
        Controls.Add(tabs);
    }

    private static TabPage Page(string title, string text)
    {
        var page = new TabPage(title);
        var box = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 10),
            Text = text,
            BackColor = Color.White
        };
        page.Controls.Add(box);
        return page;
    }
}

internal sealed class CashBarChart : Panel
{
    private readonly (double opening, double sales, double income, double expenses, double cardIncome, double cardExpenses, double expected) data;

    public CashBarChart((double opening, double sales, double income, double expenses, double cardIncome, double cardExpenses, double expected) data)
    {
        this.data = data;
        DoubleBuffered = true;
        BackColor = Color.White;
        ResizeRedraw = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using var titleFont = new Font("Segoe UI", 14, FontStyle.Bold);
        using var subtitleFont = new Font("Segoe UI", 9);
        using var labelFont = new Font("Segoe UI", 8, FontStyle.Bold);
        using var valueFont = new Font("Segoe UI", 8);
        using var axisPen = new Pen(Color.FromArgb(190, 190, 190), 1);

        e.Graphics.DrawString("CIERRE DE CAJA · COMPARATIVO", titleFont, Brushes.Black, 18, 12);
        e.Graphics.DrawString("Cada barra muestra exactamente qué concepto representa y su importe.", subtitleFont, Brushes.DimGray, 18, 38);

        var values = new[]
        {
            ("APERTURA", "Fondo inicial", Math.Max(0, data.opening)),
            ("VENTAS EFECTIVO", "Cobros en efectivo", Math.Max(0, data.sales)),
            ("INGRESOS", "Ingresos manuales", Math.Max(0, data.income)),
            ("EGRESOS", "Egresos manuales", Math.Max(0, data.expenses)),
            ("TARJETA", "Cobros con tarjeta", Math.Max(0, data.cardIncome)),
            ("GASTOS TARJETA", "Egresos asociados", Math.Max(0, data.cardExpenses)),
            ("ESPERADO", "Total esperado", Math.Max(0, data.expected))
        };

        double max = Math.Max(1, values.Max(x => x.Item3));
        int left = 62;
        int top = 78;
        int bottom = Math.Max(top + 180, Height - 95);
        int chartWidth = Math.Max(500, Width - left - 30);
        int chartHeight = Math.Max(180, bottom - top);
        double slot = chartWidth / (double)values.Length;
        float barWidth = (float)Math.Max(28, slot * 0.60);

        // Guías horizontales y escala.
        for (int g = 0; g <= 4; g++)
        {
            float y = bottom - (chartHeight * g / 4f);
            e.Graphics.DrawLine(axisPen, left, y, left + chartWidth, y);
            double scaleValue = max * g / 4d;
            string scale = $"${scaleValue:N0}";
            e.Graphics.DrawString(scale, subtitleFont, Brushes.DimGray, 5, y - 8);
        }

        for (int i = 0; i < values.Length; i++)
        {
            double amount = values[i].Item3;
            float h = (float)((amount / max) * (chartHeight - 12));
            float x = (float)(left + i * slot + (slot - barWidth) / 2);
            float y = bottom - h;

            // Paleta legible, sin depender de un componente externo.
            Color[] colors =
            {
                Color.SteelBlue, Color.DodgerBlue, Color.MediumSeaGreen,
                Color.IndianRed, Color.DarkOrange, Color.SlateGray, Color.MediumPurple
            };
            using var brush = new SolidBrush(colors[i % colors.Length]);

            e.Graphics.FillRectangle(brush, x, y, barWidth, h);
            e.Graphics.DrawRectangle(Pens.Gray, x, y, barWidth, h);

            string valueText = $"${amount:N0}";
            var valueSize = e.Graphics.MeasureString(valueText, valueFont);
            e.Graphics.DrawString(
                valueText,
                valueFont,
                Brushes.Black,
                x + (barWidth - valueSize.Width) / 2f,
                Math.Max(top, y - 20));

            // Nombre corto + descripción debajo de cada barra.
            string shortName = values[i].Item1;
            var nameSize = e.Graphics.MeasureString(shortName, labelFont);
            e.Graphics.DrawString(
                shortName,
                labelFont,
                Brushes.Black,
                x + (barWidth - nameSize.Width) / 2f,
                bottom + 7);

            string desc = values[i].Item2;
            var descSize = e.Graphics.MeasureString(desc, subtitleFont);
            float descX = x + (barWidth - descSize.Width) / 2f;
            e.Graphics.DrawString(desc, subtitleFont, Brushes.DimGray, descX, bottom + 27);
        }

        // Nota inferior para que el usuario entienda las barras aunque el valor sea 0.
        e.Graphics.DrawString(
            "APERTURA = fondo inicial · VENTAS EFECTIVO = cobros · INGRESOS/EGRESOS = movimientos · TARJETA = cobros con tarjeta · ESPERADO = total que debería quedar",
            subtitleFont,
            Brushes.DimGray,
            18,
            Height - 34);
    }
}

internal sealed class CashPieChart : Panel
{
    private readonly (double opening, double sales, double income, double expenses, double cardIncome, double cardExpenses, double expected) data;

    public CashPieChart((double opening, double sales, double income, double expenses, double cardIncome, double cardExpenses, double expected) data)
    {
        this.data = data;
        DoubleBuffered = true;
        BackColor = Color.White;
        ResizeRedraw = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using var titleFont = new Font("Segoe UI", 14, FontStyle.Bold);
        using var labelFont = new Font("Segoe UI", 9, FontStyle.Bold);
        using var smallFont = new Font("Segoe UI", 9);

        e.Graphics.DrawString("DISTRIBUCIÓN DE COBROS", titleFont, Brushes.Black, 18, 15);
        e.Graphics.DrawString("Participación de efectivo y tarjeta sobre las ventas.", smallFont, Brushes.DimGray, 18, 40);

        double cash = Math.Max(0, data.sales);
        double card = Math.Max(0, data.cardIncome);
        double total = cash + card;

        if (total <= 0)
        {
            e.Graphics.DrawString("No hay ventas para graficar.", smallFont, Brushes.Gray, 30, 100);
            return;
        }

        int diameter = Math.Min(300, Math.Min(Width - 50, Height - 190));
        var rect = new Rectangle((Width - diameter) / 2, 70, diameter, diameter);

        float cashAngle = (float)(cash / total * 360.0);
        float cardAngle = 360f - cashAngle;

        using var cashBrush = new SolidBrush(Color.SteelBlue);
        using var cardBrush = new SolidBrush(Color.Orange);

        e.Graphics.FillPie(cashBrush, rect, -90, cashAngle);
        e.Graphics.FillPie(cardBrush, rect, -90 + cashAngle, cardAngle);
        e.Graphics.DrawEllipse(Pens.Gray, rect);

        int y = rect.Bottom + 28;
        double cashPct = cash / total * 100;
        double cardPct = card / total * 100;

        DrawLegend(e.Graphics, "EFECTIVO", $"${cash:N2} · {cashPct:N1}%", Color.SteelBlue, 25, y, labelFont);
        DrawLegend(e.Graphics, "TARJETA", $"${card:N2} · {cardPct:N1}%", Color.Orange, 25, y + 35, labelFont);

        e.Graphics.DrawString($"TOTAL COBRADO: ${total:N2}", labelFont, Brushes.Black, 25, y + 75);
        e.Graphics.DrawString($"Diferencia esperado vs. cobros: ${(data.expected - total):N2}", smallFont, Brushes.DimGray, 25, y + 102);
    }

    private static void DrawLegend(Graphics g, string name, string amount, Color color, int x, int y, Font font)
    {
        using var brush = new SolidBrush(color);
        g.FillRectangle(brush, x, y, 18, 18);
        g.DrawRectangle(Pens.Gray, x, y, 18, 18);
        g.DrawString($"{name}: {amount}", font, Brushes.Black, x + 28, y - 1);
    }
}

internal sealed class CashIndicatorChart : Panel
{
    private readonly (double opening, double sales, double income, double expenses, double cardIncome, double cardExpenses, double expected) data;

    public CashIndicatorChart((double opening, double sales, double income, double expenses, double cardIncome, double cardExpenses, double expected) data)
    {
        this.data = data;
        DoubleBuffered = true;
        BackColor = Color.White;
        ResizeRedraw = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var titleFont = new Font("Segoe UI", 14, FontStyle.Bold);
        using var labelFont = new Font("Segoe UI", 10, FontStyle.Bold);
        using var valueFont = new Font("Segoe UI", 16, FontStyle.Bold);
        using var smallFont = new Font("Segoe UI", 9);

        e.Graphics.DrawString("INDICADORES DEL CIERRE", titleFont, Brushes.Black, 20, 18);
        e.Graphics.DrawString("Lectura rápida de los principales valores del turno.", smallFont, Brushes.DimGray, 20, 45);

        double cobros = Math.Max(0, data.sales + data.cardIncome);
        double movimientos = Math.Max(0, data.income);
        double egresos = Math.Max(0, data.expenses + data.cardExpenses);
        double diferencia = data.expected - cobros;
        double liquido = cobros + movimientos - egresos;

        var cards = new[]
        {
            ("TOTAL COBRADO", $"${cobros:N2}", "Ventas en efectivo + tarjeta"),
            ("TOTAL INGRESOS", $"${movimientos:N2}", "Ingresos manuales"),
            ("TOTAL EGRESOS", $"${egresos:N2}", "Egresos + gastos asociados"),
            ("NETO ESTIMADO", $"${liquido:N2}", "Cobros + ingresos - egresos"),
            ("ESPERADO", $"${data.expected:N2}", "Monto esperado al cierre"),
            ("DIFERENCIA", $"{(diferencia >= 0 ? "+" : "")}${diferencia:N2}", "Esperado menos cobrado")
        };

        int columns = 2;
        int cardWidth = Math.Max(260, (Width - 65) / columns);
        int cardHeight = 105;

        for (int i = 0; i < cards.Length; i++)
        {
            int col = i % columns;
            int row = i / columns;
            int x = 20 + col * (cardWidth + 20);
            int y = 80 + row * (cardHeight + 18);

            using var bg = new SolidBrush(Color.FromArgb(245, 247, 250));
            using var border = new Pen(Color.FromArgb(205, 210, 218));
            e.Graphics.FillRectangle(bg, x, y, cardWidth, cardHeight);
            e.Graphics.DrawRectangle(border, x, y, cardWidth, cardHeight);

            e.Graphics.DrawString(cards[i].Item1, labelFont, Brushes.DimGray, x + 15, y + 12);
            e.Graphics.DrawString(cards[i].Item2, valueFont, Brushes.Black, x + 15, y + 35);
            e.Graphics.DrawString(cards[i].Item3, smallFont, Brushes.DimGray, x + 15, y + 78);
        }
    }
}


internal sealed class CashCountForm : Form
{
    private readonly NumericUpDown[] qty; private readonly double[] denom = { 100000,50000,20000,10000,5000,2000,1000,500,200,100,50,20,10,5,2,1 };
    private readonly Label total = new(); public double Total => denom.Select((d,i)=>d*(double)qty[i].Value).Sum();
    public CashCountForm(){Text="Conteo de efectivo";Width=620;Height=650;StartPosition=FormStartPosition.CenterParent;qty=denom.Select(_=>new NumericUpDown{Minimum=0,Maximum=10000,Width=100}).ToArray();var p=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=denom.Length+2,Padding=new Padding(15)};p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,35));p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,40));for(int i=0;i<denom.Length;i++){p.Controls.Add(new Label{Text=$"${denom[i]:N0}",AutoSize=true},0,i);p.Controls.Add(qty[i],1,i);var line=new Label{AutoSize=true};var idx=i;qty[i].ValueChanged+=(s,e)=>line.Text=$"= ${(denom[idx]*(double)qty[idx].Value):N2}";line.Text="= $0,00";p.Controls.Add(line,2,i);}total.AutoSize=true;total.Font=new Font("Segoe UI",14,FontStyle.Bold);p.Controls.Add(total,0,denom.Length);p.SetColumnSpan(total,3);foreach(var n in qty)n.ValueChanged+=(s,e)=>total.Text=$"TOTAL CONTADO: ${Total:N2}";total.Text="TOTAL CONTADO: $0,00";var ok=new Button{Text="USAR TOTAL",DialogResult=DialogResult.OK,Width=120,Height=40};var cancel=new Button{Text="CANCELAR",DialogResult=DialogResult.Cancel,Width=100,Height=40};var fl=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};fl.Controls.Add(cancel);fl.Controls.Add(ok);p.Controls.Add(fl,0,denom.Length+1);p.SetColumnSpan(fl,3);Controls.Add(p);AcceptButton=ok;CancelButton=cancel;}
}
