using System.Collections;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Threading.Channels;

namespace StarfrontControl.Service;

/// <summary>Prepares closed recordings from local disk; never contacts NINA or the recorder.</summary>
public sealed class ReplayIndexWarmup(RecordingReplay replay, ILogger<ReplayIndexWarmup> logger) : BackgroundService {
    private readonly Channel<bool> changes = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) {
        SingleReader=true, FullMode=BoundedChannelFullMode.DropWrite
    });
    private readonly Dictionary<string,string> observed = new();
    private FileSystemWatcher? watcher;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        var fallback = Task.Run(async () => {
            try { while(await timer.WaitForNextTickAsync(stoppingToken)) changes.Writer.TryWrite(true); }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { }
        }, CancellationToken.None);
        changes.Writer.TryWrite(true); // Also recover a finish missed while the gateway was offline.
        try {
            await foreach(var signal in changes.Reader.ReadAllAsync(stoppingToken)) {
                Watch();
                await Task.Delay(250, stoppingToken); // Coalesce atomic rename/change bursts.
                while(changes.Reader.TryRead(out _)) { }
                try { await Refresh(stoppingToken); }
                catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { throw; }
                catch(Exception error) { logger.LogWarning(error,"Could not inspect closed recordings for replay indexing"); }
            }
        } catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { }
        finally { watcher?.Dispose(); timer.Dispose(); await fallback; }
    }
    private void Watch() {
        if(watcher is not null || !Directory.Exists(replay.RecordingsRoot)) return;
        watcher = new FileSystemWatcher(replay.RecordingsRoot,"manifest.json") {
            IncludeSubdirectories=true, NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite|NotifyFilters.Size
        };
        watcher.Created += Changed;watcher.Changed += Changed;watcher.Renamed += Changed;
        watcher.Error += (_,_) => { changes.Writer.TryWrite(true); }; // Periodic local scan also recovers missed events.
        watcher.EnableRaisingEvents=true;
    }
    private void Changed(object sender,FileSystemEventArgs args) => changes.Writer.TryWrite(true);

    public async Task Refresh(CancellationToken cancel=default) {
        // Work only on the two most recent closed nights. Rewarming the whole
        // archive would evict the very cache we just built and waste disk/CPU.
        var recent=((IEnumerable)replay.List()).OfType<JsonObject>()
            .Where(m=>m["state"]?.GetValue<string>() is "completed" or "interrupted")
            .OrderByDescending(m=>m["finishedAt"]?.GetValue<string>() ?? m["updatedAt"]?.GetValue<string>() ?? m["startedAt"]?.GetValue<string>())
            .Take(2).Reverse().ToArray();
        var retained=recent.Select(m=>m["id"]!.GetValue<string>()).ToHashSet();
        foreach(var old in observed.Keys.Where(id=>!retained.Contains(id)).ToArray())observed.Remove(old);
        foreach(var manifest in recent) {
            cancel.ThrowIfCancellationRequested();
            var id=manifest["id"]!.GetValue<string>();var revision=manifest.ToJsonString();
            if(observed.GetValueOrDefault(id)==revision)continue;
            var elapsed=Stopwatch.StartNew();
            try {
                await replay.Index(id,cancel);
                logger.LogInformation("Replay index ready for {RecordingId} in {ElapsedMs} ms",id,elapsed.ElapsedMilliseconds);
            } catch(OperationCanceledException) when(cancel.IsCancellationRequested) { throw; }
            catch(Exception error) { logger.LogWarning(error,"Automatic replay index failed for {RecordingId}; on-demand replay remains available",id); }
            // Do not retry a corrupt or oversized recording every timer tick.
            // A changed manifest or gateway restart permits a fresh attempt.
            observed[id]=revision;
        }
    }
}
