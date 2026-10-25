using System;
using System.IO;
using System.Reflection;
using NINA.Sequencer.Serialization;
using StarfrontControl.Core;
using ninaAPI.Utility;
namespace ninaAPI.Control;
public sealed partial class ControlAdapter {
    private WeakReference<object> savedSequenceRoot;
    private Guid savedSequenceProfile;
    private string savedSequenceFile;
    private string CurrentSequenceFileName => savedSequenceProfile==AdvancedAPI.Controls.Profile.ActiveProfile.Id && AdvancedAPI.Controls.Sequence.Initialized && savedSequenceRoot?.TryGetTarget(out var root)==true && ReferenceEquals(root,AdvancedAPI.Controls.Sequence.GetSequenceRoot()) ? savedSequenceFile : null;
    private (object Controller,FileSystemWatcher Watcher) TargetsWatcher() {
        var mediator=AdvancedAPI.Controls.Sequence;
        var navigation=mediator.GetType().GetField("sequenceNavigation",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(mediator);
        var view=navigation?.GetType().GetProperty("Sequence2VM")?.GetValue(navigation);
        var controller=view?.GetType().GetProperty("TargetController")?.GetValue(view);
        var watcher=controller?.GetType().GetField("sequenceTargetsFolderWatcher",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(controller) as FileSystemWatcher;
        if(watcher==null)throw new CommandConflictException("The Targets library is still initializing or this N.I.N.A. version is unsupported for dashboard saves.");
        return(controller,watcher);
    }
    private void SaveLoadedSequence(ControlCommand c) {
        sequenceTelemetry.RequireEditable(Text(c,"sequenceId"),Integer(c,"expectedRevision"));
        var(controller,watcher)=TargetsWatcher();
        // NINA 3.2's async Changed handler can concurrently re-bind its watcher.
        // Full sequence JSON is not a DSO template, so its native target list does
        // not change. Suppress only our write events, then restore the same watcher.
        lock(watcher) {
            if(!watcher.EnableRaisingEvents || controller.GetType().GetProperty("TargetsLoading")?.GetValue(controller) is true)
                throw new CommandConflictException("The Targets library is refreshing. Try Save again when it finishes.");
            watcher.EnableRaisingEvents=false;
            try {
                lock(SequenceDraftPositions.Gate) {
                    sequenceTelemetry.RequireEditable(Text(c,"sequenceId"),Integer(c,"expectedRevision"));
                    var root=AdvancedAPI.Controls.Sequence.GetSequenceRoot();
                    var json=new SequenceJsonConverter(SequenceInsertion.Factory()).Serialize(root);
                    SequenceLibrary.SaveTarget(AdvancedAPI.Controls.Profile.ActiveProfile.SequenceSettings.SequencerTargetsFolder,Text(c,"fileName"),json,Boolean(c,"replace"));
                    savedSequenceRoot=new(root);savedSequenceProfile=AdvancedAPI.Controls.Profile.ActiveProfile.Id;savedSequenceFile=Text(c,"fileName");
                }
            } finally { watcher.EnableRaisingEvents=true; }
        }
        PublishAdministration();sequenceTelemetry.Schedule();
    }
}
