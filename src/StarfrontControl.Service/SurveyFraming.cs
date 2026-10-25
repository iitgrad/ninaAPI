using System.Globalization;
using System.Text.Json;

namespace StarfrontControl.Service;

public sealed record FramingProfile(string ProfileId, string RigName, double FieldWidthDegrees, double FieldHeightDegrees);
public sealed record SurveyField(double Ra, double Dec, double Width, double Height, double Rotation)
{
    public bool Valid => new[] { Ra, Dec, Width, Height, Rotation }.All(double.IsFinite)
        && Ra >= 0 && Ra < 360 && Math.Abs(Dec) <= 90 && Width >= .01 && Width <= 20 && Height >= .01 && Height <= 20
        && Math.Abs(Rotation) <= 360;
    public string Key => string.Join(':',new[] { Ra, Dec, Width, Height, Rotation }.Select(v=>v.ToString("F6",CultureInfo.InvariantCulture)));
    public Dictionary<string,object> Wcs() {
        if (!Valid) throw new ArgumentException("Invalid survey field.");
        var width = Width >= Height ? 512 : Math.Max(16,(int)Math.Round(512*Width/Height));
        var height = Height >= Width ? 512 : Math.Max(16,(int)Math.Round(512*Height/Width));
        var radians=Math.PI/180;var angle=Rotation*radians;
        var sx=2*Math.Tan(Width*radians/2)/radians/width;
        var sy=2*Math.Tan(Height*radians/2)/radians/height;
        // FITS +Y is up, RA increases east. At PA0 north is up and east is left;
        // at PA90 the top of the camera points east.
        return new() { ["NAXIS"]=2,["NAXIS1"]=width,["NAXIS2"]=height,["CTYPE1"]="RA---TAN",["CTYPE2"]="DEC--TAN",
            ["CUNIT1"]="deg",["CUNIT2"]="deg",["CRPIX1"]=(width+1)/2d,["CRPIX2"]=(height+1)/2d,["CRVAL1"]=Ra,["CRVAL2"]=Dec,
            ["CD1_1"]=-sx*Math.Cos(angle),["CD1_2"]=sy*Math.Sin(angle),["CD2_1"]=sx*Math.Sin(angle),["CD2_2"]=sy*Math.Cos(angle) };
    }
}

// Fixed CDS endpoint; no arbitrary URL fetches, FITS downloads, disk writes or NUC work.
// At most8 JPEGs (4MiB) retained. Two simultaneous fetches, at most8 queued requests.
public sealed class SurveyFraming : IDisposable
{
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect=false }) { Timeout=TimeSpan.FromSeconds(25) };
    private readonly SemaphoreSlim slots=new(2),admission=new(10);
    private readonly Dictionary<string,byte[]> cache=new();
    private readonly Queue<string> order=new();
    public async Task<byte[]?> Image(SurveyField field,CancellationToken token) {
        if(!field.Valid)throw new ArgumentException("Invalid survey field.");
        lock(cache)if(cache.TryGetValue(field.Key,out var hit))return hit;
        if(!await admission.WaitAsync(0,token))return null;
        try {
            await slots.WaitAsync(token);
            try {
                lock(cache)if(cache.TryGetValue(field.Key,out var hit))return hit;
                using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(25));
                token=deadline.Token;
                var wcs=Uri.EscapeDataString(JsonSerializer.Serialize(field.Wcs()));
                using var response=await http.GetAsync("https://alasky.cds.unistra.fr/hips-image-services/hips2fits?hips=CDS%2FP%2FDSS2%2Fcolor&format=jpg&stretch=asinh&min_cut=0.5%25&max_cut=99.5%25&wcs="+wcs,HttpCompletionOption.ResponseHeadersRead,token);
                response.EnsureSuccessStatusCode();
                if(response.Content.Headers.ContentType?.MediaType!="image/jpeg"||response.Content.Headers.ContentLength>512*1024)throw new InvalidDataException("Survey response is not a bounded JPEG.");
                using var stream=await response.Content.ReadAsStreamAsync(token);using var bytes=new MemoryStream();var buffer=new byte[8192];int count;
                while((count=await stream.ReadAsync(buffer,token))>0){if(bytes.Length+count>512*1024)throw new InvalidDataException("Survey image exceeds512KiB.");bytes.Write(buffer,0,count);}
                var image=bytes.ToArray();if(image.Length<3||image[0]!=0xff||image[1]!=0xd8)throw new InvalidDataException("Invalid survey JPEG.");
                lock(cache){if(!cache.ContainsKey(field.Key)){if(order.Count>=8)cache.Remove(order.Dequeue());cache[field.Key]=image;order.Enqueue(field.Key);}}
                return image;
            }finally{slots.Release();}
        }finally{admission.Release();}
    }
    public void Dispose(){http.Dispose();slots.Dispose();admission.Dispose();}
}
