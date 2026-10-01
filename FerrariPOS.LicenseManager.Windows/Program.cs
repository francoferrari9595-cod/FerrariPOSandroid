namespace FerrariPOS.LicenseManager;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var login = new LoginForm();
        if (login.ShowDialog() != DialogResult.OK)
            return;
        Application.Run(new LicenseManagerForm());
    }
}
