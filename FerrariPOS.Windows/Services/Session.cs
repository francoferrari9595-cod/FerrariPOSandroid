namespace FerrarisPOS.Services;

public static class Session
{
    public static int UserId { get; private set; }
    public static string Username { get; private set; } = "";
    public static string FullName { get; private set; } = "";
    public static string Role { get; private set; } = "";
    public static string Permissions { get; private set; } = "";

    public static bool IsAdmin =>
        string.Equals(Role, "ADMIN", StringComparison.OrdinalIgnoreCase);

    public static bool HasPermission(string permission) =>
        IsAdmin || Permissions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains("CONTROL_TOTAL", StringComparer.OrdinalIgnoreCase)
        || Permissions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(permission, StringComparer.OrdinalIgnoreCase);

    public static bool CanAccess(string module) => HasPermission(module);

    public static void Start(int userId, string username, string fullName, string role, string permissions = "")
    {
        UserId = userId;
        Username = username;
        FullName = fullName;
        Role = role;
        Permissions = permissions ?? "";
    }

    public static void Clear()
    {
        UserId = 0;
        Username = "";
        FullName = "";
        Role = "";
        Permissions = "";
    }
}
