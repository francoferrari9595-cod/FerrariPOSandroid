using System.Media;

namespace FerrarisPOS.Services;

public static class SoundService
{
    private static readonly string SoundFile =
        Path.Combine(AppContext.BaseDirectory, "Resources", "cash_register.wav");

    private static readonly string TrashSoundFile =
        Path.Combine(AppContext.BaseDirectory, "Resources", "trash_delete.wav");

    public static bool Enabled =>
        FerrarisPOS.Data.Database.GetSetting("cash_register_sound_enabled", "1") == "1";


    public static void PlayTrash()
    {
        if (!File.Exists(TrashSoundFile))
            return;

        try
        {
            using var player = new SoundPlayer(TrashSoundFile);
            player.Load();
            player.Play();
        }
        catch
        {
            // El sonido nunca debe impedir eliminar un producto.
        }
    }

    public static void PlayCashRegister()
    {
        if (!Enabled || !File.Exists(SoundFile))
            return;

        try
        {
            using var player = new SoundPlayer(SoundFile);
            player.Load();
            player.Play();
        }
        catch
        {
            // El sonido nunca debe impedir completar una venta.
        }
    }
}
