using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NINA.Core.Model;
using NINA.Sequencer.SequenceItem;
using ninaAPI.Control;
namespace ninaAPI.SequenceItems;
[ExportMetadata("Name", "Disconnect Other Equipment")]
[ExportMetadata("Description", "Disconnect equipment while preserving the always-connected roof safety monitor.")]
[ExportMetadata("Icon", "DisconnectSVG")]
[ExportMetadata("Category", "Starfront Control")]
[Export(typeof(ISequenceItem))]
[JsonObject(MemberSerialization.OptIn)]
public sealed class DisconnectOtherEquipmentInstruction : SequenceItem
{
    [ImportingConstructor] public DisconnectOtherEquipmentInstruction() { }
    public override object Clone() => new DisconnectOtherEquipmentInstruction { Name=Name,Icon=Icon,Category=Category,Description=Description };
    public override async Task Execute(IProgress<ApplicationStatus> progress,CancellationToken token) {
        token.ThrowIfCancellationRequested();
        var adapter=ControlAdapter.Current ?? throw new InvalidOperationException("Starfront Control is unavailable.");
        await adapter.DisconnectOtherEquipment();
        token.ThrowIfCancellationRequested();
    }
}
