using System.Text.Json;

namespace StarfrontControl.Service;

public sealed record ServiceConfiguration(string BridgeUrl, string BridgeToken, string Token,
    string ListenUrl = "http://127.0.0.1:1890", string? BridgeHostHeader = null,
    string? PublicDashboardOrigin = null, string? PublicPinHash = null, FramingProfile[]? FramingProfiles = null)
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "starfront-control", "service.json");
    public static string ConfigurationPath => Environment.GetEnvironmentVariable("STARFRONT_SERVICE_CONFIG") ?? DefaultPath;
    public static ServiceConfiguration Load()
    {
        var path = ConfigurationPath;
        var configuration = JsonSerializer.Deserialize<ServiceConfiguration>(File.ReadAllText(path), new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException("Service configuration is required.");
        if (configuration.Token.Length < 32 || configuration.BridgeToken.Length < 32)
            throw new InvalidOperationException("Strong service and bridge credentials are required.");
        var listen = new Uri(configuration.ListenUrl);
        if (!listen.IsLoopback) throw new InvalidOperationException("This development service must listen on loopback.");
        var bridge = new Uri(configuration.BridgeUrl);
        if (bridge.Scheme != "http" && bridge.Scheme != "https") throw new InvalidOperationException("Invalid bridge URL.");
        if (configuration.BridgeHostHeader is { } host && !System.Text.RegularExpressions.Regex.IsMatch(host, @"^[a-zA-Z0-9.\-]+:\d{1,5}$")) throw new InvalidOperationException("Invalid bridge Host header.");
        if (configuration.PublicDashboardOrigin is { } origin &&
            (!Uri.TryCreate(origin, UriKind.Absolute, out var publicUri) || publicUri.Scheme != "https" || publicUri.GetLeftPart(UriPartial.Authority) != origin || configuration.PublicPinHash?.Length != 64))
            throw new InvalidOperationException("Public dashboard requires an HTTPS origin and PIN digest.");
        if(configuration.FramingProfiles is { } framing && (framing.Length>16 || framing.Any(p=>!Guid.TryParse(p.ProfileId,out _) || p.RigName.Length>96 || !new SurveyField(0,0,p.FieldWidthDegrees,p.FieldHeightDegrees,0).Valid)))
            throw new InvalidOperationException("Invalid framing profile.");
        return configuration;
    }
}
