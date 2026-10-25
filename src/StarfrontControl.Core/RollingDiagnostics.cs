using System.Text;

namespace StarfrontControl.Core;

/// <summary>Owns only Starfront diagnostic files; never rotates N.I.N.A.'s logs.</summary>
public sealed class RollingDiagnostics
{
    private readonly object gate = new();
    private readonly string directory;
    private readonly long maxBytes;
    private readonly int fileCount;
    public long DiskBytes
    {
        get
        {
            lock (gate)
                return Enumerable.Range(0, fileCount).Select(index => new FileInfo(Path.Combine(directory, $"metrics-{index}.jsonl")))
                    .Where(file => file.Exists).Sum(file => file.Length);
        }
    }
    public RollingDiagnostics(string directory, long maxBytes = 5 * 1024 * 1024, int fileCount = 4)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fileCount);
        this.directory = directory;
        this.maxBytes = maxBytes;
        this.fileCount = fileCount;
        Directory.CreateDirectory(directory);
    }

    public void Write(string json)
    {
        var data = Encoding.UTF8.GetBytes(json + "\n");
        if (data.Length > Math.Min(maxBytes, 64 * 1024)) throw new ArgumentException("Diagnostic record exceeds limit.");
        lock (gate)
        {
            var current = Path.Combine(directory, "metrics-0.jsonl");
            if (File.Exists(current) && new FileInfo(current).Length + data.Length > maxBytes)
            {
                File.Delete(Path.Combine(directory, $"metrics-{fileCount - 1}.jsonl"));
                for (var index = fileCount - 2; index >= 0; index--)
                {
                    var old = Path.Combine(directory, $"metrics-{index}.jsonl");
                    if (File.Exists(old)) File.Move(old, Path.Combine(directory, $"metrics-{index + 1}.jsonl"), true);
                }
            }
            using var stream = new FileStream(current, FileMode.Append, FileAccess.Write, FileShare.Read);
            stream.Write(data);
        }
    }
}
