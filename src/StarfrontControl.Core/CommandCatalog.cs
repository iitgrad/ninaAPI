using System.Text.Json;
namespace StarfrontControl.Core;

public sealed record CommandParameter(string Name, string Type, bool Required = false, double? Minimum = null, double? Maximum = null);
public sealed record CommandDefinition(string Device, string Action, bool Interrupt, CommandParameter[] Parameters, bool ExternalSequenceStop = false, bool ExternalMountStop = false);
public static class CommandCatalog
{
    private static CommandParameter N(string name, double min, double max, bool required = true) => new(name, "number", required, min, max);
    private static CommandParameter I(string name, double min, double max, bool required = true) => new(name, "integer", required, min, max);
    private static CommandParameter B(string name, bool required = true) => new(name, "boolean", required);
    private static CommandParameter S(string name, bool required = true) => new(name, "string", required);
    private static CommandDefinition D(string device, string action, params CommandParameter[] fields) => new(device, action, action is "abort" or "stop", fields);
    public static readonly string[] Equipment = ["camera", "mount", "focuser", "filterWheel", "rotator", "guider", "dome", "flatDevice", "switch", "weather", "safetyMonitor"];
    public static readonly CommandDefinition[] Definitions = [
        ..Equipment.SelectMany(device => new[] { D(device,"connect"), D(device,"disconnect") }),
        D("safetyMonitor","ensureConnected"),
        D("camera","capture",N("exposure",0.001,3600),I("count",1,100,false),I("bin",1,4,false),I("gain",0,10000,false),I("offset",0,10000,false),B("save",false),S("imageType",false),S("target",false)),
        new("camera","cancelTemperature",true,[]),D("camera","abort"),D("camera","cool",N("temperature",-80,50),N("rampSeconds",0,3600,false)),D("camera","warm",N("rampSeconds",0,3600,false)),
        D("camera","binning",I("x",1,4),I("y",1,4)),D("camera","readout",I("mode",0,32)),D("camera","dewHeater",B("enabled")),D("camera","usbLimit",N("value",0,100)),
        D("mount","move",I("axis",0,1),N("rate",-5,5),N("seconds",0.1,10)),D("mount","home"),D("mount","tracking",B("enabled")),D("mount","park"),D("mount","unpark"),D("mount","slew",N("ra",0,24),N("dec",-90,90)),D("mount","sync",N("ra",0,24),N("dec",-90,90)),D("mount","flip",N("ra",0,24),N("dec",-90,90)),new("mount","stop",true,[], ExternalMountStop: true),
        D("focuser","move",I("position",0,1000000)),D("focuser","stop"),D("focuser","autofocus"),
        D("filterWheel","select",I("position",0,31)),
        D("rotator","sync",N("angle",0,360)),D("rotator","move",N("angle",0,360)),D("rotator","moveMechanical",N("angle",0,360)),D("rotator","stop"),
        D("guider","start",B("forceCalibration",false)),D("guider","stop"),D("guider","clearCalibration"),
        D("dome","open"),D("dome","close"),D("dome","park"),D("dome","home"),D("dome","slew",N("azimuth",0,360)),D("dome","follow",B("enabled")),D("dome","stop"),
        D("flatDevice","light",B("enabled")),D("flatDevice","cover",B("closed")),D("flatDevice","brightness",N("value",0,10000)),
        D("switch","set",I("index",0,31),N("value",-1000000,1000000)),
        D("calibration","autoExposureFlats",I("count",1,100),N("minExposure",0.001,3600),N("maxExposure",0.001,3600),I("brightness",0,10000),N("targetMean",0.01,0.99),N("tolerance",0.001,0.2)),D("calibration","stop"),
        D("sequence","resolveTemplate",S("sequenceId"),I("expectedRevision",1,int.MaxValue),S("nodeId")),D("sequence","duplicate",S("sequenceId"),I("expectedRevision",1,int.MaxValue),S("nodeId")),D("sequence","delete",S("sequenceId"),I("expectedRevision",1,int.MaxValue),S("nodeId")),D("sequence","move",S("sequenceId"),I("expectedRevision",1,int.MaxValue),S("nodeId"),S("parentId"),I("index",0,255)),
        D("sequence","save",S("sequenceId"),I("expectedRevision",1,int.MaxValue),S("fileName"),B("replace",false)),
        D("sequence","insertDraft",S("sequenceId"),I("expectedRevision",1,int.MaxValue),S("executionId"),S("parentId"),S("afterNodeId"),new CommandParameter("draft","object",true)),
        D("sequence","insert",S("sequenceId"),I("expectedRevision",1,int.MaxValue),S("parentId"),I("index",0,255),S("type")),D("sequence","start",B("skipValidation",false)),new("sequence","stop",true,[], ExternalSequenceStop: true),new("sequence","skipCurrent",true,[]),D("sequence","reset"),D("sequence","load",S("fileName")),D("sequence","skip",S("nodeId")),D("sequence","edit",S("nodeId"),S("field"),N("value",-1,1000000),S("sequenceId",false),I("expectedRevision",1,int.MaxValue,false)),D("sequence","editField",S("nodeId"),S("field"),new CommandParameter("value","scalar",true),S("sequenceId"),I("expectedRevision",1,int.MaxValue)),D("sequence","addTarget",S("name"),N("ra",0,24),N("dec",-90,90),N("rotation",0,360),S("sequenceId",false),I("expectedRevision",1,int.MaxValue,false)),D("sequence","target",I("index",0,63),S("name"),N("ra",0,24),N("dec",-90,90),N("rotation",0,360),S("sequenceId",false),I("expectedRevision",1,int.MaxValue,false)),
        D("application","logs"),D("application","refresh"),D("application","screenshot"),D("application","selectProfile",S("profileId")),D("image","solve",S("imageId"),N("ra",0,24,false),N("dec",-90,90,false),N("focalLength",1,10000,false))
    ];
    public static CommandDefinition Validate(ControlCommand command)
    {
        var definition = Definitions.FirstOrDefault(x => x.Device == command.Device && x.Action == command.Action) ?? throw new ArgumentException("Unsupported device/action.");
        var parameters = command.Parameters ?? new Dictionary<string, JsonElement>();
        foreach (var key in parameters.Keys) if (!definition.Parameters.Any(x => x.Name == key)) throw new ArgumentException("Unknown parameter: " + key);
        foreach (var field in definition.Parameters)
        {
            if (!parameters.TryGetValue(field.Name, out var value)) { if (field.Required) throw new ArgumentException("Missing parameter: " + field.Name); continue; }
            if (field.Type is "number" or "integer" && (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var n) || !double.IsFinite(n) || n < field.Minimum || n > field.Maximum)) throw new ArgumentException("Invalid numeric parameter: " + field.Name);
            if (field.Type == "integer" && !value.TryGetInt32(out _)) throw new ArgumentException("Integer parameter required: " + field.Name);
            if (field.Type == "boolean" && value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new ArgumentException("Invalid boolean parameter: " + field.Name);
            if (field.Type == "scalar" && (value.ValueKind is not (JsonValueKind.Number or JsonValueKind.String or JsonValueKind.True or JsonValueKind.False) || value.ValueKind == JsonValueKind.String && value.GetString()!.Length > 512 || value.ValueKind == JsonValueKind.Number && (!value.TryGetDouble(out var scalar) || !double.IsFinite(scalar)))) throw new ArgumentException("Invalid scalar parameter: " + field.Name);
            if (field.Type == "object" && value.ValueKind != JsonValueKind.Object) throw new ArgumentException("Object parameter required: " + field.Name);
            if (field.Type == "string" && (value.ValueKind != JsonValueKind.String || value.GetString()!.Length > 128)) throw new ArgumentException("Invalid string parameter: " + field.Name);
        }
        return definition;
    }
    public static string ParametersKey(IReadOnlyDictionary<string, JsonElement>? parameters) => JsonSerializer.Serialize(new SortedDictionary<string, JsonElement>(parameters?.ToDictionary(x => x.Key, x => x.Value) ?? new()));
    public static bool Same(ControlOperation existing, ControlCommand command) => existing.Device == command.Device && existing.Action == command.Action && existing.DeviceId == command.DeviceId && ParametersKey(existing.Parameters) == ParametersKey(command.Parameters);
}
