namespace StarfrontControl.Core;

public static class MountStop
{
    // A native/sequence slew may have no Starfront operation. Cancel the runner
    // before stopping motion so its retry/next instruction cannot slew again.
    public static async Task Execute(Func<bool> slewing, Func<bool> sequenceRunning,
        Action cancelSequence, Func<bool> ownerActive, Action cancelOwner,
        Action stopSlew, CancellationToken cancellation, TimeSpan? timeout = null)
    {
        if (!slewing() && !ownerActive()) return; // A completed slew raced the click.
        try
        {
            cancelOwner();
            if (sequenceRunning()) cancelSequence();
        }
        finally { stopSlew(); } // Cancellation failure must never suppress the stop.
        var started = System.Diagnostics.Stopwatch.StartNew();
        while (slewing() || ownerActive() || sequenceRunning())
        {
            if (started.Elapsed > (timeout ?? TimeSpan.FromSeconds(30)))
                throw new TimeoutException("Mount or sequence did not stop in time.");
            await Task.Delay(50, cancellation);
        }
    }
}
