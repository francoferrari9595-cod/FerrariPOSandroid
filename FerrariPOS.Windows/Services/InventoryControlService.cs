using FerrarisPOS.Data;

namespace FerrarisPOS.Services;

/// <summary>Control global del uso de inventario. Cuando está deshabilitado,
/// los contadores existentes se conservan congelados y ninguna operación de venta,
/// compra, devolución, merma, conteo o ajuste modifica stock.</summary>
public static class InventoryControlService
{
    private const string SettingKey = "inventory_global_enabled";

    public static bool IsGlobalEnabled => Database.GetSetting(SettingKey, "0") == "1";
    public static bool DefaultUsesInventory => IsGlobalEnabled;

    public static void SetGlobalEnabled(bool enabled)
    {
        if (!Session.IsAdmin)
            throw new UnauthorizedAccessException("Solo el usuario ADMINISTRADOR puede modificar el uso global de inventario.");

        var previous = IsGlobalEnabled;
        Database.SetSetting(SettingKey, enabled ? "1" : "0");
        AuditService.Log(Session.UserId, "INVENTORY_GLOBAL_TOGGLE", "CONFIGURACION",
            $"Inventario global {(enabled ? "HABILITADO" : "DESHABILITADO")}. Estado anterior: {(previous ? "HABILITADO" : "DESHABILITADO")}.");
    }

    public static void RequireEnabled(string operation)
    {
        if (!IsGlobalEnabled)
            throw new InvalidOperationException($"El inventario global está deshabilitado. No se puede realizar: {operation}. Los contadores actuales están congelados.");
    }
}
