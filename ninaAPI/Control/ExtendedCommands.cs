using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using NINA.Astrometry;
using NINA.Core.Model.Equipment;
using NINA.Core.Utility;
using NINA.Equipment.Model;
using NINA.Sequencer;
using NINA.Sequencer.Container;
using NINA.Sequencer.Serialization;
using NINA.Sequencer.SequenceItem.FlatDevice;
using NINA.Core.Enum;
using ninaAPI.Utility;
using ninaAPI.WebService.V2;
using StarfrontControl.Core;

namespace ninaAPI.Control;

public sealed partial class ControlAdapter
{
    // UI-thread owned. One operation owns the acquisition system; a matching
    // interrupt may cancel it. Other viewers cannot race capture against a sequence.
    private readonly Dictionary<string, CancellationTokenSource> ownedCommands = new();
    private readonly Dictionary<string, string> ownedActions = new();
    private static double Number(ControlCommand c, string key, double fallback = 0) => c.Parameters?.TryGetValue(key, out var v) == true ? v.GetDouble() : fallback;
    private static int Integer(ControlCommand c, string key, int fallback = 0) => checked((int)Number(c, key, fallback));
    private static bool Boolean(ControlCommand c, string key, bool fallback = false) => c.Parameters?.TryGetValue(key, out var v) == true ? v.GetBoolean() : fallback;
    private static string Text(ControlCommand c, string key, string fallback = "") => c.Parameters?.TryGetValue(key, out var v) == true ? v.GetString() : fallback;
    private static object Info(string device) => device switch
    {
        "camera" => AdvancedAPI.Controls.Camera.GetInfo(),
        "mount" => AdvancedAPI.Controls.Mount.GetInfo(),
        "focuser" => AdvancedAPI.Controls.Focuser.GetInfo(),
        "filterWheel" => AdvancedAPI.Controls.FilterWheel.GetInfo(),
        "rotator" => AdvancedAPI.Controls.Rotator.GetInfo(),
        "guider" => AdvancedAPI.Controls.Guider.GetInfo(),
        "dome" => AdvancedAPI.Controls.Dome.GetInfo(),
        "flatDevice" => AdvancedAPI.Controls.FlatDevice.GetInfo(),
        "switch" => AdvancedAPI.Controls.Switch.GetInfo(),
        "weather" => AdvancedAPI.Controls.Weather.GetInfo(),
        "safetyMonitor" => AdvancedAPI.Controls.SafetyMonitor.GetInfo(),
        _ => null
    };
    private static bool IsConnected(string device) => Info(device)?.GetType().GetProperty("Connected")?.GetValue(Info(device)) is true;
    private static void Require(bool condition, string reason) { if (!condition) throw new ArgumentException(reason); }
    private void ValidateSelected(string kind)
    {
        var (chooser, _) = ControllerV2.GetDeviceVM(kind.ToLowerInvariant());
        Require(chooser.SelectedDevice != null, "No selected " + kind + ".");
        Require(!Configuration.SimulationOnly || Configuration.AllowedDeviceIds.Contains(chooser.SelectedDevice.Id), "Device is not in the simulation allowlist: " + kind);
    }
    public void Validate(ControlCommand command)
    {
        var definition = CommandCatalog.Validate(command);
        if (command.Device == "safetyMonitor" && command.Action == "disconnect") throw new ArgumentException("The safety monitor is always connected; disconnect other equipment instead.");
        if (disposed) throw new CommandConflictException("Adapter is stopping.");
        if(command.DeviceId is not null && command.Action!="connect")
            Require(CommandCatalog.Equipment.Contains(command.Device) && ControllerV2.GetDeviceVM(command.Device.ToLowerInvariant()).Item1.SelectedDevice?.Id==command.DeviceId,"deviceId does not match the selected device in the active profile.");
        if (!AdvancedAPI.Controls.Sequence.Initialized) throw new CommandConflictException("Sequence state is not available yet.");
        if (command.Device == "camera" && command.Action == "cancelTemperature")
        {
            ValidateSelected("camera");
            Require(ownedActions.TryGetValue("camera", out var action) && action is "cool" or "warm",
                "No Starfront-owned temperature ramp to cancel.");
            Require(!AdvancedAPI.Controls.Sequence.IsAdvancedSequenceRunning() && !AdvancedAPI.Controls.Camera.GetInfo().IsExposing,
                "Temperature cancellation is unavailable during acquisition.");
            return;
        }
        if (definition.Interrupt)
        {
            // The host owns execution regardless of which client started it.
            if (command.Device == "sequence" && command.Action == "stop") return;
            if (command.Device == "mount" && command.Action == "stop")
            {
                ValidateSelected("mount");
                Require(IsConnected("mount"), "Mount must be connected to stop.");
                return;
            }
            if (!ownedCommands.ContainsKey(command.Device) && !(command.Device == "guider" && ownedCommands.Count == 0 && IsConnected("guider"))) throw new CommandConflictException("No Starfront-owned operation to interrupt on this device.");
            return;
        }
        if (command.Device == "safetyMonitor" && command.Action is "ensureConnected" or "connect") { ValidateSelected("safetyMonitor");Require(command.DeviceId==null || command.DeviceId==ControllerV2.GetDeviceVM("safetymonitor").Item1.SelectedDevice?.Id,"Use the configured safety monitor; it cannot be substituted."); return; }
        if (ownedCommands.Count != 0 || AdvancedAPI.Controls.Sequence.IsAdvancedSequenceRunning() || AdvancedAPI.Controls.Camera.GetInfo().IsExposing)
            throw new CommandConflictException("An acquisition or control operation is active.");
        if(command.Device=="sequence" && command.Action=="insertDraft"){ValidateDraftInsertion(command);return;}
        if(command.Device=="sequence" && command.Action=="save"){Require(!Configuration.SimulationOnly || Configuration.AllowedProfileIds.Contains(AdvancedAPI.Controls.Profile.ActiveProfile.Id.ToString()),"Profile is outside the simulation allowlist.");sequenceTelemetry.RequireEditable(Text(command,"sequenceId"),Integer(command,"expectedRevision"));return;}
        if (command.Device == "calibration")
        {
            ValidateSelected("camera"); ValidateSelected("flatDevice");
            if (AdvancedAPI.Controls.FilterWheel.GetInfo().Connected) ValidateSelected("filterWheel");
            Require(AdvancedAPI.Controls.Camera.GetInfo().Connected && AdvancedAPI.Controls.FlatDevice.GetInfo().Connected, "Flat calibration requires connected camera and panel.");
            Require(Number(command, "minExposure") <= Number(command, "maxExposure"), "minExposure exceeds maxExposure.");
            Require(Number(command, "count") == Math.Truncate(Number(command, "count")), "count must be an integer.");
            return;
        }
        if (command.Device is "application" or "image") { ValidateAdministration(command); return; }
        if (command.Device == "sequence")
        {
            // A loaded sequence can use any selected equipment. Validate every
            // selected real device, including devices not connected yet.
            foreach (var kind in CommandCatalog.Equipment)
            {
                var (chooser, _) = ControllerV2.GetDeviceVM(kind.ToLowerInvariant());
                if (chooser.SelectedDevice?.Id is string id && id is not ("No_Device" or "No_Guider") && !string.IsNullOrEmpty(id))
                    Require(!Configuration.SimulationOnly || Configuration.AllowedDeviceIds.Contains(id), "Sequence contains a selected device outside the simulation allowlist: " + kind);
            }
            if (command.Action == "load") SequenceFile(command);
            ValidateSequenceEdit(command);
            if (command.Action == "start" && Configuration.SimulationOnly) ValidateSimulationSequence();
            return;
        }
        var (selectedChooser, _) = ControllerV2.GetDeviceVM(command.Device.ToLowerInvariant());
        if (command.Action == "connect")
        {
            var selected = selectedChooser.Devices.FirstOrDefault(x => x.Id == command.DeviceId);
            var configured = ProfileDeviceId(command.Device);
            Require((selected != null && selected is not NINA.Equipment.Equipment.OfflineDevice) ||
                (!string.IsNullOrWhiteSpace(configured) && command.DeviceId == configured), "Choose an available explicit device ID from the device catalog.");
            Require(!Configuration.SimulationOnly || Configuration.AllowedDeviceIds.Contains(command.DeviceId), "Device is not in the simulation allowlist.");
            if (IsConnected(command.Device) && selectedChooser.SelectedDevice?.Id != command.DeviceId) throw new CommandConflictException("Disconnect the current device before selecting another.");
            return;
        }
        ValidateSelected(command.Device);
        if (command.Action == "disconnect") return;
        Require(IsConnected(command.Device), "Device is disconnected.");
        // Integer-only driver fields must never be silently truncated.
        foreach (var key in new[] { "count", "bin", "gain", "offset", "x", "y", "mode", "position", "index" })
            if (command.Parameters?.ContainsKey(key) == true) Require(Number(command, key) == Math.Truncate(Number(command, key)), "Integer parameter required: " + key);
        var c = AdvancedAPI.Controls;
        var camera = c.Camera.GetInfo();
        switch (command.Device, command.Action)
        {
            case ("camera", "capture"):
                Require(Number(command, "exposure") >= camera.ExposureMin && Number(command, "exposure") <= camera.ExposureMax, "Exposure is outside the camera range.");
                Require(new[] { "LIGHT", "DARK", "FLAT", "BIAS", "SNAPSHOT" }.Contains(Text(command, "imageType", "LIGHT")), "Invalid imageType.");
                if (command.Parameters?.ContainsKey("gain") == true) Require(camera.CanSetGain && Number(command, "gain") >= camera.GainMin && Number(command, "gain") <= camera.GainMax, "Gain is unsupported or outside the camera range.");
                if (command.Parameters?.ContainsKey("offset") == true) Require(camera.CanSetOffset && Number(command, "offset") >= camera.OffsetMin && Number(command, "offset") <= camera.OffsetMax, "Offset is unsupported or outside the camera range.");
                if (command.Parameters?.ContainsKey("bin") == true) Require(camera.BinningModes.Any(x => x.X == Integer(command, "bin") && x.Y == Integer(command, "bin")), "Unsupported binning.");
                break;
            case ("camera", "cool" or "warm"): Require(camera.CanSetTemperature, "Camera does not support temperature control."); break;
            case ("camera", "binning"): Require(camera.BinningModes.Any(x => x.X == Integer(command, "x") && x.Y == Integer(command, "y")), "Unsupported binning."); break;
            case ("camera", "readout"): Require(Integer(command, "mode") < camera.ReadoutModes.Count(), "Unsupported readout mode."); break;
            case ("camera", "dewHeater"): Require(camera.HasDewHeater, "Camera has no dew heater."); break;
            case ("camera", "usbLimit"): Require(camera.CanSetUSBLimit && Number(command, "value") >= camera.USBLimitMin && Number(command, "value") <= camera.USBLimitMax, "USB limit is unsupported or outside the camera range."); break;
            case ("mount", "move"): Require(Number(command, "axis") == Math.Truncate(Number(command, "axis")) && (Integer(command, "axis") == 0 ? c.Mount.GetInfo().CanMovePrimaryAxis : c.Mount.GetInfo().CanMoveSecondaryAxis), "Mount axis is unsupported."); break;
            case ("mount", "home"): Require(c.Mount.GetInfo().CanFindHome, "Mount cannot find home."); break;
            case ("mount", "park"): Require(c.Mount.GetInfo().CanPark, "Mount cannot park."); break;
            case ("mount", "tracking"): Require(c.Mount.GetInfo().CanSetTrackingEnabled, "Mount cannot set tracking."); break;
            case ("mount", "slew" or "flip"): Require(c.Mount.GetInfo().CanSlew, "Mount cannot slew."); break;
            case ("rotator", "move"): Require(c.Rotator.GetInfo().Synced, "Sync the rotator sky angle before moving to a sky position."); break;
            case ("focuser", "autofocus"): ValidateSelected("camera"); Require(camera.Connected, "Autofocus requires a connected camera."); if (c.FilterWheel.GetInfo().Connected) ValidateSelected("filterWheel"); break;
            case ("filterWheel", "select"): Require(c.Profile.ActiveProfile.FilterWheelSettings.FilterWheelFilters.Any(x => x.Position == Integer(command, "position")), "Filter position is not configured."); break;
            case ("dome", "open" or "close"): Require(c.Dome.GetInfo().CanSetShutter, "Dome has no controllable shutter."); break;
            case ("dome", "home"): Require(c.Dome.GetInfo().CanFindHome, "Dome cannot find home."); break;
            case ("dome", "park"): Require(c.Dome.GetInfo().CanPark, "Dome cannot park."); break;
            case ("dome", "slew"): Require(c.Dome.GetInfo().CanSetAzimuth, "Dome cannot set azimuth."); break;
            case ("dome", "follow"): ValidateSelected("mount"); Require(c.Mount.GetInfo().Connected, "Following requires a connected mount."); break;
            case ("switch", "set"):
                var sw = c.Switch.GetInfo().WritableSwitches?.FirstOrDefault(x => x.Id == Integer(command, "index"));
                Require(sw != null && Number(command, "value") >= sw.Minimum && Number(command, "value") <= sw.Maximum && (sw.StepSize <= 0 || Math.Abs((Number(command, "value") - sw.Minimum) / sw.StepSize - Math.Round((Number(command, "value") - sw.Minimum) / sw.StepSize)) < 0.000001), "Switch ID, range or step is invalid."); break;
            case ("flatDevice", "light"): Require(c.FlatDevice.GetInfo().SupportsOnOff, "Flat device cannot switch its light."); break;
            case ("flatDevice", "cover"): Require(c.FlatDevice.GetInfo().SupportsOpenClose, "Flat device has no controllable cover."); break;
            case ("flatDevice", "brightness"): Require(Number(command, "value") == Math.Truncate(Number(command, "value")) && Number(command, "value") >= c.FlatDevice.GetInfo().MinBrightness && Number(command, "value") <= c.FlatDevice.GetInfo().MaxBrightness, "Brightness is outside the device range."); break;
        }
    }
    private static void ValidateSimulationSequence()
    {
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        void Walk(object item, int depth)
        {
            if (item is null || !seen.Add(item)) return;
            Require(depth <= 16 && seen.Count <= 256, "Simulation sequence exceeds inspection bounds.");
            var type = item.GetType();
            var safe = new[] { "SequenceRootContainer", "StartAreaContainer", "TargetAreaContainer", "EndAreaContainer", "DeepSkyObjectContainer", "SequentialContainer", "ParallelContainer", "TakeExposure", "SwitchFilter", "ConnectEquipment", "DisconnectEquipment", "WaitForTimeSpan", "WaitUntilTime", "LoopCondition" };
            Require((type.Assembly == typeof(SequenceRootContainer).Assembly && safe.Contains(type.Name)) || type == typeof(ninaAPI.SequenceItems.SendNotificationInstruction) || type == typeof(ninaAPI.SequenceItems.DisconnectOtherEquipmentInstruction), "Sequence instruction has not been approved for this isolated simulation: " + type.Name);
            foreach (var group in new[] { "Items", "Conditions", "Triggers" })
                if (type.GetProperty(group)?.GetValue(item) is IEnumerable children) foreach (var child in children) Walk(child, depth + 1);
        }
        Walk(AdvancedAPI.Controls.Sequence.GetSequenceRoot(), 0);
    }
    private string SequenceFile(ControlCommand command)
    {
        var settings = AdvancedAPI.Controls.Profile.ActiveProfile.SequenceSettings;
        return SequenceLibrary.Resolve(settings.DefaultSequenceFolder, settings.SequencerTargetsFolder, Text(command, "fileName"));
    }
    public async Task Execute(ControlCommand command)
    {
        try
        {
            await (await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                Validate(command);
                if (CommandCatalog.Validate(command).Interrupt)
                {
                    if (command.Device == "sequence" && command.Action == "skipCurrent") { AdvancedAPI.Controls.Sequence.GetSequenceRoot().SkipCurrentRunningItems(); sequenceTelemetry.Schedule(); return; }
                    if (command.Device == "sequence" && command.Action == "stop")
                    {
                        await SequenceStop.Execute(
                            () => AdvancedAPI.Controls.Sequence.IsAdvancedSequenceRunning(),
                            () => AdvancedAPI.Controls.Sequence.CancelAdvancedSequence(),
                            () => ownedCommands.ContainsKey("sequence"),
                            () => { if (ownedCommands.TryGetValue("sequence", out var sequenceOwner)) sequenceOwner.Cancel(); }, stop.Token);
                        sequenceTelemetry.Schedule();
                        return;
                    }
                    if (command.Device == "mount" && command.Action == "stop")
                    {
                        await MountStop.Execute(
                            () => AdvancedAPI.Controls.Mount.GetInfo().Slewing,
                            () => AdvancedAPI.Controls.Sequence.IsAdvancedSequenceRunning(),
                            () =>
                            {
                                try { if (ownedCommands.TryGetValue("sequence", out var sequenceOwner)) sequenceOwner.Cancel(); }
                                finally { AdvancedAPI.Controls.Sequence.CancelAdvancedSequence(); }
                            },
                            () => ownedCommands.ContainsKey("mount"),
                            () => { if (ownedCommands.TryGetValue("mount", out var mountOwner)) mountOwner.Cancel(); },
                            () => AdvancedAPI.Controls.Mount.StopSlew(), stop.Token);
                        sequenceTelemetry.Schedule();
                        return;
                    }
                    if (ownedCommands.TryGetValue(command.Device, out var owner)) owner.Cancel();
                    if (command.Device == "guider") { ValidateSelected("guider"); await AdvancedAPI.Controls.Guider.StopGuiding(stop.Token); using var stopped = await GuiderRpc("get_app_state", stop.Token); Require(stopped.RootElement.GetString() == "Stopped", "Guider did not stop."); }
                    if (command.Device == "camera" && command.Action != "cancelTemperature" && AdvancedAPI.Controls.Camera.GetInfo().IsExposing) AdvancedAPI.Controls.Camera.AbortExposure();
                    if (command.Device == "mount") AdvancedAPI.Controls.Mount.StopSlew();
                    var interrupted = DateTimeOffset.UtcNow;
                    while (ownedCommands.ContainsKey(command.Device))
                    {
                        if (DateTimeOffset.UtcNow - interrupted > TimeSpan.FromSeconds(30)) throw new InvalidOperationException("Interrupted operation did not settle in 30 seconds.");
                        await Task.Delay(50, stop.Token);
                    }
                    sequenceTelemetry.Schedule();
                    return;
                }
                if (command.Device == "safetyMonitor" && command.Action is "ensureConnected" or "connect") { await EnsureSafetyConnected(); return; }
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                ownedCommands.Add(command.Device, cancellation);
                ownedActions.Add(command.Device, command.Action);
                try { await ValidateRemoteDependencies(command); await ExecuteOwned(command, cancellation.Token); }
                catch(Exception) when(cancellation.IsCancellationRequested) { throw new OperationCanceledException(cancellation.Token); }
                finally { ownedActions.Remove(command.Device); ownedCommands.Remove(command.Device); RefreshCatalog(); sequenceTelemetry.Schedule(); }
            }));
        }
        catch (Exception e) { Logger.Error(e, "Starfront control operation failed"); throw; }
    }
    private async Task ExecuteOwned(ControlCommand command, CancellationToken token)
    {
        var c = AdvancedAPI.Controls;
        var progress = c.StatusMediator.GetStatus();
        if (command.Action is "connect" or "disconnect")
        {
            var (chooser, handler) = ControllerV2.GetDeviceVM(command.Device.ToLowerInvariant());
            if (command.Action == "connect")
            {
                if (IsConnected(command.Device)) return;
                var profileId = c.Profile.ActiveProfile.Id;
                var configuredId = ProfileDeviceId(command.Device);
                // Lazy discovery is part of the explicitly requested connection, never telemetry.
                await chooser.GetEquipment();
                token.ThrowIfCancellationRequested();
                Require(c.Profile.ActiveProfile.Id == profileId && ProfileDeviceId(command.Device) == configuredId,
                    "Active profile changed during discovery. Review the current selection before connecting.");
                Require(!c.Sequence.IsAdvancedSequenceRunning() && !c.Camera.GetInfo().IsExposing,
                    "An acquisition started during discovery. Connect when N.I.N.A. is idle.");
                Require(!Configuration.SimulationOnly || Configuration.AllowedDeviceIds.Contains(command.DeviceId),
                    "Device is not in the simulation allowlist.");
                var target = chooser.Devices.FirstOrDefault(x => x.Id == command.DeviceId);
                Require(target != null && target is not NINA.Equipment.Equipment.OfflineDevice,
                    "The configured device is unavailable. Check its driver and connection in N.I.N.A.");
                chooser.SelectedDevice = target;
                var connected = await (Task<bool>)handler.GetType().GetMethod("Connect").Invoke(handler, []);
                Require(connected && IsConnected(command.Device), "Device did not connect.");
            }
            else { await (Task)handler.GetType().GetMethod("Disconnect").Invoke(handler, []); Require(!IsConnected(command.Device), "Device remains connected."); }
            return;
        }
        if (command.Device is "application" or "image") { await ExecuteAdministration(command, token); return; }
        if (command.Device == "calibration")
        {
            var flats = new AutoExposureFlat(c.Profile, c.Camera, c.Imaging, c.ImageSaveMediator, c.ImageHistory, c.FilterWheel, c.FlatDevice);
            flats.GetIterations().Iterations = Integer(command, "count"); flats.MinExposure = Number(command, "minExposure"); flats.MaxExposure = Number(command, "maxExposure");
            flats.GetSetBrightnessItem().Brightness = Integer(command, "brightness"); flats.HistogramTargetPercentage = Number(command, "targetMean"); flats.HistogramTolerancePercentage = Number(command, "tolerance");
            Require(flats.Validate(), "Flat calibration validation failed. Check camera/panel settings.");
            await flats.Run(progress, token); token.ThrowIfCancellationRequested(); Require(flats.Status == SequenceEntityStatus.FINISHED, "Flat calibration did not finish successfully."); return;
        }
        Coordinates Coordinates() => new(Angle.ByDegree(Number(command, "ra") * 15), Angle.ByDegree(Number(command, "dec")), Epoch.J2000);
        void Success(bool success) { if (!success) throw new InvalidOperationException("N.I.N.A. reported that the operation did not complete."); }
        switch (command.Device, command.Action)
        {
            case ("camera", "capture"):
                for (var i = 0; i < Integer(command, "count", 1); i++)
                {
                    token.ThrowIfCancellationRequested();
                    var info = c.Camera.GetInfo();
                    var capture = new CaptureSequence(Number(command, "exposure"), Text(command, "imageType", "LIGHT"), c.FilterWheel.GetInfo().SelectedFilter, new BinningMode((short)Integer(command, "bin", info.BinX), (short)Integer(command, "bin", info.BinY)), 1);
                    if (command.Parameters?.ContainsKey("gain") == true) capture.Gain = Integer(command, "gain");
                    if (command.Parameters?.ContainsKey("offset") == true) capture.Offset = Integer(command, "offset");
                    var exposure = await c.Imaging.CaptureImage(capture, token, progress, Text(command, "target", "Starfront Control"));
                    Require(exposure != null, "No image was downloaded.");
                    var rendered = await c.Imaging.PrepareImage(exposure, new PrepareImageParameters(autoStretch: true), token);
                    if (Boolean(command, "save"))
                    {
                        var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        void Saved(object sender, NINA.WPF.Base.Interfaces.Mediator.ImageSavedEventArgs e) { if (ReferenceEquals(e.MetaData, rendered.RawImageData.MetaData) && e.PathToImage != null) saved.TrySetResult(); }
                        c.ImageSaveMediator.ImageSaved += Saved;
                        try { await c.ImageSaveMediator.Enqueue(rendered.RawImageData, Task.FromResult(rendered), progress, token); await saved.Task.WaitAsync(TimeSpan.FromSeconds(60), token); }
                        finally { c.ImageSaveMediator.ImageSaved -= Saved; }
                    }
                }
                break;
            case ("camera", "cool"): Success(await c.Camera.CoolCamera(Number(command, "temperature"), TimeSpan.FromSeconds(Number(command, "rampSeconds")), progress, token)); break;
            case ("camera", "warm"): Success(await c.Camera.WarmCamera(TimeSpan.FromSeconds(Number(command, "rampSeconds")), progress, token)); break;
            case ("camera", "binning"): c.Camera.SetBinning((short)Integer(command, "x"), (short)Integer(command, "y")); break;
            case ("camera", "readout"): c.Camera.SetReadoutModeForNormalImages((short)Integer(command, "mode")); break;
            case ("camera", "dewHeater"): c.Camera.SetDewHeater(Boolean(command, "enabled")); break;
            case ("camera", "usbLimit"): c.Camera.SetUSBLimit(Integer(command, "value")); break;
            case ("mount", "move"):
                var axis = (TelescopeAxes)Integer(command, "axis");
                try { c.Mount.MoveAxis(axis, Number(command, "rate")); await Task.Delay(TimeSpan.FromSeconds(Number(command, "seconds")), token); }
                finally { c.Mount.MoveAxis(axis, 0); }
                break;
            case ("mount", "home"): Success(await c.Mount.FindHome(progress, token)); break;
            case ("mount", "park"): Success(await c.Mount.ParkTelescope(progress, token)); break;
            case ("mount", "unpark"): Success(await c.Mount.UnparkTelescope(progress, token)); break;
            case ("mount", "tracking"): Require(c.Mount.SetTrackingEnabled(Boolean(command, "enabled")) == Boolean(command, "enabled"), "Tracking did not reach the requested state."); break;
            case ("mount", "slew"): Success(await c.Mount.SlewToCoordinatesAsync(Coordinates(), token)); break;
            case ("mount", "sync"): Success(await c.Mount.Sync(Coordinates())); break;
            case ("mount", "flip"):
                var beforePier=c.Mount.GetInfo().SideOfPier;
                Require(c.Mount.DestinationSideOfPier(Coordinates())!=beforePier,"Mount is already on the requested pier side; no flip is required.");
                Success(await c.Mount.MeridianFlip(Coordinates(),token));
                Require(c.Mount.GetInfo().SideOfPier!=beforePier,"Mount did not change pier side.");break;
            case ("focuser", "move"): Require(await c.Focuser.MoveFocuser(Integer(command, "position"), token) == Integer(command, "position"), "Focuser did not reach the requested position."); break;
            case ("focuser", "autofocus"):
                var autofocus = c.AutoFocusFactory.Create();
                NINA.WPF.Base.Utility.AutoFocus.AutoFocusReport focusResult=null;
                var focusCompleted=false;
                try {
                    focusResult=await autofocus.StartAutoFocus(c.FilterWheel.GetInfo().SelectedFilter,token,progress);
                    token.ThrowIfCancellationRequested();Require(focusResult!=null,"Autofocus returned no result.");
                    c.ImageHistory.AppendAutoFocusPoint(focusResult);focusCompleted=true;
                }
                finally {
                    lock(gate) Enqueue("autofocus",new { running=false,outcome=token.IsCancellationRequested?"cancelled":focusCompleted?"completed":"failed",result=focusResult==null?null:Copy(focusResult,"Temperature","Filter","Position"),points=focusPoints.ToArray() });
                    (autofocus as IDisposable)?.Dispose();
                }
                break;
            case ("filterWheel", "select"): await c.FilterWheel.ChangeFilter(c.Profile.ActiveProfile.FilterWheelSettings.FilterWheelFilters.First(x => x.Position == Integer(command, "position")), token, progress); break;
            case ("rotator", "sync"): c.Rotator.Sync((float)Number(command, "angle")); Require(c.Rotator.GetInfo().Synced, "Rotator sync failed."); break;
            case ("rotator", "move"): await c.Rotator.Move((float)Number(command, "angle"), token); break;
            case ("rotator", "moveMechanical"): await c.Rotator.MoveMechanical((float)Number(command, "angle"), token); break;
            case ("guider", "start"): await StartVerifiedGuiding(Boolean(command, "forceCalibration"), progress, token); break;
            case ("guider", "stop"): Success(await c.Guider.StopGuiding(token)); break;
            case ("guider", "clearCalibration"): Success(await c.Guider.ClearCalibration(token)); break;
            case ("dome", "open"): Success(await c.Dome.OpenShutter(token)); break;
            case ("dome", "close"): Success(await c.Dome.CloseShutter(token)); break;
            case ("dome", "home"): Success(await c.Dome.FindHome(token)); break;
            case ("dome", "park"): Success(await c.Dome.Park(token)); break;
            case ("dome", "slew"): Success(await c.Dome.SlewToAzimuth(Number(command, "azimuth"), token)); break;
            case ("dome", "follow"): Success(Boolean(command, "enabled") ? await c.Dome.EnableFollowing(token) : await c.Dome.DisableFollowing(token)); break;
            case ("flatDevice", "light"): await c.FlatDevice.ToggleLight(Boolean(command, "enabled"), progress, token); break;
            case ("flatDevice", "cover"): if (Boolean(command, "closed")) await c.FlatDevice.CloseCover(progress, token); else await c.FlatDevice.OpenCover(progress, token); break;
            case ("flatDevice", "brightness"): await c.FlatDevice.SetBrightness(Integer(command, "value"), progress, token); break;
            case ("switch", "set"): await c.Switch.SetSwitchValue((short)Integer(command, "index"), Number(command, "value"), progress, token); break;
            case ("sequence", "save"): SaveLoadedSequence(command);break;
            case ("sequence", "insertDraft"): InsertDraft(command);break;
            case ("sequence", "resolveTemplate" or "skip" or "edit" or "editField" or "target" or "addTarget" or "insert" or "duplicate" or "delete" or "move"): ExecuteSequenceEdit(command); break;
            case ("sequence", "start"): await c.Sequence.StartAdvancedSequence(Boolean(command, "skipValidation")); token.ThrowIfCancellationRequested(); Require(c.Sequence.GetSequenceRoot().Status == SequenceEntityStatus.FINISHED, "Sequence did not finish successfully; inspect validation and instruction errors."); break;
            case ("sequence", "reset"): sequenceTelemetry.NewExecution(); c.Sequence.GetSequenceRoot().ResetAll(); break;
            case ("sequence", "load"):
                var navigation = c.Sequence.GetType().GetField("sequenceNavigation", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(c.Sequence) ?? throw new InvalidOperationException("Unsupported host.");
                var factory = (ISequencerFactory)navigation.GetType().GetField("factory", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(navigation);
                var loaded = new SequenceJsonConverter(factory).Deserialize(File.ReadAllText(SequenceFile(command)));
                var root = loaded as SequenceRootContainer;
                if (loaded is DeepSkyObjectContainer dso) {
                    root = factory.GetContainer<SequenceRootContainer>();
                    root.Name = Path.GetFileNameWithoutExtension(Text(command, "fileName"));
                    root.Add(factory.GetContainer<StartAreaContainer>());
                    var targets = factory.GetContainer<TargetAreaContainer>(); targets.Add(dso); root.Add(targets);
                    root.Add(factory.GetContainer<EndAreaContainer>());
                }
                Require(root != null, "Choose a complete advanced sequence or saved target sequence."); c.Sequence.SetAdvancedSequence(root); savedSequenceRoot=new WeakReference<object>(root);savedSequenceProfile=AdvancedAPI.Controls.Profile.ActiveProfile.Id;savedSequenceFile=Text(command,"fileName").StartsWith("Targets/",StringComparison.Ordinal)&&loaded is SequenceRootContainer?Path.GetFileName(Text(command,"fileName")):null;PublishAdministration();c.Sequence.SwitchToAdvancedView(); break;
            default: throw new ArgumentException("Unsupported command.");
        }
        token.ThrowIfCancellationRequested();
    }
    private void CancelOwnedCommands()
    {
        void Cancel() { foreach (var item in ownedCommands.Values) item.Cancel(); if (ownedCommands.ContainsKey("sequence")) AdvancedAPI.Controls.Sequence.CancelAdvancedSequence(); }
        if (Application.Current.Dispatcher.CheckAccess()) Cancel(); else Application.Current.Dispatcher.Invoke(Cancel);
    }
}
