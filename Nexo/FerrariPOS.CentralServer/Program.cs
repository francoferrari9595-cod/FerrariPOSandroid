using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

var dataDir = Environment.GetEnvironmentVariable("FERRARIPOS_DATA_DIR")
              ?? Path.Combine(AppContext.BaseDirectory, "data");
Directory.CreateDirectory(dataDir);
var dbPath = Path.Combine(dataDir, "central.db");

var app = builder.Build();

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["Access-Control-Allow-Origin"] = "*";
    ctx.Response.Headers["Access-Control-Allow-Methods"] = "GET,POST,PUT,DELETE,OPTIONS";
    ctx.Response.Headers["Access-Control-Allow-Headers"] = "*";

    if (ctx.Request.Method == "OPTIONS")
    {
        ctx.Response.StatusCode = StatusCodes.Status204NoContent;
        return;
    }

    await next();
});

await EnsureDatabase(dbPath);

app.MapGet("/", () => Results.Content(WebPage.Html, "text/html; charset=utf-8"));

app.MapGet("/health", async () =>
{
    try
    {
        await using var cn = new SqliteConnection($"Data Source={dbPath}");
        await cn.OpenAsync();

        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM stores";
        var stores = Convert.ToInt32(await cmd.ExecuteScalarAsync());

        return Results.Ok(new
        {
            status = "ok",
            service = "FerrariPOS Central",
            database = "ok",
            stores,
            utc = DateTimeOffset.UtcNow,
            version = "4.1-live"
        });
    }
    catch
    {
        return Results.StatusCode(503);
    }
});

app.MapGet("/api/v1/config", () => Results.Ok(new
{
    service = "FerrariPOS Central",
    api_version = "v1",
    mobile_enabled = true,
    web_enabled = true,
    qr_public = true,
    registration_required = true
}));

app.MapPost("/api/v1/stores/register", async (HttpRequest request) =>
{
    var dto = await JsonSerializer.DeserializeAsync<RegisterStoreRequest>(
        request.Body,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    if (dto is null || string.IsNullOrWhiteSpace(dto.InstallationId))
        return Results.BadRequest(new { error = "installation_id_required" });

    var installationId = dto.InstallationId.Trim();
    var storeId = StableStoreId(installationId);

    await using var cn = new SqliteConnection($"Data Source={dbPath}");
    await cn.OpenAsync();

    string token;
    string? username;
    bool newAccount = false;

    await using (var find = cn.CreateCommand())
    {
        find.CommandText =
            "SELECT token, web_username FROM stores WHERE installation_id=$installation LIMIT 1";
        find.Parameters.AddWithValue("$installation", installationId);

        await using var r = await find.ExecuteReaderAsync();

        if (await r.ReadAsync())
        {
            token = r.GetString(0);
            username = r.IsDBNull(1) ? null : r.GetString(1);
        }
        else
        {
            token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            username = null;
        }
    }

    string? initialPassword = null;

    if (string.IsNullOrWhiteSpace(username))
    {
        username = "tienda_" + storeId;
        initialPassword = GeneratePassword();
        newAccount = true;

        await using var ins = cn.CreateCommand();
        ins.CommandText = """
            INSERT INTO stores
              (store_id,installation_id,name,token,updated_utc,last_heartbeat_utc,web_username,web_password_hash)
            VALUES
              ($id,$installation,$name,$token,$utc,'',$username,$hash)
            ON CONFLICT(store_id) DO UPDATE SET
              name=$name,
              token=$token,
              updated_utc=$utc,
              web_username=$username,
              web_password_hash=$hash
            """;

        ins.Parameters.AddWithValue("$id", storeId);
        ins.Parameters.AddWithValue("$installation", installationId);
        ins.Parameters.AddWithValue("$name", dto.StoreName?.Trim() ?? "FerrariPOS");
        ins.Parameters.AddWithValue("$token", token);
        ins.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        ins.Parameters.AddWithValue("$username", username);
        ins.Parameters.AddWithValue("$hash", HashPassword(initialPassword));

        await ins.ExecuteNonQueryAsync();
    }
    else
    {
        await using var upd = cn.CreateCommand();
        upd.CommandText =
            "UPDATE stores SET name=$name, updated_utc=$utc WHERE store_id=$id";

        upd.Parameters.AddWithValue("$name", dto.StoreName?.Trim() ?? "FerrariPOS");
        upd.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
        upd.Parameters.AddWithValue("$id", storeId);

        await upd.ExecuteNonQueryAsync();
    }

    return Results.Ok(new
    {
        store_id = storeId,
        token,
        web_username = username,
        web_password = initialPassword,
        web_account_created = newAccount,
        api = "/api/v1",
        central_url = $"{request.Scheme}://{request.Host}",
        qr_public = true
    });
});

app.MapPost("/api/v1/stores/{storeId}/web-password/reset",
    async (string storeId, HttpRequest request) =>
{
    if (!await Authorized(request, dbPath, storeId))
        return Results.Unauthorized();

    var password = GeneratePassword();

    await using var cn = new SqliteConnection($"Data Source={dbPath}");
    await cn.OpenAsync();

    await using var cmd = cn.CreateCommand();
    cmd.CommandText =
        "UPDATE stores SET web_password_hash=$hash, updated_utc=$utc WHERE store_id=$id";

    cmd.Parameters.AddWithValue("$hash", HashPassword(password));
    cmd.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
    cmd.Parameters.AddWithValue("$id", storeId);

    await cmd.ExecuteNonQueryAsync();

    return Results.Ok(new { ok = true, password });
});

app.MapPost("/api/v1/web/login", async (HttpRequest request) =>
{
    var dto = await JsonSerializer.DeserializeAsync<WebLoginRequest>(
        request.Body,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

    if (dto is null ||
        string.IsNullOrWhiteSpace(dto.Username) ||
        string.IsNullOrWhiteSpace(dto.Password))
        return Results.BadRequest(new { error = "credentials_required" });

    await using var cn = new SqliteConnection($"Data Source={dbPath}");
    await cn.OpenAsync();

    await using var cmd = cn.CreateCommand();
    cmd.CommandText =
        "SELECT store_id, web_password_hash, name FROM stores WHERE web_username=$u LIMIT 1";
    cmd.Parameters.AddWithValue("$u", dto.Username.Trim());

    await using var r = await cmd.ExecuteReaderAsync();

    if (!await r.ReadAsync() ||
        !VerifyPassword(dto.Password, r.GetString(1)))
        return Results.Unauthorized();

    var storeId = r.GetString(0);
    var storeName = r.GetString(2);
    var session = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

    await r.DisposeAsync();

    var days = dto.Remember ? 3650 : 1;
    var expires = DateTimeOffset.UtcNow.AddDays(days);

    await using var ins = cn.CreateCommand();
    ins.CommandText =
        "INSERT INTO web_sessions(session_token,store_id,expires_utc) VALUES($s,$id,$exp)";

    ins.Parameters.AddWithValue("$s", session);
    ins.Parameters.AddWithValue("$id", storeId);
    ins.Parameters.AddWithValue("$exp", expires.ToString("O"));

    await ins.ExecuteNonQueryAsync();

    request.HttpContext.Response.Cookies.Append(
        "ferraripos_session",
        session,
        new CookieOptions
        {
            HttpOnly = true,
            Secure = request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Expires = expires,
            MaxAge = TimeSpan.FromDays(days),
            Path = "/"
        });

    return Results.Ok(new
    {
        ok = true,
        store_id = storeId,
        store_name = storeName,
        remembered = dto.Remember
    });
});

app.MapPost("/api/v1/web/logout", async (HttpRequest request) =>
{
    var s = request.Cookies["ferraripos_session"];

    if (!string.IsNullOrWhiteSpace(s))
    {
        await using var cn = new SqliteConnection($"Data Source={dbPath}");
        await cn.OpenAsync();

        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "DELETE FROM web_sessions WHERE session_token=$s";
        cmd.Parameters.AddWithValue("$s", s);

        await cmd.ExecuteNonQueryAsync();
    }

    request.HttpContext.Response.Cookies.Delete("ferraripos_session");

    return Results.Ok(new { ok = true });
});

app.MapGet("/api/v1/web/me", async (HttpRequest request) =>
{
    var auth = await WebSession(request, dbPath);

    if (auth is null)
        return Results.Unauthorized();

    return Results.Ok(new
    {
        ok = true,
        store_id = auth.StoreId,
        store_name = auth.StoreName,
        web_username = auth.Username
    });
});

app.MapGet("/api/v1/web/dashboard", async (HttpRequest request) =>
{
    request.HttpContext.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
    request.HttpContext.Response.Headers.Pragma = "no-cache";
    var auth = await WebSession(request, dbPath);

    if (auth is null)
        return Results.Unauthorized();

    var target = Path.Combine(dataDir, "snapshots", auth.StoreId + ".json");

    if (!File.Exists(target))
    {
        return Results.Ok(new
        {
            store_id = auth.StoreId,
            store_name = auth.StoreName,
            synchronized = false,
            message = "Esperando la primera sincronización de FerrariPOS."
        });
    }

    try
    {
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(target));

        // El navegador consume el dashboard directamente. El snapshot se conserva
        // como transporte entre el POS Windows y Render, pero la respuesta expone
        // tambien sus campos en la raiz para que todas las secciones del panel
        // puedan trabajar con datos reales sin depender de un segundo endpoint.
        var root = doc.RootElement;
        var payload = new Dictionary<string, object?>
        {
            ["store_id"] = auth.StoreId,
            ["store_name"] = auth.StoreName,
            ["synchronized"] = true,
            ["snapshot_utc"] = File.GetLastWriteTimeUtc(target),
            ["data"] = root
        };

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
                payload[property.Name] = property.Value.Clone();
        }

        return Results.Json(payload);
    }
    catch
    {
        return Results.StatusCode(500);
    }
});

/* ETAPA_7_WEB_INTERACTIVE: API DE COMANDOS DEL PANEL */
app.MapPost("/api/v1/web/commands", async (HttpRequest request) =>
{
    var auth = await WebSession(request, dbPath);
    if (auth is null) return Results.Unauthorized();
    using var doc = await JsonDocument.ParseAsync(request.Body);
    var root = doc.RootElement;
    var type = root.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
    var payload = root.TryGetProperty("payload", out var p) ? p.GetRawText() : "{}";
    var allowed = new[] { "stock.adjust","customer.upsert","customer.payment","customer.delete","product.price","cash.movement","refresh.snapshot" };
    if (!allowed.Contains(type, StringComparer.OrdinalIgnoreCase)) return Results.BadRequest(new { error = "command_not_allowed" });
    var id = Guid.NewGuid().ToString("N");
    await using var cn = new SqliteConnection($"Data Source={dbPath}");
    await cn.OpenAsync();
    await using var cmd = cn.CreateCommand();
    cmd.CommandText = """
        INSERT INTO web_commands(id,store_id,type,payload,status,created_utc)
        VALUES($id,$store,$type,$payload,'PENDING',$utc)
        """;
    cmd.Parameters.AddWithValue("$id", id);
    cmd.Parameters.AddWithValue("$store", auth.StoreId);
    cmd.Parameters.AddWithValue("$type", type);
    cmd.Parameters.AddWithValue("$payload", payload);
    cmd.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
    await cmd.ExecuteNonQueryAsync();
    return Results.Ok(new { ok = true, command_id = id, status = "PENDING" });
});

app.MapGet("/api/v1/web/commands/{id}", async (string id, HttpRequest request) =>
{
    var auth = await WebSession(request, dbPath);
    if (auth is null) return Results.Unauthorized();
    await using var cn = new SqliteConnection($"Data Source={dbPath}");
    await cn.OpenAsync();
    await using var cmd = cn.CreateCommand();
    cmd.CommandText = """
        SELECT id,type,status,result,error,created_utc,finished_utc
        FROM web_commands WHERE id=$id AND store_id=$store LIMIT 1
        """;
    cmd.Parameters.AddWithValue("$id", id);
    cmd.Parameters.AddWithValue("$store", auth.StoreId);
    await using var r = await cmd.ExecuteReaderAsync();
    if (!await r.ReadAsync()) return Results.NotFound();
    return Results.Ok(new { id=r.GetString(0), type=r.GetString(1), status=r.GetString(2), result=r.IsDBNull(3)?null:r.GetString(3), error=r.IsDBNull(4)?null:r.GetString(4), created_utc=r.GetString(5), finished_utc=r.IsDBNull(6)?null:r.GetString(6) });
});

app.MapGet("/api/v1/stores/{storeId}/commands", async (string storeId, HttpRequest request) =>
{
    if (!await Authorized(request, dbPath, storeId)) return Results.Unauthorized();
    await using var cn = new SqliteConnection($"Data Source={dbPath}");
    await cn.OpenAsync();
    await using var tx = (Microsoft.Data.Sqlite.SqliteTransaction)await cn.BeginTransactionAsync();
    var list = new List<object>();
    await using (var cmd = cn.CreateCommand())
    {
        cmd.Transaction = tx;
        cmd.CommandText = """
            SELECT id,type,payload,created_utc FROM web_commands
            WHERE store_id=$store AND status='PENDING'
            ORDER BY created_utc LIMIT 10
            """;
        cmd.Parameters.AddWithValue("$store", storeId);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(new { id=r.GetString(0), type=r.GetString(1), payload=r.GetString(2), created_utc=r.GetString(3) });
    }
    if (list.Count > 0)
    {
        await using var mark = cn.CreateCommand();
        mark.Transaction = tx;
        mark.CommandText = "UPDATE web_commands SET status='DELIVERED' WHERE store_id=$store AND status='PENDING'";
        mark.Parameters.AddWithValue("$store", storeId);
        await mark.ExecuteNonQueryAsync();
    }
    await tx.CommitAsync();
    return Results.Ok(list);
});

app.MapPost("/api/v1/stores/{storeId}/commands/{id}/result", async (string storeId, string id, HttpRequest request) =>
{
    if (!await Authorized(request, dbPath, storeId)) return Results.Unauthorized();
    using var doc = await JsonDocument.ParseAsync(request.Body);
    var root = doc.RootElement;
    var ok = root.TryGetProperty("ok", out var okEl) && okEl.GetBoolean();
    var result = root.TryGetProperty("result", out var resultEl) ? resultEl.GetRawText() : "{}";
    var error = root.TryGetProperty("error", out var errEl) && errEl.ValueKind == JsonValueKind.String ? errEl.GetString() : null;
    await using var cn = new SqliteConnection($"Data Source={dbPath}");
    await cn.OpenAsync();
    await using var cmd = cn.CreateCommand();
    cmd.CommandText = """
        UPDATE web_commands SET status=$status,result=$result,error=$error,finished_utc=$utc
        WHERE id=$id AND store_id=$store
        """;
    cmd.Parameters.AddWithValue("$status", ok ? "DONE" : "ERROR");
    cmd.Parameters.AddWithValue("$result", result);
    cmd.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
    cmd.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
    cmd.Parameters.AddWithValue("$id", id);
    cmd.Parameters.AddWithValue("$store", storeId);
    await cmd.ExecuteNonQueryAsync();
    return Results.Ok(new { ok = true });
});

app.MapPost("/api/v1/stores/{storeId}/heartbeat",
    async (string storeId, HttpRequest request) =>
{
    if (!await Authorized(request, dbPath, storeId))
        return Results.Unauthorized();

    await using var cn = new SqliteConnection($"Data Source={dbPath}");
    await cn.OpenAsync();

    await using var cmd = cn.CreateCommand();
    cmd.CommandText =
        "UPDATE stores SET updated_utc=$utc, last_heartbeat_utc=$utc WHERE store_id=$id";

    cmd.Parameters.AddWithValue("$utc", DateTimeOffset.UtcNow.ToString("O"));
    cmd.Parameters.AddWithValue("$id", storeId);

    await cmd.ExecuteNonQueryAsync();

    return Results.Ok(new
    {
        ok = true,
        store_id = storeId,
        server_utc = DateTimeOffset.UtcNow
    });
});

app.MapGet("/api/v1/stores/{storeId}/pairing",
    async (string storeId, HttpRequest request) =>
{
    if (!await Authorized(request, dbPath, storeId))
        return Results.Unauthorized();

    await using var cn = new SqliteConnection($"Data Source={dbPath}");
    await cn.OpenAsync();

    await using var cmd = cn.CreateCommand();
    cmd.CommandText =
        "SELECT name, token FROM stores WHERE store_id=$id LIMIT 1";
    cmd.Parameters.AddWithValue("$id", storeId);

    await using var reader = await cmd.ExecuteReaderAsync();

    if (!await reader.ReadAsync())
        return Results.NotFound();

    var name = reader.GetString(0);
    var token = reader.GetString(1);

    return Results.Ok(new
    {
        app = "FerrariPOS Manager",
        version = 4,
        baseUrl = $"{request.Scheme}://{request.Host}",
        lanUrl = "",
        token,
        store = name,
        store_id = storeId,
        mode = "CENTRAL"
    });
});

app.MapGet("/api/v1/stores/{storeId}/status",
    async (string storeId, HttpRequest request) =>
{
    if (!await Authorized(request, dbPath, storeId))
        return Results.Unauthorized();

    await using var cn = new SqliteConnection($"Data Source={dbPath}");
    await cn.OpenAsync();

    await using var cmd = cn.CreateCommand();
    cmd.CommandText =
        "SELECT name, updated_utc, COALESCE(last_heartbeat_utc,'') FROM stores WHERE store_id=$id LIMIT 1";
    cmd.Parameters.AddWithValue("$id", storeId);

    await using var reader = await cmd.ExecuteReaderAsync();

    if (!await reader.ReadAsync())
        return Results.NotFound();

    return Results.Ok(new
    {
        store_id = storeId,
        name = reader.GetString(0),
        registered_utc = reader.GetString(1),
        last_heartbeat_utc = reader.GetString(2),
        server_utc = DateTimeOffset.UtcNow
    });
});

app.MapPost("/api/v1/stores/{storeId}/snapshot",
    async (string storeId, HttpRequest request) =>
{
    if (!await Authorized(request, dbPath, storeId))
        return Results.Unauthorized();

    var dir = Path.Combine(dataDir, "snapshots");
    Directory.CreateDirectory(dir);

    var target = Path.Combine(dir, storeId + ".json");
    var temp = target + ".tmp";

    await using (var output = File.Create(temp))
        await request.Body.CopyToAsync(output);

    File.Move(temp, target, true);

    return Results.Ok(new
    {
        ok = true,
        updated_utc = DateTimeOffset.UtcNow
    });
});

app.MapGet("/api/v1/stores/{storeId}/snapshot",
    async (string storeId, HttpRequest request) =>
{
    if (!await Authorized(request, dbPath, storeId))
        return Results.Unauthorized();

    var target = Path.Combine(dataDir, "snapshots", storeId + ".json");

    if (!File.Exists(target))
        return Results.NotFound(new { error = "snapshot_not_available" });

    return Results.File(target, "application/json");
});

app.Run();

static async Task EnsureDatabase(string dbPath)
{
    await using var cn = new SqliteConnection($"Data Source={dbPath}");
    await cn.OpenAsync();

    await using var cmd = cn.CreateCommand();

    cmd.CommandText = """
        PRAGMA journal_mode=WAL;
        PRAGMA synchronous=NORMAL;
        PRAGMA busy_timeout=10000;

        CREATE TABLE IF NOT EXISTS stores(
            store_id TEXT PRIMARY KEY,
            installation_id TEXT NOT NULL UNIQUE,
            name TEXT NOT NULL,
            token TEXT NOT NULL,
            updated_utc TEXT NOT NULL,
            last_heartbeat_utc TEXT NOT NULL DEFAULT '',
            web_username TEXT,
            web_password_hash TEXT
        );

        CREATE TABLE IF NOT EXISTS web_sessions(
            session_token TEXT PRIMARY KEY,
            store_id TEXT NOT NULL,
            expires_utc TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS web_commands(
            id TEXT PRIMARY KEY,
            store_id TEXT NOT NULL,
            type TEXT NOT NULL,
            payload TEXT NOT NULL,
            status TEXT NOT NULL,
            created_utc TEXT NOT NULL,
            finished_utc TEXT,
            result TEXT,
            error TEXT
        );
        CREATE INDEX IF NOT EXISTS idx_web_commands_store_status
            ON web_commands(store_id,status,created_utc);
        """;

    await cmd.ExecuteNonQueryAsync();

    foreach (var sql in new[]
    {
        "ALTER TABLE stores ADD COLUMN web_username TEXT",
        "ALTER TABLE stores ADD COLUMN web_password_hash TEXT"
    })
    {
        try
        {
            await using var alter = cn.CreateCommand();
            alter.CommandText = sql;
            await alter.ExecuteNonQueryAsync();
        }
        catch (SqliteException)
        {
            // Column already exists.
        }
    }
}

static async Task<bool> Authorized(
    HttpRequest request,
    string dbPath,
    string storeId)
{
    var supplied = request.Headers["X-FerrariPOS-Token"].ToString();

    if (string.IsNullOrWhiteSpace(supplied))
        return false;

    await using var cn = new SqliteConnection($"Data Source={dbPath}");
    await cn.OpenAsync();

    await using var cmd = cn.CreateCommand();
    cmd.CommandText =
        "SELECT token FROM stores WHERE store_id=$id LIMIT 1";
    cmd.Parameters.AddWithValue("$id", storeId);

    var expected = await cmd.ExecuteScalarAsync() as string;

    if (string.IsNullOrWhiteSpace(expected))
        return false;

    return CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(expected),
        Encoding.UTF8.GetBytes(supplied));
}

static async Task<WebAuth?> WebSession(
    HttpRequest request,
    string dbPath)
{
    var session = request.Cookies["ferraripos_session"];

    if (string.IsNullOrWhiteSpace(session))
        return null;

    await using var cn = new SqliteConnection($"Data Source={dbPath}");
    await cn.OpenAsync();

    await using var cmd = cn.CreateCommand();

    cmd.CommandText = """
        SELECT
            s.store_id,
            st.name,
            st.web_username,
            s.expires_utc
        FROM web_sessions s
        JOIN stores st ON st.store_id=s.store_id
        WHERE s.session_token=$s
        LIMIT 1
        """;

    cmd.Parameters.AddWithValue("$s", session);

    await using var r = await cmd.ExecuteReaderAsync();

    if (!await r.ReadAsync())
        return null;

    if (!DateTimeOffset.TryParse(
            r.GetString(3),
            out var exp) ||
        exp <= DateTimeOffset.UtcNow)
        return null;

    return new WebAuth(
        r.GetString(0),
        r.GetString(1),
        r.IsDBNull(2) ? "" : r.GetString(2));
}

static string HashPassword(string password) =>
    Convert.ToHexString(
        SHA256.HashData(
            Encoding.UTF8.GetBytes(password)))
    .ToLowerInvariant();

static bool VerifyPassword(
    string password,
    string hash)
{
    return CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(HashPassword(password)),
        Encoding.UTF8.GetBytes(hash));
}

static string GeneratePassword() =>
    Convert.ToHexString(
        RandomNumberGenerator.GetBytes(6))
    .ToLowerInvariant();

static string StableStoreId(string installationId)
{
    var hash = SHA256.HashData(
        Encoding.UTF8.GetBytes(
            installationId.Trim().ToLowerInvariant()));

    return Convert.ToHexString(hash[..10])
        .ToLowerInvariant();
}

record RegisterStoreRequest(
    string InstallationId,
    string? StoreName);

record WebLoginRequest(
    string Username,
    string Password,
    bool Remember = true);
record WebCommandRequest(string Type, JsonElement Payload);

record WebAuth(
    string StoreId,
    string StoreName,
    string Username);

static class WebPage
{
    public const string Html = """
<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>FerrariPOS Central</title>
<style>
:root{
 --bg:#07101b;--panel:#0d1724;--panel2:#101e2d;--line:#20364d;
 --text:#f5f9ff;--muted:#91a4b8;--cyan:#27d9ff;--green:#25e59a;
 --gold:#ffc45c;--red:#ff6680;--blue:#4d8dff;--shadow:0 18px 50px #0008
}
*{box-sizing:border-box}
body{
 margin:0;background:
 radial-gradient(circle at 10% 0%,#12324d 0,transparent 34%),
 radial-gradient(circle at 100% 20%,#16284b 0,transparent 30%),
 var(--bg);color:var(--text);font-family:Segoe UI,Inter,Arial,sans-serif
}
button,input{font:inherit}
button{cursor:pointer}
.wrap{max-width:1500px;margin:auto;padding:22px}
.card{
 background:linear-gradient(145deg,#101d2b,#0b1521);
 border:1px solid var(--line);border-radius:20px;
 box-shadow:var(--shadow);padding:20px
}
.login{max-width:470px;margin:8vh auto}
.brand{display:flex;align-items:center;gap:14px}
.logo{
 width:54px;height:54px;border-radius:16px;display:grid;place-items:center;
 background:linear-gradient(135deg,#ffb34d,#ff6a55);font-size:25px;font-weight:900
}
h1,h2,h3{margin:0 0 8px}
.muted{color:var(--muted)}
input{
 width:100%;padding:13px 14px;margin:8px 0;border-radius:11px;
 border:1px solid #2d455d;background:#07111d;color:white;outline:none
}
input:focus{border-color:var(--cyan);box-shadow:0 0 0 3px #27d9ff18}
.primary{
 width:100%;padding:13px;border:0;border-radius:11px;color:#001018;
 font-weight:900;background:linear-gradient(90deg,var(--cyan),#6de9ff)
}
.top{
 display:flex;justify-content:space-between;align-items:center;gap:16px;
 margin-bottom:16px
}
.actions{display:flex;gap:8px;flex-wrap:wrap}
.action{
 padding:9px 13px;border-radius:10px;border:1px solid #2c465f;
 background:#0d1b2a;color:white
}
.status{display:inline-flex;align-items:center;gap:7px;color:var(--green);font-weight:800}
.dot{width:9px;height:9px;border-radius:50%;background:var(--green);box-shadow:0 0 14px var(--green)}
.tabs{display:flex;gap:8px;overflow:auto;padding:5px 0 14px}
.tab{
 white-space:nowrap;padding:10px 15px;border:1px solid #274058;border-radius:11px;
 background:#0b1724;color:#aebed0
}
.tab.active{color:white;border-color:var(--cyan);box-shadow:0 0 18px #27d9ff18}
.metrics{
 display:grid;grid-template-columns:repeat(6,minmax(140px,1fr));gap:12px
}
.metric{
 padding:18px;border-radius:16px;background:#0a1623;border:1px solid #203b54;
 min-height:105px
}
.metric b{color:#9db0c4;font-size:12px;text-transform:uppercase;letter-spacing:1px}
.metric strong{display:block;font-size:28px;margin-top:9px}
.metric.cyan strong{color:var(--cyan)}
.metric.green strong{color:var(--green)}
.metric.gold strong{color:var(--gold)}
.metric.red strong{color:var(--red)}
.grid2{display:grid;grid-template-columns:1.4fr 1fr;gap:15px;margin-top:15px}
.section{display:none;margin-top:15px}
.section.active{display:block}
.rows{display:grid;gap:8px}
.row{
 display:grid;grid-template-columns:minmax(150px,.8fr) minmax(200px,1.2fr);
 gap:15px;padding:11px 0;border-bottom:1px solid #1d3144
}
.row:last-child{border-bottom:0}
.k{color:#8fa4ba}.v{overflow-wrap:anywhere}
.badge{
 display:inline-flex;padding:5px 9px;border-radius:999px;background:#142b3e;
 border:1px solid #29465d;color:#cce8ff;font-size:12px;font-weight:800
}
.tablewrap{overflow:auto}
table{width:100%;border-collapse:collapse;min-width:650px}
th,td{text-align:left;padding:11px;border-bottom:1px solid #203447}
th{color:#8fa5ba;font-size:12px;text-transform:uppercase}
.bar{height:10px;background:#122437;border-radius:99px;overflow:hidden}
.bar>i{display:block;height:100%;background:linear-gradient(90deg,var(--cyan),var(--green));border-radius:99px}
pre{
 white-space:pre-wrap;word-break:break-word;max-height:600px;overflow:auto;
 background:#050b12;padding:15px;border-radius:13px;color:#bfeaff
}
.search{max-width:420px}
.err{color:#ff8395;margin-top:10px}
.footer{text-align:center;color:#657b90;font-size:12px;padding:22px}
@media(max-width:1050px){.metrics{grid-template-columns:repeat(3,1fr)}}
@media(max-width:700px){
 .wrap{padding:10px}.metrics{grid-template-columns:repeat(2,1fr)}
 .grid2{grid-template-columns:1fr}.row{grid-template-columns:1fr;gap:4px}
 .top{align-items:flex-start;flex-direction:column}.card{border-radius:15px;padding:14px}
}
</style>
</head>
<body>
<div class="wrap">

<section id="loginBox" class="card login">
 <div class="brand">
  <div class="logo">F</div>
  <div><h1>FerrariPOS Central</h1><div class="muted">Panel seguro de tu negocio</div></div>
 </div>
 <p class="muted">Ingresá con el usuario y contraseña que FerrariPOS generó para esta instalación.</p>
 <input id="u" autocomplete="username" placeholder="Usuario">
 <input id="p" autocomplete="current-password" type="password" placeholder="Contraseña">
 <label class="muted"><input id="remember" type="checkbox" checked style="width:auto;margin-right:7px">Mantener la cuenta conectada</label>
 <button class="primary" onclick="login()">INGRESAR AL PANEL</button>
 <div id="err" class="err"></div>
</section>

<section id="appBox" style="display:none">
 <div class="top">
  <div>
   <div class="status"><span class="dot"></span> CONECTADO</div>
   <h1 id="storeName">FerrariPOS</h1>
   <div id="storeMeta" class="muted"></div>
  </div>
  <div class="actions">
   <button class="action" onclick="refresh(true)">↻ Actualizar</button>
   <button class="action" onclick="logout()">Cerrar sesión</button>
  </div>
 </div>

 <div class="card">
  <div class="top" style="margin-bottom:4px">
   <div><b>PANEL DE CONTROL EN TIEMPO REAL</b><div class="muted" id="syncText">Cargando...</div></div>
   <div class="badge" id="connection">Sincronizando</div>
  </div>

  <div id="liveCloudflareBox" class="card" style="display:none;margin-top:14px;padding:10px">
   <div class="top" style="margin-bottom:8px">
    <div><b>PANEL EN VIVO · CLOUDFLARE QUICK TUNNEL</b><div class="muted">Vista directa de la instancia real de FerrariPOS. Permite interactuar como desde Android.</div></div>
    <a id="liveCloudflareOpen" class="action" target="_blank" rel="noopener">Abrir en otra pestaña</a>
   </div>
   <iframe id="liveCloudflareFrame" title="FerrariPOS en vivo" style="width:100%;height:78vh;min-height:700px;border:1px solid rgba(255,255,255,.12);border-radius:14px;background:#080b10"></iframe>
  </div>

  <div id="pairingQrBox" class="card" style="margin-top:14px;padding:18px">
   <div class="top" style="margin-bottom:12px">
    <div><b>CONEXIÓN ANDROID · QR DEL MANAGER</b><div class="muted">Mismo QR que genera FerrariPOS en Windows. Se actualiza automáticamente cuando cambia la vinculación.</div></div>
    <span id="pairingQrStatus" class="badge">ESPERANDO</span>
   </div>
   <div style="display:flex;gap:22px;align-items:center;flex-wrap:wrap">
    <div style="background:#fff;padding:14px;border-radius:16px"><img id="pairingQrImage" alt="QR Manager Android" style="display:block;width:260px;height:260px;image-rendering:pixelated"></div>
    <div class="rows" style="min-width:280px;flex:1">
      <div class="row"><span>Código alternativo</span><b id="pairingQrCode">-</b></div>
      <div class="row"><span>Quick Tunnel</span><b id="pairingQrUrl">-</b></div>
      <div class="row"><span>Generado</span><b id="pairingQrTime">-</b></div>
    </div>
   </div>
  </div>

  <div class="tabs">
   <button class="tab active" data-tab="summary">RESUMEN</button>
   <button class="tab" data-tab="sales">VENTAS Y TICKETS</button>
   <button class="tab" data-tab="cash">CAJA Y PAGOS</button>
   <button class="tab" data-tab="products">PRODUCTOS / STOCK</button>
   <button class="tab" data-tab="customers">CLIENTES</button>
   <button class="tab" data-tab="operations">MESAS / OPERACIÓN</button>
   <button class="tab" data-tab="activity">ACTIVIDAD</button>
   <button class="tab" data-tab="technical">DATOS TÉCNICOS</button>
   <button class="tab" data-tab="json">DATOS COMPLETOS</button>
   <button class="tab" data-tab="actions">ACCIONES</button>
  </div>
 </div>

 <div id="summary" class="section active">
  <div id="metrics" class="metrics"></div>
  <div class="grid2">
   <div class="card"><h2>Resumen del negocio</h2><div id="summaryRows" class="rows"></div></div>
   <div class="card"><h2>Conexión</h2><div id="connectionRows" class="rows"></div></div>
  </div>
 </div>

 <div id="sales" class="section">
  <div class="card">
   <div class="top"><div><h2>Ventas del turno</h2><div class="muted">Información recibida desde la instalación</div></div><input id="saleSearch" class="search" placeholder="Buscar venta..." oninput="renderSales()"></div>
   <div id="salesTable" class="tablewrap"></div>
  </div>
 </div>

 <div id="cash" class="section">
  <div class="grid2">
   <div class="card"><h2>Caja y pagos</h2><div id="cashRows" class="rows"></div></div>
   <div class="card"><h2>Medios de pago</h2><div id="paymentsRows" class="rows"></div></div>
  </div>
 </div>

 <div id="products" class="section">
  <div class="grid2">
   <div class="card"><h2>Productos y stock</h2><div id="productsRows" class="rows"></div></div>
   <div class="card"><h2>Indicadores de inventario</h2><div id="stockRows" class="rows"></div></div>
  </div>
 </div>

 <div id="customers" class="section">
  <div class="card"><h2>Clientes</h2><div id="customersRows" class="rows"></div></div>
 </div>

 <div id="operations" class="section">
  <div class="grid2">
   <div class="card"><h2>Mesas y operación</h2><div id="operationsRows" class="rows"></div></div>
   <div class="card"><h2>Reservas / pedidos</h2><div id="ordersRows" class="rows"></div></div>
  </div>
 </div>

 <div id="activity" class="section">
  <div class="card"><h2>Actividad</h2><div id="activityRows" class="rows"></div></div>
 </div>

 <div id="technical" class="section">
  <div class="grid2">
   <div class="card"><h2>Estado técnico</h2><div id="technicalRows" class="rows"></div></div>
   <div class="card"><h2>Sincronización</h2><div id="syncRows" class="rows"></div></div>
  </div>
 </div>

 <div id="actions" class="section">
  <div class="grid2">
   <div class="card"><h2>Modificar stock en vivo</h2><div class="muted">Se ejecuta en la computadora de la tienda.</div><input id="wa_product" placeholder="ID del producto"><input id="wa_delta" type="number" step="any" placeholder="Cantidad a sumar/restar"><input id="wa_reason" placeholder="Motivo"><button class="btn primary" onclick="webStock()">Aplicar stock</button></div>
   <div class="card"><h2>Precio de producto</h2><input id="wa_price_product" placeholder="ID del producto"><input id="wa_price" type="number" step="0.01" placeholder="Nuevo precio"><input id="wa_price_reason" placeholder="Motivo"><button class="btn primary" onclick="webPrice()">Cambiar precio</button></div>
   <div class="card"><h2>Cliente</h2><input id="wa_customer_id" placeholder="ID (vacío = nuevo)"><input id="wa_customer_name" placeholder="Nombre"><input id="wa_customer_doc" placeholder="Documento"><input id="wa_customer_phone" placeholder="Teléfono"><input id="wa_customer_email" placeholder="Email"><input id="wa_customer_address" placeholder="Dirección"><input id="wa_customer_limit" type="number" step="0.01" placeholder="Límite de crédito"><button class="btn primary" onclick="webCustomer()">Guardar cliente</button></div>
   <div class="card"><h2>Abono de cliente</h2><input id="wa_payment_customer" placeholder="ID del cliente"><input id="wa_payment_amount" type="number" step="0.01" placeholder="Importe"><select id="wa_payment_method"><option>EFECTIVO</option><option>TARJETA</option><option>MIXTO</option></select><input id="wa_payment_concept" placeholder="Concepto"><button class="btn primary" onclick="webPayment()">Registrar abono</button></div>
   <div class="card"><h2>Movimiento de caja</h2><select id="wa_cash_type"><option>INGRESO</option><option>EGRESO</option></select><input id="wa_cash_amount" type="number" step="0.01" placeholder="Importe"><input id="wa_cash_concept" placeholder="Concepto"><select id="wa_cash_method"><option>EFECTIVO</option><option>TARJETA</option><option>MERCADO PAGO</option></select><button class="btn primary" onclick="webCash()">Registrar movimiento</button></div>
   <div class="card"><h2>Deudas</h2><div class="muted">Exporta las cuentas corrientes recibidas por la instalación.</div><button class="btn" onclick="webExportDebts()">⬇ Exportar deudas CSV</button></div>
  </div><div id="webActionStatus" class="muted" style="margin-top:12px"></div>
 </div>

 <div id="json" class="section">
  <div class="card">
   <div class="top"><div><h2>Datos completos</h2><div class="muted">Vista técnica de todo lo sincronizado.</div></div><input id="jsonSearch" class="search" placeholder="Filtrar datos..." oninput="renderJson()"></div>
   <pre id="jsonBox"></pre>
  </div>
 </div>

 <div class="footer">FerrariPOS Central · Actualización automática cada 10 segundos · Datos aislados por tienda</div>
</section>
</div>

<script>
let dashboard=null, health=null, timer=null;

const $=id=>document.getElementById(id);
const esc=s=>String(s??'').replace(/[&<>"']/g,m=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[m]));
const title=k=>String(k).replace(/[_-]+/g,' ').replace(/([a-z])([A-Z])/g,'$1 $2').replace(/\b\w/g,c=>c.toUpperCase());

function fmt(v){
 if(v===null||v===undefined||v==='')return '—';
 if(typeof v==='boolean')return v?'Sí':'No';
 if(typeof v==='number')return new Intl.NumberFormat('es-AR',{maximumFractionDigits:2}).format(v);
 if(typeof v==='string'){
  const d=Date.parse(v);
  if(!Number.isNaN(d)&&v.length>12)return new Date(d).toLocaleString('es-AR');
 }
 return String(v);
}

function flatten(obj,prefix='',out=[]){
 if(obj===null||obj===undefined)return out;
 if(Array.isArray(obj)){obj.forEach((v,i)=>flatten(v,prefix+'['+i+']',out));return out;}
 if(typeof obj==='object'){
  Object.entries(obj).forEach(([k,v])=>flatten(v,prefix?prefix+'.'+k:k,out));
  return out;
 }
 out.push({key:prefix,value:obj});
 return out;
}

function rows(target, obj, limit=80){
 const el=$(target);
 const data=flatten(obj).slice(0,limit);
 el.innerHTML=data.length?data.map(x=>`<div class="row"><div class="k">${esc(title(x.key))}</div><div class="v">${esc(fmt(x.value))}</div></div>`).join(''):'<div class="muted">Sin datos disponibles.</div>';
}

function findValue(obj,names){
 const all=flatten(obj);
 for(const n of names){
  const hit=all.find(x=>x.key.toLowerCase().endsWith(n.toLowerCase()));
  if(hit)return hit.value;
 }
 return null;
}

function metric(label,names,cls){
 const v=findValue(dashboard,names);
 return `<div class="metric ${cls||''}"><b>${esc(label)}</b><strong>${esc(fmt(v))}</strong></div>`;
}

function renderMetrics(){
 $('metrics').innerHTML=[
  metric('Ventas del turno',['ventas_del_turno','sales_total','total_sales','ventas'], 'gold'),
  metric('Tickets',['tickets_del_turno','ticket_count','tickets','sales_count'],'cyan'),
  metric('Productos activos',['productos_activos','products_count','active_products'],'green'),
  metric('Unidades en stock',['unidades_en_stock','stock_units','total_stock'],'cyan'),
  metric('Stock bajo',['stock_bajo','low_stock','low_stock_count'],'red'),
  metric('Deuda clientes',['deuda_clientes','credit_balance','customer_debt'],'red')
 ].join('');
}

function renderCommon(){
 rows('summaryRows',dashboard,45);
 rows('cashRows',pickObject(['cash','caja','cash_register','payments']),60);
 rows('paymentsRows',pickObject(['payments','pagos','payment_summary','medios_pago']),60);
 rows('productsRows',pickObject(['products','productos','inventory','inventario']),100);
 rows('stockRows',pickObject(['stock','inventario','inventory']),60);
 rows('customersRows',pickObject(['customers','clientes']),80);
 rows('operationsRows',pickObject(['tables','mesas','operations','operacion']),80);
 rows('ordersRows',pickObject(['orders','pedidos','reservations','reservas']),80);
 rows('activityRows',pickObject(['activity','actividad','events','eventos','logs']),100);
 rows('technicalRows',{store_id:dashboard.store_id,store_name:dashboard.store_name,snapshot_utc:dashboard.snapshot_utc,central_url:location.origin},30);
 rows('syncRows',{estado:dashboard.synchronized?'Sincronizado':'Pendiente',ultima_actualizacion:dashboard.snapshot_utc,actualizacion_automatica:'5 segundos'},20);
 $('connectionRows').innerHTML=`<div class="row"><div class="k">Servidor</div><div class="v"><span class="status"><span class="dot"></span>Online</span></div></div>
 <div class="row"><div class="k">Base central</div><div class="v">${esc(health?.database||'ok')}</div></div>
 <div class="row"><div class="k">Tiendas registradas</div><div class="v">${esc(fmt(health?.stores))}</div></div>`;
}

function pickObject(names){
 for(const n of names){
  if(dashboard && dashboard[n]!==undefined)return dashboard[n];
 }
 const found=flatten(dashboard).filter(x=>names.some(n=>x.key.toLowerCase().includes(n)));
 return found.length?Object.fromEntries(found.map(x=>[x.key,x.value])):{};
}

function renderSales(){
 const q=($('saleSearch').value||'').toLowerCase();
 let list=[];
 for(const k of ['recent_sales','sales','ventas','tickets']){
  if(Array.isArray(dashboard?.[k])){list=dashboard[k];break;}
 }
 if(!Array.isArray(list))list=[];
 list=list.filter(x=>JSON.stringify(x).toLowerCase().includes(q));
 if(!list.length){$('salesTable').innerHTML='<div class="muted">No hay ventas sincronizadas o no coinciden con la búsqueda.</div>';return;}
 const cols=[...new Set(list.flatMap(x=>typeof x==='object'?Object.keys(x):['venta']))].slice(0,8);
 $('salesTable').innerHTML='<table><thead><tr>'+cols.map(c=>`<th>${esc(title(c))}</th>`).join('')+'</tr></thead><tbody>'+
 list.slice(0,200).map(x=>'<tr>'+cols.map(c=>`<td>${esc(fmt(typeof x==='object'?x[c]:x))}</td>`).join('')+'</tr>').join('')+
 '</tbody></table>';
}

function renderJson(){
 let text=JSON.stringify(dashboard,null,2);
 const q=($('jsonSearch').value||'').toLowerCase();
 if(q){
  const lines=text.split('\n').filter(l=>l.toLowerCase().includes(q));
  $('jsonBox').textContent=lines.join('\n')||'Sin coincidencias.';
 }else $('jsonBox').textContent=text;
}

async function login(){
 $('err').textContent='';
 const r=await fetch('/api/v1/web/login',{
  method:'POST',headers:{'Content-Type':'application/json'},
  body:JSON.stringify({Username:$('u').value,Password:$('p').value,Remember:$('remember').checked})
 });
 if(!r.ok){$('err').textContent='Usuario o contraseña incorrectos.';return;}
 await showPanel();
}

async function webCommand(type,payload){const r=await fetch('/api/v1/web/commands',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({type,payload})});if(!r.ok)throw new Error(await r.text()||'No se pudo crear el comando');return await r.json();}
async function webWait(id){for(let i=0;i<60;i++){await new Promise(r=>setTimeout(r,1000));const r=await fetch('/api/v1/web/commands/'+id,{cache:'no-store'});if(!r.ok)throw new Error('No se pudo consultar el comando');const x=await r.json();if(x.status==='DONE')return x;if(x.status==='ERROR')throw new Error(x.error||'La instalación rechazó el cambio');}throw new Error('La tienda no respondió en 60 segundos');}
function webStatus(t){const e=document.getElementById('webActionStatus');if(e)e.textContent=t;}
async function webRun(type,payload){try{webStatus('Enviando '+type+'...');const c=await webCommand(type,payload);await webWait(c.command_id);webStatus('✓ Cambio aplicado. Actualizando...');await refresh(false);}catch(e){webStatus('✕ '+(e.message||e));}}
function webStock(){webRun('stock.adjust',{productId:Number(wa_product.value),quantityDelta:Number(wa_delta.value),reason:wa_reason.value||'Ajuste desde panel web'});}
function webPrice(){webRun('product.price',{productId:Number(wa_price_product.value),price:Number(wa_price.value),reason:wa_price_reason.value||'Cambio desde panel web'});}
function webCustomer(){webRun('customer.upsert',{id:Number(wa_customer_id.value||0),name:wa_customer_name.value,document:wa_customer_doc.value,phone:wa_customer_phone.value,email:wa_customer_email.value,address:wa_customer_address.value,creditLimit:Number(wa_customer_limit.value||0)});}
function webPayment(){webRun('customer.payment',{customerId:Number(wa_payment_customer.value),amount:Number(wa_payment_amount.value),method:wa_payment_method.value,concept:wa_payment_concept.value||'Abono desde panel web'});}
function webCash(){webRun('cash.movement',{movementType:wa_cash_type.value,amount:Number(wa_cash_amount.value),concept:wa_cash_concept.value||'Movimiento desde panel web',paymentMethod:wa_cash_method.value});}
function webExportDebts(){const list=(dashboard?.customers||dashboard?.clientes||[]).filter(x=>Number(x.debt||0)>0);const lines=[['Cliente','Documento','Telefono','Deuda'],...list.map(x=>[x.name,x.document,x.phone,x.debt])];const csv=lines.map(r=>r.map(v=>'"'+String(v??'').replace(/"/g,'""')+'"').join(',')).join('\n');const a=document.createElement('a');a.href=URL.createObjectURL(new Blob([csv],{type:'text/csv;charset=utf-8'}));a.download='deudas_ferraripos.csv';a.click();}

async function showPanel(){
 const me=await fetch('/api/v1/web/me');
 if(!me.ok){$('loginBox').style.display='block';$('appBox').style.display='none';return;}
 const m=await me.json();
 $('loginBox').style.display='none';
 $('appBox').style.display='block';
 $('storeName').textContent=m.store_name;
 $('storeMeta').textContent='Tienda '+m.store_id+' · Cuenta '+m.web_username;
 await refresh(false);
 if(timer)clearInterval(timer);
 timer=setInterval(()=>refresh(false),5000);
}

async function refresh(manual){
 try{
  const [h,d]=await Promise.all([
   fetch('/health',{cache:'no-store'}),
   fetch('/api/v1/web/dashboard',{cache:'no-store'})
  ]);
  health=h.ok?await h.json():null;
  if(!d.ok){
   if(d.status===401){loginDiv();return;}
   throw new Error('dashboard');
  }
  const responseData=await d.json();
  dashboard=(responseData && responseData.data && typeof responseData.data==='object')
    ? Object.assign({},responseData,responseData.data)
    : responseData;
  const liveUrl=String(dashboard?.cloudflare_url||'').trim();
  const liveBox=$('liveCloudflareBox');
  const liveFrame=$('liveCloudflareFrame');
  const liveOpen=$('liveCloudflareOpen');
  if(liveUrl && liveUrl.includes('trycloudflare.com')){
    liveFrame.src=liveUrl.replace(/\/$/,'/')+'?ferraripos_live=1';
    liveOpen.href=liveFrame.src;
    liveBox.style.display='block';
  } else {
    liveBox.style.display='none';
    liveFrame.removeAttribute('src');
  }
  const pairing=dashboard?.mobile_pairing||{};
  const qrBox=$('pairingQrBox');
  const qrImg=$('pairingQrImage');
  if(pairing.qr_png_base64){
    qrImg.src='data:image/png;base64,'+pairing.qr_png_base64;
    $('pairingQrCode').textContent=String(pairing.code||'-');
    $('pairingQrUrl').textContent=liveUrl||'-';
    $('pairingQrTime').textContent=pairing.generated_utc?new Date(pairing.generated_utc).toLocaleString('es-AR'):'-';
    $('pairingQrStatus').textContent=liveUrl?'ONLINE':'QR DISPONIBLE';
    qrBox.style.display='block';
  } else {
    qrBox.style.display='block';
    $('pairingQrStatus').textContent='ESPERANDO QR DE WINDOWS';
    $('pairingQrCode').textContent='-';
    $('pairingQrUrl').textContent=liveUrl||'Esperando Quick Tunnel';
    $('pairingQrTime').textContent='-';
    qrImg.removeAttribute('src');
  }
  $('connection').textContent=dashboard.synchronized?'● DATOS SINCRONIZADOS':'○ ESPERANDO DATOS';
  $('syncText').textContent=dashboard.snapshot_utc?'Última actualización: '+new Date(dashboard.snapshot_utc).toLocaleString('es-AR'):'Esperando primera sincronización';
  renderMetrics();renderCommon();renderSales();renderJson();
 }catch(e){
  $('connection').textContent='● ERROR DE CONEXIÓN';
 }
}

function loginDiv(){
 if(timer)clearInterval(timer);
 $('loginBox').style.display='block';
 $('appBox').style.display='none';
}

async function logout(){
 await fetch('/api/v1/web/logout',{method:'POST'});
 loginDiv();
}

document.querySelectorAll('.tab').forEach(btn=>{
 btn.addEventListener('click',()=>{
  document.querySelectorAll('.tab').forEach(x=>x.classList.remove('active'));
  document.querySelectorAll('.section').forEach(x=>x.classList.remove('active'));
  btn.classList.add('active');
  $(btn.dataset.tab).classList.add('active');
 });
});

showPanel();
</script>
</body>
</html>
""";
}
