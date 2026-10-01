using System;
using System.Drawing;
using System.Drawing.Printing;
using System.Windows.Forms;

namespace FerrarisPOS.Services
{
    /// <summary>
    /// Representación imprimible de un código interno ya guardado.
    /// La aplicación debe pasar el código existente; esta clase nunca cambia el valor.
    /// </summary>
    public sealed class PrintableBarcode
    {
        public string ProductName { get; }
        public string Code { get; }

        public PrintableBarcode(string productName, string code)
        {
            ProductName = productName ?? "";
            Code = code ?? "";
        }

        public void Print()
        {
            using var dialog = new PrintDialog();
            using var document = new PrintDocument();
            dialog.Document = document;

            document.PrintPage += (_, e) =>
            {
                var titleFont = new Font("Segoe UI", 10f, FontStyle.Bold);
                var codeFont = new Font("Segoe UI", 9f, FontStyle.Regular);
                e.Graphics.DrawString(ProductName, titleFont, Brushes.Black, 40, 35);
                e.Graphics.DrawString(Code, codeFont, Brushes.Black, 40, 150);
                e.HasMorePages = false;
                titleFont.Dispose();
                codeFont.Dispose();
            };

            if (dialog.ShowDialog() == DialogResult.OK)
                document.Print();
        }
    }
}
