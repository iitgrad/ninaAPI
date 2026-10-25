using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using NINA.Sequencer;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.Trigger;

namespace ninaAPI.Control;

// All calls run on NINA's UI thread after RequireEditable has revalidated the root/revision.
internal static class SequenceStructure
{
    public static SequenceContainer NestedBody(object item) {
        var type=item.GetType();
        if (type.Assembly.GetName().Name == "WhenPlugin") {
            for (var current=type;current!=null;current=current.BaseType)
                if (current.FullName is "WhenPlugin.When.When" or "WhenPlugin.When.IfCommand")
                    return type.GetProperty("Instructions")?.GetValue(item) as SequenceContainer;
        }
        return item is SequenceTrigger trigger ? trigger.TriggerRunner : null;
    }
    // Explicit stopped-only command. Telemetry never resolves or clones templates.
    public static object ReferencedTemplate(object item, int projectedCount) {
        var type = item?.GetType();
        if (type?.FullName != "WhenPlugin.When.TemplateByReference" || type.Assembly.GetName().Name != "WhenPlugin")
            throw new ArgumentException("Select a Powerups template reference.");
        if (type.GetProperty("SelectedTemplate")?.GetValue(item) != null || NestedBody(item)?.Items.Count > 0)
            throw new ArgumentException("Referenced actions are already loaded; they were not replaced.");
        var name = type.GetProperty("TemplateName")?.GetValue(item) as string;
        var templates = type.GetProperty("Templates")?.GetValue(item) as IEnumerable;
        var template = templates?.Cast<object>().Take(1024).FirstOrDefault(t =>
            t.GetType().GetProperty("Container")?.GetValue(t) is ISequenceEntity entity && entity.Name == name)
            ?? throw new ArgumentException("Referenced template is unavailable in NINA's template library: " + name);
        var container = template.GetType().GetProperty("Container").GetValue(template);
        var metrics = Measure(container);
        if (projectedCount + metrics.Count > 240 || metrics.Depth > 10)
            throw new ArgumentException("Referenced template exceeds the sequence editing bounds.");
        void Check(object node) {
            if (node.GetType().FullName == "WhenPlugin.When.TemplateByReference")
                throw new ArgumentException("Nested template references must be resolved in NINA first.");
            foreach (var child in Children(node)) Check(child);
        }
        Check(container);
        return template;
    }
    public static ISequenceContainer Parent(object item) => item switch {
        ISequenceCondition c => c.Parent, ISequenceTrigger t => t.Parent, ISequenceItem i => i.Parent, _ => null
    };
    public static bool Mutable(object item) => item is ISequenceEntity && Parent(item) is SequenceContainer parent
        && parent is not IImmutableContainer && Index(item, parent) >= 0 && item.GetType().Name is not ("SequenceRootContainer" or "StartAreaContainer" or "TargetAreaContainer" or "EndAreaContainer");
    private static string Kind(object item) => item is ISequenceCondition ? "condition" : item is ISequenceTrigger ? "trigger" : item is ISequenceContainer ? "container" : "instruction";
    private static IEnumerable<object> Children(object item) => item is SequenceContainer c
        ? c.Items.Cast<object>().Concat(c.Conditions.Cast<object>()).Concat(c.Triggers.Cast<object>())
        : NestedBody(item) is { } body ? [body] : [];
    internal static (int Count,int Depth,int Targets) Measure(object item, HashSet<object> seen = null) {
        if (item is ISequenceContainer && item is not SequenceContainer && NestedBody(item) == null) throw new ArgumentException("This plugin container does not expose a supported structure.");
        seen ??= new(ReferenceEqualityComparer.Instance);
        if (!seen.Add(item) || seen.Count > 256) throw new ArgumentException("Sequence subtree exceeds the editing bound or contains a cycle.");
        var count=1;var depth=1;var targets=item is DeepSkyObjectContainer?1:0;
        foreach(var child in Children(item)) { var m=Measure(child,seen);count+=m.Count;depth=Math.Max(depth,m.Depth+1);targets+=m.Targets; }
        return (count,depth,targets);
    }
    private static int Index(object item, SequenceContainer parent) => item switch {
        ISequenceCondition c=>parent.Conditions.IndexOf(c),ISequenceTrigger t=>parent.Triggers.IndexOf(t),ISequenceItem i=>parent.Items.IndexOf(i),_=>-1
    };
    private static void Remove(object item, SequenceContainer parent) {
        var removed=item switch { ISequenceCondition c=>parent.Remove(c),ISequenceTrigger t=>parent.Remove(t),ISequenceItem i=>parent.Remove(i),_=>false };
        if(!removed)throw new ArgumentException("Element is no longer attached to its parent.");
    }
    private static void Attach(object item,SequenceContainer parent,int index) {
        switch(item) { case ISequenceCondition c:parent.InsertIntoSequenceBlocks(index,c);break;case ISequenceTrigger t:parent.InsertIntoSequenceBlocks(index,t);break;case ISequenceItem i:parent.InsertIntoSequenceBlocks(index,i);break;default:throw new ArgumentException("Unsupported sequence entity."); }
    }
    public static void Validate(object item,string action,object destination,int index,int projectedCount) {
        if(!Mutable(item))throw new ArgumentException("This structural element or immutable child cannot be changed.");
        var source=(SequenceContainer)Parent(item);
        if(Index(item,source)<0)throw new ArgumentException("Element is no longer attached to its parent.");
        if(action=="delete")return;
        var metrics=Measure(item);
        if(action=="duplicate") {
            if(projectedCount+metrics.Count>240)throw new ArgumentException("Duplicating this subtree would exceed the editing size limit.");
            if(AdvancedAPI.Controls.Sequence.GetAllTargetsInAdvancedSequence().Count+metrics.Targets>32)throw new ArgumentException("Target limit reached.");
            destination=source;index=Index(item,source)+1;
        }
        if(destination is not SequenceContainer parent)throw new ArgumentException("Destination container unavailable.");
        bool Contains(object node, object target, HashSet<object> seen) {
            if (ReferenceEquals(node, target)) return true;
            if (!seen.Add(node) || seen.Count > 256) throw new ArgumentException("Invalid or oversized sequence subtree.");
            return Children(node).Any(child => Contains(child, target, seen));
        }
        if (Contains(item, parent, new(ReferenceEqualityComparer.Instance))) throw new ArgumentException("Cannot move a trigger or container inside its own actions.");
        var kind=Kind(item);var kinds=SequenceInsertion.InsertKinds(parent);
        if(!kinds.Contains(kind)&&!(kinds.Contains("target")&&item is DeepSkyObjectContainer))throw new ArgumentException("Element cannot be attached at this position.");
        var count=kind=="condition"?parent.Conditions.Count:kind=="trigger"?parent.Triggers.Count:parent.Items.Count;
        if(index<0||index>count)throw new ArgumentException("Insertion index is outside the attachment collection.");
        if(item is ISequenceCondition condition&&!condition.AllowMultiplePerSet&&parent.Conditions.Any(c=>c.GetType()==item.GetType()&&(action=="duplicate"||!ReferenceEquals(c,item))))throw new ArgumentException("This condition can occur only once per set.");
        if(item is ISequenceTrigger trigger&&!trigger.AllowMultiplePerSet&&parent.Triggers.Any(t=>t.GetType()==item.GetType()&&(action=="duplicate"||!ReferenceEquals(t,item))))throw new ArgumentException("This trigger can occur only once per set.");
        var depth=0;for(ISequenceContainer p=parent;p!=null;p=p.Parent) {
            if(ReferenceEquals(p,item))throw new ArgumentException("A container cannot be moved into itself or its descendants.");
            if(++depth>16)throw new ArgumentException("Destination hierarchy exceeds the editing depth bound.");
        }
        if(depth+metrics.Depth>16)throw new ArgumentException("Move would exceed the sequence depth bound.");
    }
    public static void Apply(object item,string action,object destination,int index,int projectedCount) {
        Validate(item,action,destination,index,projectedCount);
        var source=(SequenceContainer)Parent(item);var oldIndex=Index(item,source);
        if(action=="delete") { Remove(item,source);return; }
        if(action=="duplicate") {
            var clone=((ISequenceEntity)item).Clone()??throw new InvalidOperationException("The host could not clone this element.");
            // Verify plugin clone contracts before attaching; a plugin must not return its original template.
            if(ReferenceEquals(clone,item)||clone is not ISequenceEntity||Parent(clone)!=null)throw new InvalidOperationException("Plugin clone was not a detached sequence element.");
            var cloneMetrics=Measure(clone);if(projectedCount+cloneMetrics.Count>240)throw new ArgumentException("Cloned subtree exceeds the editing size limit.");Attach(clone,source,oldIndex+1);return;
        }
        var parent=(SequenceContainer)destination;
        if(ReferenceEquals(source,parent)&&index>oldIndex)index--;
        if(ReferenceEquals(source,parent)&&index==oldIndex)return;
        Remove(item,source);
        try { Attach(item,parent,index); }
        catch { if(Parent(item) is SequenceContainer partial)Remove(item,partial);Attach(item,source,oldIndex);throw; }
    }
}
