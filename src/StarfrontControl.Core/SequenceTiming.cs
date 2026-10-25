using System.Text.Json;
using System.Text.Json.Nodes;

namespace StarfrontControl.Core;

// Bounded receipt-time history shared by every gateway viewer. Never backdate
// a step from an initial/reconnect snapshot or treat a reset as completion.
public sealed class SequenceTiming
{
    private sealed class Entry(string state)
    {
        public string State = state;
        public string? Started, Ended, Seen;
    }
    private readonly Dictionary<string, Entry> entries = new();
    private string? scope;
    private bool baseline = true;
    public void Disconnected() => baseline = true;
    private static bool Active(string s) => s is "running" or "waiting";
    private static bool Terminal(string s) => s is "completed" or "failed" or "skipped" or "cancelled";
    public string Enrich(JsonElement snapshot, DateTimeOffset received)
    {
        if (!snapshot.TryGetProperty("states", out var states) || !states.TryGetProperty("sequence", out var topic)
            || !topic.TryGetProperty("value", out var value) || !value.TryGetProperty("nodes", out var rows)
            || rows.ValueKind != JsonValueKind.Array) return snapshot.GetRawText();
        var currentScope = snapshot.GetProperty("sessionId").GetString() + "/" + (value.TryGetProperty("sequenceId", out var sid) ? sid.GetString() : "");
        if (currentScope != scope) { scope = currentScope; entries.Clear(); baseline = true; }
        var now = received.ToString("O");
        var output = JsonNode.Parse(snapshot.GetRawText())!;
        var outputRows = output["states"]!["sequence"]!["value"]!["nodes"]!.AsArray();
        var present = new HashSet<string>();
        foreach (var node in outputRows.Take(256))
        {
            if (node?["id"]?.GetValue<string>() is not { } id) continue;
            var state = node["state"]?.GetValue<string>() ?? "unknown";
            present.Add(id);
            if (!entries.TryGetValue(id, out var entry))
            {
                entry = new Entry(state); entries[id] = entry;
                if (Active(state)) entry.Seen = now;
            }
            else if (entry.State != state)
            {
                if (state == "pending") { entry.Started = entry.Ended = entry.Seen = null; }
                else if (!baseline && Active(state) && !Active(entry.State)) { entry.Started = now; entry.Ended = entry.Seen = null; }
                else if (!baseline && Terminal(state)) entry.Ended = now;
                entry.State = state;
            }
            // Preserve canonical upstream timing if a future plugin supplies it.
            if (node["timing"] is null && (entry.Started != null || entry.Ended != null || entry.Seen != null))
                node["timing"] = new JsonObject { ["source"] = "gateway-received", ["startedAt"] = entry.Started, ["endedAt"] = entry.Ended, ["seenActiveAt"] = entry.Seen };
        }
        foreach (var id in entries.Keys.Where(id => !present.Contains(id)).ToArray()) entries.Remove(id);
        baseline = false;
        return output.ToJsonString();
    }
}
