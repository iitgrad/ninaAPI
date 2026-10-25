using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows;
using NINA.Core.Utility;
using NINA.Equipment.Equipment;
using NINA.Equipment.Equipment.MyCamera;
using NINA.Equipment.Equipment.MyTelescope;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using ninaAPI.WebService.V2;
using StarfrontControl.Core;

namespace ninaAPI.Control;

public sealed partial class ControlAdapter : ICameraConsumer, ITelescopeConsumer, IDisposable
{
    public static ControlAdapter Current { get; private set; }
    public ControlConfiguration Configuration { get; }
    public TelemetryStateStore Store { get; } = new(maxSequencePayloadBytes: 128 * 1024);
    public SnapshotHub Hub { get; } = new(8);
    public CommandCoordinator Commands { get; } = new(128);
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly object gate = new();
    private readonly Dictionary<string, (object Value, DateTimeOffset Time)> pending = new();
    private readonly Channel<bool> changed = Channel.CreateBounded<bool>(1);
    private readonly CancellationTokenSource stop = new();
    private readonly Task publisher;
    private readonly Task sampler;
    private long coalesced;
    private int publishErrors;
    private int diagnosticErrors;
    private object latestMetrics = new { available = false };
    public object CachedMetrics => Volatile.Read(ref latestMetrics);
    private bool disposed;
    private bool sequenceSubscribed;
    private readonly SequenceTelemetry sequenceTelemetry;
    private readonly List<INotifyCollectionChanged> catalogs = new();
    private readonly List<INotifyPropertyChanged> chooserSubscriptions = new();
    private int catalogRefreshQueued;
    private readonly RollingDiagnostics diagnostics = new(Path.Combine(ControlConfiguration.DirectoryPath, "Diagnostics"));

    public ControlAdapter(ControlConfiguration configuration)
    {
        Configuration = configuration;
        sequenceTelemetry = new SequenceTelemetry(Enqueue);
        Current = this;
        InitializeDashboard();
        InitializeAuxiliary();
        Commands.Changed += OperationChanged;
        AdvancedAPI.Controls.Camera.RegisterConsumer(this);
        AdvancedAPI.Controls.Mount.RegisterConsumer(this);
        publisher = PublishLoop();
        sampler = SampleLoop();
        _ = Application.Current.Dispatcher.InvokeAsync(() =>
        {
            lock (gate) { if (disposed) return; }
            UpdateDeviceInfo(AdvancedAPI.Controls.Camera.GetInfo());
            UpdateDeviceInfo(AdvancedAPI.Controls.Mount.GetInfo());
            foreach (var kind in CommandCatalog.Equipment)
            {
                var (chooser, _) = ControllerV2.GetDeviceVM(kind.ToLowerInvariant());
                if (chooser is INotifyPropertyChanged observable)
                {
                    chooserSubscriptions.Add(observable);
                    observable.PropertyChanged += ChooserChanged;
                }
                if (chooser.Devices is INotifyCollectionChanged catalog)
                {
                    catalogs.Add(catalog);
                    catalog.CollectionChanged += CatalogChanged;
                }
            }
            AdvancedAPI.Controls.Profile.ProfileChanged += ProfileCatalogChanged;
            RefreshCatalog();
            Enqueue("commands", new { items = CommandCatalog.Definitions });
            PublishAdministration();
            AttachSequence();
            InitializeSafetyConnection();
            PublishApplication(AdvancedAPI.Controls.Sequence.Initialized ? AdvancedAPI.Controls.Sequence.IsAdvancedSequenceRunning() : null);
        }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private void CatalogChanged(object sender, NotifyCollectionChangedEventArgs args) => ScheduleCatalog();
    private void ChooserChanged(object sender, PropertyChangedEventArgs args)
    {
        if (string.IsNullOrEmpty(args.PropertyName) || args.PropertyName is "Devices" or "SelectedDevice") ScheduleCatalog();
    }
    private void ProfileCatalogChanged(object sender, EventArgs args) => ScheduleCatalog();
    private void ScheduleCatalog()
    {
        if (disposed || Interlocked.Exchange(ref catalogRefreshQueued, 1) != 0) return;
        _ = Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Interlocked.Exchange(ref catalogRefreshQueued, 0);
            if (!disposed) { RefreshCatalog(); PublishAdministration(); }
        }, System.Windows.Threading.DispatcherPriority.Background);
    }
    private string ProfileDeviceId(string kind) => kind switch
    {
        "camera" => AdvancedAPI.Controls.Profile.ActiveProfile.CameraSettings.Id,
        "mount" => AdvancedAPI.Controls.Profile.ActiveProfile.TelescopeSettings.Id,
        _ => null
    };
    private void AttachSequence()
    {
        lock (gate)
        {
            if (disposed || sequenceSubscribed || !AdvancedAPI.Controls.Sequence.Initialized) return;
            AdvancedAPI.Controls.Sequence.SequenceStarting += SequenceStarting;
            AdvancedAPI.Controls.Sequence.SequenceFinished += SequenceFinished;
            sequenceSubscribed = true;
            PublishApplication(AdvancedAPI.Controls.Sequence.IsAdvancedSequenceRunning());
        }
    }
    private Task SequenceStarting(object sender, EventArgs args) { sequenceTelemetry.NewExecution(); PublishApplication(true); sequenceTelemetry.Schedule(); return Task.CompletedTask; }
    private Task SequenceFinished(object sender, EventArgs args) { PublishApplication(false); sequenceTelemetry.Schedule(); return Task.CompletedTask; }
    private void PublishApplication(bool? running) => Enqueue("application", new { name = "Starfront Control", version = typeof(ControlAdapter).Assembly.GetName().Version?.ToString(), environment = Configuration.SimulationOnly ? "simulation" : "equipment", legacyApiEnabled = Configuration.EnableLegacyApi, sequenceRunning = running });

    private void OperationChanged(ControlOperation _) =>
        Enqueue("operations", new { items = Commands.Read().OrderByDescending(x => x.AcceptedAt).Take(32).Select(x => new {x.RequestId,x.Device,x.Action,x.DeviceId,x.Status,x.AcceptedAt,x.CompletedAt,x.Error}).ToArray() });

    public void UpdateDeviceInfo(CameraInfo info) => PublishDevice("camera", info,
        "BinX", "BinY", "DewHeaterOn", "HasDewHeater", "ReadoutModeForNormalImages", "Connected", "Name", "CameraState", "IsExposing", "Temperature", "TemperatureSetPoint", "CoolerOn", "CoolerPower", "Gain", "Offset", "XSize", "YSize", "BitDepth", "ExposureMin", "ExposureMax", "CanSetGain", "CanSetOffset", "CanSetTemperature", "ExposureEndTime", "LastDownloadTime");
    public void UpdateDeviceInfo(TelescopeInfo info) => PublishDevice("mount", info,
        "Connected", "Name", "RightAscension", "Declination", "Altitude", "Azimuth", "SideOfPier", "TrackingEnabled", "Slewing", "AtPark", "AtHome");

    private void PublishDevice(string topic, object info, params string[] names)
    {
        try { Enqueue(topic, Copy(info, names)); }
        catch (Exception) { Interlocked.Increment(ref publishErrors); }
    }

    private static Dictionary<string, object> Copy(object info, params string[] names)
    {
        var result = new Dictionary<string, object>();
        foreach (var name in names)
        {
            var value = info?.GetType().GetProperty(name)?.GetValue(info);
            result[JsonNamingPolicy.CamelCase.ConvertName(name)] = value switch
            {
                double number when !double.IsFinite(number) => null,
                float number when !float.IsFinite(number) => null,
                Enum enumeration => enumeration.ToString(),
                string text => text.Length <= 128 ? text : text[..128],
                bool or byte or short or ushort or int or long or double or float or decimal => value,
                _ => null
            };
        }
        return result;
    }

    public void Enqueue(string topic, object value)
    {
        lock (gate)
        {
            if (disposed) return;
            if (pending.ContainsKey(topic)) Interlocked.Increment(ref coalesced);
            // Adapter owns this fixed topic set; no client can create topics.
            if (pending.Count >= ResourceLimits.AdapterPendingTopicCapacity && !pending.ContainsKey(topic)) return;
            pending[topic] = (value, DateTimeOffset.UtcNow);
            changed.Writer.TryWrite(true);
        }
    }

    private async Task PublishLoop()
    {
        try
        {
            while (await changed.Reader.WaitToReadAsync(stop.Token).ConfigureAwait(false))
            {
                await Task.Delay(100, stop.Token).ConfigureAwait(false);
                (string Topic, object Value, DateTimeOffset Time)[] updates;
                lock (gate)
                {
                    updates = pending.Select(x => (x.Key, x.Value.Value, x.Value.Time)).ToArray();
                    pending.Clear();
                    while (changed.Reader.TryRead(out _)) { }
                }
                foreach (var update in updates)
                {
                    try { Store.Update(update.Topic, JsonSerializer.Serialize(update.Value, Json), update.Time); }
                    catch (Exception) { Interlocked.Increment(ref publishErrors); }
                }
                Hub.Publish(Store.Read().Json);
            }
        }
        catch (OperationCanceledException) { }
    }

    private sealed record DeviceChoice(string Device,string Id,string Name,bool Allowed,bool Selected);
    public void RefreshCatalog()
    {
        var choices = new List<DeviceChoice>();
        foreach (var kind in CommandCatalog.Equipment)
        {
            var (chooser, _) = ControllerV2.GetDeviceVM(kind.ToLowerInvariant());
            var selectedId = ProfileDeviceSelection.Resolve(chooser.SelectedDevice?.Id, ProfileDeviceId(kind));
            foreach (var device in chooser.Devices.Where(x => x.Id?.Length <= 512).Take(128))
                choices.Add(new(kind, device.Id, device.Name?.Length > 128 ? device.Name[..128] : device.Name,
                    device is not NINA.Equipment.Equipment.OfflineDevice && (!Configuration.SimulationOnly || Configuration.AllowedDeviceIds.Contains(device.Id)),
                    selectedId == device.Id));
            // N.I.N.A. lazily builds chooser lists. Its saved selection remains authoritative
            // before the Equipment tab is opened; discovery happens only on explicit Connect.
            if (selectedId is { Length: <= 512 } && !choices.Any(x => x.Device == kind && x.Id == selectedId))
                choices.Add(new(kind, selectedId, chooser.SelectedDevice?.Name ?? selectedId,
                    !Configuration.SimulationOnly || Configuration.AllowedDeviceIds.Contains(selectedId), true));
        }
        var items=choices.OrderByDescending(x=>x.Selected).ThenByDescending(x=>x.Allowed).Take(128).ToList();
        var truncated=items.Count<choices.Count;
        while(JsonSerializer.SerializeToUtf8Bytes(items,Json).Length>58*1024 && items.Count>0){items.RemoveAt(items.Count-1);truncated=true;}
        Enqueue("devices", new { items,truncated,limit=128 });
    }

    public object Metrics()
    {
        using var process = Process.GetCurrentProcess();
        var memory = GC.GetGCMemoryInfo();
        var logs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NINA", "Logs");
        var logFiles = Directory.Exists(logs) ? new DirectoryInfo(logs).EnumerateFiles("*.log").Take(1025).ToArray() : [];
        int pendingCount; lock (gate) pendingCount = pending.Count;
        return new
        {
            observedAt = DateTimeOffset.UtcNow, processId = process.Id,
            processPrivateBytes = process.PrivateMemorySize64, workingSetBytes = process.WorkingSet64,
            cpuSecondsTotal = process.TotalProcessorTime.TotalSeconds,
            managedLiveEstimateBytes = GC.GetTotalMemory(false), gcHeapBytes = memory.HeapSizeBytes,
            gcCommittedBytes = memory.TotalCommittedBytes, gcFragmentedBytes = memory.FragmentedBytes,
            allocatedBytesTotal = GC.GetTotalAllocatedBytes(false), gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2),
            handles = process.HandleCount, threads = process.Threads.Count,
            pendingTopics = pendingCount, pendingTopicCapacity = ResourceLimits.AdapterPendingTopicCapacity, stateTopics = Store.Read().TopicCount, statePayloadBytes = Store.Read().PayloadBytes,
            eventHistoryCount = WebSocketV2.Events.Count, eventHistoryCapacity = WebSocketV2.Events.Capacity,
            clients = Hub.ClientCount, retainedTransportClients = ControlSocket.Current?.RetainedClients ?? 0, sendFailures = ControlSocket.Current?.SendFailures ?? 0, clientLimit = 8, pendingSnapshotsPerClient = 1,
            coalescedUpdates = Interlocked.Read(ref coalesced), coalescedClientFrames = Hub.CoalescedCount,
            sequenceSubscriptions = sequenceTelemetry.Subscriptions, activeCommands = Commands.ActiveCount, retainedOperations = Commands.Read().Length,
            publishErrors, diagnosticErrors, ninaLogBytes = logFiles.Sum(x => x.Length),
            ninaLogFiles = logFiles.Length, ninaLogScanTruncated = logFiles.Length > 1024,
            ownLogBytes = diagnostics.DiskBytes, ownLogLimitBytes = 20 * 1024 * 1024
        };
    }

    private async Task SampleLoop()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
            while (await timer.WaitForNextTickAsync(stop.Token).ConfigureAwait(false))
            {
                try
                {
                    if (!sequenceSubscribed) await Application.Current.Dispatcher.InvokeAsync(AttachSequence);
                    sequenceTelemetry.Schedule();
                    var metrics = Metrics();
                    Volatile.Write(ref latestMetrics, metrics);
                    diagnostics.Write(JsonSerializer.Serialize(metrics, Json));
                    Enqueue("diagnostics", metrics);
                }
                catch (Exception) { Interlocked.Increment(ref diagnosticErrors); }
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        lock (gate) { if (disposed) return; disposed = true; pending.Clear(); }
        safetyConnections?.Dispose();
        if(Application.Current.Dispatcher.CheckAccess()) safetyTimer?.Stop();
        else _=Application.Current.Dispatcher.InvokeAsync(()=>safetyTimer?.Stop());
        CancelOwnedCommands();
        DisposeDashboard();
        DisposeAuxiliary();
        sequenceTelemetry.Dispose();
        AdvancedAPI.Controls.Camera.RemoveConsumer(this);
        AdvancedAPI.Controls.Mount.RemoveConsumer(this);
        if (sequenceSubscribed)
        {
            AdvancedAPI.Controls.Sequence.SequenceStarting -= SequenceStarting;
            AdvancedAPI.Controls.Sequence.SequenceFinished -= SequenceFinished;
        }
        foreach (var catalog in catalogs) catalog.CollectionChanged -= CatalogChanged;
        catalogs.Clear();
        foreach (var chooser in chooserSubscriptions) chooser.PropertyChanged -= ChooserChanged;
        chooserSubscriptions.Clear();
        AdvancedAPI.Controls.Profile.ProfileChanged -= ProfileCatalogChanged;
        Commands.Changed -= OperationChanged;
        stop.Cancel();
        changed.Writer.TryComplete();
        if (ReferenceEquals(Current, this)) Current = null;
        _ = Task.WhenAll(publisher, sampler, previewWorker).ContinueWith(_ => stop.Dispose(), TaskScheduler.Default);
    }
}
