using System.Text.Json;
using StarfrontControl.Core;
using Xunit;
public sealed class LiveConfigurationTests {
    static IReadOnlyDictionary<string,JsonElement> Parameters(string json)=>JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(json)!;
    [Fact] public void SequenceConfigurationWritesRemainExclusiveWithAcquisition() {
        var commands=new CommandCoordinator();var running=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        commands.Submit(new(Guid.NewGuid().ToString(),"sequence","start"),()=>running.Task);
        var draft=new ControlCommand(Guid.NewGuid().ToString(),"sequence","insertDraft",Parameters:Parameters("""{"sequenceId":"s","expectedRevision":1,"executionId":"run","parentId":"p","afterNodeId":"a","draft":{"type":"WaitForTimeSpan","fields":{"Time":1}}}"""));
        Assert.Throws<CommandConflictException>(()=>commands.Submit(draft,()=>Task.CompletedTask));running.SetResult();
    }
}
