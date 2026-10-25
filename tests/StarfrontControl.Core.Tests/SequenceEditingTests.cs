using System.Text.Json;
using StarfrontControl.Core;
using Xunit;

namespace StarfrontControl.Core.Tests;
public class SequenceEditingTests
{
    [Fact] public void NotificationTextUsesItsOwnBoundAndPhotoIsTyped() {
        var field = new SequenceEditField("Message", "Message", "string", "", MaxLength: 512);
        SequenceEditing.Validate(field, JsonSerializer.SerializeToElement(new string('x', 512)));
        Assert.Throws<ArgumentException>(() => SequenceEditing.Validate(field, JsonSerializer.SerializeToElement(new string('x', 513))));
        Assert.Throws<ArgumentException>(() => SequenceEditing.Validate(field, JsonSerializer.SerializeToElement(" ")));
        SequenceEditing.Validate(new("AttachPreview", "Attach photo", "boolean", false), JsonSerializer.SerializeToElement(true));
        Assert.Throws<ArgumentException>(() => SequenceEditing.Validate(new("AttachPreview", "Attach photo", "boolean", false), JsonSerializer.SerializeToElement("true")));
    }
    private static JsonElement J(object? value) => JsonSerializer.SerializeToElement(value);
    [Fact] public void MetadataExcludesReadOnlyAndUnrelatedProperties() {
        var fields = SequenceEditing.NumericFields(new Instruction());
        Assert.Equal(new[] { "ExposureTime", "ExposureCount", "Gain" }, fields.Select(x => x.Key));
        Assert.Equal("s", fields[0].Unit); Assert.Equal("integer", fields[1].Type);
    }
    [Theory] [InlineData(0)] [InlineData(101)] [InlineData(1.5)]
    public void CountRejectsInvalidValues(double count) {
        var item = new Instruction(); var f = SequenceEditing.NumericFields(item).Single(x => x.Key == "ExposureCount");
        Assert.Throws<ArgumentException>(() => SequenceEditing.SetNumeric(item, f, J(count))); Assert.Equal(3, item.ExposureCount);
    }
    [Fact] public void ValidChangesUseTypedSetters() {
        var item = new Instruction(); var fields = SequenceEditing.NumericFields(item);
        SequenceEditing.SetNumeric(item, fields[0], J(1.25)); SequenceEditing.SetNumeric(item, fields[1], J(4));
        Assert.Equal(1.25, item.ExposureTime); Assert.Equal(4, item.ExposureCount);
    }
    [Theory] [InlineData("")] [InlineData(" ")]
    public void TargetNamesMustBeMeaningful(string name) => Assert.Throws<ArgumentException>(() => SequenceEditing.Validate(new("TargetName", "Target", "string", "old"), J(name)));
    [Fact] public void NamesHaveABoundAndNumericFieldsRejectStrings() {
        Assert.Throws<ArgumentException>(() => SequenceEditing.Validate(new("TargetName", "Target", "string", "old"), J(new string('x', 97))));
        Assert.Throws<ArgumentException>(() => SequenceEditing.Validate(new("Gain", "Gain", "integer", 1, -1, 10000), J("5")));
    }
    [Fact] public void FilterChoicesMustBeConfigured() {
        var f = new SequenceEditField("Filter", "Filter", "choice", 0, 0, 31, Choices: [new(0, "L"), new(2, "Ha")]);
        SequenceEditing.Validate(f, J(2)); Assert.Throws<ArgumentException>(() => SequenceEditing.Validate(f, J(1)));
    }
    [Fact] public void RepeatedProjectionDoesNotChangeConfigurationRevision() {
        var v = new SequenceConfigurationVersion(); v.Observe("root", new { exposure = 3 }); v.Observe("root", new { exposure = 3 });
        Assert.Equal(1, v.Revision); v.RequireCurrent("root", 1);
    }
    [Fact] public void EditAndReloadInvalidatePreviousCommands() {
        var v = new SequenceConfigurationVersion(); v.Observe("root", new { exposure = 3 }); v.Observe("root", new { exposure = 4 });
        Assert.Equal(2, v.Revision); Assert.Throws<CommandConflictException>(() => v.RequireCurrent("root", 1));
        v.Observe("new-root", new { exposure = 4 }); Assert.Equal(1, v.Revision);
        Assert.Throws<CommandConflictException>(() => v.RequireCurrent("root", 1));
    }
    [Fact] public void CommandRequiresConfigurationIdentityAndAcceptsTypedScalar() {
        ControlCommand C(object value) => new(Guid.NewGuid().ToString(), "sequence", "editField", Parameters: new Dictionary<string,JsonElement> {
            ["nodeId"]=J("node"), ["field"]=J("TargetName"), ["value"]=J(value), ["sequenceId"]=J("sequence"), ["expectedRevision"]=J(1) });
        CommandCatalog.Validate(C("M31")); CommandCatalog.Validate(C(1.5));
        CommandCatalog.Validate(C(true)); Assert.Throws<ArgumentException>(() => CommandCatalog.Validate(C(new { nested = 1 })));
        var command=C("M31"); var p=command.Parameters!.ToDictionary(); p.Remove("expectedRevision");
        Assert.Throws<ArgumentException>(() => CommandCatalog.Validate(command with { Parameters=p }));
    }
    [Fact] public void LegacyCatalogRemainsCompatibleWithOlderHosts() {
        var parameters=new Dictionary<string,JsonElement>{["nodeId"]=J("node"),["field"]=J("Gain"),["value"]=J(10)};
        CommandCatalog.Validate(new(Guid.NewGuid().ToString(),"sequence","edit",Parameters:parameters));
    }
    [Fact] public void BooleansAreTypedAndNotNumeric() {
        var field=new SequenceEditField("OnOff","Heater","boolean",false);
        SequenceEditing.Validate(field,J(true));
        Assert.Throws<ArgumentException>(()=>SequenceEditing.Validate(field,J(1)));
        Assert.Throws<ArgumentException>(()=>SequenceEditing.Validate(field,J("true")));
    }
    [Theory] [InlineData("duplicate")] [InlineData("delete")] [InlineData("move")] [InlineData("resolveTemplate")]
    public void StructuralCommandsRequireCurrentConfiguration(string action) {
        var parameters=new Dictionary<string,JsonElement>{["nodeId"]=J("node"),["sequenceId"]=J("root"),["expectedRevision"]=J(1)};
        if(action=="move") { parameters["parentId"]=J("parent");parameters["index"]=J(0); }
        var command=new ControlCommand(Guid.NewGuid().ToString(),"sequence",action,Parameters:parameters);
        CommandCatalog.Validate(command);parameters.Remove("expectedRevision");
        Assert.Throws<ArgumentException>(()=>CommandCatalog.Validate(command));
    }
    public class Instruction {
        public double ExposureTime {get;set;}=3; public int ExposureCount {get;set;}=3;
        public int Gain {get;set;}=-1; public double Time => 5; public string Dangerous {get;set;}="";
    }
}
