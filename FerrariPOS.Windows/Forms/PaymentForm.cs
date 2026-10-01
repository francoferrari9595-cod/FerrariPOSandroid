using FerrarisPOS.Data;
using FerrarisPOS.Models;
using FerrarisPOS.Services;
using System.Globalization;

namespace FerrarisPOS.Forms;

public class PaymentForm : Form
{
    private readonly List<CartItem> items;
    private SafeComboBox customer = new(), method = new(), channel = new();
    private TextBox saleNotes = new(), deliveryAddress = new();
    private TextBox amount = new(), reference = new();
    private Label totalLabel = new(), remaining = new(), change = new(), dollarInfo = new(), creditInfo = new(), creditDebt = new();
    private NumericUpDown cash = new(), mercadoPago = new(), transfer = new(), credit = new(), dollars = new(), creditAmount = new();
    private Panel mixed = new();
    private TabControl tabs = new();
    private FlowLayoutPanel paymentMethodsPanel = new();
    private readonly List<Button> paymentButtons = new();
    private int paymentIndex;
    private bool updating;
    private bool splitEnabled;
    private int splitParts;
    private double splitPaid;
    private readonly List<PaymentLine> splitPayments = new();
    private readonly int initialCustomerId;
    private readonly Label mixedCreditCustomer = new();
    private readonly TextBox creditCustomerSearch = new();
    private readonly ListBox creditCustomerList = new();
    private List<Customer> creditCustomers = new();

    private static readonly (string Name, string Icon)[] PaymentMethods =
    {
        ("EFECTIVO", "💵"), ("MERCADO PAGO", "📱"), ("MIXTO", "🔀"), ("TRANSFERENCIA", "🏦"),
        ("CRÉDITO", "👤"), ("DÓLARES", "USD")
    };

    public int CustomerId => customer.SelectedValue is int i ? i : 1;
    public bool PrintRequested { get; private set; }
    public double Received { get; private set; }
    public double Change { get; private set; }
    public string SaleChannel => channel.Text;
    public string SaleNotes => saleNotes.Text.Trim();
    public string DeliveryAddress => deliveryAddress.Text.Trim();
    public string DeliveryStatus => SaleChannel == "DELIVERY" ? "PENDIENTE" : "N/A";
    public List<PaymentLine> Payments { get; private set; } = new();
    public string DiscountReason { get; private set; } = "";
    private double Total => Math.Round(items.Sum(x => x.Total), 2);
    private double PaymentTotal => method.Text == "MIXTO"
        ? (double)(cash.Value + mercadoPago.Value + transfer.Value + credit.Value + dollars.Value)
        : Parse(amount.Text);

    public PaymentForm(List<CartItem> items, int customerId = 1)
    {
        this.items = items;
        initialCustomerId = customerId;
        Text = "FerrarisPOS · Cobrar venta";
        Width = 1150; Height = 850; MinimumSize = new Size(1150, 850); AutoScroll = true; BackColor = Color.White;
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;
        Build(); LoadCustomers();
        if (initialCustomerId > 1 && customer.DataSource != null)
        {
            try { customer.SelectedValue = initialCustomerId; } catch { }
        }
        SelectPayment("EFECTIVO"); UpdateView();
        KeyDown += OnKeyDown;
    }

    private void Build()
    {
        Controls.Add(new Label
        {
            Text = "COBRAR VENTA", Font = new Font("Segoe UI", 24, FontStyle.Bold),
            ForeColor = Color.Navy, Location = new Point(25, 18), AutoSize = true
        });
        totalLabel = new Label { Location = new Point(25, 70), Font = new Font("Segoe UI", 30, FontStyle.Bold), AutoSize = true };
        Controls.Add(totalLabel);

        tabs = new TabControl { Location = new Point(20, 125), Size = new Size(1080, 555), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
        tabs.TabPages.Add(BuildPaymentTab());
        tabs.TabPages.Add(BuildCreditTab());
        tabs.SelectedIndexChanged += (_, _) =>
        {
            if (updating) return;
            if (tabs.SelectedIndex == 0 && method.Text == "CRÉDITO")
            {
                SelectPayment("EFECTIVO");
            }
            else if (tabs.SelectedIndex == 1 && method.Text != "CRÉDITO" && method.Text != "MIXTO")
            {
                SelectPayment("CRÉDITO");
            }
            UpdateView();
        };
        Controls.Add(tabs);

        var split = MakeActionButton("DIVIDIR CUENTA", 205, 700, 180);
        split.Click += (_, _) => ConfigureSplit(); Controls.Add(split);
        var print = MakeActionButton("F1 · COBRAR E IMPRIMIR TICKET", 520, 700, 300);
        print.Click += (_, _) => Accept(true); Controls.Add(print);
        var save = MakeActionButton("F2 · COBRAR SOLO REGISTRANDO", 830, 700, 270);
        save.Click += (_, _) => Accept(false); Controls.Add(save);
        var cancel = MakeActionButton("ESC · CANCELAR", 20, 700, 180);
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); }; Controls.Add(cancel);
    }

    private static Button MakeActionButton(string text, int x, int y, int width) =>
        new() { Text = text, Location = new Point(x, y), Width = width, Height = 55, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 10, FontStyle.Bold) };

    private TabPage BuildPaymentTab()
    {
        var page = new TabPage("COBRO");
        page.Controls.Add(new Label { Text = "MEDIO DE PAGO · ← / → PARA CAMBIAR · ENTER PARA SELECCIONAR · CRÉDITO: ENTER PARA ENTRAR", Location = new Point(25, 18), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) });

        paymentMethodsPanel = new FlowLayoutPanel
        {
            Location = new Point(22, 45), Size = new Size(1020, 92),
            FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            AutoScroll = true, Padding = new Padding(2), BackColor = Color.WhiteSmoke
        };
        page.Controls.Add(paymentMethodsPanel);

        // Combo oculto: se conserva internamente para no romper la lógica existente.
        method = new SafeComboBox { Visible = false, DropDownStyle = ComboBoxStyle.DropDownList };
        method.Items.AddRange(PaymentMethods.Select(x => x.Name).ToArray());
        method.SelectedIndexChanged += (_, _) => { SyncPaymentButtons(); UpdateView(); };
        page.Controls.Add(method);

        for (int i = 0; i < PaymentMethods.Length; i++)
        {
            var idx = i;
            var b = new Button
            {
                Text = PaymentMethods[i].Icon + Environment.NewLine + PaymentMethods[i].Name,
                Width = 118, Height = 78, Margin = new Padding(4),
                FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand,
                Tag = idx
            };
            b.Visible = PaymentMethods[idx].Name != "MERCADO PAGO" || CashService.IsMercadoPagoEnabled();
            b.Click += (_, _) => { paymentIndex = idx; SelectPayment(PaymentMethods[idx].Name); };
            b.Enter += (_, _) => { paymentIndex = idx; SyncPaymentButtons(); };
            paymentMethodsPanel.Controls.Add(b); paymentButtons.Add(b);
        }

        // Datos adicionales de la venta: bloque independiente a la derecha.
        // Se deja espacio suficiente para que nunca se monte sobre el panel MIXTO.
        var orderData = new GroupBox
        {
            Text = "DATOS DEL PEDIDO",
            Location = new Point(555, 245),
            Size = new Size(465, 270),
            Padding = new Padding(12)
        };
        orderData.Controls.Add(new Label { Text = "CANAL DE VENTA", Location = new Point(12, 28), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        channel = new SafeComboBox { Location = new Point(12, 52), Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
        channel.Items.AddRange(new object[] { "SALÓN", "TAKE AWAY", "DELIVERY" });
        if (channel.Items.Count > 0) channel.SelectedIndex = 0;
        orderData.Controls.Add(channel);

        orderData.Controls.Add(new Label { Text = "NOTAS DEL PEDIDO", Location = new Point(12, 90), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        saleNotes = new TextBox { Location = new Point(12, 114), Width = 425, Height = 62, Multiline = true, ScrollBars = ScrollBars.Vertical };
        orderData.Controls.Add(saleNotes);

        orderData.Controls.Add(new Label { Text = "DIRECCIÓN DELIVERY", Location = new Point(12, 185), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        deliveryAddress = new TextBox { Location = new Point(12, 209), Width = 425 };
        orderData.Controls.Add(deliveryAddress);

        channel.SelectedIndexChanged += (_, _) =>
        {
            deliveryAddress.Enabled = channel.Text == "DELIVERY";
            if (!deliveryAddress.Enabled) deliveryAddress.Clear();
        };
        deliveryAddress.Enabled = false;
        page.Controls.Add(orderData);

        page.Controls.Add(new Label { Text = "IMPORTE RECIBIDO / PAGADO", Location = new Point(25, 150), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        amount = new TextBox { Location = new Point(25, 175), Width = 430, Font = new Font("Segoe UI", 17), Text = Total.ToString("0.00") };
        amount.TextChanged += (_, _) => UpdateView(); page.Controls.Add(amount);

        page.Controls.Add(new Label { Text = "REFERENCIA / Nº OPERACIÓN", Location = new Point(25, 230), AutoSize = true, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        reference = new TextBox { Location = new Point(25, 255), Width = 430 }; page.Controls.Add(reference);

        dollarInfo = new Label { Location = new Point(25, 300), AutoSize = true, ForeColor = Color.DimGray }; page.Controls.Add(dollarInfo);
        remaining = new Label { Location = new Point(520, 150), Font = new Font("Segoe UI", 18, FontStyle.Bold), AutoSize = true }; page.Controls.Add(remaining);
        change = new Label { Location = new Point(520, 200), Font = new Font("Segoe UI", 18, FontStyle.Bold), AutoSize = true }; page.Controls.Add(change);

        BuildMixed(page);
        return page;
    }

    private void SyncPaymentButtons()
    {
        if (method.SelectedIndex >= 0) paymentIndex = method.SelectedIndex;
        for (int i = 0; i < paymentButtons.Count; i++)
        {
            var selected = i == paymentIndex;
            paymentButtons[i].BackColor = selected ? Color.FromArgb(25, 110, 190) : Color.White;
            paymentButtons[i].ForeColor = selected ? Color.White : Color.FromArgb(35, 35, 35);
            paymentButtons[i].FlatAppearance.BorderColor = selected ? Color.FromArgb(15, 80, 150) : Color.Silver;
            paymentButtons[i].FlatAppearance.BorderSize = selected ? 2 : 1;
        }
    }

    private void SelectPayment(string name)
    {
        if (name == "MERCADO PAGO" && !CashService.IsMercadoPagoEnabled())
        {
            name = "EFECTIVO";
        }
        var idx = Array.FindIndex(PaymentMethods, x => x.Name == name);
        if (idx < 0) idx = 0;
        if (method.Items.Count == 0) return;
        idx = Math.Clamp(idx, 0, method.Items.Count - 1);
        paymentIndex = idx;
        method.SelectedIndex = idx;
        SyncPaymentButtons();
    }

    private TabPage BuildCreditTab()
    {
        var page = new TabPage("CRÉDITO / CUENTA CORRIENTE");
        page.Controls.Add(new Label { Text = "BUSCAR DEUDOR / CLIENTE", Location = new Point(30, 25), AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold) });

        creditCustomerSearch.Location = new Point(30, 52);
        creditCustomerSearch.Width = 360;
        creditCustomerSearch.Font = new Font("Segoe UI", 13);
        creditCustomerSearch.PlaceholderText = "Escribí el nombre para buscar...";
        creditCustomerSearch.TextChanged += (_, _) => FilterCreditCustomers();
        creditCustomerSearch.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                SelectFirstCreditCustomer();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };
        page.Controls.Add(creditCustomerSearch);

        creditCustomerList.Location = new Point(30, 92);
        creditCustomerList.Size = new Size(360, 145);
        creditCustomerList.Font = new Font("Segoe UI", 11);
        creditCustomerList.HorizontalScrollbar = true;
        creditCustomerList.SelectedIndexChanged += (_, _) => SelectCreditCustomerFromList();
        creditCustomerList.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                SelectCreditCustomerFromList();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        };
        page.Controls.Add(creditCustomerList);

        // Combo interno: conserva toda la lógica de crédito existente.
        customer = new SafeComboBox { Visible = false, DropDownStyle = ComboBoxStyle.DropDownList };
        customer.SelectedIndexChanged += (_, _) => UpdateCreditInfo();
        page.Controls.Add(customer);

        creditInfo = new Label { Location = new Point(430, 60), AutoSize = true, Font = new Font("Segoe UI", 13, FontStyle.Bold), ForeColor = Color.Navy }; page.Controls.Add(creditInfo);
        creditDebt = new Label { Location = new Point(430, 100), AutoSize = true, Font = new Font("Segoe UI", 11), ForeColor = Color.DimGray }; page.Controls.Add(creditDebt);
        page.Controls.Add(new Label { Text = "MONTO QUE SE CARGARÁ A LA CUENTA", Location = new Point(430, 160), AutoSize = true, Font = new Font("Segoe UI", 10, FontStyle.Bold) });
        creditAmount = new NumericUpDown { Location = new Point(430, 190), Width = 300, DecimalPlaces = 2, Maximum = 100000000, Increment = 1 }; page.Controls.Add(creditAmount);
        page.Controls.Add(new Label { Text = "0 = crédito infinito. El sistema no permite superar el límite disponible del cliente.", Location = new Point(430, 235), AutoSize = true, ForeColor = Color.DimGray });
        var useCredit = new Button { Text = "USAR ESTE CLIENTE PARA LA VENTA A CRÉDITO", Location = new Point(430, 280), Width = 360, Height = 48 };
        useCredit.Click += (_, _) => { SelectPayment("CRÉDITO"); UpdateView(); }; page.Controls.Add(useCredit);
        return page;
    }

    private void BuildMixed(TabPage page)
    {
        mixed = new Panel
        {
            Location = new Point(25, 315),
            Size = new Size(500, 210),
            Visible = false,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.WhiteSmoke
        };
        mixed.Controls.Add(new Label
        {
            Text = "PAGO MIXTO · DISTRIBUÍ EL TOTAL ENTRE UNO O MÁS MEDIOS",
            Location = new Point(12, 6),
            Size = new Size(470, 22),
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        });
        mixedCreditCustomer.Location = new Point(15, 165);
        mixedCreditCustomer.Size = new Size(465, 30);
        mixedCreditCustomer.Font = new Font("Segoe UI", 9, FontStyle.Bold);
        mixedCreditCustomer.ForeColor = Color.Navy;
        mixedCreditCustomer.Visible = false;
        mixed.Controls.Add(mixedCreditCustomer);
        var chooseMixedCustomer = new Button
        {
            Text = "ELEGIR CLIENTE PARA CRÉDITO",
            Location = new Point(285, 165),
            Width = 195,
            Height = 30,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 8.5f, FontStyle.Bold)
        };
        chooseMixedCustomer.Click += (_, _) => { tabs.SelectedIndex = 1; customer.Focus(); };
        mixed.Controls.Add(chooseMixedCustomer);
        mixedCreditCustomer.Width = 265;
        page.Controls.Add(mixed);
        var rows = new[] { ("Efectivo", 0), ("Mercado Pago", 1), ("Transferencia", 2), ("Crédito", 3), ("Dólares", 4) };
        foreach (var row in rows)
        {
            var rowY = 34 + row.Item2 * 25;
            var rowVisible = row.Item1 != "Mercado Pago" || CashService.IsMercadoPagoEnabled();
            var rowLabel = new Label { Text = row.Item1, Location = new Point(15, rowY + 4), Width = 130, Visible = rowVisible };
            mixed.Controls.Add(rowLabel);
            var box = MakeMoneyBox(155, rowY); box.Tag = row.Item2; box.Visible = rowVisible; box.ValueChanged += (_, _) => UpdateView(); mixed.Controls.Add(box);
            switch (row.Item2)
            {
                case 0: cash = box; break; case 1: mercadoPago = box; break; case 2: transfer = box; break;
                case 3: credit = box; break; case 4: dollars = box; break;
            }
        }
    }

    private static NumericUpDown MakeMoneyBox(int x, int y) => new() { DecimalPlaces = 2, Maximum = 100000000, Increment = 1, Location = new Point(x, y), Width = 220 };

    private void LoadCustomers()
    {
        var list = CustomerService.All();
        creditCustomers = list.Where(x => x.Id > 1).OrderBy(x => x.Name).ToList();
        customer.DataSource = list;
        customer.DisplayMember = "Name";
        customer.ValueMember = "Id";
        FilterCreditCustomers();
        // Una venta normal NO debe quedar asociada al último cliente usado para crédito.
        // Público General (ID 1) es siempre el valor seguro por defecto; el cliente real
        // solo se utiliza cuando la venta contiene una parte a crédito.
        var publicIndex = list.FindIndex(x => x.Id == 1);
        if (publicIndex >= 0) customer.SelectedIndex = publicIndex;
        else if (list.Count > 0) customer.SelectedIndex = 0;
        UpdateCreditInfo();
    }

    private static double Parse(string text) => double.TryParse(text.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? Math.Round(v, 2) : 0;

    private void FilterCreditCustomers()
    {
        if (creditCustomerList.IsDisposed) return;
        var term = creditCustomerSearch.Text.Trim();
        var filtered = string.IsNullOrWhiteSpace(term)
            ? creditCustomers.Take(20).ToList()
            : creditCustomers.Where(x => x.Name.Contains(term, StringComparison.OrdinalIgnoreCase)).Take(20).ToList();

        creditCustomerList.BeginUpdate();
        try
        {
            creditCustomerList.Items.Clear();
            foreach (var c in filtered) creditCustomerList.Items.Add(c);
            creditCustomerList.DisplayMember = "Name";
            if (creditCustomerList.Items.Count > 0) creditCustomerList.SelectedIndex = 0;
        }
        finally { creditCustomerList.EndUpdate(); }
    }

    private void SelectFirstCreditCustomer()
    {
        if (creditCustomerList.Items.Count == 0) return;
        creditCustomerList.SelectedIndex = 0;
        SelectCreditCustomerFromList();
    }

    private void SelectCreditCustomerFromList()
    {
        if (creditCustomerList.SelectedItem is not Customer selected) return;
        if (customer.DataSource != null) customer.SelectedValue = selected.Id;
        UpdateCreditInfo();
    }

    private void UpdateView()
    {
        if (updating) return; updating = true;
        try
        {
            if (method.Text == "CRÉDITO") { amount.Text = Total.ToString("0.00"); creditAmount.Value = Math.Min((decimal)Total, creditAmount.Maximum); }
            var paid = PaymentTotal;
            totalLabel.Text = $"TOTAL A COBRAR  ${Total:N2}";
            remaining.Text = $"RESTANTE  ${Math.Max(0, Total - paid):N2}";
            change.Text = $"CAMBIO  ${Math.Max(0, paid - Total):N2}";
            mixed.Visible = method.Text == "MIXTO";
            amount.Enabled = method.Text != "MIXTO" && method.Text != "CRÉDITO";
            if (method.Text == "DÓLARES") dollarInfo.Text = $"Tipo de cambio configurado: ${Database.GetSetting("exchange_rate", "1")} por USD. El importe se registra en moneda local."; else dollarInfo.Text = "";
            UpdateCreditInfo(); SyncPaymentButtons();
        }
        finally { updating = false; }
    }

    private void UpdateCreditInfo()
    {
        if (customer.Items.Count == 0) return;
        var id = CustomerId; var balance = CustomerService.Balance(id); var available = CustomerService.AvailableCredit(id);
        creditInfo.Text = $"SALDO ACTUAL: ${balance:N2}";
        creditDebt.Text = double.IsPositiveInfinity(available) ? "CRÉDITO DISPONIBLE: SIN LÍMITE" : $"LÍMITE: ${available + balance:N2} · CRÉDITO DISPONIBLE: ${available:N2}";
        creditAmount.Maximum = 100000000m;
        if (method.Text == "CRÉDITO" && id != 1 && !double.IsPositiveInfinity(available)) creditAmount.Maximum = Math.Min(creditAmount.Maximum, (decimal)Math.Max(0, available));
        if (method.Text == "CRÉDITO" && id != 1)
        {
            var max = double.IsPositiveInfinity(available) ? Total : Math.Min(Total, Math.Max(0, available));
            creditAmount.Value = Math.Max(0m, Math.Min(creditAmount.Maximum, (decimal)max));
        }
        if (mixedCreditCustomer != null)
        {
            var name = customer.SelectedItem is Customer c ? c.Name : "Público General";
            var creditValue = (double)credit.Value;
            mixedCreditCustomer.Text = creditValue > 0
                ? (id > 1
                    ? $"👤 CRÉDITO ${creditValue:N2} → {name} · Disponible: {(double.IsPositiveInfinity(available) ? "SIN LÍMITE" : "$" + available.ToString("N2"))}"
                    : "⚠ CRÉDITO: falta elegir el cliente que recibirá esta parte")
                : "👤 Parte a crédito: elegí el cliente en la solapa CRÉDITO / CUENTA CORRIENTE";
            mixedCreditCustomer.Visible = method.Text == "MIXTO";
        }
    }

    private void OnKeyDown(object? s, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            // CRÉDITO requiere una confirmación adicional: el primer ENTER entra
            // a la solapa de créditos. Un segundo ENTER confirma el cobro.
            if (tabs.SelectedIndex == 0 && method.Text == "CRÉDITO")
            {
                tabs.SelectedIndex = 1;
                creditCustomerSearch.Focus();
                e.Handled = true; e.SuppressKeyPress = true;
                return;
            }
            Accept(false);
            e.Handled = true; e.SuppressKeyPress = true;
            return;
        }
        if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right)
        {
            // Navegación en bucle por TODOS los medios de pago disponibles.
            // EFECTIVO → MERCADO PAGO* → MIXTO → TRANSFERENCIA → CRÉDITO → DÓLARES → EFECTIVO.
            // * Mercado Pago solo entra al recorrido si la caja paralela está habilitada.
            var availableMethods = PaymentMethods
                .Where(x => x.Name != "MERCADO PAGO" || CashService.IsMercadoPagoEnabled())
                .Select(x => x.Name)
                .ToList();
            if (availableMethods.Count == 0) availableMethods.Add("EFECTIVO");

            var currentName = method.Text;
            var current = availableMethods.IndexOf(currentName);
            if (current < 0) current = e.KeyCode == Keys.Left ? 0 : -1;

            var next = e.KeyCode == Keys.Left
                ? (current - 1 + availableMethods.Count) % availableMethods.Count
                : (current + 1) % availableMethods.Count;

            SelectPayment(availableMethods[next]);
            e.Handled = true; e.SuppressKeyPress = true; return;
        }
        if (e.KeyCode == Keys.F1) Accept(true);
        else if (e.KeyCode == Keys.F2) Accept(false);
        else if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
        else return;
        e.Handled = true; e.SuppressKeyPress = true;
    }

    private void ConfigureSplit()
    {
        using var f = new SplitAccountForm(Total);
        if (f.ShowDialog(this) != DialogResult.OK) return;
        splitEnabled = f.Parts > 1; splitParts = f.Parts; splitPaid = 0; splitPayments.Clear();
        amount.Text = f.PartAmount.ToString("0.00");
        MessageBox.Show($"Cuenta dividida en {splitParts} partes de ${f.PartAmount:N2}. Cada vez que cobres una parte se acumulará hasta completar el total.", "Cuenta dividida", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void Accept(bool print)
    {
        if (splitEnabled)
        {
            var part = Parse(amount.Text);
            var remainingPart = Total - splitPaid;
            if (part <= 0 || part > remainingPart + 0.01) { MessageBox.Show($"La parte debe ser mayor a cero y no superar el saldo ${remainingPart:N2}."); return; }
            if (method.Text == "CRÉDITO" && CustomerId == 1) { MessageBox.Show("Seleccioná el cliente para el crédito."); tabs.SelectedIndex = 1; return; }
            splitPayments.Add(new PaymentLine(method.Text, part, reference.Text.Trim())); splitPaid += part;
            if (splitPaid + 0.01 < Total)
            {
                amount.Text = Math.Max(0, Total - splitPaid).ToString("0.00");
                MessageBox.Show($"Parte registrada: ${part:N2}. Falta cobrar ${Total - splitPaid:N2}.", "Cuenta dividida", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!CaptureDiscountReason()) return;
            Received = splitPaid; Change = 0; Payments = splitPayments.ToList(); PrintRequested = print; DialogResult = DialogResult.OK; Close(); return;
        }

        if (method.Text == "MERCADO PAGO" && !CashService.IsMercadoPagoEnabled())
        { MessageBox.Show("Mercado Pago no está habilitado en esta sesión. Activá la caja paralela desde la apertura."); return; }
        if (method.Text == "MIXTO" && mercadoPago.Value > 0 && !CashService.IsMercadoPagoEnabled())
        { MessageBox.Show("Mercado Pago no está habilitado en esta sesión. Activá la caja paralela desde la apertura."); return; }
        if (method.Text == "CRÉDITO")
        {
            if (CustomerId == 1) { MessageBox.Show("Seleccioná un cliente en la solapa CRÉDITO / CUENTA CORRIENTE."); return; }
            var available = CustomerService.AvailableCredit(CustomerId);
            if (!double.IsPositiveInfinity(available) && Total > available + 0.01) { MessageBox.Show($"El cliente no tiene crédito suficiente.\nDisponible: ${available:N2}\nVenta: ${Total:N2}"); return; }
            creditAmount.Value = Math.Min((decimal)Total, creditAmount.Maximum);
            if (Math.Abs((double)creditAmount.Value - Total) > 0.01) { MessageBox.Show("La venta completa debe entrar en crédito. Para combinar crédito con otros medios usá la forma de pago MIXTO."); return; }
        }
        if (method.Text == "MIXTO" && credit.Value > 0 && CustomerId == 1) { MessageBox.Show("El pago mixto con crédito requiere seleccionar un cliente en la solapa CRÉDITO / CUENTA CORRIENTE."); tabs.SelectedIndex = 1; return; }
        if (method.Text == "MIXTO" && credit.Value > 0)
        {
            var available = CustomerService.AvailableCredit(CustomerId);
            if (!double.IsPositiveInfinity(available) && (double)credit.Value > available + 0.01) { MessageBox.Show($"El crédito disponible del cliente es ${available:N2}."); return; }
        }
        if (method.Text == "MIXTO" && PaymentTotal + 0.01 < Total) { MessageBox.Show("El importe es insuficiente."); return; }
        if (method.Text == "MIXTO" && Math.Abs(PaymentTotal - Total) > 0.01) { MessageBox.Show("En pago mixto, los importes deben sumar exactamente el total."); return; }
        if (method.Text == "EFECTIVO" && Parse(amount.Text) <= 0) { MessageBox.Show("Ingresá el efectivo recibido."); return; }
        if (method.Text != "MIXTO")
        {
            var received = method.Text == "CRÉDITO" ? Total : Parse(amount.Text);
            Received = received; Change = Math.Max(0, received - Total);
            Payments = new List<PaymentLine> { new(method.Text, Math.Min(received, Total), reference.Text.Trim()) };
        }
        else
        {
            Received = PaymentTotal; Change = 0; Payments = new List<PaymentLine>();
            Add("EFECTIVO", cash.Value); Add("MERCADO PAGO", mercadoPago.Value); Add("TRANSFERENCIA", transfer.Value); Add("CRÉDITO", credit.Value); Add("DÓLARES", dollars.Value);
        }
        if (!CaptureDiscountReason()) return;
        PrintRequested = print; DialogResult = DialogResult.OK; Close();
    }

    private bool CaptureDiscountReason()
    {
        if (!items.Any(x => x.DiscountAmount > 0.005))
        {
            DiscountReason = "";
            return true;
        }

        using var f = new InputBoxForm(
            "Motivo del descuento",
            "");
        f.Text = "Motivo del descuento · obligatorio";
        if (f.ShowDialog(this) != DialogResult.OK) return false;
        if (string.IsNullOrWhiteSpace(f.Value))
        {
            MessageBox.Show("Debés indicar el motivo del descuento para poder finalizar la venta. Esta información quedará guardada en el reporte y corte de caja.", "Descuento · motivo obligatorio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        DiscountReason = f.Value.Trim();
        return true;
    }

    private void Add(string methodName, decimal value) { if (value > 0) Payments.Add(new PaymentLine(methodName, (double)value, reference.Text.Trim())); }
}

internal sealed class SplitAccountForm : Form
{
    private readonly NumericUpDown parts = new() { Minimum = 2, Maximum = 20, Value = 2 };
    private readonly Label amount = new(); private readonly double total;
    public int Parts => (int)parts.Value;
    public double PartAmount => total / Parts;
    public SplitAccountForm(double total)
    {
        this.total = total; Text = "Dividir cuenta"; Width = 430; Height = 240; StartPosition = FormStartPosition.CenterParent; Padding = new Padding(18);
        Controls.Add(new Label { Text = $"TOTAL: ${total:N2}", AutoSize = true, Font = new Font("Segoe UI", 16, FontStyle.Bold), Location = new Point(18, 18) });
        Controls.Add(new Label { Text = "CANTIDAD DE PARTES", AutoSize = true, Location = new Point(18, 65) }); parts.Location = new Point(190, 60); parts.Width = 100; parts.ValueChanged += (_, _) => RefreshAmount(); Controls.Add(parts);
        amount.Location = new Point(18, 105); amount.AutoSize = true; amount.Font = new Font("Segoe UI", 13, FontStyle.Bold); Controls.Add(amount); RefreshAmount();
        var ok = new Button { Text = "APLICAR", DialogResult = DialogResult.OK, Location = new Point(205, 145), Width = 95, Height = 38 }; var cancel = new Button { Text = "CANCELAR", DialogResult = DialogResult.Cancel, Location = new Point(310, 145), Width = 95, Height = 38 }; Controls.Add(ok); Controls.Add(cancel); AcceptButton = ok; CancelButton = cancel;
    }
    private void RefreshAmount() => amount.Text = $"Cada parte: ${PartAmount:N2}";
}
