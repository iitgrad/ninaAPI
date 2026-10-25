using System.Diagnostics;
using System.Text.Json;
using StarfrontControl.Core;

namespace StarfrontControl.Service;

public sealed class ServiceDiagnostics(BridgeConnection bridge) : BackgroundService
{
    public object Latest { get; private set; } = new { available = false };
    private int writeErrors;
    private readonly RollingDiagnostics metrics = new(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ServiceConfiguration.ConfigurationPath))!, "Diagnostics", "Metrics"));
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                using var process = Process.GetCurrentProcess();
                var gc = GC.GetGCMemoryInfo();
                var sample = new
                {
                    observedAt = DateTimeOffset.UtcNow, processId = process.Id,
                    processPrivateBytes = process.PrivateMemorySize64 > 0 ? process.PrivateMemorySize64 : (long?)null, workingSetBytes = process.WorkingSet64,
                    cpuSecondsTotal = process.TotalProcessorTime.TotalSeconds,
                    managedLiveEstimateBytes = GC.GetTotalMemory(false), gcHeapBytes = gc.HeapSizeBytes,
                    gcCommittedBytes = gc.TotalCommittedBytes, gcFragmentedBytes = gc.FragmentedBytes,
                    serverGc = System.Runtime.GCSettings.IsServerGC,
                    gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2),
                    allocatedBytesTotal = GC.GetTotalAllocatedBytes(false), bridgeConnected = bridge.Connected,
                    clients = bridge.Clients.ClientCount, coalescedClientFrames = bridge.Clients.CoalescedCount,
                    ownMetricLogBytes = metrics.DiskBytes, ownMetricLogLimitBytes = 20 * 1024 * 1024, diagnosticWriteErrors = writeErrors
                };
                Latest = sample;
                metrics.Write(JsonSerializer.Serialize(sample));
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { Interlocked.Increment(ref writeErrors); }
            }
        }
        catch (OperationCanceledException) { }
    }
}

public sealed class RollingLoggerProvider : ILoggerProvider
{
    private readonly RollingDiagnostics log = new(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ServiceConfiguration.ConfigurationPath))!, "Diagnostics", "Logs"));
    public ILogger CreateLogger(string categoryName) => new RollingLogger(log, categoryName);
    public void Dispose() { }
    private sealed class RollingLogger(RollingDiagnostics log, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Warning;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(level)) return;
            // Do not record request bodies, headers, configuration or exception
            // text; those may carry credentials. Category/event identify faults.
            try { log.Write(JsonSerializer.Serialize(new { at = DateTimeOffset.UtcNow, level = level.ToString(), category, eventId = eventId.Id, exceptionType = exception?.GetType().Name })); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
