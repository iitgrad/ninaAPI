using Xunit;
namespace StarfrontControl.Core.Tests;
public class SequencePresentationTests
{
    [Theory]
    [InlineData("CREATED", "WaitForTimeSpan", "pending")]
    [InlineData("RUNNING", "WaitForTimeSpan", "waiting")]
    [InlineData("RUNNING", "WaitUntilSafe", "waiting")]
    [InlineData("RUNNING", "TakeExposure", "running")]
    [InlineData("FINISHED", "TakeExposure", "completed")]
    [InlineData("SKIPPED", "TakeExposure", "skipped")]
    [InlineData("DISABLED", "TakeExposure", "disabled")]
    [InlineData("FAILED", "TakeExposure", "failed")]
    [InlineData("NEW_HOST_STATE", "TakeExposure", "unknown")]
    public void CompletionAndWaitsFollowHostStatus(string status, string type, string expected)
        => Assert.Equal(expected, SequencePresentation.State(status, type));
}
