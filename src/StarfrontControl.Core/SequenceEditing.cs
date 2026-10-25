using System.Text.Json.Serialization;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace StarfrontControl.Core;

public sealed record SequenceEditChoice(int Value, string Label);
public sealed record SequenceEditField(string Key, string Label, string Type, object? Value,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] double? Minimum = null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] double? Maximum = null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] string? Unit = null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] SequenceEditChoice[]? Choices = null,
    [property:JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] int? MaxLength = null);

/// <summary>Explicit scalar allowlist. Never serialize host graphs or retain instructions.</summary>
public static class SequenceEditing
{
    private static readonly (string Key, string Label, double Min, double Max, string? Unit)[] Numeric = [
        ("ExposureTime", "Exposure", .001, 3600, "s"), ("ExposureCount", "Exposures", 1, 100, null),
        ("Iterations", "Iterations", 1, 100, null), ("Time", "Wait", 0, 3600, "s"),
        ("AfterExposures", "Every exposures", 1, 100, null), ("Gain", "Gain", -1, 10000, null), ("Offset", "Offset", -1, 10000, null)
    ];
    public static SequenceEditField[] NumericFields(object item) => Numeric.Select(spec => {
        var p = item.GetType().GetProperty(spec.Key);
        return p?.CanWrite == true && (p.PropertyType == typeof(int) || p.PropertyType == typeof(double))
            ? new SequenceEditField(spec.Key, spec.Label, p.PropertyType == typeof(int) ? "integer" : "number", p.GetValue(item), spec.Min, spec.Max, spec.Unit) : null;
    }).OfType<SequenceEditField>().ToArray();

    public static void Validate(SequenceEditField field, JsonElement value)
    {
        if (field.Type == "boolean") {
            if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new ArgumentException("Boolean field required.");
            return;
        }
        if (field.Type == "string") {
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()) || value.GetString()!.Length > (field.MaxLength ?? 96))
                throw new ArgumentException($"Use nonempty text of at most {field.MaxLength ?? 96} characters.");
            return;
        }
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var n) || !double.IsFinite(n) || n < field.Minimum || n > field.Maximum ||
            field.Type is "integer" or "choice" && n != Math.Truncate(n) ||
            field.Choices is not null && !field.Choices.Any(x => x.Value == n))
            throw new ArgumentException("Sequence field value is outside its supported range or choices.");
    }
    public static void SetNumeric(object item, SequenceEditField field, JsonElement value) {
        Validate(field, value);
        var property = item.GetType().GetProperty(field.Key)!;
        property.SetValue(item, Convert.ChangeType(value.GetDouble(), property.PropertyType, CultureInfo.InvariantCulture));
    }
}

/// <summary>One fingerprint, no revision history. Execution status never enters this tracker.</summary>
public sealed class SequenceConfigurationVersion
{
    private string fingerprint = "";
    public string SequenceId { get; private set; } = "";
    public int Revision { get; private set; }
    public void Observe(string sequenceId, object configuration) {
        var next = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(configuration)));
        if (SequenceId != sequenceId) { SequenceId = sequenceId; Revision = 1; fingerprint = next; }
        else if (fingerprint != next) { Revision = checked(Revision + 1); fingerprint = next; }
    }
    public void RequireCurrent(string sequenceId, int expectedRevision) {
        if (SequenceId.Length == 0 || sequenceId != SequenceId || expectedRevision != Revision)
            throw new CommandConflictException("Sequence changed. Refresh the sequence and review your edit.");
    }
}
