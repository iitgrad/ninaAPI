using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using StarfrontControl.Core;

namespace StarfrontControl.Service;

public sealed record NotificationPreferences(bool Enabled = true, bool Problems = true, bool Targets = true,
    bool Sequence = true, bool Images = true, bool Steps = false);
public sealed record PushDevice(string Id, string SecretHash, string Token, string Environment, NotificationPreferences Preferences);
public sealed record PushConfiguration(string TeamId, string KeyId, string KeyPath, string Topic, string PublicOrigin, int ImageInterval = 5);
public sealed record PushRegistration(string Code, string Token, string Environment, string? DeviceId = null, string? DeviceSecret = null);
public sealed record NoticeDelivery(string State, DateTimeOffset At, string? Reason = null);
public sealed record StoredNotice(ObservatoryNotice Notice, Dictionary<string, NoticeDelivery> Deliveries);

/// <summary>Bounded, durable notification history and direct APNs delivery. No equipment commands.</summary>
public sealed class PushNotifications : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string root;
    private readonly object gate = new();
    private readonly HttpClient http;
    private readonly Channel<(string? Snapshot, ObservatoryNotice? Test)> inbox = Channel.CreateBounded<(string?, ObservatoryNotice?)>(new BoundedChannelOptions(32) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
    private readonly NotificationProjection projection = new();
    private readonly Dictionary<string, DateTimeOffset> lastTests = [];
    private readonly List<PushDevice> devices;
    private readonly List<StoredNotice> history;
    private readonly Dictionary<string, (byte[] Bytes, string Key, DateTimeOffset Expires)> media = [];
    private byte[]? latestPreview;
    private DateTimeOffset previewAt;
    private bool baseline = true;
    private string? jwt;
    private DateTimeOffset jwtAt;
    public string? LastDeliveryError { get; private set; }
    public bool Configured => File.Exists(Path.Combine(root, "apns.json"));
    private PushConfiguration Configuration => JsonSerializer.Deserialize<PushConfiguration>(File.ReadAllText(Path.Combine(root, "apns.json")), Json)!;
    public PushNotifications(HttpClient? client = null, string? storageRoot = null)
    {
        root = storageRoot ?? Path.Combine(Path.GetDirectoryName(ServiceConfiguration.ConfigurationPath)!, "Notifications");
        Directory.CreateDirectory(root);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        http = client ?? new HttpClient(new SocketsHttpHandler { EnableMultipleHttp2Connections = false }) { Timeout = TimeSpan.FromSeconds(10), DefaultRequestVersion = HttpVersion.Version20, DefaultVersionPolicy = HttpVersionPolicy.RequestVersionExact };
        devices = Load<List<PushDevice>>("devices.json") ?? [];
        history = Load<List<StoredNotice>>("history.json") ?? [];
        history = history.TakeLast(200).ToList();
    }
    private T? Load<T>(string name) => File.Exists(Path.Combine(root, name)) ? JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(root, name)), Json) : default;
    private void Save(string name, object value)
    {
        var path = Path.Combine(root, name); var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(tmp, path, true);
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool ValidToken(string? value) => value is { Length: >= 32 and <= 256 } && value.All(char.IsAsciiHexDigit);
    public static bool Same(string a, string b) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    public PushDevice? Authenticate(HttpContext context)
    {
        var id = context.Request.Headers["X-Starfront-Device-Id"].ToString();
        var secret = context.Request.Headers["X-Starfront-Device-Key"].ToString();
        if (secret.Length != 64) return null;
        lock (gate) return devices.FirstOrDefault(d => d.Id == id && Same(d.SecretHash, Hash(secret)));
    }
    public object Register(PushRegistration input)
    {
        if (!ValidToken(input.Token) || input.Environment is not ("sandbox" or "production")) throw new ArgumentException("Invalid APNs registration.");
        lock (gate)
        {
            var existing = devices.FirstOrDefault(d => d.Id == input.DeviceId && input.DeviceSecret is { Length: 64 } secret && Same(d.SecretHash, Hash(secret)));
            if (existing is null && devices.Count >= 8) throw new InvalidOperationException("Device registration limit reached.");
            var id = existing?.Id ?? Guid.NewGuid().ToString("N");
            var key = existing is null ? Convert.ToHexString(RandomNumberGenerator.GetBytes(32)) : input.DeviceSecret!;
            devices.RemoveAll(d => d.Id == id || d.Token == input.Token);
            devices.Add(new(id, Hash(key), input.Token, input.Environment, existing?.Preferences ?? new()));
            Save("devices.json", devices);
            return new { deviceId = id, deviceSecret = key, configured = Configured };
        }
    }
    public void Preferences(PushDevice device, NotificationPreferences preferences)
    {
        lock (gate) { var i = devices.FindIndex(d => d.Id == device.Id); if (i < 0) return; devices[i] = device with { Preferences = preferences }; Save("devices.json", devices); }
    }
    public object Status(PushDevice device)
    {
        lock (gate) return new { configured = Configured, preferences = devices.FirstOrDefault(d => d.Id == device.Id)?.Preferences, lastDeliveryError = LastDeliveryError, imageInterval = Configured ? Configuration.ImageInterval : 5 };
    }
    public object History(PushDevice device)
    {
        lock (gate) return history.TakeLast(100).Reverse().Select(h => new { h.Notice.Id, h.Notice.Kind, h.Notice.Title, h.Notice.Body, h.Notice.At, h.Notice.Target, delivery = h.Deliveries.GetValueOrDefault(device.Id), previewUrl = media.TryGetValue(h.Notice.Id, out var item) && item.Expires > DateTimeOffset.UtcNow && Configured ? Configuration.PublicOrigin.TrimEnd('/') + "/mobile/notifications/media/" + h.Notice.Id + "?key=" + item.Key : null }).ToArray();
    }
    public void Receipt(PushDevice device, string id, bool opened)
    {
        lock (gate) {
            var entry = history.FirstOrDefault(h => h.Notice.Id == id && h.Deliveries.ContainsKey(device.Id));
            if (entry is null) return;
            entry.Deliveries[device.Id] = new(opened ? "opened-on-phone" : "received-in-app", DateTimeOffset.UtcNow);
            Save("history.json", history);
        }
    }
    public void Remove(PushDevice device) { lock (gate) { devices.RemoveAll(d => d.Id == device.Id); Save("devices.json", devices); } }
    public byte[]? Media(string id, string key)
    {
        lock (gate) return media.TryGetValue(id, out var item) && item.Expires > DateTimeOffset.UtcNow && Same(item.Key, key) ? item.Bytes : null;
    }
    public void Observe(string frame) => inbox.Writer.TryWrite((frame, null));
    public bool Test(PushDevice device)
    {
        lock (gate) {
            if (lastTests.TryGetValue(device.Id, out var prior) && DateTimeOffset.UtcNow - prior < TimeSpan.FromSeconds(10)) return false;
            if (lastTests.Count >= 8) lastTests.Clear();
            lastTests[device.Id] = DateTimeOffset.UtcNow;
        }
        return inbox.Writer.TryWrite((null, new ObservatoryNotice(Guid.NewGuid().ToString("N"), "test:" + device.Id, "Starfront test", "Native notifications are connected. No equipment action was performed.", DateTimeOffset.UtcNow, AttachPreview: true)));
    }
    private static bool Wants(PushDevice d, string kind) => d.Preferences.Enabled && (kind.StartsWith("test:") ? kind == "test:" + d.Id : kind switch { "problem" => d.Preferences.Problems, "safety" => d.Preferences.Problems, "target" => d.Preferences.Targets, "sequence" => d.Preferences.Sequence, "image" => d.Preferences.Images, "step" => d.Preferences.Steps, _ => false });
    private string Token(PushConfiguration config)
    {
        if (jwt is not null && DateTimeOffset.UtcNow - jwtAt < TimeSpan.FromMinutes(40)) return jwt;
        static string B64(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        jwtAt = DateTimeOffset.UtcNow;
        var data = B64(JsonSerializer.SerializeToUtf8Bytes(new { alg = "ES256", kid = config.KeyId })) + "." + B64(JsonSerializer.SerializeToUtf8Bytes(new { iss = config.TeamId, iat = jwtAt.ToUnixTimeSeconds() }));
        using var key = ECDsa.Create(); key.ImportFromPem(File.ReadAllText(config.KeyPath));
        jwt = data + "." + B64(key.SignData(Encoding.ASCII.GetBytes(data), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
        return jwt;
    }
    private async Task<NoticeDelivery> Deliver(PushDevice device, ObservatoryNotice notice, string? imageUrl, CancellationToken stop)
    {
        if (!Configured) return new("not-configured", DateTimeOffset.UtcNow, "APNs signing key is not configured.");
        var config = Configuration;
        var payload = new Dictionary<string, object> { ["aps"] = new Dictionary<string, object> { ["alert"] = new { title = notice.Title, body = notice.Body }, ["sound"] = "default", ["thread-id"] = "starfront" }, ["eventId"] = notice.Id, ["kind"] = notice.Kind };
        if (imageUrl is not null) { ((Dictionary<string, object>)payload["aps"])["mutable-content"] = 1; payload["imageUrl"] = imageUrl; }
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://" + (device.Environment == "sandbox" ? "api.sandbox.push.apple.com" : "api.push.apple.com") + "/3/device/" + device.Token) { Version = HttpVersion.Version20, VersionPolicy = HttpVersionPolicy.RequestVersionExact, Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };
        request.Headers.Authorization = new AuthenticationHeaderValue("bearer", Token(config));
        request.Headers.Add("apns-topic", config.Topic); request.Headers.Add("apns-push-type", "alert"); request.Headers.Add("apns-priority", "10"); request.Headers.Add("apns-expiration", DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString());
        using var response = await http.SendAsync(request, stop);
        if (response.IsSuccessStatusCode) return new("accepted-by-apple", DateTimeOffset.UtcNow);
        var reason = ((int)response.StatusCode).ToString();
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(stop));
        if (body.RootElement.TryGetProperty("reason", out var field)) reason += " " + field.GetString();
        if (response.StatusCode == HttpStatusCode.Gone || reason.Contains("BadDeviceToken") || reason.Contains("DeviceTokenNotForTopic"))
        { lock (gate) { devices.RemoveAll(d => d.Id == device.Id); Save("devices.json", devices); } }
        return new("failed", DateTimeOffset.UtcNow, reason);
    }
    private async Task Emit(ObservatoryNotice notice, CancellationToken stop)
    {
        PushDevice[] recipients; StoredNotice stored; string? imageUrl = null;
        lock (gate)
        {
            recipients = devices.Where(d => Wants(d, notice.Kind)).ToArray();
            if (notice.Kind == "step" && recipients.Length == 0) return;
            stored = new(notice, []); history.Add(stored); if (history.Count > 200) history.RemoveAt(0);
            foreach (var key in media.Where(x => x.Value.Expires < DateTimeOffset.UtcNow).Select(x => x.Key).ToArray()) media.Remove(key);
            if (notice.AttachPreview && latestPreview is not null && DateTimeOffset.UtcNow - previewAt < TimeSpan.FromMinutes(5) && Configured)
            {
                if (media.Count >= 32) media.Remove(media.Keys.First());
                var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
                media[notice.Id] = (latestPreview, key, DateTimeOffset.UtcNow.AddHours(2));
                imageUrl = Configuration.PublicOrigin.TrimEnd('/') + "/mobile/notifications/media/" + notice.Id + "?key=" + key;
            }
            foreach (var device in recipients) stored.Deliveries[device.Id] = new("pending", DateTimeOffset.UtcNow);
            Save("history.json", history);
        }
        foreach (var device in recipients)
        {
            NoticeDelivery delivery;
            try { delivery = await Deliver(device, notice, imageUrl, stop); }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or CryptographicException or IOException or JsonException) { delivery = new("failed", DateTimeOffset.UtcNow, e.GetType().Name); }
            lock (gate) { stored.Deliveries[device.Id] = delivery; LastDeliveryError = delivery.State == "failed" ? delivery.Reason : null; Save("history.json", history); }
        }
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in inbox.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                if (item.Test is { } test) { await Emit(test, stoppingToken); continue; }
                using var document = JsonDocument.Parse(item.Snapshot!);
                var frame = document.RootElement;
                if (!frame.TryGetProperty("bridge", out var bridge) || !bridge.GetProperty("connected").GetBoolean()) { baseline = true; continue; }
                var snapshot = frame.GetProperty("snapshot");
                var preview = NotificationProjection.Topic(snapshot, "preview");
                if (NotificationProjection.Text(preview, "jpeg") is { Length: <= 140000 } jpeg && snapshot.GetProperty("states").GetProperty("preview").TryGetProperty("observedAt", out var observed) && DateTimeOffset.TryParse(observed.GetString(), out var at) && at > previewAt)
                { var bytes = Convert.FromBase64String(jpeg); if (bytes.Length <= 100000) { lock (gate) { latestPreview = bytes; previewAt = at; } } }
                var notices = projection.Observe(snapshot, baseline, Configured ? Configuration.ImageInterval : 5); baseline = false;
                foreach (var notice in notices) await Emit(notice, stoppingToken);
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            { LastDeliveryError = e.GetType().Name; }
        }
    }
}
