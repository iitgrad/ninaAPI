using StarfrontControl.Core;
using Xunit;
namespace StarfrontControl.Core.Tests;
public class MountStopTests
{
    [Fact] public async Task ExternalSlewStopsWithoutOwner() {
        var moving=true;
        await MountStop.Execute(()=>moving,()=>false,()=>throw new Exception(),()=>false,()=>{},()=>moving=false,default);
        Assert.False(moving);
    }
    [Fact] public async Task SequenceIsCancelledBeforeStoppingAndCompletionIsObserved() {
        var moving=true;var running=true;var events=new List<string>();
        var task=MountStop.Execute(()=>moving,()=>running,()=>events.Add("sequence"),()=>false,()=>events.Add("owner"),()=>{events.Add("stop");moving=false;},default);
        Assert.Equal(new[]{"owner","sequence","stop"},events);Assert.False(task.IsCompleted);
        running=false;await task;
    }
    [Fact] public async Task IdleRaceDoesNotCancelRunningSequence() {
        await MountStop.Execute(()=>false,()=>true,()=>throw new Exception(),()=>false,()=>throw new Exception(),()=>throw new Exception(),default);
    }
    [Fact] public async Task CancellationFailureStillRequestsImmediateStop() {
        var stopped=false;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>MountStop.Execute(()=>true,()=>true,()=>throw new InvalidOperationException(),()=>false,()=>{},()=>stopped=true,default));
        Assert.True(stopped);
    }
    [Fact] public async Task MovingDriverCannotReportSuccessfulStop() {
        await Assert.ThrowsAsync<TimeoutException>(()=>MountStop.Execute(()=>true,()=>false,()=>{},()=>false,()=>{},()=>{},default,TimeSpan.Zero));
    }
    [Fact] public async Task OwnedAxisMoveWithoutSlewingFlagIsCancelled() {
        var owner=true;
        await MountStop.Execute(()=>false,()=>false,()=>{},()=>owner,()=>owner=false,()=>{},default);
        Assert.False(owner);
    }
}
