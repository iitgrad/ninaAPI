namespace StarfrontControl.Core;

public sealed record SequenceLibraryListing(string[] Files, bool Truncated);

/// <summary>Read-only browsing within the active profile's two configured sequence folders.</summary>
public static class SequenceLibrary
{
    public const int Limit = 128;
    public static string SaveTarget(string targets,string name,string json,bool replace) {
        if(string.IsNullOrWhiteSpace(targets))throw new ArgumentException("Targets folder is not configured.");
        if(string.IsNullOrWhiteSpace(name) || name.Length>100 || !name.EndsWith(".json",StringComparison.OrdinalIgnoreCase)
           || name.Any(c=>char.IsControl(c)||"/\\:*?\"<>|".Contains(c)) || name.StartsWith('.') || name.EndsWith(' ') || name[..^5].EndsWith('.')
           || System.Text.RegularExpressions.Regex.IsMatch(name,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)",System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            throw new ArgumentException("Use a plain sequence name ending in .json.");
        var data=System.Text.Encoding.UTF8.GetBytes(json);
        if(data.Length>1024*1024)throw new ArgumentException("Sequence exceeds the 1 MiB library limit.");
        var folder=Directory.CreateDirectory(targets);
        if((folder.Attributes&FileAttributes.ReparsePoint)!=0)throw new ArgumentException("Linked sequence directories are not supported.");
        var path=Path.Combine(folder.FullName,name);
        if(File.Exists(path)) {
            if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new ArgumentException("Linked sequence files cannot be replaced.");
            if(!replace)throw new CommandConflictException("A sequence with this name exists. Confirm replacement or choose a different name.");
        }
        var temporary=Path.Combine(folder.FullName,".starfront-"+Guid.NewGuid().ToString("N")+".tmp");
        try {
            using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(data);stream.Flush(true);}
            File.Move(temporary,path,replace);
        } finally { if(File.Exists(temporary))File.Delete(temporary); }
        return path;
    }
    private static string Folder(string root, string targets, string name, out string leaf)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Contains(':') || name.Contains('\0'))
            throw new ArgumentException("Choose a JSON file from the sequence library.");
        var parts = name.Replace('\\', '/').Split('/');
        var target = parts.Length == 2 && parts[0] == "Targets";
        if (!(parts.Length == 1 || target) || parts.Any(p => string.IsNullOrWhiteSpace(p) || p is "." or ".."))
            throw new ArgumentException("Choose a JSON file from the sequence library.");
        leaf = parts[^1];
        if (Path.GetFileName(leaf) != leaf || !leaf.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a JSON file from the sequence library.");
        var folder = target ? targets : root;
        if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("Sequence folder is not configured.");
        return Path.GetFullPath(folder);
    }
    public static string Resolve(string root, string targets, string name)
    {
        var folder = Folder(root, targets, name, out var leaf);
        var file = new FileInfo(Path.Combine(folder, leaf));
        if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0 || file.Length > 1024 * 1024)
            throw new ArgumentException("Sequence is missing, linked, or larger than 1 MiB.");
        return file.FullName;
    }
    public static SequenceLibraryListing List(string root, string targets)
    {
        var names = new List<string>(); var truncated = false;
        foreach (var (folder, prefix) in new[] { (targets, "Targets/"), (root, "") })
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) continue;
            foreach (var file in new DirectoryInfo(folder).EnumerateFiles())
            {
                if (!file.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase) || (file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                var name = prefix + file.Name;
                if (name.Length > 128 || file.Length > 1024 * 1024) { truncated = true; continue; }
                if (names.Count >= Limit) { truncated = true; break; }
                names.Add(name);
            }
        }
        return new(names.Order(StringComparer.OrdinalIgnoreCase).ToArray(), truncated);
    }
}
