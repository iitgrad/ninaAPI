using System;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Core.Utility;
using NINA.Sequencer.Conditions;
using NINA.Sequencer.Container;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Trigger;
using StarfrontControl.Core;

namespace ninaAPI.SequenceItems;

[ExportMetadata("Name", "Wait Until Safe or Deadline")]
[ExportMetadata("Description", "Wait for the safety monitor. Run the enclosed recovery only if conditions become safe before the deadline.")]
[ExportMetadata("Icon", "SequentialSVG")]
[ExportMetadata("Category", "Starfront Control")]
[Export(typeof(ISequenceItem))]
[Export(typeof(ISequenceContainer))]
[JsonObject(MemberSerialization.OptIn)]
public sealed class WaitUntilSafeOrDeadline : SequentialContainer
{
    private string deadline = string.Empty;

    [ImportingConstructor]
    public WaitUntilSafeOrDeadline() { }

    [JsonProperty]
    public string Deadline
    {
        get => deadline;
        set
        {
            deadline = value;
            RaisePropertyChanged();
            Validate();
        }
    }

    public override bool Validate()
    {
        bool valid = base.Validate();
        Issues.Clear();
        if (!SafeOrDeadline.TryParseDeadline(Deadline, out _))
            Issues.Add("Enter the imaging cutoff with its UTC offset, for example 2026-11-20T05:45:46-06:00.");
        return valid && Issues.Count == 0;
    }

    public override object Clone()
    {
        var clone = new WaitUntilSafeOrDeadline
        {
            Deadline = Deadline,
            Name = Name,
            Icon = Icon,
            Category = Category,
            Description = Description,
            Items = new ObservableCollection<ISequenceItem>(Items.Select(item => (ISequenceItem)item.Clone())),
            Conditions = new ObservableCollection<ISequenceCondition>(Conditions.Select(condition => (ISequenceCondition)condition.Clone())),
            Triggers = new ObservableCollection<ISequenceTrigger>(Triggers.Select(trigger => (ISequenceTrigger)trigger.Clone())),
        };

        foreach (var item in clone.Items) item.AttachNewParent(clone);
        foreach (var condition in clone.Conditions) condition.AttachNewParent(clone);
        foreach (var trigger in clone.Triggers) trigger.AttachNewParent(clone);
        return clone;
    }

    public override async Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token)
    {
        if (!SafeOrDeadline.TryParseDeadline(Deadline, out var cutoff))
            throw new InvalidOperationException("A deadline with a UTC offset is required.");

        bool IsSafe() => AdvancedAPI.Controls?.SafetyMonitor?.GetInfo() is { Connected: true, IsSafe: true };
        progress.Report(new ApplicationStatus { Status = "Waiting for safe conditions or the imaging cutoff" });

        while (await SafeOrDeadline.WaitAsync(cutoff, IsSafe, token))
        {
            token.ThrowIfCancellationRequested();
            if (DateTimeOffset.UtcNow >= cutoff) break;
            if (!IsSafe()) continue;
            Logger.Info("Safety monitor is safe before the imaging cutoff. Running recovery.");
            await base.Execute(progress, token);
            return;
        }

        Logger.Info("Imaging cutoff reached during the safety wait. Skipping recovery.");
    }
}
