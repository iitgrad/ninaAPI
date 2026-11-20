using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ninaAPI.Control;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ushort[] pixels = Enumerable.Range(0, 1200 * 800).Select(i => (ushort)(505 + i % 11)).ToArray();
        var source = BitmapSource.Create(1200, 800, 96, 96, PixelFormats.Gray16, null, pixels, 2400);
        Check(source, "nonfrozen-gray16");
        // Exercise the exact precision promotion in the ImagePrepared callback.
        var queued = new WriteableBitmap(new TransformedBitmap(source, new ScaleTransform(2d / 3, 2d / 3)));
        queued.Freeze();
        Check(queued, "queued-float-thumbnail");
        source.Freeze();
        Check(source, "frozen-gray16");
        var rgb = BitmapSource.Create(1200, 800, 96, 96, PixelFormats.Rgb48, null,
            pixels.SelectMany(x => new[] { x, x, x }).ToArray(), 7200);
        Check(rgb, "rgb48");
        var gray8 = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Gray8, null, new byte[] { 20, 50, 100, 255 }, 2);
        var pass = DashboardPreview.Render(gray8, 0.2, -2.8);
        var actual = new byte[4]; pass.CopyPixels(actual, 2, 0);
        if (!actual.SequenceEqual(new byte[] { 20, 50, 100, 255 })) throw new Exception("8-bit image was stretched twice");

        if (args.Length == 2)
        {
            var before = SHA256.HashData(File.ReadAllBytes(args[0]));
            var science = ReadFits(args[0]); science.Freeze();
            Check(science, "real-science-FITS");
            var encoded = DashboardPreview.Encode(science, 0.2, -2.8);
            File.WriteAllBytes(args[1], encoded.Bytes);
            var decoded = new JpegBitmapDecoder(new Uri(Path.GetFullPath(args[1])), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            var gray = new FormatConvertedBitmap(decoded, PixelFormats.Gray8, null, 0);
            var bytes = new byte[gray.PixelWidth * gray.PixelHeight]; gray.CopyPixels(bytes, gray.PixelWidth, 0);
            Array.Sort(bytes);
            if (bytes[bytes.Length / 2] < 35 || encoded.Bytes.Length > 40 * 1024) throw new Exception("JPEG lost stretch or exceeded transport bound");
            if (!before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(args[0])))) throw new Exception("Science FITS changed");
            Console.WriteLine(JsonSerializer.Serialize(new { jpegBytes = encoded.Bytes.Length, encoded.Width, encoded.Height, jpegMedian = bytes[bytes.Length / 2], sourceSHA256 = Convert.ToHexString(before) }));
        }
    }

    private static BitmapSource Check(BitmapSource source, string name)
    {
        var rendered = DashboardPreview.Render(source, 0.2, -2.8);
        var grayscale = new FormatConvertedBitmap(rendered, PixelFormats.Gray8, null, 0);
        var pixels = new byte[grayscale.PixelWidth * grayscale.PixelHeight]; grayscale.CopyPixels(pixels, grayscale.PixelWidth, 0);
        Array.Sort(pixels);
        var median = pixels[pixels.Length / 2]; var p99 = pixels[(int)(pixels.Length * 0.99)];
        if (median < 35 || median > 75) throw new Exception($"{name}: background {median} is not stretched");
        if (!rendered.IsFrozen || Math.Max(rendered.PixelWidth, rendered.PixelHeight) > 800) throw new Exception("Unbounded/unfrozen preview");
        Console.WriteLine(JsonSerializer.Serialize(new { name, inputFormat = source.Format.ToString(), outputFormat = rendered.Format.ToString(), width = rendered.PixelWidth, height = rendered.PixelHeight, median, p99 }));
        return rendered;
    }

    private static BitmapSource ReadFits(string path)
    {
        var bytes = File.ReadAllBytes(path); var header = new Dictionary<string, string>(); var offset = 0;
        for (; offset + 80 <= bytes.Length; offset += 80)
        {
            var card = Encoding.ASCII.GetString(bytes, offset, 80); var key = card[..8].Trim();
            if (key == "END") { offset = (offset + 80 + 2879) / 2880 * 2880; break; }
            if (card[8] == '=') header[key] = card[10..].Split('/')[0].Trim();
        }
        if (header["BITPIX"] != "16" || header["NAXIS"] != "2") throw new Exception("Checks require a 16-bit monochrome FITS");
        var width = int.Parse(header["NAXIS1"]); var height = int.Parse(header["NAXIS2"]);
        var zero = int.Parse(header.GetValueOrDefault("BZERO", "0"));
        if (double.Parse(header.GetValueOrDefault("BSCALE", "1"), System.Globalization.CultureInfo.InvariantCulture) != 1) throw new Exception("Unsupported FITS scale");
        var pixels = new ushort[checked(width * height)];
        for (var index = 0; index < pixels.Length; index++) pixels[index] = checked((ushort)(BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(offset + index * 2, 2)) + zero));
        return BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray16, null, pixels, width * 2);
    }
}
