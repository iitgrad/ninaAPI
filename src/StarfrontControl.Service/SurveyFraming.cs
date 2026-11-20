using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace StarfrontControl.Service;

public sealed record FramingProfile(string ProfileId, string RigName, double FieldWidthDegrees, double FieldHeightDegrees);
public sealed record SurveyField(double Ra, double Dec, double Width, double Height, double Rotation)
{
    public bool Valid => new[] { Ra, Dec, Width, Height, Rotation }.All(double.IsFinite)
        && Ra >= 0 && Ra < 360 && Math.Abs(Dec) <= 90 && Width >= .01 && Width <= 20 && Height >= .01 && Height <= 20
        && Math.Abs(Rotation) <= 360;
    public string Key => string.Join(':', new[] { Ra, Dec, Width, Height, Rotation }.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
    public Dictionary<string, object> Wcs() {
        if (!Valid) throw new ArgumentException("Invalid survey field.");
        var width = Width >= Height ? 512 : Math.Max(16, (int)Math.Round(512 * Width / Height));
        var height = Height >= Width ? 512 : Math.Max(16, (int)Math.Round(512 * Height / Width));
        var radians = Math.PI / 180; var angle = Rotation * radians;
        var sx = 2 * Math.Tan(Width * radians / 2) / radians / width;
        var sy = 2 * Math.Tan(Height * radians / 2) / radians / height;
        return new() { ["NAXIS"]=2,["NAXIS1"]=width,["NAXIS2"]=height,["CTYPE1"]="RA---TAN",["CTYPE2"]="DEC--TAN",
            ["CUNIT1"]="deg",["CUNIT2"]="deg",["CRPIX1"]=(width+1)/2d,["CRPIX2"]=(height+1)/2d,["CRVAL1"]=Ra,["CRVAL2"]=Dec,
            ["CD1_1"]=-sx*Math.Cos(angle),["CD1_2"]=sy*Math.Sin(angle),["CD2_1"]=sx*Math.Sin(angle),["CD2_2"]=sy*Math.Cos(angle) };
    }
}

// Public surveys only. Does not contact NINA or read/write science images.
public sealed class SurveyFraming : IDisposable
{
    private readonly HttpClient http;
    private readonly string directory;
    private readonly TimeSpan timeout;
    private readonly SemaphoreSlim slots = new(2), admission = new(10);
    private readonly Dictionary<string, byte[]> cache = new();
    private readonly Queue<string> order = new();
    public SurveyFraming() : this(null, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "starfront-web", "Framing-v2")) { }
    public SurveyFraming(HttpMessageHandler? handler, string directory, TimeSpan? timeout = null) {
        http = new(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        this.directory = directory; this.timeout = timeout ?? TimeSpan.FromSeconds(50);
    }
    public async Task<byte[]?> Image(SurveyField field, CancellationToken cancellation) {
        if (!field.Valid) throw new ArgumentException("Invalid survey field.");
        lock (cache) if (cache.TryGetValue(field.Key, out var hit)) return hit;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(timeout); var token = deadline.Token;
        if (!await admission.WaitAsync(0, token)) return null;
        try {
            await slots.WaitAsync(token);
            try {
                lock (cache) if (cache.TryGetValue(field.Key, out var hit)) return hit;
                var path = Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("v2:" + field.Key))) + ".svg");
                try {
                    if (new FileInfo(path) is { Exists: true, Length: > 100 and < 2_000_000 }) {
                        var disk = await File.ReadAllBytesAsync(path, token);
                        // Only our generated, self-contained SVGs are cached.
                        if (Encoding.UTF8.GetString(disk).StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\"", StringComparison.Ordinal)) {
                            Remember(field.Key, disk); return disk;
                        }
                    }
                } catch (IOException) { } catch (UnauthorizedAccessException) { }
                byte[] image;
                try {
                    using var primaryDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
                    primaryDeadline.CancelAfter(TimeSpan.FromSeconds(7));
                    var wcs = Uri.EscapeDataString(JsonSerializer.Serialize(field.Wcs()));
                    var jpeg = await Fetch("https://alasky.cds.unistra.fr/hips-image-services/hips2fits?hips=CDS%2FP%2FDSS2%2Fcolor&format=jpg&stretch=asinh&min_cut=0.5%25&max_cut=99.5%25&wcs=" + wcs, 512*1024, primaryDeadline.Token);
                    var geometry = field.Wcs();
                    SurveyFramingRenderer.CheckJpeg(jpeg, (int)geometry["NAXIS1"], (int)geometry["NAXIS2"]);
                    image = SurveyFramingRenderer.Wrap(jpeg, geometry, null);
                } catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException) {
                    token.ThrowIfCancellationRequested();
                    image = await SkyView(field, token);
                }
                token.ThrowIfCancellationRequested(); Remember(field.Key, image);
                try {
                    Directory.CreateDirectory(directory);
                    var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try { await File.WriteAllBytesAsync(temporary, image, token); File.Move(temporary, path, true); }
                    finally { File.Delete(temporary); }
                    foreach (var stale in new DirectoryInfo(directory).GetFiles("*.svg").OrderByDescending(f => f.LastWriteTimeUtc).Skip(32)) stale.Delete();
                } catch (IOException) { } catch (UnauthorizedAccessException) { }
                return image;
            } finally { slots.Release(); }
        } finally { admission.Release(); }
    }
    private void Remember(string key, byte[] image) {
        lock (cache) {
            if (cache.ContainsKey(key)) return;
            if (order.Count >= 8) cache.Remove(order.Dequeue());
            cache[key] = image; order.Enqueue(key);
        }
    }
    private async Task<byte[]> SkyView(SurveyField field, CancellationToken token) {
        var w = field.Wcs(); double N(string key) => Convert.ToDouble(w[key], CultureInfo.InvariantCulture);
        var scale = Math.Max(Math.Abs(N("CD1_1"))*N("NAXIS1") + Math.Abs(N("CD1_2"))*N("NAXIS2"),
            Math.Abs(N("CD2_1"))*N("NAXIS1") + Math.Abs(N("CD2_2"))*N("NAXIS2")) / 768 * 1.02;
        var url = "https://skyview.gsfc.nasa.gov/current/cgi/runquery.pl?Survey=DSS2%20Red,DSS2%20Red,DSS2%20Blue&Position=" +
            field.Ra.ToString("R", CultureInfo.InvariantCulture) + "," + field.Dec.ToString("R", CultureInfo.InvariantCulture) +
            "&Pixels=768,768&Scale=" + scale.ToString("R", CultureInfo.InvariantCulture) + "&Projection=Tan&Coordinates=J2000&Rotation=0&RGB=true&Scaling=HistEq&Return=filename";
        var name = Encoding.UTF8.GetString(await Fetch(url, 1024, token)).Trim();
        if (name.Length is < 1 or > 99 || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_' && c != '-')) throw new InvalidDataException("Invalid SkyView image set.");
        var root = "https://skyview.gsfc.nasa.gov/tempspace/fits/" + name;
        var jpegTask = Fetch(root + "_rgb.jpg", 1_000_000, token);
        var fitsTask = Fetch(root + "_1.fits", 4_000_000, token);
        await Task.WhenAll(jpegTask, fitsTask);
        var jpeg = await jpegTask; SurveyFramingRenderer.CheckJpeg(jpeg, 768, 768);
        return SurveyFramingRenderer.Wrap(jpeg, w, SurveyFramingRenderer.Header(await fitsTask));
    }
    private async Task<byte[]> Fetch(string url, int bound, CancellationToken token) {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > bound) throw new InvalidDataException("Survey response too large.");
        using var stream = await response.Content.ReadAsStreamAsync(token); using var bytes = new MemoryStream();
        var buffer = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(buffer, token)) > 0) {
            if (bytes.Length + count > bound) throw new InvalidDataException("Survey response too large.");
            bytes.Write(buffer, 0, count);
        }
        return bytes.ToArray();
    }
    public void Dispose() { http.Dispose(); slots.Dispose(); admission.Dispose(); }
}
