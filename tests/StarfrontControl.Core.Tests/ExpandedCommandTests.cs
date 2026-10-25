using System.Text.Json;
using StarfrontControl.Core;
using Xunit;
namespace StarfrontControl.Core.Tests;

public class ExpandedCommandTests
{
    private static Dictionary<string, JsonElement> Parameters(string json) => JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"exposure\":1,\"count\":1.1}")]
    [InlineData("{\"exposure\":0}")]
    [InlineData("{\"exposure\":4000}")]
    [InlineData("{\"exposure\":1,\"save\":\"yes\"}")]
    [InlineData("{\"exposure\":1,\"unknown\":1}")]
    public void RejectsInvalidParameters(string json) => Assert.Throws<ArgumentException>(() => CommandCatalog.Validate(new(Guid.NewGuid().ToString(), "camera", "capture", Parameters: Parameters(json))));
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"skipValidation\":false}")]
    [InlineData("{\"skipValidation\":true}")]
    public void SequenceStartAcceptsExplicitOptionalValidationMode(string json) =>
        CommandCatalog.Validate(new(Guid.NewGuid().ToString(), "sequence", "start", Parameters: Parameters(json)));
    [Theory]
    [InlineData("{\"skipValidation\":\"true\"}")]
    [InlineData("{\"skipValidation\":1}")]
    public void SequenceStartRejectsNonBooleanValidationMode(string json) =>
        Assert.Throws<ArgumentException>(() => CommandCatalog.Validate(new(Guid.NewGuid().ToString(), "sequence", "start", Parameters: Parameters(json))));
    [Fact]
    public void TemperatureCancellationUsesExclusiveInterruptLane()
    {
        var command = new ControlCommand(Guid.NewGuid().ToString(), "camera", "cancelTemperature");
        Assert.True(CommandCatalog.Validate(command).Interrupt);
        Assert.Throws<ArgumentException>(() => CommandCatalog.Validate(command with { Parameters = Parameters("{\"temperature\":-10}") }));
    }
    [Fact]
    public async Task ParameterIdentityIsStableAndProtectedFromCallerMutation()
    {
        var coordinator = new CommandCoordinator(); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var parameters = Parameters("{\"exposure\":1,\"count\":2}");
        var command = new ControlCommand(Guid.NewGuid().ToString(), "camera", "capture", Parameters: parameters);
        coordinator.Submit(command, () => release.Task);
        parameters["exposure"] = JsonSerializer.SerializeToElement(3);
        Assert.Throws<CommandConflictException>(() => coordinator.Submit(command, () => Task.CompletedTask));
        coordinator.Submit(command with { Parameters = Parameters("{\"count\":2,\"exposure\":1}") }, () => throw new Exception("Duplicate"));
        release.SetResult();
        for (var i = 0; i < 100 && coordinator.ActiveCount != 0; i++) await Task.Delay(5);
        Assert.Equal("succeeded", coordinator.Find(command.RequestId)!.Status);
    }
    [Fact]
    public async Task InterruptCanRunAlongsideCaptureButCannotPileUp()
    {
        var coordinator = new CommandCoordinator(); var capture = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var interrupt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        coordinator.Submit(new(Guid.NewGuid().ToString(), "camera", "capture", Parameters: Parameters("{\"exposure\":30}")), () => capture.Task);
        coordinator.Submit(new(Guid.NewGuid().ToString(), "camera", "abort"), () => interrupt.Task);
        Assert.Equal(2, coordinator.ActiveCount);
        Assert.Throws<CommandConflictException>(() => coordinator.Submit(new(Guid.NewGuid().ToString(), "camera", "abort"), () => Task.CompletedTask));
        interrupt.SetResult(); capture.SetCanceled();
        for (var i = 0; i < 100 && coordinator.ActiveCount != 0; i++) await Task.Delay(5);
        Assert.Equal(0, coordinator.ActiveCount); Assert.Contains(coordinator.Read(), x => x.Status == "cancelled");
    }
}
