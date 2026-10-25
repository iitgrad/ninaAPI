using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;
using ninaAPI.WebService.V2;
using StarfrontControl.Core;
namespace ninaAPI.Control;
public sealed partial class ControlAdapter {
    private DispatcherTimer safetyTimer;
    private SafetyConnectionManager safetyConnections;
    private string safetyPublished;
    private void InitializeSafetyConnection() {
        safetyConnections=new SafetyConnectionManager(()=>IsConnected("safetyMonitor"),ConnectSafety);
        safetyConnections.Changed+=state=>{
            var serialized=System.Text.Json.JsonSerializer.Serialize(state);
            if(serialized==safetyPublished)return;
            safetyPublished=serialized;Enqueue("safetyConnection",state);
        };
        safetyTimer=new DispatcherTimer(TimeSpan.FromSeconds(1),DispatcherPriority.Background,async(_,_)=>{
            if(disposed || safetyConnections.Status.Connecting)return;
            try{await EnsureSafetyConnected();}catch{/* Driver errors are published; no roof state is invented. */}
        },System.Windows.Application.Current.Dispatcher);
        safetyTimer.Start();
    }
    public Task EnsureSafetyConnected() {
        if(disposed)throw new InvalidOperationException("N.I.N.A. is shutting down.");
        return safetyConnections?.EnsureAsync()??throw new InvalidOperationException("Roof connection manager is initializing.");
    }
    private async Task ConnectSafety() {
        if(!AdvancedAPI.Controls.Sequence.Initialized)throw new InvalidOperationException("Waiting for N.I.N.A. initialization.");
        if(Configuration.SimulationOnly&&!Configuration.AllowedProfileIds.Contains(AdvancedAPI.Controls.Profile.ActiveProfile.Id.ToString()))throw new InvalidOperationException("Profile is outside the simulation allowlist.");
        var(chooser,handler)=ControllerV2.GetDeviceVM("safetymonitor");var selected=chooser.SelectedDevice;
        if(selected is NINA.Equipment.Equipment.OfflineDevice)throw new InvalidOperationException("Configured safety monitor is unavailable.");
        if(selected==null||selected.Id=="No_Device")throw new InvalidOperationException("No configured safety monitor. Select one in N.I.N.A.");
        ValidateSelected("safetyMonitor");
        if(handler.GetType().GetProperty("IsConnecting")?.GetValue(handler) is true||handler.GetType().GetProperty("Connecting")?.GetValue(handler) is true)throw new InvalidOperationException("Waiting for the existing N.I.N.A. safety connection.");
        var profile=AdvancedAPI.Controls.Profile.ActiveProfile.Id;var id=selected.Id;
        var result=await(Task<bool>)handler.GetType().GetMethod("Connect").Invoke(handler,[]);
        if(profile!=AdvancedAPI.Controls.Profile.ActiveProfile.Id||chooser.SelectedDevice?.Id!=id)throw new InvalidOperationException("Safety monitor selection changed during connection.");
        if(!result)throw new InvalidOperationException("Safety monitor did not connect.");
    }
    public async Task DisconnectOtherEquipment() {
        if(!System.Windows.Application.Current.Dispatcher.CheckAccess()){await System.Windows.Application.Current.Dispatcher.InvokeAsync(DisconnectOtherEquipment).Task.Unwrap();return;}
        foreach(var kind in CommandCatalog.Equipment.Reverse().Where(k=>k!="safetyMonitor")){
            if(!IsConnected(kind))continue;
            var(_,handler)=ControllerV2.GetDeviceVM(kind.ToLowerInvariant());await(Task)handler.GetType().GetMethod("Disconnect").Invoke(handler,[]);
        }
    }
}
