using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using StarfrontControl.Service;
using Xunit;

public sealed class PushNotificationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "starfront-notifications-" + Guid.NewGuid());
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    private static DefaultHttpContext Context(JsonElement pair)
    {
        var c = new DefaultHttpContext();
        c.Request.Headers["X-Starfront-Device-Id"] = pair.GetProperty("deviceId").GetString();
        c.Request.Headers["X-Starfront-Device-Key"] = pair.GetProperty("deviceSecret").GetString();
        return c;
    }
    [Fact] public void RegistrationIsBoundedAuthenticatedAndCredentialsAreHashed()
    {
        using var service = new PushNotifications(storageRoot: root);
        var pair = Json(service.Register(new("", new string('a', 64), "sandbox")));
        Assert.NotNull(service.Authenticate(Context(pair)));
        Assert.Null(service.Authenticate(new DefaultHttpContext()));
        var stored = File.ReadAllText(Path.Combine(root, "devices.json"));
        Assert.DoesNotContain(pair.GetProperty("deviceSecret").GetString()!, stored);
        Assert.Throws<ArgumentException>(() => service.Register(new("", "not-token", "sandbox")));
        Assert.Throws<ArgumentException>(() => service.Register(new("", new string('b', 64), "arbitrary-host")));
        service.Preferences(service.Authenticate(Context(pair))!, new(Enabled: false));
        using var reloaded = new PushNotifications(storageRoot: root);
        Assert.False(Json(reloaded.Status(reloaded.Authenticate(Context(pair))!)).GetProperty("preferences").GetProperty("enabled").GetBoolean());
        reloaded.Remove(reloaded.Authenticate(Context(pair))!);
        Assert.Null(reloaded.Authenticate(Context(pair)));
    }
    [Fact] public async Task APNsRequestUsesHttp2ES256AndCorrectTopicAndEnvironment()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        Directory.CreateDirectory(root); var keyPath = Path.Combine(root, "test.p8");
        File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(Path.Combine(root, "apns.json"), JsonSerializer.Serialize(new PushConfiguration("TEAM", "KEY", keyPath, "com.example.starfront", "https://example.invalid")));
        var handler = new FakeHandler(key);
        using var service = new PushNotifications(new HttpClient(handler), root);
        var pair = Json(service.Register(new("", new string('a', 64), "sandbox")));
        await service.StartAsync(CancellationToken.None);
        service.Test(service.Authenticate(Context(pair))!);
        for (var i = 0; i < 100 && !handler.Received; i++) await Task.Delay(20);
        Assert.True(handler.Received);
        for (var i = 0; i < 100; i++) {
            var events = Json(service.History(service.Authenticate(Context(pair))!));
            if (events.GetArrayLength() > 0 && events[0].GetProperty("delivery").GetProperty("state").GetString() == "accepted-by-apple") break;
            await Task.Delay(20);
        }
        Assert.Equal("accepted-by-apple", Json(service.History(service.Authenticate(Context(pair))!))[0].GetProperty("delivery").GetProperty("state").GetString());
        await service.StopAsync(CancellationToken.None);
    }
    private sealed class FakeHandler(ECDsa key) : HttpMessageHandler
    {
        public volatile bool Received;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken stop)
        {
            Assert.Equal(new Version(2, 0), request.Version);
            Assert.Equal("api.sandbox.push.apple.com", request.RequestUri!.Host);
            Assert.Equal("com.example.starfront", request.Headers.GetValues("apns-topic").Single());
            Assert.Equal("alert", request.Headers.GetValues("apns-push-type").Single());
            var jwt = request.Headers.Authorization!.Parameter!.Split('.');
            static byte[] Decode(string s) => Convert.FromBase64String(s.Replace('-', '+').Replace('_', '/').PadRight((s.Length + 3) / 4 * 4, '='));
            Assert.True(key.VerifyData(Encoding.ASCII.GetBytes(jwt[0] + "." + jwt[1]), Decode(jwt[2]), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(stop));
            Assert.Equal("Starfront test", payload.RootElement.GetProperty("aps").GetProperty("alert").GetProperty("title").GetString());
            Received = true;
            return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }
}
