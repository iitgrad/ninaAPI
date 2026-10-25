using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StarfrontControl.Service;

var config = ServiceConfiguration.Load();
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(config.ListenUrl).ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 8192;
    options.Limits.MaxConcurrentConnections = 64;
    options.Limits.MaxConcurrentUpgradedConnections = 16;
});
builder.Logging.ClearProviders();
builder.Logging.AddProvider(new RollingLoggerProvider());
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddSingleton(config);
builder.Services.AddSingleton<SurveyFraming>();
builder.Services.AddSingleton<RecordingReplay>();
builder.Services.AddSingleton<PushNotifications>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<PushNotifications>());
builder.Services.AddSingleton<BridgeConnection>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<BridgeConnection>());
builder.Services.AddSingleton<ServiceDiagnostics>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<ServiceDiagnostics>());
var app = builder.Build();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(15) });
var bridge = app.Services.GetRequiredService<BridgeConnection>();
var tickets = new ConcurrentDictionary<string, DateTimeOffset>();
var http = new HttpClient { BaseAddress = new Uri(config.BridgeUrl), Timeout = TimeSpan.FromSeconds(15) };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.BridgeToken);
if (config.BridgeHostHeader is { } bridgeHost) http.DefaultRequestHeaders.Host = bridgeHost;
var publicAccess = new PublicDashboardAccess(config);
var notifications = app.Services.GetRequiredService<PushNotifications>();
bridge.SnapshotReceived += notifications.Observe;
bool Match(string? value) => value is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(value), Encoding.UTF8.GetBytes(config.Token));
bool Auth(HttpContext context) => Match(context.Request.Headers.Authorization.ToString().Replace("Bearer ", "", StringComparison.Ordinal)) || Match(context.Request.Cookies["starfront-session"]);
bool OriginAllowed(HttpContext context) => !context.Request.Headers.ContainsKey("Origin") || context.Request.Headers.Origin.ToString() == config.ListenUrl.TrimEnd('/') ||
    (context.Request.Headers["X-Starfront-Web"] == "1" && context.Request.Headers.Origin.ToString() == config.PublicDashboardOrigin);

app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'";
    if (context.Request.Path.StartsWithSegments("/mobile/notifications"))
    {
        // This separate namespace admits only notification pairing, device-owned
        // preferences/history and short-lived preview capabilities. It never
        // bypasses authentication for dashboard, telemetry or control routes.
        if (context.Request.Headers.ContainsKey("Origin") && context.Request.Headers.Origin.ToString() != config.PublicDashboardOrigin)
        { context.Response.StatusCode = 403; return; }
        var path = context.Request.Path.Value ?? "";
        var pairing = path == "/mobile/notifications/pair" && context.Request.Method == "POST";
        var media = path.StartsWith("/mobile/notifications/media/", StringComparison.Ordinal) && context.Request.Method == "GET";
        if (!pairing && !media && notifications.Authenticate(context) is null)
        { context.Response.StatusCode = 401; return; }
        await next(context); return;
    }
    if (config.PublicDashboardOrigin is not null)
    {
        // The tunnel origin always requires the private server credential, even
        // for static assets and local-session bootstrap routes.
        if (!Auth(context)) { context.Response.StatusCode = 401; return; }
        if (context.Request.Headers["X-Starfront-Web"] == "1" && !await publicAccess.Authorize(context)) return;
    }
    if ((context.Request.Path.StartsWithSegments("/api") || context.Request.Path == "/socket") && (!Auth(context) || !OriginAllowed(context)))
    {
        context.Response.StatusCode = 401;
        await context.Response.WriteAsJsonAsync(new { error = "Authentication required." });
        return;
    }
    await next(context);
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapPost("/auth/dashboard-session", () => Results.Ok(new { authenticated = true }));
app.MapGet("/favicon.ico", () => Results.NoContent());
app.MapGet("/api/v1/framing/profiles", () => config.FramingProfiles ?? []);
app.MapGet("/api/v1/framing/image", async (HttpContext context, SurveyFraming survey, double ra, double dec, double width, double height, double rotation) => {
    var field=new SurveyField(ra,dec,width,height,rotation);
    if(!field.Valid)return Results.BadRequest(new {error="Invalid camera field."});
    try {
        var image=await survey.Image(field,context.RequestAborted);
        if(image is null)return Results.StatusCode(429);
        context.Response.Headers.CacheControl="private, max-age=86400";
        return Results.Bytes(image,"image/jpeg");
    }catch(Exception e) when(e is HttpRequestException or IOException or OperationCanceledException) {
        return Results.StatusCode(503);
    }
});
app.MapPost("/mobile/notifications/pair", async (HttpContext context) =>
{
    var input = await context.Request.ReadFromJsonAsync<PushRegistration>();
    if (input is null) return Results.BadRequest();
    var result = publicAccess.VerifyNotificationCode(input.Code);
    if (result != 200) return Results.StatusCode(result);
    try { return Results.Ok(notifications.Register(input)); }
    catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return Results.BadRequest(new { error = e.Message }); }
});
app.MapGet("/mobile/notifications/status", (HttpContext context) => Results.Ok(notifications.Status(notifications.Authenticate(context)!)));
app.MapGet("/mobile/notifications/events", (HttpContext context) => Results.Ok(notifications.History(notifications.Authenticate(context)!)));
app.MapPost("/mobile/notifications/preferences", async (HttpContext context) =>
{
    var preferences = await context.Request.ReadFromJsonAsync<NotificationPreferences>();
    if (preferences is null) return Results.BadRequest();
    notifications.Preferences(notifications.Authenticate(context)!, preferences);
    return Results.Ok(new { saved = true });
});
app.MapPost("/mobile/notifications/token", async (HttpContext context) =>
{
    var input = await context.Request.ReadFromJsonAsync<PushRegistration>();
    var device = notifications.Authenticate(context)!;
    if (input is null || input.DeviceId != device.Id) return Results.BadRequest();
    try { return Results.Ok(notifications.Register(input with { DeviceSecret = context.Request.Headers["X-Starfront-Device-Key"].ToString() })); }
    catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
});
app.MapPost("/mobile/notifications/test", (HttpContext context) => { return notifications.Test(notifications.Authenticate(context)!) ? Results.Accepted() : Results.StatusCode(429); });
app.MapPost("/mobile/notifications/receipt", async (HttpContext context) =>
{
    using var input = await JsonDocument.ParseAsync(context.Request.Body);
    if (!input.RootElement.TryGetProperty("eventId", out var id) || id.ValueKind != JsonValueKind.String) return Results.BadRequest();
    var opened = input.RootElement.TryGetProperty("opened", out var value) && value.ValueKind == JsonValueKind.True;
    notifications.Receipt(notifications.Authenticate(context)!, id.GetString()!, opened);
    return Results.Ok(new { saved = true });
});
app.MapPost("/mobile/notifications/unregister", (HttpContext context) => { notifications.Remove(notifications.Authenticate(context)!); return Results.Ok(new { removed = true }); });
app.MapGet("/mobile/notifications/media/{id}", (HttpContext context, string id) => notifications.Media(id, context.Request.Query["key"].ToString()) is { } bytes ? Results.File(bytes, "image/jpeg") : Results.NotFound());


// The local monitor opens directly. A same-origin loopback request creates
// its HttpOnly session without exposing the CLI/bridge credentials to scripts.
app.MapPost("/auth/local-session", (HttpContext context) =>
{
    var expected = new Uri(config.ListenUrl);
    if (context.Connection.RemoteIpAddress is not { } remote || !System.Net.IPAddress.IsLoopback(remote) ||
        context.Request.Host.Value != expected.Authority ||
        context.Request.Headers.Origin.ToString() != config.ListenUrl.TrimEnd('/'))
        return Results.Unauthorized();
    context.Response.Cookies.Append("starfront-session", config.Token, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, IsEssential = true, Path = "/" });
    return Results.Ok(new { authenticated = true });
});

app.MapPost("/auth/session", async (HttpContext context) =>
{
    if (!OriginAllowed(context)) return Results.Unauthorized();
    var request = await context.Request.ReadFromJsonAsync<Dictionary<string, string>>();
    var token = request?.GetValueOrDefault("token");
    var ticket = request?.GetValueOrDefault("ticket");
    var accepted = Match(token) || (ticket is not null && tickets.TryRemove(ticket, out var expires) && expires > DateTimeOffset.UtcNow);
    if (!accepted) return Results.Unauthorized();
    context.Response.Cookies.Append("starfront-session", config.Token, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, IsEssential = true, Path = "/" });
    return Results.Ok(new { authenticated = true });
});
app.MapPost("/api/v1/browser-session", () =>
{
    lock (tickets)
    {
    foreach (var expired in tickets.Where(x => x.Value < DateTimeOffset.UtcNow)) tickets.TryRemove(expired.Key, out _);
    if (tickets.Count >= 4) return Results.StatusCode(429);
    var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
    tickets[ticket] = DateTimeOffset.UtcNow.AddMinutes(1);
    return Results.Ok(new { ticket });
    }
});
async Task<IResult> ReplayRead(Func<Task<object>> read) {
    try {return Results.Ok(await read());}
    catch(ArgumentException){return Results.BadRequest(new {error="Invalid recording request."});}
    catch(FileNotFoundException){return Results.NotFound(new {error="Recording not found."});}
    catch(DirectoryNotFoundException){return Results.NotFound(new {error="Recording not found."});}
    catch(InvalidOperationException e){return Results.Conflict(new {error=e.Message});}
    catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException){return Results.UnprocessableEntity(new {error="Recording could not be read safely. Original files were preserved."});}
}
app.MapGet("/api/v1/recordings", (RecordingReplay replay)=>ReplayRead(()=>Task.FromResult(replay.List())));
app.MapGet("/api/v1/recordings/{id}", (RecordingReplay replay,string id,HttpContext ctx)=>ReplayRead(async()=>await replay.Index(id,ctx.RequestAborted)));
app.MapGet("/api/v1/recordings/{id}/state", (RecordingReplay replay,string id,long at,HttpContext ctx)=>ReplayRead(()=>replay.At(id,at,ctx.RequestAborted)));
app.MapGet("/api/v1/status", () => Results.Text(bridge.Read(), "application/json"));
app.MapGet("/api/v1/doctor", () => Results.Ok(new { service = "Starfront Control", version = typeof(ServiceConfiguration).Assembly.GetName().Version?.ToString(), bridgeConnected = bridge.Connected, clients = bridge.Clients.ClientCount, clientLimit = 16, pendingFramesPerClient = 1, coalescedFrames = bridge.Clients.CoalescedCount, diagnostics = app.Services.GetRequiredService<ServiceDiagnostics>().Latest }));

async Task Proxy(HttpContext context, string path)
{
    if (context.Request.ContentLength > 8192) { context.Response.StatusCode = 413; return; }
    if (!bridge.Connected) { context.Response.StatusCode = 503; await context.Response.WriteAsJsonAsync(new { error = "N.I.N.A. bridge is offline." }); return; }
    try
    {
        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), path);
        if (context.Request.Method == "POST")
        {
            using var memory = new MemoryStream();
            var buffer = new byte[8193]; var used = 0;
            while (used < buffer.Length)
            {
                var count = await context.Request.Body.ReadAsync(buffer.AsMemory(used), context.RequestAborted);
                if (count == 0) break;
                used += count;
            }
            if (used > 8192) { context.Response.StatusCode = 413; return; }
            request.Content = new ByteArrayContent(buffer, 0, used);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
        context.Response.StatusCode = (int)response.StatusCode;
        context.Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
        if (response.Content.Headers.ContentLength > 128 * 1024 * 1024) { context.Response.StatusCode = 502; return; }
        await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
    }
    catch (BadHttpRequestException exception) when (exception.StatusCode == 413)
    {
        context.Response.StatusCode = 413;
    }
    catch (Exception) when (!context.RequestAborted.IsCancellationRequested)
    {
        context.Response.StatusCode = 502;
        await context.Response.WriteAsJsonAsync(new { error = "Bridge request did not complete. Check the operation by requestId before retrying." });
    }
}
app.MapGet("/api/v1/sequence/insertion-schema", (HttpContext context, string type) => Proxy(context, "/api/v1/sequence/insertion-schema?type=" + Uri.EscapeDataString(type)));
app.MapGet("/api/v1/images/{id:guid}", (HttpContext context, string id) => Proxy(context, "/api/v1/images/" + id));
app.MapPost("/api/v1/commands", (HttpContext context) => Proxy(context, "/api/v1/commands"));
app.MapPost("/api/v1/commands/validate", (HttpContext context) => Proxy(context, "/api/v1/commands/validate"));
app.MapGet("/api/v1/operations/{id:guid}", (HttpContext context, string id) => Proxy(context, "/api/v1/operations/" + id));

app.Map("/socket", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
    StarfrontControl.Core.SnapshotHub.Subscription subscription;
    try { subscription = bridge.Clients.Subscribe(); }
    catch (InvalidOperationException) { context.Response.StatusCode = 429; return; }
    using (subscription)
    using (var socket = await context.WebSockets.AcceptWebSocketAsync())
    using (var stop = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, app.Lifetime.ApplicationStopping))
    {
        if (context.Request.Headers["X-Starfront-Web"] == "1") stop.CancelAfter(publicAccess.Remaining(context));
        var receiver = Task.Run(async () =>
        {
            try
            {
                var data = new byte[1024];
                while (socket.State == WebSocketState.Open)
                {
                    var message = await socket.ReceiveAsync(data, stop.Token);
                    if (message.MessageType == WebSocketMessageType.Close || message.Count > 0) break;
                }
            }
            catch (Exception) { }
            finally { stop.Cancel(); }
        });
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var frame = await subscription.ReadAsync(stop.Token);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                await socket.SendAsync(Encoding.UTF8.GetBytes(frame), WebSocketMessageType.Text, true, deadline.Token);
            }
        }
        catch (Exception) { }
        finally { stop.Cancel(); socket.Abort(); await receiver; }
    }
});
app.Run();
