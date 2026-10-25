using StarfrontControl.Core;
using Xunit;

namespace StarfrontControl.Core.Tests;

public class SnapshotHubTests
{
    [Fact]
    public async Task SlowReaderRetainsOnlyTheLatestPendingFrame()
    {
        var hub = new SnapshotHub();
        using var slow = hub.Subscribe();
        for (var index = 0; index < 100_000; index++) hub.Publish(index.ToString());
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.Equal("99999", await slow.ReadAsync(deadline.Token));
        Assert.Equal(99_999, hub.CoalescedCount);
    }

    [Fact]
    public async Task ReconnectGetsCurrentSnapshotAndDisconnectFreesCapacity()
    {
        var hub = new SnapshotHub(1);
        hub.Publish("initial");
        using (var first = hub.Subscribe())
        {
            Assert.Throws<InvalidOperationException>(() => hub.Subscribe());
            hub.Publish("latest");
        }
        Assert.Equal(0, hub.ClientCount);
        using var reconnect = hub.Subscribe();
        Assert.Equal("latest", await reconnect.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task FastReaderDoesNotWaitForSlowReader()
    {
        var hub = new SnapshotHub();
        using var slow = hub.Subscribe();
        using var fast = hub.Subscribe();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        for (var index = 0; index < 1000; index++)
        {
            hub.Publish(index.ToString());
            Assert.Equal(index.ToString(), await fast.ReadAsync(deadline.Token));
        }
        Assert.Equal("999", await slow.ReadAsync(deadline.Token));
    }
}
