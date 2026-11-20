using System.Globalization;
using System.Text;
using System.Text.Json;

namespace StarfrontControl.Service;

/// SVG embeds the JPEG and an exact affine TAN-to-TAN registration. This avoids
/// native image libraries and keeps the output entirely self-contained.
public static class SurveyFramingRenderer
{
    public static void CheckJpeg(byte[] data, int width, int height) {
        if (data.Length < 4 || data[0] != 255 || data[1] != 216 || data[^2] != 255 || data[^1] != 217) throw new InvalidDataException("Invalid JPEG.");
        for (var i = 2; i + 4 < data.Length;) {
            if (data[i++] != 255) throw new InvalidDataException("Invalid JPEG marker.");
            while (i < data.Length && data[i] == 255) i++;
            if (i >= data.Length) break;
            var marker = data[i++];
            if (marker == 0xda || marker == 0xd9) break;
            if (marker is 0x01 or >= 0xd0 and <= 0xd7) continue;
            if (i + 2 > data.Length) break;
            var length = (data[i] << 8) | data[i+1];
            if (length < 2 || i + length > data.Length) break;
            if (marker is 0xc0 or 0xc1 or 0xc2) {
                if (length >= 8 && ((data[i+3] << 8) | data[i+4]) == height && ((data[i+5] << 8) | data[i+6]) == width) return;
                throw new InvalidDataException("Unexpected JPEG dimensions.");
            }
            i += length;
        }
        throw new InvalidDataException("JPEG has no supported frame header.");
    }
    public static Dictionary<string, double> Header(byte[] data) {
        var values = new Dictionary<string,string>();
        for (var i = 0; i + 80 <= Math.Min(data.Length, 28800); i += 80) {
            var card = Encoding.ASCII.GetString(data, i, 80); var key = card[..8].Trim();
            if (key == "END") break;
            values[key] = card[10..].Split('/')[0].Trim().Trim('\'').Trim();
        }
        double N(string key) => values.TryGetValue(key, out var value) && double.TryParse(value.Replace('D','E'), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : double.NaN;
        if (values.GetValueOrDefault("CTYPE1") != "RA---TAN" || values.GetValueOrDefault("CTYPE2") != "DEC--TAN" || N("NAXIS1") != 768 || N("NAXIS2") != 768 ||
            values.Keys.Any(k => k.StartsWith("PC", StringComparison.Ordinal)) || (values.ContainsKey("CROTA2") && N("CROTA2") != 0)) throw new InvalidDataException("Unexpected survey projection.");
        var w = new[] { "CRPIX1", "CRPIX2", "CRVAL1", "CRVAL2" }.ToDictionary(k => k, N);
        w["CD1_1"] = values.ContainsKey("CD1_1") ? N("CD1_1") : N("CDELT1");
        w["CD2_2"] = values.ContainsKey("CD2_2") ? N("CD2_2") : N("CDELT2");
        w["CD1_2"] = values.ContainsKey("CD1_2") ? N("CD1_2") : 0;
        w["CD2_1"] = values.ContainsKey("CD2_1") ? N("CD2_1") : 0;
        if (w.Values.Any(v => !double.IsFinite(v)) || w["CD1_2"] != 0 || w["CD2_1"] != 0 || w["CD1_1"] == 0 || w["CD2_2"] == 0) throw new InvalidDataException("Invalid survey WCS.");
        return w;
    }
    public static byte[] Wrap(byte[] jpeg, Dictionary<string,object> target, Dictionary<string,double>? source) {
        double T(string k) => Convert.ToDouble(target[k], CultureInfo.InvariantCulture);
        string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        var width = (int)target["NAXIS1"]; var height = (int)target["NAXIS2"];
        var transform = ""; var size = $"width=\"{width}\" height=\"{height}\"";
        if (source != null) {
            double S(string k) => source[k];
            if (Math.Abs(S("CRVAL1")-T("CRVAL1")) > 1e-7 || Math.Abs(S("CRVAL2")-T("CRVAL2")) > 1e-7) throw new InvalidDataException("Survey tangent point changed.");
            var a=T("CD1_1"); var b=T("CD1_2"); var c=T("CD2_1"); var d=T("CD2_2"); var det=a*d-b*c;
            if (!double.IsFinite(det) || Math.Abs(det) < 1e-15) throw new InvalidDataException("Invalid camera WCS.");
            var sx=S("CD1_1"); var sy=S("CD2_2");
            var aa=d*sx/det; var bb=b*sy/det; var cc=c*sx/det; var dd=a*sy/det;
            var x=T("CRPIX1")-.5+aa*(.5-S("CRPIX1"))-bb*(768.5-S("CRPIX2"));
            var y=height+.5-T("CRPIX2")+cc*(.5-S("CRPIX1"))-dd*(768.5-S("CRPIX2"));
            transform = $" transform=\"matrix({F(aa)} {F(cc)} {F(bb)} {F(dd)} {F(x)} {F(y)})\"";
            size = "width=\"768\" height=\"768\"";
        }
        var attribution = source == null ? "DSS2 / CDS" : "DSS2 / NASA SkyView (reference colors)";
        var metadata = System.Security.SecurityElement.Escape(JsonSerializer.Serialize(new { camera = target, survey = source }));
        return Encoding.UTF8.GetBytes($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\"><title>{attribution}</title><metadata>{metadata}</metadata><image {size}{transform} href=\"data:image/jpeg;base64,{Convert.ToBase64String(jpeg)}\"/></svg>");
    }
}
