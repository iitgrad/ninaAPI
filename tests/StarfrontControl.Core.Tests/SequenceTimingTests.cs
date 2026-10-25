using System.Text.Json;
using StarfrontControl.Core;
using Xunit;

public class SequenceTimingTests
{
    private static readonly DateTimeOffset T = DateTimeOffset.Parse("2026-10-25T01:00:00Z");
    private static JsonElement Snapshot(string state, string session="one", string root="root") => JsonSerializer.SerializeToElement(new { sessionId=session, states=new { sequence=new { value=new { sequenceId=root, nodes=new[] {new {id="step",state}} } } } });
    private static JsonElement Node(string json) => JsonDocument.Parse(json).RootElement.GetProperty("states").GetProperty("sequence").GetProperty("value").GetProperty("nodes")[0];
    [Fact] public void RecordsTransitionsAndKeepsStartAcrossWaitingAndDuplicateFrames()
    {
        var s=new SequenceTiming();s.Enrich(Snapshot("pending"),T);
        var n=Node(s.Enrich(Snapshot("running"),T.AddSeconds(2)));Assert.Equal(T.AddSeconds(2),n.GetProperty("timing").GetProperty("startedAt").GetDateTimeOffset());
        s.Enrich(Snapshot("waiting"),T.AddSeconds(3));s.Enrich(Snapshot("waiting"),T.AddSeconds(4));
        n=Node(s.Enrich(Snapshot("completed"),T.AddSeconds(5)));Assert.Equal(T.AddSeconds(2),n.GetProperty("timing").GetProperty("startedAt").GetDateTimeOffset());Assert.Equal(T.AddSeconds(5),n.GetProperty("timing").GetProperty("endedAt").GetDateTimeOffset());
        Assert.False(Node(s.Enrich(Snapshot("pending"),T.AddSeconds(6))).TryGetProperty("timing",out _));
    }
    [Fact] public void DoesNotInventPastTimesOrTransitionsDuringDisconnection()
    {
        var s=new SequenceTiming();Assert.False(Node(s.Enrich(Snapshot("completed"),T)).TryGetProperty("timing",out _));
        var n=Node(s.Enrich(Snapshot("running",root:"new"),T));Assert.Equal(JsonValueKind.Null,n.GetProperty("timing").GetProperty("startedAt").ValueKind);Assert.Equal(T,n.GetProperty("timing").GetProperty("seenActiveAt").GetDateTimeOffset());
        s.Disconnected();n=Node(s.Enrich(Snapshot("completed",root:"new"),T.AddSeconds(10)));Assert.Equal(JsonValueKind.Null,n.GetProperty("timing").GetProperty("endedAt").ValueKind);
        Assert.False(Node(s.Enrich(Snapshot("completed",session:"two"),T)).TryGetProperty("timing",out _));
    }
}
