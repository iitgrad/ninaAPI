using StarfrontControl.Core;
using Xunit;

namespace StarfrontControl.Core.Tests;

public sealed class SafeOrDeadlineTests
{
    [Fact]
    public void DeadlinePreservesTheObservatoryOffsetAcrossMidnight()
    {
        Assert.True(SafeOrDeadline.TryParseDeadline("2026-11-20T05:45:46-06:00", out var value));
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 11, 45, 46, TimeSpan.Zero), value.ToUniversalTime());
        Assert.False(SafeOrDeadline.TryParseDeadline("2026-11-20T05:45:46", out _));
    }

    [Fact]
    public async Task SafeBeforeDeadlinePermitsRecovery()
    {
        Assert.True(await SafeOrDeadline.WaitAsync(DateTimeOffset.UtcNow.AddMinutes(1), () => true, default));
    }

    [Fact]
    public async Task ExpiredDeadlineTakesPriorityOverSafe()
    {
        Assert.False(await SafeOrDeadline.WaitAsync(DateTimeOffset.UtcNow.AddSeconds(-1), () => true, default));
    }

    [Fact]
    public async Task ContinuouslyUnsafeWaitExpires()
    {
        var wait = SafeOrDeadline.WaitAsync(DateTimeOffset.UtcNow.AddMilliseconds(100), () => false, default);
        Assert.False(await wait.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task CancellingDoesNotPermitRecovery()
    {
        using var cancellation = new CancellationTokenSource();
        var wait = SafeOrDeadline.WaitAsync(DateTimeOffset.UtcNow.AddMinutes(1), () => false, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait);
    }
}
