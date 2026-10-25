using StarfrontControl.Core;
using Xunit;
namespace StarfrontControl.Core.Tests;
public class SequenceStopTests
{
    [Fact] public async Task NativeRunWithoutOwnerWaitsForNativeCompletion() {
        var running = true; var requested = 0;
        var task = SequenceStop.Execute(() => running, () => requested++, () => false, () => {}, default);
        Assert.Equal(1, requested); Assert.False(task.IsCompleted);
        running = false; await task;
    }
    [Fact] public async Task OwnedRunWaitsForBothRunnerAndStartOperation() {
        var running = true; var owner = true; var cancelledOwner = false;
        var task = SequenceStop.Execute(() => running, () => running = false, () => owner, () => cancelledOwner = true, default);
        Assert.True(cancelledOwner); Assert.False(task.IsCompleted);
        owner = false; await task;
    }
    [Fact] public async Task IdleStopIsHarmlessWithoutOwner() {
        await SequenceStop.Execute(() => false, () => throw new Exception("Must not cancel idle runner"), () => false, () => {}, default);
    }
    [Fact] public async Task UnsettledNativeRunnerDoesNotReportSuccess() {
        await Assert.ThrowsAsync<TimeoutException>(() => SequenceStop.Execute(() => true, () => {}, () => false, () => {}, default, TimeSpan.Zero));
    }
}
