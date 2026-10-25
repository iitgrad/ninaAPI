using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NINA.Core.Locale;
using NINA.Sequencer;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.Trigger;
using NINA.Sequencer.Trigger.Autofocus;
using NINA.Sequencer.Trigger.Guider;
using NINA.Sequencer.SequenceItem.Imaging;
using NINA.Sequencer.SequenceItem.Utility;
using NINA.Sequencer.SequenceItem.FilterWheel;

namespace ninaAPI.Control;

// Discover only templates already loaded by NINA. Never resolve client-provided CLR names.
internal static class SequenceInsertion
{
    private sealed record Entry(string Type, string NodeType, string Label, string Group, string Kind, string Source, bool Target);
    private sealed record Template(Entry Entry, ISequenceEntity Prototype);
    private sealed class CatalogState {
        public Template[] Templates;
        public object Snapshot;
        public int Count;
        public string Revision;
    }
    private static readonly ConditionalWeakTable<ISequencerFactory, CatalogState> cache = new();
    internal static ISequencerFactory Factory() {
        var mediator = AdvancedAPI.Controls.Sequence;
        var navigation = mediator.GetType().GetField("sequenceNavigation", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(mediator);
        return navigation?.GetType().GetField("factory", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(navigation) as ISequencerFactory
            ?? throw new InvalidOperationException("Sequence factory unavailable in this NINA host.");
    }
    private static string Localize(string value, string fallback) {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        if (value.StartsWith("Lbl_", StringComparison.Ordinal)) { try { value = Loc.Instance[value] ?? fallback; } catch { value = fallback; } }
        return value[..Math.Min(value.Length, 160)];
    }
    private static CatalogState State() {
        var factory = Factory();
        var count = factory.Items.Count + factory.Container.Count + factory.Conditions.Count + factory.Triggers.Count;
        if (cache.TryGetValue(factory, out var existing) && existing.Count == count) return existing;
        var prototypes = factory.Items.Cast<ISequenceEntity>().Concat(factory.Container.Cast<ISequenceEntity>())
            .Concat(factory.Conditions.Cast<ISequenceEntity>()).Concat(factory.Triggers.Cast<ISequenceEntity>())
            .GroupBy(p => p.GetType()).Select(g => g.First())
            .Where(p => p.GetType().Name is not ("SequenceRootContainer" or "StartAreaContainer" or "TargetAreaContainer" or "EndAreaContainer")).ToArray();
        var templates = prototypes.Select(p => {
            var type = p.GetType();
            var source = type.Assembly.GetName().Name ?? "Plugin";
            var id = source == "NINA.Sequencer" && type.Name.Length <= 64 ? type.Name
                : "host_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source + ":" + type.FullName)))[..24];
            var kind = p is ISequenceCondition ? "condition" : p is ISequenceTrigger ? "trigger" : p is ISequenceContainer ? "container" : "instruction";
            return new Template(new Entry(id, type.Name, Localize(p.Name, type.Name),
                Localize(p.Category, kind == "container" ? "Instruction sets" : kind == "condition" ? "Conditions" : kind == "trigger" ? "Triggers" : "Other"),
                kind, source, p is DeepSkyObjectContainer), p);
        }).OrderBy(t => t.Entry.Group).ThenBy(t => t.Entry.Label).ToArray();
        var entries = templates.Select(t => t.Entry).ToArray();
        var revision = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(entries, ControlAdapter.Json)));
        object snapshot = new { items = entries, count = entries.Length, revision, complete = true };
        if (JsonSerializer.SerializeToUtf8Bytes(snapshot, ControlAdapter.Json).Length > 60 * 1024)
            snapshot = new { items = Array.Empty<object>(), count = entries.Length, revision, complete = false, error = "Host catalog exceeds the telemetry budget; no entries were silently omitted." };
        var state = new CatalogState { Templates = templates, Count = count, Snapshot = snapshot, Revision = revision };
        cache.Remove(factory);cache.Add(factory, state);return state;
    }
    internal static ISequenceItem Detached(string id) {
        var template = Resolve(id);
        if (template.Entry.Kind is not ("instruction" or "container")) throw new ArgumentException("Only instructions and populated groups may be drafted.");
        var clone = template.Prototype.Clone();
        if (clone is not ISequenceItem item || ReferenceEquals(clone, template.Prototype) || item.Parent != null)
            throw new InvalidOperationException("Host template did not produce a detached instruction.");
        if (item is TakeExposure exposure) exposure.ExposureTime = 1;
        if (item is WaitForTimeSpan wait) wait.Time = 1;
        if (item is SwitchFilter filter) filter.Filter = AdvancedAPI.Controls.Profile.ActiveProfile.FilterWheelSettings.FilterWheelFilters.FirstOrDefault();
        if (item is TakeManyExposures many) { many.GetTakeExposure().ExposureTime=1;many.GetLoopCondition().Iterations=1; }
        if (item is SmartExposure smart) { smart.GetTakeExposure().ExposureTime=1;smart.GetLoopCondition().Iterations=1;smart.GetSwitchFilter().Filter=AdvancedAPI.Controls.Profile.ActiveProfile.FilterWheelSettings.FilterWheelFilters.FirstOrDefault(); }
        return item;
    }
    internal static object Schema(string id) {
        var item=Detached(id);
        // Schema uses the same scalar allowlist as stopped editing. Unmapped third-party
        // settings are never silently accepted as configured by a client.
        var fields=SequenceEditSchema.Fields(item);
        return new { type=id, label=Localize(item.Name,item.GetType().Name), fields,
            group=IsDraftGroup(item), supported=DraftSupported(item), maxDepth=16, maxNodes=240 };
    }
    internal static bool IsDraftGroup(ISequenceItem item)=>item.GetType()==typeof(SequentialContainer)||item.GetType()==typeof(ParallelContainer);
    internal static bool DraftSupported(ISequenceItem item) => item.GetType()==typeof(SequentialContainer) || item.GetType()==typeof(ParallelContainer)
        || (item.GetType().Name!="DisconnectEquipment" && (item is not ISequenceContainer || item.GetType().Assembly.GetName().Name=="NINA.Sequencer" && item.GetType().Name is "SmartExposure" or "TakeManyExposures") && (SequenceEditSchema.Fields(item).Length>0 || item.GetType().Name is
            "ConnectCamera" or "ConnectFilterWheel" or "ConnectFocuser" or "ConnectRotator" or "ConnectTelescope" or "ConnectGuider" or "ConnectSafetyMonitor"
            or "DisconnectOtherEquipmentInstruction" or "StartGuiding" or "StopGuiding" or "WaitUntilSafe" or "WaitForSafety"));
    public static object Catalog => State().Snapshot;
    public static string CatalogRevision => State().Revision;
    private static Template Resolve(string id) => State().Templates.SingleOrDefault(t => t.Entry.Type == id)
        ?? throw new ArgumentException("Element is not in this NINA host's loaded sequence catalog.");
    public static string[] InsertKinds(object parent) {
        if (parent is not SequenceContainer || parent is IImmutableContainer) return [];
        return parent.GetType().Name switch {
            "SequenceRootContainer" => [],
            "TargetAreaContainer" => ["target"],
            "StartAreaContainer" or "EndAreaContainer" => ["instruction", "container"],
            _ => ["instruction", "container", "condition", "trigger"]
        };
    }
    public static void Validate(object parent, string id, int index) {
        var template = Resolve(id);var kinds = InsertKinds(parent);var kind = template.Entry.Kind;
        if (!kinds.Contains(kind) && !(kinds.Contains("target") && template.Entry.Target))
            throw new ArgumentException("Element cannot be attached at this position.");
        var container = (SequenceContainer)parent;
        var count = kind == "condition" ? container.Conditions.Count : kind == "trigger" ? container.Triggers.Count : container.Items.Count;
        if (index < 0 || index > count) throw new ArgumentException("Insertion index is outside the attachment collection.");
        if (template.Prototype is ISequenceCondition condition && !condition.AllowMultiplePerSet && container.Conditions.Any(c => c.GetType() == condition.GetType()))
            throw new ArgumentException("This condition can occur only once per set.");
        if (template.Prototype is ISequenceTrigger trigger && !trigger.AllowMultiplePerSet && container.Triggers.Any(t => t.GetType() == trigger.GetType()))
            throw new ArgumentException("This trigger can occur only once per set.");
        if (template.Prototype is SwitchFilter or SmartExposure && !AdvancedAPI.Controls.Profile.ActiveProfile.FilterWheelSettings.FilterWheelFilters.Any())
            throw new ArgumentException("Configure filters in the active profile first.");
        if (template.Entry.Target && AdvancedAPI.Controls.Sequence.GetAllTargetsInAdvancedSequence().Count >= 32)
            throw new ArgumentException("Target limit reached.");
        var depth = 0;for (var item = (ISequenceItem)parent; item.Parent != null; item = item.Parent)
            if (++depth >= 15) throw new ArgumentException("Insertion would exceed the sequence depth bound.");
    }
    public static void Insert(object parent, string id, int index) {
        Validate(parent,id,index);
        var prototype = Resolve(id).Prototype;
        var clone = prototype.Clone() ?? throw new InvalidOperationException("The host template could not be cloned.");
        var container = (SequenceContainer)parent;
        if (clone is ISequenceCondition condition) { if (condition is LoopCondition loop) loop.Iterations = 2;container.InsertIntoSequenceBlocks(index,condition);return; }
        if (clone is ISequenceTrigger trigger) {
            if (trigger is AutofocusAfterExposures autofocus) autofocus.AfterExposures = 10;
            if (trigger is DitherAfterExposures dither) dither.AfterExposures = 5;
            container.InsertIntoSequenceBlocks(index,trigger);return;
        }
        if (clone is not ISequenceItem item) throw new InvalidOperationException("Host clone has an unsupported sequence interface.");
        if (item is TakeExposure exposure) exposure.ExposureTime = 1;
        if (item is TakeManyExposures many) { many.GetTakeExposure().ExposureTime = 1;many.GetLoopCondition().Iterations = 1; }
        if (item is SmartExposure smart) {
            smart.GetTakeExposure().ExposureTime = 1;smart.GetLoopCondition().Iterations = 1;
            smart.GetSwitchFilter().Filter = AdvancedAPI.Controls.Profile.ActiveProfile.FilterWheelSettings.FilterWheelFilters.First();
            smart.GetDitherAfterExposures().AfterExposures = 5;
        }
        if (item is WaitForTimeSpan wait) wait.Time = 1;
        if (item is SwitchFilter filter) filter.Filter = AdvancedAPI.Controls.Profile.ActiveProfile.FilterWheelSettings.FilterWheelFilters.First();
        if (item is DeepSkyObjectContainer target) { target.Name = "New deep sky object";target.Target.TargetName = target.Name; }
        if (item.GetType() == typeof(SequentialContainer)) item.Name = "Sequential set";
        container.InsertIntoSequenceBlocks(index,item);
    }
}
