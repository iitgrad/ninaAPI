using System;
using System.Linq;
using System.Collections;
using System.Text.Json;
using NINA.Sequencer.Container;
using StarfrontControl.Core;

namespace ninaAPI.Control;

internal static class SequenceEditSchema
{
    public static SequenceEditField[] Fields(object item)
    {
        if (item is ninaAPI.SequenceItems.SendNotificationInstruction notice) return [
            new("Message", "Message", "string", notice.Message, MaxLength: SequenceNotificationOutbox.MessageLimit),
            new("AttachPreview", "Attach photo", "boolean", notice.AttachPreview)
        ];
        if (item.GetType().FullName == "WhenPlugin.When.DIYMeridianFlipTrigger" && item.GetType().Assembly.GetName().Name == "WhenPlugin") return [
            new("MinutesAfterMeridian", "Flip after meridian", "number", item.GetType().GetProperty("MinutesAfterMeridian").GetValue(item), 0, 120, "min"),
            new("PauseTimeBeforeMeridian", "Pause before meridian", "number", item.GetType().GetProperty("PauseTimeBeforeMeridian").GetValue(item), 0, 120, "min"),
            new("MaxMinutesAfterMeridian", "Latest flip after meridian", "number", item.GetType().GetProperty("MaxMinutesAfterMeridian").GetValue(item), 0, 120, "min")
        ];
        if (item.GetType().FullName == "WhenPlugin.When.WaitForTimeSpan" && item.GetType().Assembly.GetName().Name == "WhenPlugin") {
            var expression = item.GetType().GetProperty("WaitExpr").GetValue(item);
            return [new("WaitExpression", "Wait (seconds or expression)", "string", expression?.GetType().GetProperty("Expression")?.GetValue(expression) ?? "", MaxLength: 96)];
        }
        // Third-party instructions may expose matching names with different semantics.
        if (item.GetType().Assembly.GetName().Name != "NINA.Sequencer") return [];
        var fields = SequenceEditing.NumericFields(item).Where(x => x.Key != "ExposureCount" && (x.Key != "Iterations" || item.GetType().Name == "LoopCondition")).ToList();
        if (item.GetType().Name is "TakeManyExposures" or "SmartExposure") {
            var exposure = item.GetType().GetMethod("GetTakeExposure").Invoke(item, null);
            fields.AddRange(Fields(exposure));
            var loop = item.GetType().GetMethod("GetLoopCondition").Invoke(item, null);
            fields.Add(new("ExposureCount", "Exposures", "integer", loop.GetType().GetProperty("Iterations").GetValue(loop), 1, 100));
            if (item.GetType().Name == "SmartExposure") fields.AddRange(Fields(item.GetType().GetMethod("GetSwitchFilter").Invoke(item, null)));
        }
        if (item.GetType() == typeof(SequentialContainer) || item.GetType() == typeof(ParallelContainer)) fields.Add(new("Name", "Group name", "string", item.GetType().GetProperty("Name")?.GetValue(item)));
        if (item.GetType().Name is "TimeCondition" or "WaitForTime") {
            var providers = ((IEnumerable)item.GetType().GetProperty("DateTimeProviders").GetValue(item)).Cast<object>().Take(32).ToArray();
            var selected = item.GetType().GetProperty("SelectedProvider").GetValue(item);
            fields.Add(new("TimeReference", "Time reference", "choice", Array.IndexOf(providers, selected), 0, 31,
                Choices: providers.Select((p, index) => new SequenceEditChoice(index, p.GetType().GetProperty("Name")?.GetValue(p)?.ToString() ?? p.GetType().Name)).ToArray()));
            if (selected?.GetType().Name == "TimeProvider") {
                fields.Add(new("Hours", "Hour", "integer", item.GetType().GetProperty("Hours").GetValue(item), 0, 23));
                fields.Add(new("Minutes", "Minute", "integer", item.GetType().GetProperty("Minutes").GetValue(item), 0, 59));
                fields.Add(new("Seconds", "Second", "integer", item.GetType().GetProperty("Seconds").GetValue(item), 0, 59));
            } else fields.Add(new("MinutesOffset", "Offset", "integer", item.GetType().GetProperty("MinutesOffset").GetValue(item), -1440, 1440, "min"));
        }
        if (item.GetType().Name is "CoolCamera" or "WarmCamera") {
            if (item.GetType().Name == "CoolCamera") fields.Add(new("Temperature", "Temperature", "number", item.GetType().GetProperty("Temperature")?.GetValue(item), -80, 50, "°C"));
            fields.Add(new("Duration", "Ramp duration", "number", item.GetType().GetProperty("Duration")?.GetValue(item), 0, 60, "min"));
        }
        if (item.GetType().Name == "DewHeater") fields.Add(new("OnOff", "Heater enabled", "boolean", item.GetType().GetProperty("OnOff")?.GetValue(item)));
        if (item.GetType().Name == "SetUSBLimit") fields.Add(new("USBLimit", "USB limit", "integer", item.GetType().GetProperty("USBLimit")?.GetValue(item), 0, 100));
        if (item.GetType().Name == "SetReadoutMode") {
            var modes = AdvancedAPI.Controls.Camera.GetInfo().ReadoutModes;
            var choices = modes?.Take(32).Select((x, index) => new SequenceEditChoice(index, x)).ToArray() ?? [];
            fields.Add(new("Mode", "Readout mode", "choice", item.GetType().GetProperty("Mode")?.GetValue(item), 0, 31, Choices: choices));
        }
        if (item.GetType().Name == "SwitchFilter") {
            var choices = AdvancedAPI.Controls.Profile.ActiveProfile.FilterWheelSettings.FilterWheelFilters.Take(32)
                .Select(x => new SequenceEditChoice(x.Position, x.Name)).ToArray();
            var filter = item.GetType().GetProperty("Filter")?.GetValue(item);
            fields.Add(new("Filter", "Filter", "choice", filter?.GetType().GetProperty("Position")?.GetValue(filter), 0, 31, Choices: choices));
        }
        if (item is DeepSkyObjectContainer target) {
            var coordinates = target.Target.InputCoordinates.Coordinates;
            fields.Add(new("TargetName", "Target", "string", target.Target.TargetName));
            fields.Add(new("RightAscension", "Right ascension", "number", coordinates.RA, 0, 24, "h"));
            fields.Add(new("Declination", "Declination", "number", coordinates.Dec, -90, 90, "deg"));
            fields.Add(new("Rotation", "Rotation", "number", target.Target.PositionAngle, 0, 360, "deg"));
        }
        foreach(var key in new[]{"TrackingMode","Equipment"}) {
            var property=item.GetType().GetProperty(key);
            if(property?.CanWrite!=true || !property.PropertyType.IsEnum || fields.Any(f=>f.Key==key))continue;
            var choices=Enum.GetValues(property.PropertyType).Cast<object>().Select(v=>new SequenceEditChoice(Convert.ToInt32(v),v.ToString())).ToArray();
            fields.Add(new(key,key,"choice",Convert.ToInt32(property.GetValue(item)),choices.Min(c=>c.Value),choices.Max(c=>c.Value),Choices:choices));
        }
        return fields.ToArray();
    }
    public static SequenceEditField Field(object item, string key) => Fields(item).FirstOrDefault(x => x.Key == key)
        ?? throw new ArgumentException("This instruction field is not editable through the API.");
    public static void Apply(object item, string key, JsonElement value)
    {
        var field = Field(item, key); SequenceEditing.Validate(field, value);
        if (key == "WaitExpression") {
            var expression = item.GetType().GetProperty("WaitExpr").GetValue(item);
            expression.GetType().GetProperty("Expression").SetValue(expression, value.GetString());
            return;
        }
        if (item.GetType().FullName == "WhenPlugin.When.DIYMeridianFlipTrigger") {
            var earliest = key == "MinutesAfterMeridian" ? value.GetDouble() : (double)item.GetType().GetProperty("MinutesAfterMeridian").GetValue(item);
            var latest = key == "MaxMinutesAfterMeridian" ? value.GetDouble() : (double)item.GetType().GetProperty("MaxMinutesAfterMeridian").GetValue(item);
            if (latest < earliest) throw new ArgumentException("Latest flip time must not precede the preferred flip time.");
        }
        if (item.GetType().Name is "TakeManyExposures" or "SmartExposure" && key != "Name") {
            if (key == "ExposureCount") { Apply(item.GetType().GetMethod("GetLoopCondition").Invoke(item, null), "Iterations", value); return; }
            var method = key == "Filter" ? "GetSwitchFilter" : "GetTakeExposure";
            Apply(item.GetType().GetMethod(method).Invoke(item, null), key, value); return;
        }
        if (key == "TimeReference") {
            var providers = ((IEnumerable)item.GetType().GetProperty("DateTimeProviders").GetValue(item)).Cast<object>().Take(32).ToArray();
            item.GetType().GetProperty("SelectedProvider").SetValue(item, providers[value.GetInt32()]);
        }
        else if (key == "Filter") {
            var selected = AdvancedAPI.Controls.Profile.ActiveProfile.FilterWheelSettings.FilterWheelFilters.Single(x => x.Position == value.GetInt32());
            item.GetType().GetProperty("Filter").SetValue(item, selected);
        }
        else if (item is DeepSkyObjectContainer target && key is "TargetName" or "RightAscension" or "Declination" or "Rotation") {
            if (key == "TargetName") { target.Name = value.GetString(); target.Target.TargetName = value.GetString(); }
            else if (key == "Rotation") target.Target.PositionAngle = value.GetDouble();
            else {
                var coordinates = target.Target.InputCoordinates.Coordinates;
                target.Target.InputCoordinates.Coordinates = new NINA.Astrometry.Coordinates(
                    NINA.Astrometry.Angle.ByDegree((key == "RightAscension" ? value.GetDouble() : coordinates.RA) * 15),
                    NINA.Astrometry.Angle.ByDegree(key == "Declination" ? value.GetDouble() : coordinates.Dec), NINA.Astrometry.Epoch.J2000);
            }
        }
        else if(item.GetType().GetProperty(key)?.PropertyType.IsEnum==true) item.GetType().GetProperty(key).SetValue(item,Enum.ToObject(item.GetType().GetProperty(key).PropertyType,value.GetInt32()));
        else if (field.Type == "boolean") item.GetType().GetProperty(key).SetValue(item, value.GetBoolean());
        else if (field.Type == "string") item.GetType().GetProperty(key).SetValue(item, value.GetString());
        else SequenceEditing.SetNumeric(item, field, value);
        // Setters can normalize a value; telemetry always reports the actual readback.
    }
}
