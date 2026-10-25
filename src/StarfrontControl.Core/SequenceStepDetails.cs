using System.Reflection;
using System.Text.Json;

namespace StarfrontControl.Core;

/// <summary>Bounded instruction values only; never serialize host objects or invoke equipment.</summary>
public static class SequenceStepDetails
{
    public static Dictionary<string, object> Project(object item)
    {
        var details = new Dictionary<string, object>();
        foreach (var field in new[] { "ExposureTime", "ExposureCount", "ProgressExposureCount", "Iterations", "CompletedIterations", "Time", "Position", "Gain", "Offset" })
        {
            var value = Read(item, field);
            if (value is int or long || value is double d && double.IsFinite(d))
                details[JsonNamingPolicy.CamelCase.ConvertName(field)] = value;
        }
        switch (item.GetType().Name)
        {
            case "ConnectEquipment":
            case "DisconnectEquipment":
                AddText(details, "equipment", Read(item, "SelectedDevice"));
                break;
            case "CoolCamera":
                AddScalar(details,"temperature",Read(item,"Temperature"));
                AddScalar(details,"duration",Read(item,"Duration"));
                AddText(details,"temperatureExpression",Read(Read(item,"TempExpr"),"Expression"));
                AddText(details,"durationExpression",Read(Read(item,"DurExpr"),"Expression"));
                break;
            case "WarmCamera": AddScalar(details,"duration",Read(item,"Duration")); break;
            case "Center":
            case "CenterAndRotate":
                var coordinates=Read(Read(item,"Coordinates"),"Coordinates");
                AddScalar(details,"rightAscension",Read(coordinates,"RA"));AddScalar(details,"declination",Read(coordinates,"Dec"));
                AddScalar(details,"inherited",Read(item,"Inherited"));
                break;
            case "SetTracking": AddScalar(details,"trackingMode",Read(item,"TrackingMode")); break;
            case "WaitForTime":
            case "TimeCondition":
                foreach(var key in new[]{"Hours","Minutes","Seconds","MinutesOffset"}) AddScalar(details,JsonNamingPolicy.CamelCase.ConvertName(key),Read(item,key));
                var provider=Read(item,"SelectedProvider");
                AddText(details,"timeReference",Read(provider,"Name") ?? provider?.GetType().Name);
                if(provider!=null) details["timeReferenceFixed"]=provider.GetType().Name=="TimeProvider";
                if(Read(item,"RemainingTime") is TimeSpan remaining && double.IsFinite(remaining.TotalSeconds)) details["remainingSeconds"]=Math.Max(0,remaining.TotalSeconds);
                if(Read(item,"RolloverTime") is TimeOnly rollover) details["rolloverTime"]=rollover.ToString("HH:mm:ss");
                break;
            case "TemplateByReference": AddText(details,"templateName",Read(item,"TemplateName")); details["templateLoaded"] = Read(item,"SelectedTemplate") != null; break;
            case "WaitForTimeSpan":
                var waitExpression = Read(Read(item,"WaitExpr"),"Expression");
                if (waitExpression is string) { details.Remove("time"); AddText(details,"waitExpression",waitExpression); }
                break;
            case "DIYMeridianFlipTrigger":
                AddScalar(details,"useSideOfPier",Read(item,"UseSideOfPier"));
                break;
            case "SendNotificationInstruction": AddText(details,"message",Read(item,"Message"),512); AddScalar(details,"attachPreview",Read(item,"AttachPreview")); break;
            case "SendToDiscordWebhook": AddText(details,"message",Read(item,"Message")); break;
            case "StartGuiding": AddScalar(details,"forceCalibration",Read(item,"ForceCalibration")); break;
            case "DewHeater": AddScalar(details,"onOff",Read(item,"OnOff")); break;
            case "SetUSBLimit": AddScalar(details,"usbLimit",Read(item,"USBLimit")); break;
            case "SetReadoutMode": AddScalar(details,"readoutMode",Read(item,"Mode")); break;
            case "TakeExposure":
            case "TakeSubframeExposure":
                AddText(details,"imageType",Read(item,"ImageType"));
                var binning=Read(item,"Binning");
                AddScalar(details,"binX",Read(binning,"X")); AddScalar(details,"binY",Read(binning,"Y"));
                break;
            case "SwitchFilter":
                AddText(details, "filter", Read(Read(item, "Filter"), "Name"));
                break;
        }
        return details;
    }

    private static void AddScalar(Dictionary<string,object> details,string key,object? value)
    {
        if(value is bool or byte or short or ushort or int or uint or long || value is double d && double.IsFinite(d)) details[key]=value;
        else if(value is Enum) AddText(details,key,value.ToString());
    }
    private static object? Read(object? source, string property)
    {
        try { return source?.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance)?.GetValue(source); }
        catch (TargetInvocationException) { return null; }
    }
    private static void AddText(Dictionary<string, object> details, string key, object? value, int limit = 96)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text)) return;
        text = text.Trim();
        details[key] = text[..Math.Min(text.Length, limit)];
    }
}
