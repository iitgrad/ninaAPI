using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.ViewModel;
namespace StarfrontSimulation;
[Export(typeof(IEquipmentProvider))]
public sealed class SimulationSafetyProvider : IEquipmentProvider<ISafetyMonitor> {
    public string Name=>"Starfront Simulation";
    public IList<ISafetyMonitor> GetEquipment()=>new ISafetyMonitor[]{new SimulationSafety()};
}
// Test VM only. A missing/unavailable control file is CLOSED, never Safe.
public sealed class SimulationSafety : ISafetyMonitor {
    public const string DeviceId="e12b8a60-266d-4319-9037-5ecb6cb98cec";
    private const string Folder=@"C:\StarfrontControlTest";
    private static int attempts;
    private bool connected;
    public event PropertyChangedEventHandler? PropertyChanged;
    private static bool Flag(string name){try{using var d=JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder,"safety-test.json")));return d.RootElement.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.True;}catch{return false;}}
    public bool Connected=>connected&&!Flag("drop");
    public bool IsSafe=>Connected&&Flag("open");
    public bool HasSetupDialog=>false;
    public string Id=>DeviceId;
    public string Name=>"Starfront File Safety Simulator";
    public string DisplayName=>Name;
    public string Category=>"Starfront Simulation";
    public string Description=>"Isolated file-driven roof fixture; no physical equipment.";
    public string DriverInfo=>Description;
    public string DriverVersion=>"1.0";
    public IList<string> SupportedActions=>Array.Empty<string>();
    public async Task<bool> Connect(CancellationToken token){attempts++;Metrics();await Task.Delay(2000,token);connected=!Flag("fail");Metrics();PropertyChanged?.Invoke(this,new(nameof(Connected)));return Connected;}
    public void Disconnect(){connected=false;Metrics();PropertyChanged?.Invoke(this,new(nameof(Connected)));}
    private void Metrics(){Directory.CreateDirectory(Folder);File.WriteAllText(Path.Combine(Folder,"safety-test-metrics.json"),JsonSerializer.Serialize(new{attempts,connected,at=DateTimeOffset.UtcNow}));}
    public void SetupDialog(){}
    public string Action(string name,string parameters)=>throw new NotSupportedException();
    public string SendCommandString(string command,bool raw=true)=>throw new NotSupportedException();
    public bool SendCommandBool(string command,bool raw=true)=>throw new NotSupportedException();
    public void SendCommandBlind(string command,bool raw=true)=>throw new NotSupportedException();
}
