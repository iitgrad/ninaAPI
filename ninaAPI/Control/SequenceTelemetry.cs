using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using NINA.Core.Locale;
using StarfrontControl.Core;

namespace ninaAPI.Control;

// Host objects never leave the UI thread or enter the telemetry cache. Every
// notification schedules one coalesced projection; viewer count adds no work.
public sealed class SequenceTelemetry : IDisposable
{
    private readonly Action<string, object> publish;
    private readonly DispatcherTimer timer;
    private readonly List<Action> unsubscribe = new();
    private readonly ConditionalWeakTable<object, Identity> identities = new();
    private sealed class Identity { public string Id { get; } = Guid.NewGuid().ToString("N"); }
    private bool disposed;
    private readonly Dictionary<string, WeakReference<object>> projected = new();
    public object Resolve(string id) => projected.TryGetValue(id, out var item) && item.TryGetTarget(out var value) ? value : null;
    public int ProjectedCount => projected.Count;
    private long revision;
    private string lastCatalogRevision;
    private readonly SequenceConfigurationVersion configuration = new();
    private bool editable;
    private string executionId = Guid.NewGuid().ToString("N");
    private object executionRoot;
    private string previousRootStatus;
    public void NewExecution() { executionId = Guid.NewGuid().ToString("N"); Schedule(); }
    public void RequireEditable(string sequenceId,int expectedRevision) {
        Refresh();
        if(!editable)throw new CommandConflictException("Sequence editing and saving require stopped acquisition and a complete advanced sequence.");
        configuration.RequireCurrent(sequenceId,expectedRevision);
    }
    public void RequireConfiguration(string sequenceId,int expectedRevision) => RequireEditable(sequenceId,expectedRevision);
    public void RequireDraft(string sequenceId,int expectedRevision,string execution) {
        RequireEditable(sequenceId,expectedRevision);
        if(executionId!=execution)throw new CommandConflictException("Sequence execution changed. Keep the draft and choose another position.");
    }
    public void RefreshAfterEdit() => Refresh();
    private int refreshQueued;
    public int Subscriptions => unsubscribe.Count;
    public SequenceTelemetry(Action<string, object> publish)
    {
        this.publish = publish;
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, Tick, Application.Current.Dispatcher);
        timer.Stop();
        Schedule();
    }
    private void Tick(object sender, EventArgs args) { timer.Stop(); System.Threading.Interlocked.Exchange(ref refreshQueued, 0); Refresh(); }
    public void Schedule()
    {
        if (disposed || System.Threading.Interlocked.Exchange(ref refreshQueued, 1) != 0) return;
        _ = Application.Current.Dispatcher.InvokeAsync(() => { if (!disposed && !timer.IsEnabled) timer.Start(); });
    }
    private static object Get(object source, string name) => source?.GetType().GetProperty(name)?.GetValue(source);
    private static IEnumerable<object> Items(object source) => source is IEnumerable values ? values.Cast<object>() : Enumerable.Empty<object>();
    private static readonly HashSet<string> RelevantProperties = new() { "Status", "Name", "Items", "Conditions", "Triggers", "MainContainer", "ActiveSequencerVM", "SelectedTarget", "Sequencer", "Sequence2VM", "SimpleSequenceVM", "Iterations", "CompletedIterations", "ProgressExposureCount", "ExposureTime", "ExposureCount", "Time", "Position", "Gain", "Offset", "IsEnabled", "SelectedDevice", "Filter", "ComboBoxText", "TargetName", "PositionAngle", "Coordinates", "RA", "Dec", "RAHours", "RAMinutes", "RASeconds", "DecDegrees", "DecMinutes", "DecSeconds", "AfterExposures", "ProgressExposures", "Temperature", "Duration", "OnOff", "Mode", "USBLimit", "Message", "AttachPreview", "Hours", "Minutes", "Seconds", "MinutesOffset", "SelectedProvider", "TriggerRunner", "MinutesAfterMeridian", "PauseTimeBeforeMeridian", "MaxMinutesAfterMeridian", "UseSideOfPier", "WaitExpr", "Expression", "Instructions", "TemplateName", "SelectedTemplate" };
    private void Observe(object source, HashSet<object> seen)
    {
        if (source is null || !seen.Add(source)) return;
        if (source is INotifyPropertyChanged properties)
        {
            PropertyChangedEventHandler handler = (_, e) => { if (string.IsNullOrEmpty(e.PropertyName) || RelevantProperties.Contains(e.PropertyName)) Schedule(); };
            properties.PropertyChanged += handler; unsubscribe.Add(() => properties.PropertyChanged -= handler);
        }
        if (source is INotifyCollectionChanged collection)
        {
            NotifyCollectionChangedEventHandler handler = (_, _) => Schedule();
            collection.CollectionChanged += handler; unsubscribe.Add(() => collection.CollectionChanged -= handler);
        }
    }
    private void Clear() { foreach (var action in unsubscribe) action(); unsubscribe.Clear(); }
    private void Refresh()
    {
        if (disposed) return;
        Clear();
        projected.Clear();
        editable = false;
        try
        {
            var mediator = AdvancedAPI.Controls.Sequence;
            if (!mediator.Initialized) { publish("sequence", new { available = false, reason = "N.I.N.A. sequence initialization pending", nodes = Array.Empty<object>() }); return; }
            // N.I.N.A. 3.2's public mediator exposes running items but not the root.
            // Keep this single version-sensitive access isolated and fail visibly.
            var navigation = mediator.GetType().GetField("sequenceNavigation", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(mediator);
            if (navigation is null) throw new InvalidOperationException("Sequence navigation unavailable in this host version.");
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            Observe(navigation, seen);
            var active = Get(navigation, "ActiveSequencerVM");
            var simple = Get(navigation, "SimpleSequenceVM");
            var advanced = Get(navigation, "Sequence2VM");
            Observe(simple, seen); Observe(advanced, seen);
            var mode = ReferenceEquals(active, simple) ? "simple" : "advanced";
            var viewModel = mode == "simple" ? simple : advanced;
            var sequencer = Get(viewModel, "Sequencer"); Observe(sequencer, seen);
            var root = Get(sequencer, "MainContainer");
            if (root is null) throw new InvalidOperationException("Sequence root unavailable.");
            if (!ReferenceEquals(root, executionRoot)) { executionRoot=root;NewExecution(); }
            var rootStatus=Get(root,"Status")?.ToString();
            if(rootStatus=="CREATED" && previousRootStatus is not (null or "CREATED")) NewExecution();
            previousRootStatus=rootStatus;
            var catalogRevision = SequenceInsertion.CatalogRevision;
            if (catalogRevision != lastCatalogRevision) { publish("sequenceCatalog", SequenceInsertion.Catalog); lastCatalogRevision = catalogRevision; }
            var configurationNodes = new List<object>();
            var profile = AdvancedAPI.Controls.Profile.ActiveProfile;
            Observe(profile.FilterWheelSettings.FilterWheelFilters, seen);
            foreach (var filter in profile.FilterWheelSettings.FilterWheelFilters.Take(32)) Observe(filter, seen);
            var nodes = new List<object>(); var visited = new HashSet<object>(ReferenceEqualityComparer.Instance); var truncated = false;
            void Walk(object item, string parentId, string kind, int depth)
            {
                if (item is null) return;
                if (nodes.Count >= 1024 || depth > 16) { truncated = true; return; }
                if (!visited.Add(item)) return;
                Observe(item, seen);
                var id = identities.GetOrCreateValue(item).Id;
                projected[id] = new WeakReference<object>(item);
                var name = Get(item, "Name")?.ToString();
                if (string.IsNullOrWhiteSpace(name)) name = item.GetType().Name == "IfContainer" ? "Trigger actions" : item.GetType().Name;
                try { if (name.StartsWith("Lbl_", StringComparison.Ordinal)) name = Loc.Instance[name] ?? name; } catch { }
                var status = Get(item, "Status")?.ToString() ?? "UNKNOWN";
                var type = item.GetType().Name;
                var details = SequenceStepDetails.Project(item);
                var fields = SequenceEditSchema.Fields(item);
                if (item is NINA.Sequencer.Container.DeepSkyObjectContainer target) {
                    Observe(target.Target, seen); Observe(target.Target.InputCoordinates, seen); Observe(target.Target.InputCoordinates.Coordinates, seen);
                }
                configurationNodes.Add(new { id, parentId, kind, type, name, fields, enabled = Get(item, "IsEnabled"),
                    settings = details.Where(x => x.Key is not ("progressExposureCount" or "completedIterations" or "iterations" or "exposureCount" or "remainingSeconds")).ToDictionary(x => x.Key, x => x.Value) });
                Observe(Get(item, "WaitExpr"), seen);
                // Filter renames also change the displayed instruction selection.
                if (type == "SwitchFilter") Observe(Get(item, "Filter"), seen);
                nodes.Add(new { id, parentId, kind = Get(item, "Items") is IEnumerable ? "container" : kind, name = name[..Math.Min(name.Length, 96)], type = type[..Math.Min(type.Length, 64)], sourceStatus = status,
                    state = SequencePresentation.State(status, type), details, editableFields = fields, canStructure = SequenceStructure.Mutable(item), insertKinds = SequenceInsertion.InsertKinds(item) });
                if (SequenceStructure.NestedBody(item) is { } body) Walk(body, id, "container", depth + 1);
                foreach (var group in new[] { "Items", "Conditions", "Triggers" })
                {
                    var children = Get(item, group); Observe(children, seen);
                    foreach (var child in Items(children).Take(1025)) Walk(child, id, group == "Items" ? "instruction" : group == "Conditions" ? "condition" : "trigger", depth + 1);
                }
            }
            Walk(root, null, "container", 0);
            var activeNodeIds = mediator.GetAdvancedSequencerCurrentRunningItems()
                .Select(item => identities.GetOrCreateValue(item).Id).Take(64).ToArray();
            var boundedNodes = SequenceProjection.Bound(nodes.Select(item => JsonSerializer.SerializeToElement(item, ControlAdapter.Json)), activeNodeIds, maxBytes: 120 * 1024);
            truncated |= boundedNodes.Length != nodes.Count;
            var activeComplete = activeNodeIds.All(id => boundedNodes.Any(row => row.GetProperty("id").GetString() == id));
            configuration.Observe(identities.GetOrCreateValue(root).Id, new { mode, profileId = profile.Id, truncated, catalogRevision, configurationNodes });
            editable = mode == "advanced" && !truncated && !mediator.IsAdvancedSequenceRunning() && !AdvancedAPI.Controls.Camera.GetInfo().IsExposing;
            publish("sequence", new { available = true, mode, revision = ++revision, executionId,
                draftInsertion = new { supported=editable }, liveInsertion = new { supported=false, positions=Array.Empty<object>(), reason="Sequence editing requires stopped acquisition." },
                sequenceId = configuration.SequenceId, configurationRevision = configuration.Revision,
                editing = new { supported = mode == "advanced" && !truncated, stopped = !mediator.IsAdvancedSequenceRunning(), canEdit = editable, persistence = "loaded-sequence-only" },
                insertCatalog = Array.Empty<object>(), catalogTopic = "sequenceCatalog", catalogRevision = SequenceInsertion.CatalogRevision, nodeLimit = 256, truncated, activeNodeIds, activeComplete, nodes = boundedNodes });
        }
        catch (Exception) { publish("sequence", new { available = false, reason = "Sequence projection unavailable; inspect host compatibility.", nodes = Array.Empty<object>() }); }
    }
    public void Dispose()
    {
        disposed = true;
        void Cleanup() { timer.Stop(); timer.Tick -= Tick; Clear(); projected.Clear(); }
        if (Application.Current.Dispatcher.CheckAccess()) Cleanup(); else _ = Application.Current.Dispatcher.InvokeAsync(Cleanup);
    }
}
