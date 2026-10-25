using Xunit;
using StarfrontControl.Core;
namespace StarfrontControl.Core.Tests;
public class ProfileDeviceSelectionTests
{
    [Theory]
    [InlineData(null, "ASCOM.ASIMount.Telescope", "ASCOM.ASIMount.Telescope")]
    [InlineData(null, "ZWOptical_ZWO ASI6200MM Pro_", "ZWOptical_ZWO ASI6200MM Pro_")]
    [InlineData("No_Device", "camera-profile-id", "camera-profile-id")]
    [InlineData("new-camera", "old-camera", "new-camera")]
    [InlineData(null, "No_Device", null)]
    [InlineData(null, null, null)]
    public void UsesProfileBeforeLazyChooserInitialization(string? chooser, string? profile, string? expected) =>
        Assert.Equal(expected, ProfileDeviceSelection.Resolve(chooser, profile));
}
