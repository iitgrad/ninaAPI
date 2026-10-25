namespace StarfrontControl.Core;

public static class SequenceStop
{
    // Execution belongs to NINA; a Starfront operation is optional. Completion
    // requires both the native runner and any tracked Start to settle.
    public static async Task Execute(Func<bool> running, Action requestStop,
        Func<bool> ownerActive, Action cancelOwner, CancellationToken cancellation,
        TimeSpan? timeout = null)
    {
        cancelOwner();
        if (running()) requestStop();
        var started = System.Diagnostics.Stopwatch.StartNew();
        while (running() || ownerActive())
        {
            if (started.Elapsed > (timeout ?? TimeSpan.FromSeconds(30)))
                throw new TimeoutException("N.I.N.A. sequence did not stop in time.");
            await Task.Delay(50, cancellation);
        }
    }
}
