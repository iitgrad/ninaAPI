using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using NINA.Core.Interfaces;
using NINA.Equipment.Equipment.MyFilterWheel;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Equipment.MyGuider;
using NINA.Equipment.Equipment.MyRotator;
using NINA.Equipment.Equipment.MyWeatherData;
using NINA.Equipment.Interfaces;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Equipment.Interfaces.ViewModel;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Utility.AutoFocus;
using OxyPlot;

namespace ninaAPI.Control;

public sealed partial class ControlAdapter : IFocuserConsumer, IFilterWheelConsumer, IRotatorConsumer, IWeatherDataConsumer, IGuiderConsumer
{
    private readonly Queue<object> focusPoints = new();
    private readonly Queue<object> guidePoints = new();
    private readonly Queue<object> imageHistory = new();
    private readonly Channel<BitmapSource> previews = Channel.CreateBounded<BitmapSource>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropOldest });
    private Task previewWorker;
    private long imageNumber;
    private long previewNumber;
    private void InitializeDashboard()
    {
        var c = AdvancedAPI.Controls;
        c.Focuser.RegisterConsumer(this); c.FilterWheel.RegisterConsumer(this); c.Rotator.RegisterConsumer(this); c.Weather.RegisterConsumer(this); c.Guider.RegisterConsumer(this);
        c.SafetyMonitor.IsSafeChanged += SafetyChanged;
        c.SafetyMonitor.Connected += SafetyConnection; c.SafetyMonitor.Disconnected += SafetyConnection;
        c.Guider.GuideEvent += GuideEvent;
        c.ImageSaveMediator.ImageSaved += ImageSaved;
        c.Imaging.ImagePrepared += ImagePrepared;
        previewWorker = PreviewLoop();
        UpdateDeviceInfo(c.Focuser.GetInfo()); UpdateDeviceInfo(c.FilterWheel.GetInfo()); UpdateDeviceInfo(c.Rotator.GetInfo()); UpdateDeviceInfo(c.Weather.GetInfo()); UpdateDeviceInfo(c.Guider.GetInfo());
        PublishSafety();
    }
    public void UpdateDeviceInfo(FocuserInfo info) => PublishDevice("focuser", info, "Connected", "Name", "Position", "Temperature", "IsMoving", "StepSize", "MaxStep", "TempComp");
    public void UpdateDeviceInfo(FilterWheelInfo info)
    {
        var fields = Copy(info, "Connected", "Name", "IsMoving");
        fields["position"] = info.SelectedFilter?.Position;
        fields["filter"] = info.SelectedFilter?.Name;
        Enqueue("filterWheel", fields);
    }
    public void UpdateDeviceInfo(RotatorInfo info) => PublishDevice("rotator", info, "Connected", "Name", "Position", "MechanicalPosition", "IsMoving", "Reverse");
    public void UpdateDeviceInfo(WeatherDataInfo info) => PublishDevice("weather", info, "Connected", "Name", "Temperature", "Humidity", "DewPoint", "CloudCover", "WindSpeed", "WindGust", "WindDirection", "Pressure", "RainRate", "SkyBrightness", "SkyQuality", "SkyTemperature", "StarFWHM");
    public void UpdateDeviceInfo(GuiderInfo info)
    {
        var fields = Copy(info, "Connected", "Name", "PixelScale");
        fields["rms"] = new { ra=Copy(info.RMSError?.RA,"Pixel","Arcseconds"),dec=Copy(info.RMSError?.Dec,"Pixel","Arcseconds"),total=Copy(info.RMSError?.Total,"Pixel","Arcseconds") };
        Enqueue("guider", fields);
    }
    private void PublishSafety() => PublishDevice("safety", AdvancedAPI.Controls.SafetyMonitor.GetInfo(), "Connected", "Name", "IsSafe");
    private void SafetyChanged(object sender, IsSafeEventArgs args) => PublishSafety();
    private Task SafetyConnection(object sender, EventArgs args) { PublishSafety(); return Task.CompletedTask; }
    public void AutoFocusRunStarting() { lock (gate) { focusPoints.Clear(); Enqueue("autofocus", new { running = true, points = focusPoints.ToArray() }); } }
    public void NewAutoFocusPoint(DataPoint point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) return;
        lock (gate) { if (disposed) return; if (focusPoints.Count >= 64) focusPoints.Dequeue(); focusPoints.Enqueue(new { position = point.X, hfr = point.Y }); Enqueue("autofocus", new { running = true, points = focusPoints.ToArray() }); }
    }
    public void UpdateEndAutoFocusRun(AutoFocusInfo info) { lock (gate) Enqueue("autofocus", new { running = false, result = Copy(info, "Temperature", "Filter", "Position", "HFR"), points = focusPoints.ToArray() }); }
    public void UpdateUserFocused(FocuserInfo info) => UpdateDeviceInfo(info);
    private void GuideEvent(object sender, IGuideStep step)
    {
        lock (gate)
        {
            if (disposed) return;
            if (guidePoints.Count >= 120) guidePoints.Dequeue();
            guidePoints.Enqueue(new { at = DateTimeOffset.UtcNow, ra = double.IsFinite(step.RADistanceRaw) ? (double?)step.RADistanceRaw : null, dec = double.IsFinite(step.DECDistanceRaw) ? (double?)step.DECDistanceRaw : null, raDuration = step.RADuration, decDuration = step.DECDuration });
            Enqueue("guideHistory", new { points = guidePoints.ToArray(), units = "guider raw distance", capacity = 120 });
        }
    }
    private void ImageSaved(object sender, ImageSavedEventArgs e)
    {
        try
        {
            var id=Guid.NewGuid().ToString("N");
            RegisterSavedFile(id,e.PathToImage.LocalPath);
            var image = new { id, number = Interlocked.Increment(ref imageNumber), at = DateTimeOffset.UtcNow,
                filename = Path.GetFileName(e.PathToImage?.LocalPath), exposure = e.Duration, filter = e.Filter,
                width = e.Image?.PixelWidth, height = e.Image?.PixelHeight,
                mean = Finite(e.Statistics?.Mean), median = Finite(e.Statistics?.Median), stDev = Finite(e.Statistics?.StDev), min = Finite(e.Statistics?.Min), max = Finite(e.Statistics?.Max),
                stars = e.StarDetectionAnalysis?.DetectedStars, hfr = Finite(e.StarDetectionAnalysis?.HFR), hfrStDev = Finite(e.StarDetectionAnalysis?.HFRStDev),
                gain = e.MetaData?.Camera?.Gain, offset = e.MetaData?.Camera?.Offset, imageType = e.MetaData?.Image?.ImageType };
            lock (gate) { if (disposed) return; if (imageHistory.Count >= 32) imageHistory.Dequeue(); imageHistory.Enqueue(image); Enqueue("imageHistory", new { items = imageHistory.ToArray(), capacity = 32 }); }
            Enqueue("image", image);
        }
        catch (Exception) { Interlocked.Increment(ref publishErrors); }
    }
    private static double? Finite(double? value) => value.HasValue && double.IsFinite(value.Value) ? value : null;
    private void ImagePrepared(object sender, ImagePreparedEventArgs e)
    {
        try
        {
            if (disposed) return;
            var bitmap = e.RenderedImage?.Image;
            if (bitmap is null) return;
            // Never freeze/mutate a host-owned image. Nonfrozen sources are copied
            // to a small thumbnail on their owning thread before enqueueing.
            if (!bitmap.IsFrozen)
            {
                var thumb = new TransformedBitmap(bitmap, new ScaleTransform(Math.Min(1, 800d / bitmap.PixelWidth), Math.Min(1, 800d / bitmap.PixelWidth)));
                bitmap = BitmapFrame.Create(thumb); bitmap.Freeze();
            }
            previews.Writer.TryWrite(bitmap);
        }
        catch (Exception) { Interlocked.Increment(ref publishErrors); }
    }
    private async Task PreviewLoop()
    {
        try
        {
            while (await previews.Reader.WaitToReadAsync(stop.Token).ConfigureAwait(false))
            {
                if (!previews.Reader.TryRead(out var source)) continue;
                try
                {
                    byte[] bytes = null; int width = 0, height = 0;
                    foreach (var limit in new[] { 800, 480, 256 })
                    {
                        var scale = Math.Min(1, (double)limit / Math.Max(source.PixelWidth, source.PixelHeight));
                        var image = new TransformedBitmap(source, new ScaleTransform(scale, scale)); image.Freeze();
                        var encoder = new JpegBitmapEncoder { QualityLevel = 65 }; encoder.Frames.Add(BitmapFrame.Create(image));
                        using var memory = new MemoryStream(); encoder.Save(memory); bytes = memory.ToArray(); width = image.PixelWidth; height = image.PixelHeight;
                        if (bytes.Length <= 40 * 1024) break;
                    }
                    if (bytes.Length > 40 * 1024) throw new InvalidDataException("Preview exceeds bound.");
                    Enqueue("preview", new { revision = Interlocked.Increment(ref previewNumber), width, height, jpeg = Convert.ToBase64String(bytes), bytes = bytes.Length });
                }
                catch (Exception) { Interlocked.Increment(ref publishErrors); }
                source = null;
            }
        }
        catch (OperationCanceledException) { }
    }
    private void DisposeDashboard()
    {
        var c = AdvancedAPI.Controls;
        c.Focuser.RemoveConsumer(this); c.FilterWheel.RemoveConsumer(this); c.Rotator.RemoveConsumer(this); c.Weather.RemoveConsumer(this); c.Guider.RemoveConsumer(this);
        c.SafetyMonitor.IsSafeChanged -= SafetyChanged; c.SafetyMonitor.Connected -= SafetyConnection; c.SafetyMonitor.Disconnected -= SafetyConnection;
        c.Guider.GuideEvent -= GuideEvent; c.ImageSaveMediator.ImageSaved -= ImageSaved; c.Imaging.ImagePrepared -= ImagePrepared;
        previews.Writer.TryComplete(); while (previews.Reader.TryRead(out _)) { }
        lock (gate) { focusPoints.Clear(); guidePoints.Clear(); imageHistory.Clear(); savedFiles.Clear(); savedIds.Clear(); }
    }
}
