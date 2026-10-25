using StarfrontControl.Core;
using Xunit;
public sealed class SafetyConnectionTests {
    [Fact] public async Task ManyViewersShareOneAttemptAndReconnectAfterLoss() {
        bool connected=false;int calls=0;var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var manager=new SafetyConnectionManager(()=>connected,async()=>{calls++;await release.Task;connected=true;});
        var requests=Enumerable.Range(0,20).Select(_=>manager.EnsureAsync()).ToArray();Assert.Equal(1,calls);Assert.True(manager.Status.Connecting);
        release.SetResult();await Task.WhenAll(requests);Assert.False(manager.Status.Connecting);Assert.Null(manager.Status.Error);
        connected=false;await manager.EnsureAsync();Assert.Equal(2,calls);
        manager.Dispose();connected=false;await manager.EnsureAsync();Assert.Equal(2,calls);
    }
    [Fact] public async Task FailuresUseBoundedBackoffAndViewersCannotDefeatIt() {
        var time=DateTimeOffset.UtcNow;int calls=0;using var manager=new SafetyConnectionManager(()=>false,()=>{calls++;throw new InvalidOperationException("No configured safety monitor");},()=>time);
        foreach(var seconds in new[]{1,2,4,8,16,30,30}){
            await Assert.ThrowsAsync<InvalidOperationException>(()=>manager.EnsureAsync());Assert.Equal(time.AddSeconds(seconds),manager.Status.RetryAt);
            var before=calls;await manager.EnsureAsync();Assert.Equal(before,calls);time=time.AddSeconds(seconds);
        }
        Assert.Contains("No configured",manager.Status.Error);
    }
    [Fact] public async Task ShutdownDoesNotPublishOrStartAnotherAttemptAfterPendingConnectionSettles() {
        var connected=false;var calls=0;var events=0;var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var manager=new SafetyConnectionManager(()=>connected,async()=>{calls++;await release.Task;connected=true;});manager.Changed+=_=>events++;
        var attempt=manager.EnsureAsync();manager.Dispose();var stoppedEvents=events;release.SetResult();await attempt;
        connected=false;await manager.EnsureAsync();Assert.Equal(1,calls);Assert.Equal(stoppedEvents,events);
    }
}
