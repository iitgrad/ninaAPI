using System.Runtime.CompilerServices;
using StarfrontControl.Core;
using Xunit;

namespace StarfrontControl.Core.Tests;

public class BoundedHistoryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidCapacityIsRejected(int capacity) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedHistory<int>(capacity));

    [Fact]
    public void LongRunRetainsOnlyTheNewestEventsInOrder()
    {
        var history = new BoundedHistory<int>(ResourceLimits.EventHistoryCapacity);
        const int totalEvents = 1_000_000;
        for (var index = 0; index < totalEvents; index++)
            history.Add(index);

        Assert.Equal(history.Capacity, history.Count);
        Assert.Equal(totalEvents - history.Capacity, history.EvictedCount);
        Assert.Equal(Enumerable.Range(totalEvents - history.Capacity, history.Capacity), history.Snapshot());
    }

    [Fact]
    public void PollingDoesNotAddOrEvictEvents()
    {
        var history = new BoundedHistory<int>(8);
        history.Add(42);
        for (var index = 0; index < 14_400; index++)
            Assert.Equal(new[] { 42 }, history.Snapshot());

        Assert.Equal(1, history.Count);
        Assert.Equal(0, history.EvictedCount);
    }

    [Fact]
    public void SnapshotRemainsStableWhileWritersWrapTheRing()
    {
        var history = new BoundedHistory<int>(2);
        history.Add(1);
        history.Add(2);
        var snapshot = history.Snapshot();
        history.Add(3);
        history.Add(4);

        Assert.Equal(new[] { 1, 2 }, snapshot);
        Assert.Equal(new[] { 3, 4 }, history.Snapshot());
    }

    [Fact]
    public async Task EnumerationDoesNotHoldTheHistoryLock()
    {
        var history = new BoundedHistory<int>(2);
        history.Add(1);
        history.Add(2);
        using var reader = history.GetEnumerator();
        Assert.True(reader.MoveNext());
        var writer = Task.Run(() => history.Add(3));

        await writer.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(reader.MoveNext());
        Assert.Equal(2, reader.Current);
        Assert.False(reader.MoveNext());
    }

    [Fact]
    public async Task ConcurrentWritersAndPollingKeepConsistentSnapshots()
    {
        var history = new BoundedHistory<int>(128);
        var writer = Task.Run(() =>
        {
            for (var index = 0; index < 100_000; index++)
                history.Add(index);
        });
        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            for (var iteration = 0; iteration < 2_000; iteration++)
            {
                var snapshot = history.Snapshot();
                Assert.InRange(snapshot.Length, 0, history.Capacity);
                for (var index = 1; index < snapshot.Length; index++)
                    Assert.Equal(snapshot[index - 1] + 1, snapshot[index]);
            }
        })).ToArray();

        await Task.WhenAll(readers.Append(writer));
        Assert.Equal(Enumerable.Range(100_000 - history.Capacity, history.Capacity), history.Snapshot());
    }

    [Fact]
    public async Task ConcurrentWritersDoNotLoseRetentionAccounting()
    {
        var history = new BoundedHistory<int>(64);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(writer => Task.Run(() =>
        {
            for (var index = 0; index < 10_000; index++)
                history.Add(writer * 10_000 + index);
        })));

        Assert.Equal(64, history.Count);
        Assert.Equal(80_000 - 64, history.EvictedCount);
        Assert.Equal(64, history.Snapshot().Distinct().Count());
    }

    [Fact]
    public void EvictedPayloadsCanBeCollected()
    {
        var history = new BoundedHistory<byte[]>(1);
        var oldPayload = AddPayload(history);
        history.Add(new byte[1024]);
        Collect();

        Assert.False(oldPayload.IsAlive);
        GC.KeepAlive(history);
    }

    [Fact]
    public void ClearReleasesPayloadsAndAllowsReuse()
    {
        var history = new BoundedHistory<byte[]>(2);
        var oldPayload = AddPayload(history);
        history.Clear();
        Collect();

        Assert.False(oldPayload.IsAlive);
        Assert.Empty(history.Snapshot());
        var newPayload = new byte[1];
        history.Add(newPayload);
        Assert.Same(newPayload, Assert.Single(history.Snapshot()));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference AddPayload(BoundedHistory<byte[]> history)
    {
        var payload = new byte[1024 * 1024];
        history.Add(payload);
        return new WeakReference(payload);
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
