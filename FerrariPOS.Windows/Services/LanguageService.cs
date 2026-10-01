using FerrarisPOS.Data;
using System.Globalization;

namespace FerrarisPOS.Services;

/// <summary>
/// Localización ligera y segura del POS. El idioma elegido durante la instalación
/// se usa como idioma inicial; después el usuario puede cambiarlo desde F7.
/// </summary>
public static class LanguageService
{
    public const string Spanish = "es";
    public const string English = "en";
    public const string Portuguese = "pt";
    public const string French = "fr";

    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        [Spanish] = "Español",
        [English] = "English",
        [Portuguese] = "Português",
        [French] = "Français"
    };

    private static readonly Dictionary<string, Dictionary<string, string>> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        [English] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Ferrari'sPOS® - Punto de Venta"] = "Ferrari'sPOS® - Point of Sale",
            ["FerrarisPOS · Salón"] = "FerrarisPOS · Floor",
            ["SALÓN:"] = "FLOOR:", ["+ SALÓN"] = "+ FLOOR", ["RENOMBRAR"] = "RENAME",
            ["+ MESA REDONDA"] = "+ ROUND TABLE", ["+ MESA"] = "+ TABLE", ["+ OVALADA"] = "+ OVAL TABLE",
            ["+ CÉSPED"] = "+ GRASS", ["+ PLANTA"] = "+ PLANT", ["EDITANDO"] = "EDITING",
            ["EDITAR"] = "EDIT", ["DESBLOQUEAR"] = "UNLOCK", ["BLOQUEAR"] = "LOCK", ["CERRAR"] = "CLOSE",
            ["DISEÑO BLOQUEADO"] = "DESIGN LOCKED", ["MODO EDICIÓN"] = "EDIT MODE", ["MODO OPERACIÓN"] = "OPERATION MODE",
            ["CONFIGURACIÓN DEL PROGRAMA"] = "PROGRAM SETTINGS", ["ACTIVACIÓN / RENOVACIÓN / DESACTIVACIÓN"] = "ACTIVATION / RENEWAL / DEACTIVATION",
            ["IDENTIDAD DEL COMERCIO Y APARIENCIA"] = "BUSINESS IDENTITY & APPEARANCE", ["NOMBRE DEL COMERCIO"] = "BUSINESS NAME",
            ["TIPO DE CAMBIO USD"] = "USD EXCHANGE RATE", ["SÍMBOLO DE MONEDA"] = "CURRENCY SYMBOL", ["GUARDAR NOMBRE"] = "SAVE NAME",
            ["GUARDAR MONEDA"] = "SAVE CURRENCY", ["TEMA DE LA INTERFAZ"] = "INTERFACE THEME", ["APLICAR TEMA"] = "APPLY THEME",
            ["IDIOMA DEL PROGRAMA"] = "PROGRAM LANGUAGE", ["IDIOMA"] = "LANGUAGE", ["GUARDAR IDIOMA"] = "SAVE LANGUAGE",
            ["APARIENCIA Y TIPOGRAFÍA DEL PROGRAMA"] = "PROGRAM APPEARANCE & TYPOGRAPHY", ["FUENTE PREDETERMINADA"] = "DEFAULT FONT",
            ["TAMAÑO"] = "SIZE", ["GUARDAR TAMAÑO"] = "SAVE SIZE", ["SONIDO DE CAJA REGISTRADORA"] = "CASH REGISTER SOUND",
            ["GUARDAR SONIDO"] = "SAVE SOUND", ["MESAS Y SALÓN"] = "TABLES & FLOOR", ["GUARDAR MESAS"] = "SAVE TABLES",
            ["ABRIR / EDITAR SALÓN"] = "OPEN / EDIT FLOOR", ["HABILITAR SISTEMA DE MESAS"] = "ENABLE TABLE SYSTEM",
            ["MOSTRAR SALÓN DENTRO DE LA PANTALLA PRINCIPAL"] = "SHOW FLOOR IN MAIN SCREEN",
            ["PERMITIR EDITAR Y ACOMODAR EL SALÓN"] = "ALLOW FLOOR EDITING AND LAYOUT",
            ["MOSTRAR ELEMENTOS DECORATIVOS (CÉSPED, PLANTAS, ETC.)"] = "SHOW DECORATIVE ELEMENTS (GRASS, PLANTS, ETC.)",
            ["USUARIO"] = "USER", ["CONTRASEÑA"] = "PASSWORD", ["INGRESAR AL SISTEMA"] = "SIGN IN",
            ["SALIR"] = "EXIT", ["INICIO DE SESIÓN"] = "SIGN IN", ["ACTIVAR LICENCIA"] = "ACTIVATE LICENSE",
            ["CÓDIGO DE BARRAS · Enter para agregar · 5*CODIGO = 5 unidades"] = "BARCODE · Enter to add · 5*CODE = 5 units",
            ["TOTAL A PAGAR"] = "TOTAL TO PAY", ["CÓDIGO"] = "CODE", ["DESCRIPCIÓN"] = "DESCRIPTION", ["PRECIO"] = "PRICE", ["CANTIDAD"] = "QUANTITY",
            ["IMPORTE"] = "AMOUNT", ["STOCK"] = "STOCK", ["DESCUENTO"] = "DISCOUNT", ["TIPO"] = "TYPE",
            ["Sistema listo"] = "System ready", ["ACTIVADA · SIN VENCIMIENTO"] = "ACTIVE · NO EXPIRY", ["VENCIDA · ACTIVAR EN F7"] = "EXPIRED · ACTIVATE IN F7",
            ["VENTA"] = "SALE", ["CLIENTES"] = "CUSTOMERS", ["PRODUCTOS"] = "PRODUCTS", ["INVENTARIO"] = "INVENTORY",
            ["CAJA"] = "CASH", ["REPORTES"] = "REPORTS", ["CONFIGURACION"] = "SETTINGS", ["CONFIGURACIÓN"] = "SETTINGS",
            ["USUARIOS"] = "USERS", ["PROVEEDORES"] = "SUPPLIERS", ["BUSCAR"] = "SEARCH", ["RESPALDO"] = "BACKUP", ["COBRAR"] = "CHARGE"
        },
        [Portuguese] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Ferrari'sPOS® - Punto de Venta"] = "Ferrari'sPOS® - Ponto de Venda", ["FerrarisPOS · Salón"] = "FerrarisPOS · Salão",
            ["SALÓN:"] = "SALÃO:", ["+ SALÓN"] = "+ SALÃO", ["RENOMBRAR"] = "RENOMEAR", ["+ MESA REDONDA"] = "+ MESA REDONDA",
            ["+ MESA"] = "+ MESA", ["+ OVALADA"] = "+ OVAL", ["EDITAR"] = "EDITAR", ["CERRAR"] = "FECHAR",
            ["CONFIGURACIÓN DEL PROGRAMA"] = "CONFIGURAÇÕES DO PROGRAMA", ["NOMBRE DEL COMERCIO"] = "NOME DO COMÉRCIO",
            ["SÍMBOLO DE MONEDA"] = "SÍMBOLO DA MOEDA", ["GUARDAR NOMBRE"] = "SALVAR NOME", ["GUARDAR MONEDA"] = "SALVAR MOEDA",
            ["TEMA DE LA INTERFAZ"] = "TEMA DA INTERFACE", ["APLICAR TEMA"] = "APLICAR TEMA", ["IDIOMA DEL PROGRAMA"] = "IDIOMA DO PROGRAMA",
            ["IDIOMA"] = "IDIOMA", ["GUARDAR IDIOMA"] = "SALVAR IDIOMA", ["MESAS Y SALÓN"] = "MESAS E SALÃO",
            ["GUARDAR MESAS"] = "SALVAR MESAS", ["ABRIR / EDITAR SALÓN"] = "ABRIR / EDITAR SALÃO", ["USUARIO"] = "USUÁRIO",
            ["CONTRASEÑA"] = "SENHA", ["INGRESAR AL SISTEMA"] = "ENTRAR NO SISTEMA", ["SALIR"] = "SAIR", ["INICIO DE SESIÓN"] = "LOGIN",
            ["ACTIVAR LICENCIA"] = "ATIVAR LICENÇA", ["VENTA"] = "VENDA", ["CLIENTES"] = "CLIENTES", ["PRODUCTOS"] = "PRODUTOS",
            ["INVENTARIO"] = "ESTOQUE", ["CAJA"] = "CAIXA", ["REPORTES"] = "RELATÓRIOS", ["CONFIGURACION"] = "CONFIGURAÇÕES", ["CONFIGURACIÓN"] = "CONFIGURAÇÕES",
            ["USUARIOS"] = "USUÁRIOS", ["PROVEEDORES"] = "FORNECEDORES", ["BUSCAR"] = "BUSCAR", ["RESPALDO"] = "BACKUP", ["COBRAR"] = "COBRAR"
        },
        [French] = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Ferrari'sPOS® - Punto de Venta"] = "Ferrari'sPOS® - Point de vente", ["FerrarisPOS · Salón"] = "FerrarisPOS · Salle",
            ["SALÓN:"] = "SALLE :", ["+ SALÓN"] = "+ SALLE", ["RENOMBRAR"] = "RENOMMER", ["+ MESA REDONDA"] = "+ TABLE RONDE",
            ["+ MESA"] = "+ TABLE", ["+ OVALADA"] = "+ OVALE", ["EDITAR"] = "MODIFIER", ["CERRAR"] = "FERMER",
            ["CONFIGURACIÓN DEL PROGRAMA"] = "PARAMÈTRES DU PROGRAMME", ["NOMBRE DEL COMERCIO"] = "NOM DU COMMERCE",
            ["SÍMBOLO DE MONEDA"] = "SYMBOLE MONÉTAIRE", ["GUARDAR NOMBRE"] = "ENREGISTRER LE NOM", ["GUARDAR MONEDA"] = "ENREGISTRER LA MONNAIE",
            ["TEMA DE LA INTERFAZ"] = "THÈME DE L'INTERFACE", ["APLICAR TEMA"] = "APPLIQUER LE THÈME", ["IDIOMA DEL PROGRAMA"] = "LANGUE DU PROGRAMME",
            ["IDIOMA"] = "LANGUE", ["GUARDAR IDIOMA"] = "ENREGISTRER LA LANGUE", ["MESAS Y SALÓN"] = "TABLES ET SALLE",
            ["GUARDAR MESAS"] = "ENREGISTRER LES TABLES", ["ABRIR / EDITAR SALÓN"] = "OUVRIR / MODIFIER LA SALLE", ["USUARIO"] = "UTILISATEUR",
            ["CONTRASEÑA"] = "MOT DE PASSE", ["INGRESAR AL SISTEMA"] = "SE CONNECTER", ["SALIR"] = "QUITTER", ["INICIO DE SESIÓN"] = "CONNEXION",
            ["ACTIVAR LICENCIA"] = "ACTIVER LA LICENCE", ["VENTA"] = "VENTE", ["CLIENTES"] = "CLIENTS", ["PRODUCTOS"] = "PRODUITS",
            ["INVENTARIO"] = "STOCK", ["CAJA"] = "CAISSE", ["REPORTES"] = "RAPPORTS", ["CONFIGURACION"] = "PARAMÈTRES", ["CONFIGURACIÓN"] = "PARAMÈTRES",
            ["USUARIOS"] = "UTILISATEURS", ["PROVEEDORES"] = "FOURNISSEURS", ["BUSCAR"] = "RECHERCHER", ["RESPALDO"] = "SAUVEGARDE", ["COBRAR"] = "ENCAISSER"
        }
    };

    public static string CurrentCode
    {
        get
        {
            var value = Database.GetSetting("language", Spanish).Trim().ToLowerInvariant();
            return Names.ContainsKey(value) ? value : Spanish;
        }
    }

    public static string CurrentName => Names[CurrentCode];

    public static IReadOnlyList<(string Code, string Name)> Options =>
        new[] { (Spanish, Names[Spanish]), (English, Names[English]), (Portuguese, Names[Portuguese]), (French, Names[French]) };

    public static string Translate(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || CurrentCode == Spanish) return text;
        if (Map.TryGetValue(CurrentCode, out var dict) && dict.TryGetValue(text, out var translated)) return translated;
        return text;
    }

    public static void Apply(Control root)
    {
        if (root == null) return;
        ApplyRecursive(root);
        root.Invalidate(true);
    }

    private static void ApplyRecursive(Control control)
    {
        if (!string.IsNullOrWhiteSpace(control.Text) && control is not TextBox)
            control.Text = Translate(control.Text);
        if (control is TextBox tb && !string.IsNullOrWhiteSpace(tb.PlaceholderText))
            tb.PlaceholderText = Translate(tb.PlaceholderText);
        foreach (Control child in control.Controls) ApplyRecursive(child);
    }

    public static void InitializeFromInstaller()
    {
        var selected = ReadInstallerLanguage();
        if (string.IsNullOrWhiteSpace(selected)) return;
        var code = InstallerNameToCode(selected);
        if (string.IsNullOrWhiteSpace(code)) return;
        var initialized = Database.GetSetting("language_initialized", "0") == "1";
        if (!initialized)
        {
            Database.SetSetting("language", code);
            Database.SetSetting("language_initialized", "1");
        }
    }

    public static void SetLanguage(string code)
    {
        code = code.Trim().ToLowerInvariant();
        if (!Names.ContainsKey(code)) code = Spanish;
        Database.SetSetting("language", code);
        Database.SetSetting("language_user_selected", "1");
        foreach (Form form in Application.OpenForms)
            Apply(form);
    }

    private static string? ReadInstallerLanguage()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "FerrarisPOS.install.language");
            if (!File.Exists(path)) return null;
            foreach (var line in File.ReadAllLines(path))
            {
                var parts = line.Split('=', 2);
                if (parts.Length == 2 && parts[0].Trim().Equals("Language", StringComparison.OrdinalIgnoreCase))
                    return parts[1].Trim();
            }
        }
        catch { }
        return null;
    }

    private static string InstallerNameToCode(string value) => value.ToLowerInvariant() switch
    {
        "spanish" => Spanish,
        "english" => English,
        "brazilianportuguese" => Portuguese,
        "french" => French,
        _ => Spanish
    };
}
