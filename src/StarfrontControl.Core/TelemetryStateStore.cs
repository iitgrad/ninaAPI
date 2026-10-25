// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at https://mozilla.org/MPL/2.0/.

using System.Text;
using System.Text.Json;

namespace StarfrontControl.Core;

public sealed record TelemetrySnapshot(long Revision, string Json, int TopicCount, int PayloadBytes);

/// <summary>
/// Transport-independent latest state for the future telemetry adapter. Each
/// update prepares one immutable reconnect snapshot; reads do no serialization.
/// Limits cover retained input payloads, not total process memory or consumers
/// that choose to retain old snapshots. This is not yet wired to N.I.N.A.
/// </summary>
public sealed class TelemetryStateStore
{
    private sealed record StateValue(string Json, int Bytes, DateTimeOffset ObservedAt);

    private readonly object gate = new();
    private readonly int maxTopics;
    private readonly int maxPayloadBytes;
    private readonly int maxSequencePayloadBytes;
    private readonly int maxTotalPayloadBytes;
    private readonly Guid sessionId = Guid.NewGuid();
    private Dictionary<string, StateValue> states = new(StringComparer.Ordinal);
    private TelemetrySnapshot snapshot;

    public TelemetryStateStore(int maxTopics = 32, int maxPayloadBytes = 64 * 1024,
        int maxTotalPayloadBytes = 256 * 1024, int? maxSequencePayloadBytes = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTopics);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPayloadBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTotalPayloadBytes);
        this.maxTopics = maxTopics;
        this.maxPayloadBytes = maxPayloadBytes;
        this.maxSequencePayloadBytes = maxSequencePayloadBytes ?? maxPayloadBytes;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(this.maxSequencePayloadBytes);
        this.maxTotalPayloadBytes = maxTotalPayloadBytes;
        snapshot = Serialize(states, 0, 0);
    }

    public TelemetrySnapshot Read() => Volatile.Read(ref snapshot);

    /// <returns>False for an older observation or an exact duplicate.</returns>
    public bool Update(string topic, string json, DateTimeOffset observedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(json);
        if (topic.Length > 128)
            throw new ArgumentException("Topic names must be at most 128 characters.", nameof(topic));

        var bytes = Encoding.UTF8.GetByteCount(json);
        if (bytes > (topic == "sequence" ? maxSequencePayloadBytes : maxPayloadBytes))
            throw new ArgumentException("Telemetry payload exceeds its byte limit.", nameof(json));
        using (var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 }))
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Telemetry state must be a JSON object.", nameof(json));
        }

        lock (gate)
        {
            states.TryGetValue(topic, out var previous);
            if (previous is not null && (observedAt < previous.ObservedAt ||
                (observedAt == previous.ObservedAt && json == previous.Json)))
                return false;
            if (previous is null && states.Count == maxTopics)
                throw new InvalidOperationException("Telemetry topic limit reached.");

            var totalBytes = checked(snapshot.PayloadBytes - (previous?.Bytes ?? 0) + bytes);
            if (totalBytes > maxTotalPayloadBytes)
                throw new InvalidOperationException("Total telemetry payload byte limit reached.");

            // Build first so a failed update never changes the published revision
            // or leaves the internal state ahead of the reconnect snapshot.
            var next = new Dictionary<string, StateValue>(states, StringComparer.Ordinal)
            {
                [topic] = new(json, bytes, observedAt.ToUniversalTime())
            };
            var nextSnapshot = Serialize(next, checked(snapshot.Revision + 1), totalBytes);
            states = next;
            Volatile.Write(ref snapshot, nextSnapshot);
            return true;
        }
    }

    private TelemetrySnapshot Serialize(Dictionary<string, StateValue> values, long revision, int bytes)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteString("sessionId", sessionId);
            writer.WriteNumber("revision", revision);
            writer.WriteStartObject("states");
            foreach (var (topic, value) in values)
            {
                writer.WriteStartObject(topic);
                writer.WriteString("observedAt", value.ObservedAt);
                writer.WritePropertyName("value");
                writer.WriteRawValue(value.Json, skipInputValidation: true);
                writer.WriteEndObject();
            }
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return new(revision, Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length), values.Count, bytes);
    }
}
