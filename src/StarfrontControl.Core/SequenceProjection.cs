using System.Text.Json;
namespace StarfrontControl.Core;

// Keep the current execution branch in a bounded snapshot. Remove whole leaf
// branches first so every retained child still has its parent.
public static class SequenceProjection
{
    public static JsonElement[] Bound(IEnumerable<JsonElement> source, IReadOnlyCollection<string> currentIds,
        int maxNodes = 256, int maxBytes = 58 * 1024)
    {
        var rows = source.ToArray();
        var ids = rows.Select(x => x.GetProperty("id").GetString()!).ToArray();
        var parents = rows.Select(x => x.GetProperty("parentId").GetString()).ToArray();
        var lookup = ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        var keep = Enumerable.Repeat(true, rows.Length).ToArray();
        var protect = new HashSet<int>();
        foreach (var id in currentIds.Concat(rows.Where(x => x.GetProperty("state").GetString() is "running" or "waiting").Select(x => x.GetProperty("id").GetString()!)))
        {
            var next = id;
            while (next != null && lookup.TryGetValue(next, out var i) && protect.Add(i)) next = parents[i];
        }
        // Preserve the structural root even when there is no running item.
        for (var i = 0; i < rows.Length; i++) if (parents[i] == null || (rows[i].TryGetProperty("type", out var type) && type.GetString() is "StartAreaContainer" or "TargetAreaContainer" or "EndAreaContainer")) protect.Add(i);
        var sizes = rows.Select(x => System.Text.Encoding.UTF8.GetByteCount(x.GetRawText()) + 1).ToArray();
        var bytes = 1 + sizes.Sum();
        var count = rows.Length;
        while (count > maxNodes || bytes > maxBytes)
        {
            var hasChildren = new HashSet<string>(Enumerable.Range(0, rows.Length).Where(i => keep[i] && parents[i] != null).Select(i => parents[i]!));
            var remove = -1;
            for (var i = rows.Length - 1; i >= 0; i--)
                if (keep[i] && !protect.Contains(i) && !hasChildren.Contains(ids[i])) { remove = i; break; }
            // An oversized active branch must fail explicitly instead of silently
            // presenting another instruction as current or exceeding the topic cap.
            if (remove < 0) throw new InvalidOperationException("Current sequence branch exceeds telemetry limits.");
            keep[remove] = false; bytes -= sizes[remove]; count--;
        }
        return rows.Where((_, i) => keep[i]).ToArray();
    }
}
