// This Source Code Form is subject to the terms of the Mozilla Public License,
// v. 2.0. If a copy was not distributed with this file, obtain one at https://mozilla.org/MPL/2.0/.
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Validations;
using ninaAPI.Control;
using StarfrontControl.Core;

namespace ninaAPI.SequenceItems;

[ExportMetadata("Name", "Send Notification")]
[ExportMetadata("Description", "Queue a native Starfront phone message with an optional latest camera preview.")]
[ExportMetadata("Icon", "ScriptSVG")]
[ExportMetadata("Category", "Starfront Control")]
[Export(typeof(ISequenceItem))]
[JsonObject(MemberSerialization.OptIn)]
public sealed class SendNotificationInstruction : SequenceItem, IValidatable
{
    private string message = "Starfront update";
    private bool attachPreview;
    private IList<string> issues = new List<string>();
    [JsonProperty] public string Message { get => message; set { message = value; RaisePropertyChanged(); } }
    [JsonProperty] public bool AttachPreview { get => attachPreview; set { attachPreview = value; RaisePropertyChanged(); } }
    public IList<string> Issues { get => issues; set { issues = value; RaisePropertyChanged(); } }
    [ImportingConstructor] public SendNotificationInstruction() { }
    public override object Clone() => new SendNotificationInstruction {
        Message = Message, AttachPreview = AttachPreview, Name = Name, Icon = Icon, Category = Category, Description = Description
    };
    public bool Validate()
    {
        var next = new List<string>();
        if (string.IsNullOrWhiteSpace(Message) || Message.Trim().Length > SequenceNotificationOutbox.MessageLimit)
            next.Add("Enter a message of 1–512 characters.");
        if (ControlAdapter.Current is null) next.Add("Starfront Control must be running.");
        Issues = next; return next.Count == 0;
    }
    public override Task Execute(IProgress<ApplicationStatus> progress, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // Only enqueue local telemetry. APNs/network availability never blocks an imaging step.
        ControlAdapter.Current?.SendSequenceNotification(Message, AttachPreview);
        return Task.CompletedTask;
    }
}
