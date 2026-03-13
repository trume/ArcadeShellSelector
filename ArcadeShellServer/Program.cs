using ArcadeShellSelector;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var configPath = Path.Combine(AppContext.BaseDirectory, "config.json");

// Locate the source (project-root) config.json so mobile edits survive dotnet clean+build.
// Walk up from the exe directory looking for a sibling .csproj or .sln file.
string? sourceConfigPath = null;
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir?.Parent != null)
    {
        dir = dir.Parent;
        var candidate = Path.Combine(dir.FullName, "config.json");
        if (File.Exists(candidate) &&
            (dir.GetFiles("*.csproj").Length > 0 || dir.GetFiles("*.sln").Length > 0))
        {
            sourceConfigPath = candidate;
            break;
        }
    }
}

var (cfg, err) = AppConfig.TryLoadFromFile(configPath);
if (cfg == null)
{
    Console.Error.WriteLine($"Failed to load config: {err}");
    return 1;
}

var remote = cfg.RemoteAccess;
if (!remote.Enabled)
{
    Console.WriteLine("Remote access is disabled in config.json (remoteAccess.enabled = false).");
    Console.WriteLine("Enable it in the Configurator or set it manually, then restart.");
    return 0;
}

int port = Math.Clamp(remote.Port, 1024, 65535);
string pin = remote.Pin ?? "0000";

// Initialize shared debug logger
DebugLogger.Init(cfg.Activa.Activa);

// Token store: valid session tokens (PIN-derived, 1h expiry)
var validTokens = new Dictionary<string, DateTime>();
string GenerateToken()
{
    var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    validTokens[token] = DateTime.UtcNow.AddHours(1);
    return token;
}
bool IsValidToken(HttpContext ctx)
{
    // Prune expired
    var expired = validTokens.Where(kv => kv.Value < DateTime.UtcNow).Select(kv => kv.Key).ToList();
    foreach (var k in expired) validTokens.Remove(k);

    var token = ctx.Request.Cookies["ass_token"] ?? ctx.Request.Headers["X-Auth-Token"].FirstOrDefault();
    return token != null && validTokens.ContainsKey(token);
}

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(k =>
{
    k.ListenAnyIP(port);
});
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.SetMinimumLevel(remote.Verbose ? LogLevel.Information : LogLevel.Warning);

var app = builder.Build();
string localIp = GetLocalIpAddress();

// --- HTTP request logging middleware ---
app.Use(async (ctx, next) =>
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    await next();
    sw.Stop();
    var method = ctx.Request.Method;
    var path = ctx.Request.Path;
    var status = ctx.Response.StatusCode;
    var ip = ctx.Connection.RemoteIpAddress;
    DebugLogger.Info("HTTP", $"{method} {path} → {status} ({sw.ElapsedMilliseconds}ms) from {ip}");
});

// --- Serve embedded static files ---
app.MapGet("/", (HttpContext ctx) =>
{
    ctx.Response.ContentType = "text/html; charset=utf-8";
    return GetEmbeddedResource("index.html");
});

app.MapGet("/style.css", (HttpContext ctx) =>
{
    ctx.Response.ContentType = "text/css; charset=utf-8";
    return GetEmbeddedResource("style.css");
});

app.MapGet("/app.js", (HttpContext ctx) =>
{
    ctx.Response.ContentType = "application/javascript; charset=utf-8";
    return GetEmbeddedResource("app.js");
});

app.MapGet("/favicon.ico", async (HttpContext ctx) =>
{
    var icoPath = Path.Combine(AppContext.BaseDirectory, "app.ico");
    if (!File.Exists(icoPath)) { ctx.Response.StatusCode = 404; return; }
    ctx.Response.ContentType = "image/x-icon";
    await ctx.Response.SendFileAsync(icoPath);
});

// --- Auth ---
app.MapPost("/api/auth", async (HttpContext ctx) =>
{
    var body = await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body);
    var submittedPin = body.TryGetProperty("pin", out var p) ? p.GetString() : null;

    if (!string.Equals(submittedPin, pin, StringComparison.Ordinal))
    {
        ctx.Response.StatusCode = 401;
        return Results.Json(new { error = "PIN incorrecto" });
    }

    var token = GenerateToken();
    ctx.Response.Cookies.Append("ass_token", token, new CookieOptions
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Strict,
        MaxAge = TimeSpan.FromHours(1)
    });
    return Results.Json(new { ok = true });
});

// --- API (all require auth) ---
app.MapGet("/api/config", (HttpContext ctx) =>
{
    if (!IsValidToken(ctx)) return Results.StatusCode(401);

    var (current, _) = AppConfig.TryLoadFromFile(configPath);
    if (current == null) return Results.StatusCode(500);

    var opts = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    return Results.Json(current, opts);
});

app.MapPut("/api/config", async (HttpContext ctx) =>
{
    if (!IsValidToken(ctx)) return Results.StatusCode(401);

    try
    {
        var json = await new StreamReader(ctx.Request.Body, Encoding.UTF8).ReadToEndAsync();
        var opts = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        var updated = JsonSerializer.Deserialize<AppConfig>(json, opts);
        if (updated == null) return Results.BadRequest(new { error = "Invalid config JSON" });

        // The mobile UI only manages label/exe/image for options.
        // Preserve thumbVideo and waitForProcessName from the existing config on disk
        // so the mobile save never wipes fields it doesn't display.
        var (existing, _) = AppConfig.TryLoadFromFile(configPath);
        if (existing != null)
        {
            for (int i = 0; i < updated.Options.Count && i < existing.Options.Count; i++)
            {
                var src = existing.Options[i];
                var dst = updated.Options[i];
                // Only carry forward if the incoming value is null (mobile doesn't set these)
                if (dst.ThumbVideo == null) dst.ThumbVideo = src.ThumbVideo;
                if (dst.WaitForProcessName == null) dst.WaitForProcessName = src.WaitForProcessName;
            }
        }

        // Validate basic structure
        if (updated.RemoteAccess.Port < 1024 || updated.RemoteAccess.Port > 65535)
            return Results.BadRequest(new { error = "Port must be 1024–65535" });

        // Write atomically: write to temp, then move
        var tmpPath = configPath + ".tmp";
        var writeOpts = new JsonSerializerOptions { WriteIndented = true };
        var output = JsonSerializer.Serialize(updated, writeOpts);
        await File.WriteAllTextAsync(tmpPath, output, Encoding.UTF8);
        File.Move(tmpPath, configPath, overwrite: true);

        // Also update the source (project-root) config so changes survive dotnet clean+build
        if (sourceConfigPath != null)
        {
            try
            {
                var srcTmp = sourceConfigPath + ".tmp";
                await File.WriteAllTextAsync(srcTmp, output, Encoding.UTF8);
                File.Move(srcTmp, sourceConfigPath, overwrite: true);
                DebugLogger.Info("CONFIG", $"Source config synced: {sourceConfigPath}");
            }
            catch (Exception syncEx)
            {
                DebugLogger.Warn("CONFIG", $"Failed to sync source config: {syncEx.Message}");
            }
        }

        return Results.Json(new { ok = true });
    }
    catch (JsonException ex)
    {
        return Results.BadRequest(new { error = $"Invalid JSON: {ex.Message}" });
    }
});

app.MapGet("/api/status", (HttpContext ctx) =>
{
    if (!IsValidToken(ctx)) return Results.StatusCode(401);

    var (current, _) = AppConfig.TryLoadFromFile(configPath);
    var input = current?.Input;
    var inputMethod = (input?.XInputEnabled == true, input?.DInputEnabled == true) switch
    {
        (true, true) => "XInput + DInput",
        (true, false) => "XInput",
        (false, true) => "DInput",
        _ => "Ninguno"
    };
    return Results.Json(new
    {
        uptime = Environment.TickCount64 / 1000,
        hostname = Environment.MachineName,
        serverIp = localIp,
        serverPort = port,
        inputMethod,
        musicEnabled = current?.Music.Enabled ?? false
    });
});

// --- Start ---
Console.WriteLine($"ArcadeShell Remote Access Server");
Console.WriteLine($"  Listening on: http://{localIp}:{port}");
Console.WriteLine($"  PIN required: {(pin == "0000" ? "0000 (default — change in Configurator!)" : "****")}");
Console.WriteLine($"  Press Ctrl+C to stop.");

app.Run();
return 0;

// ── Helpers ─────────────────────────────────────────────────────────────────

static string GetLocalIpAddress()
{
    try
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            foreach (var addr in ni.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    return addr.Address.ToString();
            }
        }
    }
    catch { }
    return "0.0.0.0";
}

static string GetEmbeddedResource(string name)
{
    var asm = typeof(Program).Assembly;
    var resourceName = asm.GetManifestResourceNames()
        .FirstOrDefault(n => n.EndsWith($"wwwroot.{name}", StringComparison.OrdinalIgnoreCase));
    if (resourceName == null) return $"<!-- resource {name} not found -->";
    using var stream = asm.GetManifestResourceStream(resourceName)!;
    using var reader = new StreamReader(stream, Encoding.UTF8);
    return reader.ReadToEnd();
}
