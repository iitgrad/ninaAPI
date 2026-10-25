using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StarfrontControl.Service;

/// <summary>Additional short-code gate for the website proxy; never replaces the strong origin credential.</summary>
public sealed class PublicDashboardAccess(ServiceConfiguration config)
{
    private const string Cookie = "__Secure-starfront-dashboard";
    private readonly object gate = new();
    private readonly string attemptsPath = Path.Combine(Path.GetDirectoryName(ServiceConfiguration.ConfigurationPath)!, "dashboard-attempts.json");
    private string Sign(string value) => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(config.Token), Encoding.UTF8.GetBytes(value)));
    private static bool Equal(string left, string right) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
    public TimeSpan Remaining(HttpContext context)
    {
        var parts = context.Request.Cookies[Cookie]?.Split('.');
        if (parts?.Length != 3 || !long.TryParse(parts[0], out var expiry) || parts[1].Length != 32 || !Equal(Sign("dashboard:" + parts[0] + "." + parts[1]), parts[2])) return TimeSpan.Zero;
        var seconds = expiry - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return seconds is > 0 and <= 43200 ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
    }
    private bool TakeAttempt()
    {
        lock (gate)
        {
            var cutoff = DateTimeOffset.UtcNow.AddMinutes(-15).ToUnixTimeSeconds();
            var attempts = File.Exists(attemptsPath) ? JsonSerializer.Deserialize<long[]>(File.ReadAllText(attemptsPath))! : [];
            var recent = attempts.Where(x => x > cutoff).TakeLast(20).ToList();
            if (recent.Count >= 20) return false;
            recent.Add(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            // One bounded global attempt window persists across service restarts.
            var tmp = attemptsPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(recent));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(tmp, attemptsPath, true);
            return true;
        }
    }
    public int VerifyNotificationCode(string? pin)
    {
        if (config.PublicPinHash is null) return 503;
        if (!TakeAttempt()) return 429;
        return pin is { Length: 4 } && pin.All(char.IsAsciiDigit) && Equal(Sign("pin:" + pin), config.PublicPinHash.ToUpperInvariant()) ? 200 : 401;
    }
    public async Task<bool> Authorize(HttpContext context)
    {
        var path = context.Request.Path.Value;
        if (context.Request.Headers.ContainsKey("Origin") && context.Request.Headers.Origin.ToString() != config.PublicDashboardOrigin)
        { context.Response.StatusCode = 403; return false; }
        if (path == "/auth/dashboard-login" && context.Request.Method == "POST")
        {
            try
            {
                if (!TakeAttempt()) { context.Response.StatusCode = 429; context.Response.Headers.RetryAfter = "900"; await context.Response.WriteAsJsonAsync(new { error = "Too many attempts. Try again in 15 minutes." }); return false; }
                var input = await context.Request.ReadFromJsonAsync<Dictionary<string, string>>();
                var pin = input?.GetValueOrDefault("code") ?? "";
                if (pin.Length != 4 || !pin.All(char.IsAsciiDigit) || !Equal(Sign("pin:" + pin), config.PublicPinHash?.ToUpperInvariant() ?? ""))
                { context.Response.StatusCode = 401; await context.Response.WriteAsJsonAsync(new { error = "Incorrect code." }); return false; }
                var expiry = DateTimeOffset.UtcNow.AddHours(12);
                var payload = expiry.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture) + "." + Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
                context.Response.Cookies.Append(Cookie, payload + "." + Sign("dashboard:" + payload), new CookieOptions { HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = "/dashboard", Expires = expiry, IsEssential = true });
                await context.Response.WriteAsJsonAsync(new { authenticated = true });
            }
            catch (Exception e) when (e is IOException or JsonException or BadHttpRequestException or UnauthorizedAccessException)
            { context.Response.StatusCode = 503; await context.Response.WriteAsJsonAsync(new { error = "Dashboard access temporarily unavailable." }); }
            return false;
        }
        if (path == "/pin.js" && context.Request.Method == "GET") return true;
        if (Remaining(context) <= TimeSpan.Zero)
        {
            if (context.Request.Method == "GET" && path is "/" or "/index.html")
            {
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.WriteAsync("""
                <!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Starfront · Dashboard access</title><style>body{margin:0;min-height:100dvh;display:grid;place-items:center;background:#101922;color:#e8efef;font:16px system-ui}main{width:min(320px,85vw)}h1{font-size:26px}label{display:block;margin:24px 0 8px}input,button{box-sizing:border-box;width:100%;font:inherit;padding:14px;border-radius:8px;border:1px solid #44515b}input{background:#172632;color:inherit;letter-spacing:.7em;text-align:center;font-size:24px}button{margin-top:16px;background:#64c1b1;color:#10201e;cursor:pointer}a{color:#8ed2c8}#error{min-height:24px;color:#ffc7ad}</style><script defer src="/dashboard/pin.js"></script></head><body><main><a href="/">← Starfront</a><h1>Observatory dashboard</h1><form id="pin-form" method="post" action="/dashboard/auth/dashboard-login"><label for="code">Enter your four-digit code</label><input id="code" name="code" type="password" inputmode="numeric" pattern="[0-9]{4}" minlength="4" maxlength="4" autocomplete="off" required autofocus><button>Open dashboard</button><p id="error" role="alert"></p></form></main></body></html>
                """);
            }
            else { context.Response.StatusCode = 401; await context.Response.WriteAsJsonAsync(new { error = "Dashboard code required." }); }
            return false;
        }
        if (path == "/auth/dashboard-logout" && context.Request.Method == "POST")
        {
            context.Response.Cookies.Delete(Cookie, new CookieOptions { Secure = true, HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/dashboard" });
            await context.Response.WriteAsJsonAsync(new { authenticated = false }); return false;
        }
        return true;
    }
}
