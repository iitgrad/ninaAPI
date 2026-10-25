using System.Text.Json;
using System.Runtime.CompilerServices;
using StarfrontControl.Core;
using Xunit;

namespace StarfrontControl.Core.Tests;

public class TelemetryStateStoreTests
{
    [Fact] public void LargerSequenceBudgetKeepsOtherTopicsAndTotalBounded() {
        var store = new TelemetryStateStore(maxPayloadBytes: 32, maxTotalPayloadBytes: 160, maxSequencePayloadBytes: 128);
        store.Update("sequence", "{\"text\":\"" + new string('x', 80) + "\"}", DateTimeOffset.UtcNow);
        Assert.Throws<ArgumentException>(() => store.Update("other", "{\"text\":\"" + new string('x', 80) + "\"}", DateTimeOffset.UtcNow));
        Assert.Throws<ArgumentException>(() => store.Update("sequence", "{\"text\":\"" + new string('x', 130) + "\"}", DateTimeOffset.UtcNow));
        Assert.True(store.Read().PayloadBytes <= 160);
    }
    [Fact]
    public void ReplacedSnapshotsAreNotRetainedByTheStore()
    {
        var store = new TelemetryStateStore();
        var previous = PublishSnapshot(store);
        store.Update("camera", "{}", DateTimeOffset.UtcNow);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(previous.IsAlive);
        GC.KeepAlive(store);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference PublishSnapshot(TelemetryStateStore store)
    {
        store.Update("camera", "{\"connected\":true}", DateTimeOffset.MinValue);
        return new WeakReference(store.Read());
    }

    [Fact]
    public void PollingReusesThePreparedSnapshotWithoutAllocating()
    {
        var store = new TelemetryStateStore();
        store.Update("camera", "{\"temperature\":-10}", DateTimeOffset.UtcNow);
        var snapshot = store.Read();
        // Warm up before measuring this thread's reads, without test assertions
        // or serialization in the measured section.
        for (var index = 0; index < 1000; index++) store.Read();
        var before = GC.GetAllocatedBytesForCurrentThread();
        TelemetrySnapshot? last = null;
        for (var index = 0; index < 100_000; index++) last = store.Read();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Same(snapshot, last);
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void UpdatesReplaceLatestStateAndKeepExistingSnapshotsImmutable()
    {
        var store = new TelemetryStateStore();
        var time = DateTimeOffset.UtcNow;
        store.Update("camera", "{\"temperature\":0}", time);
        var previous = store.Read();
        for (var index = 1; index <= 10_000; index++)
            store.Update("camera", $"{{\"temperature\":{index}}}", time.AddSeconds(index));
        var current = store.Read();
        using var oldJson = JsonDocument.Parse(previous.Json);
        using var newJson = JsonDocument.Parse(current.Json);

        Assert.Equal(1, current.TopicCount);
        Assert.Equal(10_001, current.Revision);
        Assert.Equal(0, oldJson.RootElement.GetProperty("states").GetProperty("camera").GetProperty("value").GetProperty("temperature").GetInt32());
        Assert.Equal(10_000, newJson.RootElement.GetProperty("states").GetProperty("camera").GetProperty("value").GetProperty("temperature").GetInt32());
    }

    [Fact]
    public void LateEventsAndDuplicateDeliveryDoNotOverwriteNewerState()
    {
        var store = new TelemetryStateStore();
        var time = DateTimeOffset.UtcNow;
        Assert.True(store.Update("camera", "{}", time));
        var snapshot = store.Read();
        Assert.False(store.Update("camera", "{\"old\":true}", time.AddSeconds(-1)));
        Assert.False(store.Update("camera", "{}", time));
        Assert.Same(snapshot, store.Read());

        // Receiving the same value at a genuinely newer observation time is
        // meaningful freshness information and must be published.
        Assert.True(store.Update("camera", "{}", time.AddSeconds(1)));
        Assert.Equal(2, store.Read().Revision);
    }

    [Fact]
    public void TopicLimitStillAllowsUpdatingAnExistingTopic()
    {
        var store = new TelemetryStateStore(maxTopics: 1);
        var time = DateTimeOffset.UtcNow;
        store.Update("camera", "{}", time);
        var snapshot = store.Read();
        Assert.Throws<InvalidOperationException>(() => store.Update("mount", "{}", time));
        Assert.Same(snapshot, store.Read());
        Assert.True(store.Update("camera", "{\"connected\":true}", time.AddSeconds(1)));
    }

    [Fact]
    public void PayloadLimitsMeasureUtf8BytesAndRejectWithoutChangingState()
    {
        var store = new TelemetryStateStore(maxPayloadBytes: 11, maxTotalPayloadBytes: 14);
        var time = DateTimeOffset.UtcNow;
        store.Update("camera", "{\"a\":1}", time);
        store.Update("mount", "{\"a\":2}", time);
        var snapshot = store.Read();

        Assert.Throws<ArgumentException>(() => store.Update("camera", "{\"a\":\"🌠\"}", time));
        Assert.Throws<InvalidOperationException>(() => store.Update("mount", "{\"a\":22}", time));
        Assert.Same(snapshot, store.Read());
        Assert.Equal(14, snapshot.PayloadBytes);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    public void InvalidStateDoesNotChangeTheSnapshot(string json)
    {
        var store = new TelemetryStateStore();
        var snapshot = store.Read();
        Assert.Throws<ArgumentException>(() => store.Update("camera", json, DateTimeOffset.UtcNow));
        Assert.Same(snapshot, store.Read());
    }

    [Fact]
    public void MalformedJsonDoesNotChangeTheSnapshot()
    {
        var store = new TelemetryStateStore();
        var snapshot = store.Read();
        Assert.ThrowsAny<JsonException>(() => store.Update("camera", "not json", DateTimeOffset.UtcNow));
        Assert.Same(snapshot, store.Read());
    }

    [Fact]
    public void RestartStartsANewSessionWithNoOldState()
    {
        var previous = new TelemetryStateStore();
        previous.Update("camera", "{}", DateTimeOffset.UtcNow);
        var current = new TelemetryStateStore();
        using var oldJson = JsonDocument.Parse(previous.Read().Json);
        using var newJson = JsonDocument.Parse(current.Read().Json);

        Assert.NotEqual(oldJson.RootElement.GetProperty("sessionId").GetGuid(), newJson.RootElement.GetProperty("sessionId").GetGuid());
        Assert.Equal(0, current.Read().Revision);
        Assert.Equal(0, current.Read().TopicCount);
    }

    [Fact]
    public async Task ConcurrentUpdatesPublishCompleteMonotonicSnapshots()
    {
        var store = new TelemetryStateStore();
        var time = DateTimeOffset.UtcNow;
        var writers = Enumerable.Range(0, 4).Select(writer => Task.Run(() =>
        {
            for (var index = 0; index < 1000; index++)
                store.Update($"device-{writer}", $"{{\"step\":{index}}}", time.AddSeconds(index));
        })).ToArray();
        var reader = Task.Run(() =>
        {
            long revision = 0;
            for (var index = 0; index < 2000; index++)
            {
                var snapshot = store.Read();
                using var json = JsonDocument.Parse(snapshot.Json);
                Assert.True(snapshot.Revision >= revision);
                Assert.Equal(snapshot.Revision, json.RootElement.GetProperty("revision").GetInt64());
                Assert.Equal(snapshot.TopicCount, json.RootElement.GetProperty("states").EnumerateObject().Count());
                revision = snapshot.Revision;
            }
        });

        await Task.WhenAll(writers.Append(reader));
        Assert.Equal(4000, store.Read().Revision);
        Assert.Equal(4, store.Read().TopicCount);
    }
}
