using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Text.RegularExpressions;
using NINA.Core.Enum;
using NINA.Image.Interfaces;
using NINA.PlateSolving;
using NINA.Sequencer.SequenceItem;
using NINA.Sequencer.Container;
using NINA.Sequencer;
using System.Reflection;
using StarfrontControl.Core;
using ninaAPI.Utility;

namespace ninaAPI.Control;

public sealed partial class ControlAdapter
{
    private readonly Dictionary<string, string> savedFiles = new();
    private readonly Queue<string> savedIds = new();
    private void RegisterSavedFile(string id, string path)
    {
        lock (gate) { if (savedIds.Count >= 32) savedFiles.Remove(savedIds.Dequeue()); savedIds.Enqueue(id); savedFiles[id] = path; }
    }
    public string SavedFile(string id)
    {
        if (!Guid.TryParse(id, out var parsed)) return null;
        id = parsed.ToString("N");
        lock (gate) return savedFiles.TryGetValue(id, out var path) && File.Exists(path) && new FileInfo(path).Length <= 128 * 1024 * 1024 ? path : null;
    }
    private void PublishAdministration()
    {
        var p = AdvancedAPI.Controls.Profile;
        var library = SequenceLibrary.List(p.ActiveProfile.SequenceSettings.DefaultSequenceFolder, p.ActiveProfile.SequenceSettings.SequencerTargetsFolder);
        Enqueue("administration", new
        {
            profile = new { id = p.ActiveProfile.Id, name = p.ActiveProfile.Name },
            site = new { latitude = p.ActiveProfile.AstrometrySettings.Latitude, longitude = p.ActiveProfile.AstrometrySettings.Longitude, elevation = p.ActiveProfile.AstrometrySettings.Elevation, name = p.ActiveProfile.AstrometrySettings.Site, timeZone = TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana) ? iana : TimeZoneInfo.Local.Id },
            profiles = p.Profiles.Take(32).Select(x => new { id = x.Id, name = x.Name, allowed = !Configuration.SimulationOnly || Configuration.AllowedProfileIds.Contains(x.Id.ToString()) }).ToArray(),
            sequenceFileName = CurrentSequenceFileName,
            sequences = library.Files,
            sequencesTruncated = library.Truncated,
            filters = p.ActiveProfile.FilterWheelSettings.FilterWheelFilters.Take(32).Select(x => new { position = x.Position, name = x.Name }).ToArray()
        });
    }
    private void ValidateAdministration(ControlCommand command)
    {
        if (command.Device == "image")
        {
            Require(SavedFile(Text(command, "imageId")) != null, "Image is not in the retained saved-image catalog, is missing, or exceeds 128 MiB.");
            Require((command.Parameters?.ContainsKey("ra") == true) == (command.Parameters?.ContainsKey("dec") == true), "Supply both ra and dec for a solve hint.");
        }
        if (command.Device == "application" && command.Action == "selectProfile")
        {
            Require(Guid.TryParse(Text(command, "profileId"), out var id) && AdvancedAPI.Controls.Profile.Profiles.Any(x => x.Id == id), "Unknown profile ID.");
            Require(!Configuration.SimulationOnly || Configuration.AllowedProfileIds.Contains(id.ToString()), "Profile is not in the simulation allowlist.");
            Require(!CommandCatalog.Equipment.Any(IsConnected), "Disconnect devices before switching profiles.");
        }
    }
    private async Task ExecuteAdministration(ControlCommand command, CancellationToken token)
    {
        var c = AdvancedAPI.Controls;
        if (command.Device == "image")
        {
            var path = SavedFile(Text(command, "imageId")) ?? throw new InvalidOperationException("Image is no longer retained.");
            var image = await c.ImageDataFactory.CreateFromFile(path, 16, false, RawConverterEnum.FREEIMAGE, token);
            var settings = c.Profile.ActiveProfile.PlateSolveSettings;
            var parameters = new CaptureSolverParameter { Attempts = 1, Binning = settings.Binning, BlindFailoverEnabled = settings.BlindFailoverEnabled, Coordinates = command.Parameters?.ContainsKey("ra") == true ? new NINA.Astrometry.Coordinates(NINA.Astrometry.Angle.ByDegree(Number(command, "ra") * 15), NINA.Astrometry.Angle.ByDegree(Number(command, "dec")), NINA.Astrometry.Epoch.J2000) : c.Mount.GetCurrentPosition(), DownSampleFactor = settings.DownSampleFactor, FocalLength = Number(command, "focalLength", c.Profile.ActiveProfile.TelescopeSettings.FocalLength), MaxObjects = settings.MaxObjects, Regions = settings.Regions, SearchRadius = settings.SearchRadius, PixelSize = image.MetaData.Camera.PixelSize };
            var solver = c.PlateSolver.GetImageSolver(c.PlateSolver.GetPlateSolver(settings), c.PlateSolver.GetBlindSolver(settings));
            var result = await solver.Solve(image, parameters, c.StatusMediator.GetStatus(), token);
            Enqueue("solve", new { imageId = Text(command, "imageId"), success = result.Success, ra = result.Coordinates?.RA, dec = result.Coordinates?.Dec, orientation = Finite(result.Orientation), pixelScale = Finite(result.Pixscale) });
            Require(result.Success, "Plate solving did not find a solution."); return;
        }
        if (command.Action == "selectProfile" && c.Profile.ActiveProfile.Id != Guid.Parse(Text(command, "profileId"))) Require(c.Profile.SelectProfile(c.Profile.Profiles.First(x => x.Id == Guid.Parse(Text(command, "profileId")))), "Profile switch failed.");
        if (command.Action == "logs")
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NINA", "Logs");
            var latest = new DirectoryInfo(directory).EnumerateFiles("*.log").Take(1024).MaxBy(x => x.LastWriteTimeUtc);
            var tail = "";
            if (latest != null)
            {
                using var stream = new FileStream(latest.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                stream.Seek(Math.Max(0, stream.Length - 8000), SeekOrigin.Begin);
                using var reader = new StreamReader(stream); tail = await reader.ReadToEndAsync(token);
            }
            tail = tail.Replace(Configuration.Token, "[redacted]");
            tail = Regex.Replace(tail, @"(?i)(bearer\s+|(?:password|token|secret|apikey|api_key)\s*[:=]\s*)[^\s,;]+", "$1[redacted]", RegexOptions.None, TimeSpan.FromSeconds(1));
            Enqueue("logTail", new { text = tail, at = DateTimeOffset.UtcNow, truncated = true });
        }
        if (command.Action == "screenshot")
        {
            // Explicit request only. No retained Bitmap/Graphics, no screenshot loop.
            var bounds = Screen.PrimaryScreen.Bounds;
            using var bitmap = new Bitmap(bounds.Width, bounds.Height);
            using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            using var thumbnail = new Bitmap(bitmap, new Size(640, 400));
            using var output = new MemoryStream();
            var codec = ImageCodecInfo.GetImageEncoders().First(x => x.FormatID == ImageFormat.Jpeg.Guid);
            foreach (var quality in new[] { 25L, 15L, 8L })
            {
                output.SetLength(0);
                using var encoder = new EncoderParameters(1); encoder.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                thumbnail.Save(output, codec, encoder);
                if (output.Length <= 16 * 1024) break;
            }
            Require(output.Length <= 16 * 1024, "Screenshot exceeds the 16 KiB bound.");
            Enqueue("screenshot", new { jpeg = Convert.ToBase64String(output.ToArray()), width = 640, height = 400, at = DateTimeOffset.UtcNow });
        }
        PublishAdministration();
    }
    private void ValidateSequenceEdit(ControlCommand command)
    {
        if (command.Action is "resolveTemplate" or "edit" or "editField" or "target" or "addTarget" or "insert" or "duplicate" or "delete" or "move")
            sequenceTelemetry.RequireEditable(Text(command, "sequenceId"), Integer(command, "expectedRevision"));
        if (command.Action is "duplicate" or "delete" or "move")
            SequenceStructure.Validate(sequenceTelemetry.Resolve(Text(command, "nodeId")), command.Action, sequenceTelemetry.Resolve(Text(command, "parentId")), Integer(command, "index"), sequenceTelemetry.ProjectedCount);
        if (command.Action == "resolveTemplate") SequenceStructure.ReferencedTemplate(sequenceTelemetry.Resolve(Text(command, "nodeId")), sequenceTelemetry.ProjectedCount);
        if (command.Action == "insert") {
            Require(sequenceTelemetry.ProjectedCount < 240, "Sequence has reached the editing size limit.");
            SequenceInsertion.Validate(sequenceTelemetry.Resolve(Text(command, "parentId")), Text(command, "type"), Integer(command, "index"));
        }
        if (command.Action is "skip" or "edit" or "editField")
        {
            var item = sequenceTelemetry.Resolve(Text(command, "nodeId"));
            Require(item is NINA.Sequencer.ISequenceEntity, "Sequence element is no longer in the projected tree.");
            if (command.Action == "skip") Require(item is ISequenceItem && item.GetType().GetProperty("Items") == null, "Skip an instruction, not a container.");
            if (command.Action is "edit" or "editField")
                SequenceEditing.Validate(SequenceEditSchema.Field(item, Text(command, "field")), command.Parameters["value"]);
        }
        if (command.Action == "addTarget") Require(AdvancedAPI.Controls.Sequence.GetAllTargetsInAdvancedSequence().Count < 32, "Target limit reached.");
        if (command.Action == "target") Require(Integer(command, "index") == Number(command, "index") && Integer(command, "index") < AdvancedAPI.Controls.Sequence.GetAllTargetsInAdvancedSequence().Count, "Unknown target index.");
    }
    private void ExecuteSequenceEdit(ControlCommand command)
    {
        var item = sequenceTelemetry.Resolve(Text(command, "nodeId"));
        if (command.Action == "resolveTemplate") {
            var template = SequenceStructure.ReferencedTemplate(item, sequenceTelemetry.ProjectedCount);
            item.GetType().GetProperty("SelectedTemplate").SetValue(item, template);
        }
        if (command.Action == "insert") SequenceInsertion.Insert(sequenceTelemetry.Resolve(Text(command, "parentId")), Text(command, "type"), Integer(command, "index"));
        if (command.Action is "duplicate" or "delete" or "move")
            SequenceStructure.Apply(item, command.Action, sequenceTelemetry.Resolve(Text(command, "parentId")), Integer(command, "index"), sequenceTelemetry.ProjectedCount);
        if (command.Action == "skip") ((ISequenceItem)item).Skip();
        if (command.Action is "edit" or "editField")
            SequenceEditSchema.Apply(item, Text(command, "field"), command.Parameters["value"]);
        if (command.Action == "addTarget")
        {
            var sequence = AdvancedAPI.Controls.Sequence;
            var navigation = sequence.GetType().GetField("sequenceNavigation", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(sequence) ?? throw new InvalidOperationException("Unsupported host.");
            var factory = (ISequencerFactory)navigation.GetType().GetField("factory", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(navigation);
            var created = factory.GetContainer<DeepSkyObjectContainer>();
            created.Name = Text(command, "name"); created.Target.TargetName = Text(command, "name");
            created.Target.InputCoordinates.Coordinates = new NINA.Astrometry.Coordinates(NINA.Astrometry.Angle.ByDegree(Number(command, "ra") * 15), NINA.Astrometry.Angle.ByDegree(Number(command, "dec")), NINA.Astrometry.Epoch.J2000);
            created.Target.PositionAngle = Number(command, "rotation");
            sequence.GetSequenceRoot().Items.OfType<TargetAreaContainer>().Single().Add(created);
        }
        if (command.Action == "target")
        {
            var target = AdvancedAPI.Controls.Sequence.GetAllTargetsInAdvancedSequence()[Integer(command, "index")];
            target.Target.TargetName = Text(command, "name"); target.Name = Text(command, "name");
            target.Target.InputCoordinates.Coordinates = new NINA.Astrometry.Coordinates(NINA.Astrometry.Angle.ByDegree(Number(command, "ra") * 15), NINA.Astrometry.Angle.ByDegree(Number(command, "dec")), NINA.Astrometry.Epoch.J2000);
            target.Target.PositionAngle = Number(command, "rotation");
        }
        sequenceTelemetry.RefreshAfterEdit();
    }
}
