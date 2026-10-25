using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ninaAPI.WebService.V2;
using StarfrontControl.Core;
namespace ninaAPI.Control;

public sealed partial class ControlAdapter
{
    public async Task ValidateRemoteDependencies(ControlCommand command)
    {
        if (!Configuration.SimulationOnly || !(command.Device == "guider" || command.Device == "sequence" && command.Action == "start")) return;
        var selected = command.Device == "guider" && command.Action == "connect" ? command.DeviceId : await Application.Current.Dispatcher.InvokeAsync(() => ControllerV2.GetDeviceVM("guider").Item1.SelectedDevice?.Id);
        if (selected != "PHD2_Single" && command.Device != "guider") return;
        var settings = await Application.Current.Dispatcher.InvokeAsync(() => new { host = AdvancedAPI.Controls.Profile.ActiveProfile.GuiderSettings.PHD2ServerUrl, port = AdvancedAPI.Controls.Profile.ActiveProfile.GuiderSettings.PHD2ServerPort, profileId = AdvancedAPI.Controls.Profile.ActiveProfile.GuiderSettings.PHD2ProfileId });
        Require(selected == "PHD2_Single" && (settings.host == "127.0.0.1" || settings.host == "localhost") && settings.port == 4400, "Simulation guiding requires the isolated local PHD2 server.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token); deadline.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            using var profile = await GuiderRpc("get_profile", deadline.Token);
            Require(settings.profileId == profile.RootElement.GetProperty("id").GetInt32() && Configuration.AllowedPHD2ProfileIds.Contains(profile.RootElement.GetProperty("id").GetInt32()), "PHD2 profile is not in the simulation allowlist.");
            using var equipment = await GuiderRpc("get_current_equipment", deadline.Token);
            var value = equipment.RootElement;
            Require(value.GetProperty("camera").GetProperty("name").GetString() == "Simulator" && value.GetProperty("mount").GetProperty("name").GetString() == "On Camera" && !value.TryGetProperty("aux_mount", out _) && !value.TryGetProperty("ao", out _) && !value.TryGetProperty("rotator", out _), "PHD2 is not configured exclusively with its built-in simulated camera and mount.");
        }
        catch (Exception e) when (e is not ArgumentException && e is not CommandConflictException)
        { throw new CommandConflictException("The local PHD2 simulator could not be verified."); }
    }
    private static async Task StartVerifiedGuiding(bool recalibrate, System.IProgress<NINA.Core.Model.ApplicationStatus> progress, CancellationToken token)
    {
        var c = AdvancedAPI.Controls;
        if (c.Guider.GetInfo().Name != "PHD2") { Require(await c.Guider.StartGuiding(recalibrate, progress, token), "Guider did not start."); return; }
        using var initial = await GuiderRpc("get_app_state", token);
        if (initial.RootElement.GetString() == "Guiding" && !recalibrate) return;
        if (initial.RootElement.GetString() != "Stopped" && initial.RootElement.GetString() != "Looping") await c.Guider.StopGuiding(token);
        using var observerStop = CancellationTokenSource.CreateLinkedTokenSource(token);
        observerStop.CancelAfter(TimeSpan.FromMinutes(4));
        using var client = new TcpClient();
        var settings = c.Profile.ActiveProfile.GuiderSettings;
        await client.ConnectAsync(settings.PHD2ServerUrl, settings.PHD2ServerPort, observerStop.Token);
        var observed = ObserveSettle(client.GetStream(), observerStop.Token);
        try
        {
            Require(await c.Guider.StartGuiding(recalibrate, progress, observerStop.Token), "Guider did not start.");
            await observed;
            using var state = await GuiderRpc("get_app_state", observerStop.Token);
            Require(state.RootElement.GetString() == "Guiding", "PHD2 is not guiding after settling.");
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("PHD2 did not settle within four minutes."); }
        finally { observerStop.Cancel(); client.Dispose(); try { await observed; } catch { } }
    }
    private static async Task ObserveSettle(Stream stream, CancellationToken token)
    {
        var buffer = new byte[8192]; var used = 0;
        while (true)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(used, 1), token); if (n == 0) throw new IOException("PHD2 disconnected before settling.");
            if (buffer[used++] != 10) { if (used == buffer.Length) throw new IOException("PHD2 event exceeds bound."); continue; }
            using var document = JsonDocument.Parse(buffer.AsMemory(0, used)); used = 0;
            if (document.RootElement.TryGetProperty("Event", out var name) && name.GetString() == "SettleDone")
            {
                Require(document.RootElement.GetProperty("Status").GetInt32() == 0, "PHD2 reported settling failure."); return;
            }
        }
    }
    private static async Task<JsonDocument> GuiderRpc(string method, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token); deadline.CancelAfter(TimeSpan.FromSeconds(5)); token = deadline.Token;
        using var client = new TcpClient(); var settings = AdvancedAPI.Controls.Profile.ActiveProfile.GuiderSettings;
        await client.ConnectAsync(settings.PHD2ServerUrl, settings.PHD2ServerPort, token);
        using var stream = client.GetStream();
        await stream.WriteAsync(Encoding.UTF8.GetBytes("{\"method\":\"" + method + "\",\"id\":1}\n"), token);
        var buffer = new byte[8192]; var used = 0; var scanned = 0;
        while (scanned++ < 16)
        {
            // Read one bounded line. Do not let an untrusted peer allocate an unbounded string.
            while (used < buffer.Length) { var n = await stream.ReadAsync(buffer.AsMemory(used, 1), token); if (n == 0) throw new IOException("PHD2 closed."); if (buffer[used++] == 10) break; }
            if (used == buffer.Length) throw new IOException("PHD2 response exceeds bound.");
            using var document = JsonDocument.Parse(buffer.AsMemory(0, used)); used = 0;
            if (document.RootElement.TryGetProperty("id", out var id) && id.GetInt32() == 1)
            { if (document.RootElement.TryGetProperty("error", out _)) throw new IOException("PHD2 rejected request."); return JsonDocument.Parse(document.RootElement.GetProperty("result").GetRawText()); }
        }
        throw new IOException("PHD2 response missing.");
    }
}
