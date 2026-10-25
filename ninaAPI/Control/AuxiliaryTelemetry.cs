using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Equipment.MyDome;
using NINA.Equipment.Equipment.MyFlatDevice;
using NINA.Equipment.Equipment.MySwitch;
using System.Linq;
namespace ninaAPI.Control;

public sealed partial class ControlAdapter : IDomeConsumer, IFlatDeviceConsumer, ISwitchConsumer
{
    public void UpdateDeviceInfo(DomeInfo info) => PublishDevice("dome", info, "Connected", "Name", "Azimuth", "Slewing", "ShutterStatus", "AtPark", "AtHome", "CanSetShutter", "CanSetAzimuth", "CanFindHome", "CanPark");
    public void UpdateDeviceInfo(FlatDeviceInfo info) => PublishDevice("flatDevice", info, "Connected", "Name", "CoverState", "LightOn", "Brightness", "MinBrightness", "MaxBrightness", "SupportsOpenClose", "SupportsOnOff");
    public void UpdateDeviceInfo(SwitchInfo info) => Enqueue("switch", new { connected = info.Connected, name = info.Name, items = info.WritableSwitches?.Take(32).Select(x => new { id = x.Id, name = x.Name, value = x.Value, minimum = x.Minimum, maximum = x.Maximum, stepSize = x.StepSize }).ToArray() });
    private void InitializeAuxiliary() { var c = AdvancedAPI.Controls; c.Dome.RegisterConsumer(this); c.FlatDevice.RegisterConsumer(this); c.Switch.RegisterConsumer(this); UpdateDeviceInfo(c.Dome.GetInfo()); UpdateDeviceInfo(c.FlatDevice.GetInfo()); UpdateDeviceInfo(c.Switch.GetInfo()); }
    private void DisposeAuxiliary() { var c = AdvancedAPI.Controls; c.Dome.RemoveConsumer(this); c.FlatDevice.RemoveConsumer(this); c.Switch.RemoveConsumer(this); }
}
