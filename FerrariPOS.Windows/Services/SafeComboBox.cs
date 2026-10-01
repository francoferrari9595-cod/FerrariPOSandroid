using System.Windows.Forms;

namespace FerrarisPOS.Services;

/// <summary>
/// ComboBox resistente a listas vacías. WinForms puede lanzar InvalidArgument
/// cuando un valor externo intenta seleccionar un índice que todavía no existe.
/// La UI del POS debe poder arrancar aunque una consulta no devuelva registros.
/// </summary>
public class SafeComboBox : ComboBox
{
    public new int SelectedIndex
    {
        get => base.SelectedIndex;
        set
        {
            if (value < -1 || value >= Items.Count)
            {
                base.SelectedIndex = -1;
                return;
            }

            base.SelectedIndex = value;
        }
    }

    public new object? SelectedItem
    {
        get => base.SelectedItem;
        set
        {
            if (value is null)
            {
                base.SelectedIndex = -1;
                return;
            }

            try
            {
                base.SelectedItem = value;
            }
            catch (ArgumentException)
            {
                base.SelectedIndex = -1;
            }
        }
    }
}
