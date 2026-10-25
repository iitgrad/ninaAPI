using Xunit;
using StarfrontControl.Core;

namespace StarfrontControl.Core.Tests;

public class SequenceStepDetailsTests
{
    [Fact] public void ConnectionUsesTheInstructionSelection()
    {
        Assert.Equal("Camera", SequenceStepDetails.Project(new ConnectEquipment { SelectedDevice = "Camera" })["equipment"]);
        Assert.Equal("Filter Wheel", SequenceStepDetails.Project(new DisconnectEquipment { SelectedDevice = "Filter Wheel" })["equipment"]);
    }
    [Theory] [InlineData("L")] [InlineData("Ha")]
    public void FilterUsesConfiguredName(string name) =>
        Assert.Equal(name, SequenceStepDetails.Project(new SwitchFilter { Filter = new FilterInfo { Name = name } })["filter"]);
    [Fact] public void MissingSelectionIsNotInvented()
    {
        Assert.False(SequenceStepDetails.Project(new SwitchFilter()).ContainsKey("filter"));
        Assert.False(SequenceStepDetails.Project(new ConnectEquipment { SelectedDevice = " " }).ContainsKey("equipment"));
        Assert.False(SequenceStepDetails.Project(new OtherInstruction()).ContainsKey("equipment"));
    }
    [Fact] public void ExistingNumericDetailsAndStringBudgetArePreserved()
    {
        var details = SequenceStepDetails.Project(new ConnectEquipment { SelectedDevice = new string('x', 200), ExposureTime = 300 });
        Assert.Equal(96, ((string)details["equipment"]).Length);
        Assert.Equal(300d, details["exposureTime"]);
    }
    [Fact] public void CoolingExpressionsAreReadAsTextWithoutEvaluation() {
        var result=SequenceStepDetails.Project(new CoolCamera());
        Assert.Equal("FocuserTemp-10",result["temperatureExpression"]);
        Assert.Equal("5",result["durationExpression"]);
    }
    [Fact] public void TrackingAndCalibrationSettingsAreTyped() {
        Assert.Equal("SIDEREAL",SequenceStepDetails.Project(new SetTracking())["trackingMode"]);
        Assert.Equal(false,SequenceStepDetails.Project(new StartGuiding())["forceCalibration"]);
    }
    [Fact] public void OptionalFieldMetadataOmitsNullsToPreserveTelemetryBudget() {
        var json=System.Text.Json.JsonSerializer.Serialize(new SequenceEditField("Gain","Gain","integer",100));
        Assert.DoesNotContain("Minimum",json);Assert.DoesNotContain("Choices",json);Assert.Contains("100",json);
    }
    [Fact] public void TimeConditionReportsNativeRolloverAndRemainingTime() {
        var d=SequenceStepDetails.Project(new TimeCondition());
        Assert.Equal("12:00:00",d["rolloverTime"]);Assert.Equal(3723d,d["remainingSeconds"]);
    }
    public class TimeCondition {public TimeSpan RemainingTime=>TimeSpan.FromSeconds(3723);public TimeOnly RolloverTime=>new(12,0);}
    public class Expr { public string Expression {get;set;}=""; public object Value=>throw new InvalidOperationException("Must not evaluate"); }
    public class CoolCamera { public Expr TempExpr {get;}=new(){Expression="FocuserTemp-10"}; public Expr DurExpr {get;}=new(){Expression="5"}; }
    public enum Tracking {SIDEREAL}
    public class SetTracking { public Tracking TrackingMode=>Tracking.SIDEREAL; }
    public class StartGuiding { public bool ForceCalibration=>false; }
    public class ConnectEquipment { public string? SelectedDevice { get; set; } public double ExposureTime { get; set; } }
    public class DisconnectEquipment { public string? SelectedDevice { get; set; } }
    public class SwitchFilter { public FilterInfo? Filter { get; set; } }
    public class FilterInfo { public string? Name { get; set; } }
    public class OtherInstruction { public string SelectedDevice => "Not relevant"; }
}
