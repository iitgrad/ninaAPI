using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Reflection;
using System.Runtime.InteropServices;
using NINA.Core.Model.Equipment;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Image.Interfaces;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Model.Equipment.MyCamera.Simulator;

[assembly: Guid("f1f5c5f3-fd51-4edc-bc94-490f9b72e879")]
[assembly: AssemblyMetadata("MinimumApplicationVersion", "3.2.0.9001")]
[assembly: AssemblyMetadata("License", "MPL-2.0")]
[assembly: AssemblyMetadata("LicenseURL", "https://www.mozilla.org/en-US/MPL/2.0/")]

namespace StarfrontSimulation;

[Export(typeof(IPluginManifest))]
public sealed class SimulationManifest : PluginBase { }

[Export(typeof(IEquipmentProvider))]
public sealed class SimulationCameraProvider : IEquipmentProvider<ICamera>
{
    private readonly IProfileService profiles;
    private readonly ITelescopeMediator telescope;
    private readonly IExposureDataFactory exposures;
    private readonly IImageDataFactory images;
    [ImportingConstructor]
    public SimulationCameraProvider(IProfileService profiles, ITelescopeMediator telescope, IExposureDataFactory exposures, IImageDataFactory images)
    { this.profiles = profiles; this.telescope = telescope; this.exposures = exposures; this.images = images; }
    public string Name => "Starfront Simulation";
    public IList<ICamera> GetEquipment() => new ICamera[] { new SimulationCamera(profiles, telescope, exposures, images) };
}

/// Reuses NINA's image/directory optics; explicitly reimplements ICamera's thermal members.
/// This driver is a virtual device and must only be installed in the test VM.
public sealed class SimulationCamera : SimulatorCamera, ICamera
{
    private readonly ThermalModel thermal = new();
    public const string DeviceId = "d038dcf9-969f-4bd4-b215-2fd61c182242";
    public SimulationCamera(IProfileService profiles, ITelescopeMediator telescope, IExposureDataFactory exposures, IImageDataFactory images)
        : base(profiles, telescope, exposures, images) { }
    public new string Id => DeviceId;
    public new string Name => "Starfront Thermal Simulator Camera";
    public new string DisplayName => Name;
    public new string Category => "Starfront Simulation";
    public new bool CanSetTemperature => true;
    public new bool CanSetCCDTemperature => true;
    public new double Temperature => thermal.Temperature;
    public new double CCDTemperature => thermal.Temperature;
    public new double TemperatureSetPoint { get => thermal.Target; set => thermal.Target = value; }
    public new double SetCCDTemperature { get => thermal.Target; set => thermal.Target = value; }
    public new bool CoolerOn { get => thermal.Cooling; set => thermal.Cooling = value; }
    public new double CoolerPower => thermal.Power;
}
