using FerrarisPOS.Services;

namespace FerrarisPOS.Forms;

public sealed class TrialStatusForm : Form
{
    private readonly Label daysLabel = new();
    private readonly Label detailLabel = new();
    private readonly Button continueButton = new();
    private readonly Button activateButton = new();

    public TrialStatusForm()
    {
        Text = "FerrariPOS - Bienvenido";
        Width = 560;
        Height = 360;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        Build();
        ThemeService.Apply(this);
        RefreshStatus();
    }

    private void Build()
    {
        var title = new Label
        {
            Text = "BIENVENIDO A FERRARIPOS",
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 20, FontStyle.Bold),
            Location = new Point(30, 22),
            Size = new Size(500, 45)
        };
        Controls.Add(title);

        daysLabel.AutoSize = false;
        daysLabel.TextAlign = ContentAlignment.MiddleCenter;
        daysLabel.Font = new Font("Segoe UI", 30, FontStyle.Bold);
        daysLabel.Location = new Point(30, 78);
        daysLabel.Size = new Size(500, 70);
        Controls.Add(daysLabel);

        detailLabel.AutoSize = false;
        detailLabel.TextAlign = ContentAlignment.MiddleCenter;
        detailLabel.Font = new Font("Segoe UI", 10);
        detailLabel.Location = new Point(45, 150);
        detailLabel.Size = new Size(470, 60);
        Controls.Add(detailLabel);

        continueButton.Text = "CONTINUAR";
        continueButton.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        continueButton.Size = new Size(180, 45);
        continueButton.Location = new Point(80, 235);
        continueButton.Click += (_, _) =>
        {
            if (LicenseService.CanRun())
            {
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                RefreshStatus();
            }
        };
        Controls.Add(continueButton);

        activateButton.Text = "ACTIVAR LICENCIA";
        activateButton.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        activateButton.Size = new Size(180, 45);
        activateButton.Location = new Point(300, 235);
        activateButton.Click += (_, _) =>
        {
            using var settings = new SettingsForm();
            settings.ShowDialog(this);
            RefreshStatus();

            if (LicenseService.IsPermanent)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        };
        Controls.Add(activateButton);
        AcceptButton = continueButton;
    }

    private void RefreshStatus()
    {
        if (LicenseService.IsPermanent)
        {
            daysLabel.Text = "LICENCIA ACTIVADA";
            detailLabel.Text = "Esta computadora fue activada correctamente.\nPodés continuar usando FerrariPOS sin límite de prueba.";
            daysLabel.ForeColor = Color.DarkGreen;
            continueButton.Enabled = true;
            activateButton.Enabled = true;
            activateButton.Enabled = false;
            return;
        }

        var days = LicenseService.DaysRemaining;
        if (!LicenseService.IsActivated)
        {
            daysLabel.Text = "LICENCIA REQUERIDA";
            detailLabel.Text = "Esta computadora todavía no está activada.\nUsá el Machine ID de Configuración para generar una licencia.";
            daysLabel.ForeColor = Color.DarkRed;
            continueButton.Enabled = false;
            activateButton.Enabled = true;
            return;
        }

        daysLabel.Text = days == 1 ? "1 DÍA RESTANTE" : $"{days} DÍAS RESTANTES";

        if (days > 0)
        {
            detailLabel.Text = "Tu licencia está activa.\nPodés continuar utilizando FerrariPOS.";
            daysLabel.ForeColor = days <= 7 ? Color.DarkRed : Color.DarkGreen;
            continueButton.Enabled = true;
        }
        else
        {
            detailLabel.Text = "El período de prueba finalizó.\nActivá la licencia para continuar utilizando FerrariPOS.";
            daysLabel.ForeColor = Color.DarkRed;
            continueButton.Enabled = false;
            activateButton.Enabled = true;
        }
    }
}
