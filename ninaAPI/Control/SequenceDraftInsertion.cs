using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using NINA.Core.Enum;
using NINA.Core.Locale;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using StarfrontControl.Core;

namespace ninaAPI.Control;

internal static class SequenceDraftPositions {
    internal static object Gate => typeof(SequenceContainer).GetField("lockObj",BindingFlags.Static|BindingFlags.NonPublic)?.GetValue(null)
        ?? throw new CommandConflictException("Sequence editing compatibility is unavailable in this N.I.N.A. host.");
}

public sealed partial class ControlAdapter
{
    private (SequenceContainer Parent,int Index) DraftDestination(ControlCommand c) {
        Require(!Configuration.SimulationOnly || Configuration.AllowedProfileIds.Contains(AdvancedAPI.Controls.Profile.ActiveProfile.Id.ToString()),"Profile is outside the simulation allowlist.");
        sequenceTelemetry.RequireDraft(Text(c,"sequenceId"),Integer(c,"expectedRevision"),Text(c,"executionId"));
        var parent=sequenceTelemetry.Resolve(Text(c,"parentId")) as SequenceContainer;
        var anchorId=Text(c,"afterNodeId");
        var anchor=string.IsNullOrEmpty(anchorId)?null:sequenceTelemetry.Resolve(anchorId) as ISequenceItem;
        if(parent==null || (!string.IsNullOrEmpty(anchorId)&&anchor==null) || !(SequenceInsertion.InsertKinds(parent).Contains("instruction") && (anchor==null || parent.Items.Contains(anchor))))
            throw new CommandConflictException("The chosen insertion position is no longer available. Keep the draft and choose another position.");
        return(parent,anchor==null?0:parent.Items.IndexOf(anchor)+1);
    }
    private ISequenceItem BuildDraft(JsonElement draft, int depth, ref int count) {
        Require(draft.ValueKind==JsonValueKind.Object && ++count+sequenceTelemetry.ProjectedCount<=240 && depth<=16,"Draft exceeds sequence size/depth limits.");
        Require(draft.EnumerateObject().All(p=>p.Name is "type" or "fields" or "items"),"Unknown draft property.");
        Require(draft.TryGetProperty("type",out var type)&&type.ValueKind==JsonValueKind.String,"Draft type is required.");
        var item=SequenceInsertion.Detached(type.GetString());
        Require(SequenceInsertion.DraftSupported(item),"This instruction needs configuration not yet supported by the detached editor; configure it in N.I.N.A. while stopped.");
        Require(draft.TryGetProperty("fields",out var fields)&&fields.ValueKind==JsonValueKind.Object,"Configured draft fields are required.");
        foreach(var field in fields.EnumerateObject()) SequenceEditSchema.Apply(item,field.Name,field.Value);
        foreach(var field in SequenceEditSchema.Fields(item)) {
            Require(fields.TryGetProperty(field.Key,out var value),"Required draft field: "+field.Label);
            SequenceEditing.Validate(field,value);
        }
        if(SequenceInsertion.IsDraftGroup(item) && item is SequenceContainer group) {
            Require(draft.TryGetProperty("items",out var children)&&children.ValueKind==JsonValueKind.Array&&children.GetArrayLength()>0,"Add at least one instruction to the draft group.");
            Require(group.Items.Count==0 && group.Triggers.Count==0 && group.Conditions.Count==0,"Only empty native group templates can be populated as drafts.");
            foreach(var child in children.EnumerateArray()) group.InsertIntoSequenceBlocks(group.Items.Count,BuildDraft(child,depth+1,ref count));
        } else Require(!draft.TryGetProperty("items",out var children)||children.ValueKind==JsonValueKind.Array&&children.GetArrayLength()==0,"Instructions cannot contain draft children.");
        return item;
    }
    private ISequenceItem ConfiguredDraft(ControlCommand c,SequenceContainer parent) {
        var depth=0;for(var p=parent;p!=null;p=p.Parent as SequenceContainer)depth++;
        var count=0;var item=BuildDraft(c.Parameters["draft"],depth+1,ref count);
        var metrics=SequenceStructure.Measure(item);
        Require(metrics.Count+sequenceTelemetry.ProjectedCount<=240 && depth+metrics.Depth<=16,"Draft including native child instructions exceeds sequence bounds.");
        // Device choices are those already selected by the active profile, never supplied CLR/device instances.
        foreach(var kind in CommandCatalog.Equipment) {
            var (chooser,_)=ninaAPI.WebService.V2.ControllerV2.GetDeviceVM(kind.ToLowerInvariant());
            if(chooser.SelectedDevice?.Id is string id && id is not ("No_Device" or "No_Guider") && id.Length>0) ValidateSelected(kind);
        }
        item.AttachNewParent(parent);
        try {
            void Check(object current) {
            if(current is SequenceContainer container) { foreach(var child in container.Items)Check(child);foreach(var condition in container.Conditions)Check(condition);foreach(var trigger in container.Triggers)Check(trigger);return; }
            var validate=current.GetType().GetMethod("Validate",Type.EmptyTypes);
            if(validate?.Invoke(current,null) is false) {
                var issues=current.GetType().GetProperty("Issues")?.GetValue(current) as IEnumerable<string>;
                var pending=(issues??["required settings are missing"]).ToArray();
                // Native editing permits building a sequence before connecting equipment.
                // Defer only the exact host's localized disconnected-device messages,
                // and only when that device is explicitly selected/allowed. Every other
                // native validation failure still rejects the detached draft.
                bool Deferred(string issue) {
                    foreach(var pair in new[]{("camera","LblCameraNotConnected"),("mount","LblTelescopeNotConnected"),("filterWheel","LblFilterWheelNotConnected"),("focuser","LblFocuserNotConnected"),("rotator","LblRotatorNotConnected"),("guider","LblGuiderNotConnected")}) {
                        if(issue!=Loc.Instance[pair.Item2])continue;
                        ValidateSelected(pair.Item1);
                        var selected=ninaAPI.WebService.V2.ControllerV2.GetDeviceVM(pair.Item1.ToLowerInvariant()).Item1.SelectedDevice;
                        Require(selected?.Id is string selectedId && selectedId is not ("No_Device" or "No_Guider") && selectedId.Length>0,"Select a configured "+pair.Item1+" before adding this instruction.");
                        return true;
                    }
                    return false;
                }
                if(pending.Length==0 || !pending.All(Deferred))throw new ArgumentException("Draft validation: "+string.Join("; ",pending));
            }
            }
            Check(item);
        } finally { item.AttachNewParent(null); }
        return item;
    }
    private void ValidateDraftInsertion(ControlCommand c) {
        lock(SequenceDraftPositions.Gate) {var destination=DraftDestination(c);ConfiguredDraft(c,destination.Parent);}
    }
    private void InsertDraft(ControlCommand c) {
        // Stopped-only. Validate and attach on the host UI thread without resetting completed work.
        lock(SequenceDraftPositions.Gate) {
            var destination=DraftDestination(c);var item=ConfiguredDraft(c,destination.Parent);
            destination=DraftDestination(c);
            destination.Parent.InsertIntoSequenceBlocks(destination.Index,item);
        }
        sequenceTelemetry.RefreshAfterEdit();
    }
}
