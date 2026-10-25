using System.Text.Json;

namespace StarfrontControl.Core;

public sealed record ObservatoryNotice(string Id, string Kind, string Title, string Body, DateTimeOffset At,
    string? Target = null, bool AttachPreview = false);

/// <summary>Observes pushed state only; reconnect snapshots establish a baseline, never replay alerts.</summary>
public sealed class NotificationProjection
{
    private string? session;
    private bool? running, safe;
    private string? target, step, flatFilter;
    private long imageNumber, notificationNumber;
    private readonly Dictionary<string, int> imageCounts = new();
    private HashSet<string> failures = [];
    public static JsonElement Topic(JsonElement snapshot, string name) => snapshot.TryGetProperty("states", out var states)
        && states.TryGetProperty(name, out var topic) && topic.TryGetProperty("value", out var value) ? value : default;
    public static string? Text(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.String ? field.GetString() : null;
    private static bool? Flag(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var field) && field.ValueKind is JsonValueKind.True or JsonValueKind.False ? field.GetBoolean() : null;
    private static long Number(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var field) && field.TryGetInt64(out var n) ? n : 0;
    private static JsonElement[] Items(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var field) && field.ValueKind == JsonValueKind.Array ? field.EnumerateArray().ToArray() : [];
    private static bool IsFailed(JsonElement node) => new[] {"failed", "error"}.Contains(Text(node, "state")?.ToLowerInvariant()) || new[] {"ERROR", "FAILED"}.Contains(Text(node, "sourceStatus")?.ToUpperInvariant());

    public IReadOnlyList<ObservatoryNotice> Observe(JsonElement snapshot, bool baseline = false, int imageInterval = 5)
    {
        var result = new List<ObservatoryNotice>();
        var nextSession = Text(snapshot, "sessionId");
        if (nextSession is null) return result;
        var newSession = nextSession != session;
        baseline |= newSession;
        if (newSession) { imageCounts.Clear(); imageNumber = 0; notificationNumber = 0; }
        var app = Topic(snapshot, "application");
        var nextRunning = Flag(app, "sequenceRunning");
        var safety = Topic(snapshot, "safety");
        var nextSafe = Flag(safety, "connected") == true ? Flag(safety, "isSafe") : null;
        var sequence = Topic(snapshot, "sequence");
        var nodes = Items(sequence, "nodes");
        var active = nodes.LastOrDefault(n => Text(n, "state") is "running" or "waiting");
        string? nextTarget = null;
        var current = active;
        var seen = new HashSet<string>();
        while (Text(current, "id") is { } id && seen.Add(id))
        {
            if (Text(current, "type") is "DeepSkyObjectContainer" or "TargetContainer") { nextTarget = Text(current, "name"); break; }
            var parent = Text(current, "parentId");
            current = nodes.FirstOrDefault(n => Text(n, "id") == parent);
        }
        var nextStep = Text(active, "name");
        var nextFailures = nodes.Where(IsFailed).Select(n => Text(n, "id") ?? "").ToHashSet();
        void Add(string kind, string title, string body, bool preview = false) => result.Add(new(Guid.NewGuid().ToString("N"), kind, title, body, DateTimeOffset.UtcNow, nextTarget, preview));
        if (!baseline)
        {
            if (running == false && nextRunning == true) { imageCounts.Clear(); Add("sequence", "Sequence started", "Starfront is running the loaded sequence."); }
            if (running == true && nextRunning == false)
            {
                var complete = nodes.Any(n => Text(n, "type") == "SequenceRootContainer" && Text(n, "state") == "completed");
                Add("sequence", complete ? "Sequence completed" : "Sequence stopped", complete ? "The sequence reports completion." : "The sequence is no longer running.");
            }
            if (safe == true && nextSafe == false) Add("problem", "Roof closed", "The safety monitor reports unsafe conditions.");
            if (safe == false && nextSafe == true) Add("safety", "Roof open", "The safety monitor reports safe conditions.");
            if (nextRunning == true && nextTarget is not null && nextTarget != target) Add("target", "Starting " + nextTarget, "The sequence has moved to this target.");
            if (nextRunning == true && nextStep is not null && nextStep != step) Add("step", nextStep, nextTarget ?? "Current sequence step");
            foreach (var node in nodes.Where(n => IsFailed(n) && !failures.Contains(Text(n, "id") ?? "")))
                Add("problem", "Sequence instruction failed", Text(node, "name") ?? "A sequence step reports failure.");
        }
        foreach (var notice in Items(Topic(snapshot, "sequenceNotifications"), "items").OrderBy(n => Number(n, "number")))
        {
            var number = Number(notice, "number");
            if (number <= notificationNumber) continue;
            notificationNumber = number;
            if (baseline) continue;
            var message = Text(notice, "message");
            if (!string.IsNullOrWhiteSpace(message) && message.Length <= SequenceNotificationOutbox.MessageLimit)
                Add("sequence", "Starfront update", message, Flag(notice, "attachPreview") == true);
        }
        var images = Items(Topic(snapshot, "imageHistory"), "items");
        var latest = Topic(snapshot, "image");
        if (images.Length == 0 && latest.ValueKind == JsonValueKind.Object) images = [latest];
        foreach (var image in images.OrderBy(i => Number(i, "number")))
        {
            var n = Number(image, "number");
            if (n <= imageNumber) continue;
            imageNumber = n;
            if (baseline) continue;
            var type = Text(image, "imageType");
            var filter = Text(image, "filter") ?? "current filter";
            if (type is "FLAT" or "FLAT FRAME")
            {
                if (flatFilter != filter) Add("target", "Taking flats · " + filter, "Flat-field acquisition is in progress.");
                flatFilter = filter;
            }
            else if (type is "LIGHT" or "LIGHT FRAME" or "SCIENCE")
            {
                flatFilter = null;
                var key = nextTarget ?? "Science";
                var count = imageCounts.GetValueOrDefault(key) + 1;
                imageCounts[key] = count;
                if (count == 1 || count % Math.Clamp(imageInterval, 1, 100) == 0)
                    Add("image", nextTarget ?? "New science image", filter + " · latest camera preview", true);
            }
        }
        session = nextSession; running = nextRunning; safe = nextSafe; target = nextTarget; step = nextStep; failures = nextFailures;
        return result;
    }
}
