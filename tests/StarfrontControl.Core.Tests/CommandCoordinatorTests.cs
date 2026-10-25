using StarfrontControl.Core;
using Xunit;

namespace StarfrontControl.Core.Tests;

public class CommandCoordinatorTests
{
    private static ControlCommand Command(string device = "camera") => new(Guid.NewGuid().ToString(), device, "connect", "simulator");
    private static TaskCompletionSource<ControlOperation> Terminal(CommandCoordinator commands)
    {
        var result = new TaskCompletionSource<ControlOperation>(TaskCreationOptions.RunContinuationsAsynchronously);
        commands.Changed += operation => { if (operation.CompletedAt is not null) result.TrySetResult(operation); };
        return result;
    }

    [Fact]
    public async Task DuplicateDeliveryExecutesOnlyOnce()
    {
        var commands = new CommandCoordinator();
        var command = Command();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminal = Terminal(commands);
        var executions = 0;
        var accepted = commands.Submit(command, async () => { Interlocked.Increment(ref executions); await release.Task; });
        for (var index = 0; index < 1000; index++) commands.Submit(command, () => throw new Exception("Duplicate must never run"));
        Assert.Equal("accepted", accepted.Status);
        Assert.Equal(1, commands.ActiveCount);
        release.SetResult();
        Assert.Equal("succeeded", (await terminal.Task.WaitAsync(TimeSpan.FromSeconds(5))).Status);
        Assert.Equal(1, executions);
        Assert.Equal(0, commands.ActiveCount);
    }

    [Fact]
    public async Task EquivalentUuidFormsShareOneExecutionAndLookup()
    {
        var commands = new CommandCoordinator();
        var command = Command();
        var id = Guid.Parse(command.RequestId);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminal = Terminal(commands);
        var executions = 0;
        commands.Submit(command, async () => { Interlocked.Increment(ref executions); await release.Task; });
        foreach (var alternate in new[] { id.ToString("D").ToUpperInvariant(), id.ToString("B"), id.ToString("N") })
        {
            var duplicate = commands.Submit(command with { RequestId = alternate }, () => throw new Exception("Duplicate must not execute"));
            Assert.Equal(id.ToString("D"), duplicate.RequestId);
            Assert.Equal(id.ToString("D"), commands.Find(alternate)!.RequestId);
        }
        Assert.Single(commands.Read());
        Assert.Null(commands.Find("invalid"));
        release.SetResult();
        await terminal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, executions);
    }

    [Fact]
    public async Task ConflictsAreRejectedWithoutQueuingMoreWork()
    {
        var commands = new CommandCoordinator();
        var command = Command();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var terminal = Terminal(commands);
        commands.Submit(command, () => release.Task);
        Assert.Throws<CommandConflictException>(() => commands.Submit(Command(), () => Task.CompletedTask));
        Assert.Throws<CommandConflictException>(() => commands.Submit(command with { Action = "disconnect" }, () => Task.CompletedTask));
        Assert.Single(commands.Read());
        release.SetResult();
        await terminal.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task FailureReleasesTheDeviceAndDoesNotExposeExceptionSecrets()
    {
        var commands = new CommandCoordinator();
        var terminal = Terminal(commands);
        commands.Submit(Command(), () => throw new Exception("password=canary"));
        var failed = await terminal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("failed", failed.Status);
        Assert.DoesNotContain("canary", failed.Error);
        Assert.Equal(0, commands.ActiveCount);
    }

    [Fact]
    public async Task CompletedHistoryIsBounded()
    {
        var commands = new CommandCoordinator(4);
        for (var index = 0; index < 100; index++)
        {
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var command = Command();
            void Changed(ControlOperation operation) { if (operation.RequestId == command.RequestId && operation.CompletedAt is not null) finished.TrySetResult(); }
            commands.Changed += Changed;
            commands.Submit(command, () => Task.CompletedTask);
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
            commands.Changed -= Changed;
        }
        Assert.Equal(4, commands.Read().Length);
        Assert.Equal(0, commands.ActiveCount);
    }

    [Fact]
    public async Task BrokenObserverDoesNotStrandOrRepeatTheOperation()
    {
        var commands = new CommandCoordinator();
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        commands.Changed += _ => throw new Exception("Observer failed");
        var command = Command();
        commands.Submit(command, () => { complete.SetResult(); return Task.CompletedTask; });
        await complete.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // The observer is broken, so inspect the authoritative registry.
        for (var index = 0; index < 100 && commands.ActiveCount > 0; index++) await Task.Delay(5);
        Assert.Equal("succeeded", commands.Find(command.RequestId)!.Status);
    }
}
