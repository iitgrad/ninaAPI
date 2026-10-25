namespace StarfrontControl.Core;

public static class ProfileDeviceSelection
{
    public static string? Resolve(string? chooserId, string? profileId)
    {
        static bool Valid(string? id) => !string.IsNullOrWhiteSpace(id) && id != "No_Device";
        return Valid(chooserId) ? chooserId : Valid(profileId) ? profileId : null;
    }
}
