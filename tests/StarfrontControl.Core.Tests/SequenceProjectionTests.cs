using System.Text.Json;
using StarfrontControl.Core;
using Xunit;
namespace StarfrontControl.Core.Tests;
public class SequenceProjectionTests
{
    private static JsonElement Row(string id, string? parent, string state = "pending", int padding = 0) =>
        JsonSerializer.SerializeToElement(new { id, parentId = parent, state, details = new string('x', padding) });
    [Fact] public void RetainsLateCurrentBranchAndAncestorsWithinBothCaps()
    {
        var rows = new List<JsonElement> { Row("root", null) };
        for (var i=0; i<300; i++) rows.Add(Row("old"+i,"root", padding:300));
        rows.Add(Row("target","root","running")); rows.Add(Row("wait","target","waiting"));
        var result = SequenceProjection.Bound(rows, ["wait"], 30, 3000);
        Assert.True(result.Length <= 30);
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(result).Length <= 3000);
        Assert.Equal(new[] { "target", "wait" }, result.TakeLast(2).Select(x => x.GetProperty("id").GetString()));
        var ids = result.Select(x => x.GetProperty("id").GetString()).ToHashSet();
        Assert.All(result.Where(x => x.GetProperty("parentId").ValueKind != JsonValueKind.Null), x => Assert.Contains(x.GetProperty("parentId").GetString(), ids));
    }
    [Fact] public void UnchangedSequencePreservesOrderAndContents() {
        var rows = new[] { Row("root",null), Row("a","root"), Row("b","root") };
        Assert.Equal(rows.Select(x => x.GetRawText()), SequenceProjection.Bound(rows, []).Select(x => x.GetRawText()));
    }
    [Fact] public void OversizedActiveBranchFailsExplicitly() => Assert.Throws<InvalidOperationException>(() =>
        SequenceProjection.Bound([Row("root", null), Row("current","root","running",2000)], ["current"], maxBytes:1000));
    [Fact] public void StopAdvertisesHostOwnedExecution() => Assert.True(CommandCatalog.Definitions.Single(x => x.Device=="sequence" && x.Action=="stop").ExternalSequenceStop);
}
