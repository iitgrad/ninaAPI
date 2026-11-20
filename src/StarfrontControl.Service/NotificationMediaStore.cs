using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StarfrontControl.Service;

/// <summary>Private, bounded JPEG retention. Capability URLs survive gateway restarts.</summary>
public sealed class NotificationMediaStore
{
    public const int MaximumItems = 512;
    public const int MaximumImageBytes = 100_000;
    public static readonly TimeSpan Retention = TimeSpan.FromDays(7);
    private sealed record Entry(string Id, string Key, DateTimeOffset Expires, int Length);
    private readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    private readonly object gate = new();
    private readonly string root;
    private readonly TimeProvider clock;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public NotificationMediaStore(string root, TimeProvider? clock = null)
    {
        this.root = root; this.clock = clock ?? TimeProvider.System;
        Directory.CreateDirectory(root);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var index = Path.Combine(root, "index.json");
        if (File.Exists(index) && new FileInfo(index).Length <= 512_000)
        {
            try
            {
                foreach (var item in (JsonSerializer.Deserialize<Entry[]>(File.ReadAllText(index), Json) ?? [])
                    .Where(e => e.Id is { Length: > 0 and <= 256 } && e.Key is { Length: 48 }
                        && e.Key.All(char.IsAsciiHexDigit) && e.Length is >= 4 and <= MaximumImageBytes)
                    .OrderByDescending(e => e.Expires).Take(MaximumItems))
                    if (item.Expires > this.clock.GetUtcNow() && File.Exists(ImagePath(item.Id))
                        && new FileInfo(ImagePath(item.Id)).Length == item.Length) entries[item.Id] = item;
            }
            catch (JsonException) { /* A damaged index must never prevent the gateway from starting. */ }
        }
        Prune();
    }

    private string ImagePath(string id) => Path.Combine(root, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))) + ".jpg");
    private void AtomicWrite(string path, byte[] bytes)
    {
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, bytes);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temp, path, true);
    }
    private void Persist() => AtomicWrite(Path.Combine(root, "index.json"), JsonSerializer.SerializeToUtf8Bytes(entries.Values, Json));
    private void Prune()
    {
        foreach (var id in entries.Values.Where(e => e.Expires <= clock.GetUtcNow()).Select(e => e.Id).ToArray()) entries.Remove(id);
        while (entries.Count > MaximumItems) entries.Remove(entries.Values.MinBy(e => e.Expires)!.Id);
        var retained = entries.Keys.Select(ImagePath).ToHashSet(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*.jpg")) if (!retained.Contains(file)) File.Delete(file);
        Persist();
    }
    public string Put(string id, byte[] bytes)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 256 || bytes.Length is < 4 or > MaximumImageBytes
            || bytes[0] != 0xff || bytes[1] != 0xd8) throw new ArgumentException("Invalid notification JPEG.");
        lock (gate)
        {
            // Re-observing an event must not invalidate a capability already delivered to a phone.
            if (entries.TryGetValue(id, out var prior) && prior.Expires > clock.GetUtcNow() && File.Exists(ImagePath(id))) return prior.Key;
            var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            AtomicWrite(ImagePath(id), bytes);
            entries[id] = new(id, key, clock.GetUtcNow() + Retention, bytes.Length);
            Prune(); return key;
        }
    }
    public string? Key(string id)
    {
        lock (gate) return entries.TryGetValue(id, out var item) && item.Expires > clock.GetUtcNow()
            && File.Exists(ImagePath(id)) ? item.Key : null;
    }
    public byte[]? Read(string id, string key)
    {
        lock (gate)
        {
            if (!entries.TryGetValue(id, out var item) || item.Expires <= clock.GetUtcNow()
                || !PushNotifications.Same(item.Key, key)) return null;
            try
            {
                var file = new FileInfo(ImagePath(id));
                if (!file.Exists || file.Length != item.Length || file.Length > MaximumImageBytes) return null;
                return File.ReadAllBytes(file.FullName);
            }
            catch (IOException) { return null; }
        }
    }
}
