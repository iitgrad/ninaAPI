using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace ninaAPI.Control;

public sealed class ControlConfiguration
{
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StarfrontControl");
    public static string FilePath => Path.Combine(DirectoryPath, "control.json");
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    public bool Enabled { get; set; }
    public string BindAddress { get; set; } = "127.0.0.1";
    public bool EnableLegacyApi { get; set; }
    public bool SimulationOnly { get; set; } = true;
    public string[] AllowedDeviceIds { get; set; } = [];
    public int[] AllowedPHD2ProfileIds { get; set; } = [];
    public string[] AllowedProfileIds { get; set; } = [];
    public string Token { get; set; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    public static ControlConfiguration Load()
    {
        Directory.CreateDirectory(DirectoryPath);
        if (File.Exists(FilePath))
        {
            var config = JsonSerializer.Deserialize<ControlConfiguration>(File.ReadAllText(FilePath), Json);
            if (config is null) throw new InvalidDataException("Invalid Starfront Control configuration.");
            config.AllowedDeviceIds ??= [];
            config.AllowedProfileIds ??= [];
            config.AllowedPHD2ProfileIds ??= [];
            if (config.BindAddress != "127.0.0.1" && !System.Net.IPAddress.TryParse(config.BindAddress, out _))
                throw new InvalidDataException("BindAddress must be a literal IP address.");
            return config;
        }
        var created = new ControlConfiguration();
        created.Save();
        return created;
    }

    public void Save() => File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
}
