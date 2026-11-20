using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StarfrontControl.Core;

namespace ninaAPI.Control;

internal static class DashboardPreview
{
    internal static (byte[] Bytes, int Width, int Height) Encode(BitmapSource source, double background, double clipping)
    {
        var rendered = Render(source, background, clipping);
        foreach (var limit in new[] { 800, 480, 256 })
        {
            var scale = Math.Min(1, (double)limit / Math.Max(rendered.PixelWidth, rendered.PixelHeight));
            var image = new TransformedBitmap(rendered, new ScaleTransform(scale, scale)); image.Freeze();
            var encoder = new JpegBitmapEncoder { QualityLevel = 65 };
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var memory = new MemoryStream(); encoder.Save(memory);
            if (memory.Length <= 40 * 1024) return (memory.ToArray(), image.PixelWidth, image.PixelHeight);
        }
        throw new InvalidDataException("Preview exceeds bound.");
    }

    internal static BitmapSource Render(BitmapSource source, double background, double clipping)
    {
        var scale = Math.Min(1, 800d / Math.Max(source.PixelWidth, source.PixelHeight));
        BitmapSource thumbnail = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        // WPF resampling promotes Gray16 to Gray32Float. Preserve its precision
        // through a 16-bit conversion; Gray8/JPEG at this point destroys nebulosity.
        var gray = source.Format == PixelFormats.Gray16 || source.Format == PixelFormats.Gray32Float;
        var color = source.Format == PixelFormats.Rgb48 || source.Format == PixelFormats.Rgba64
            || source.Format == PixelFormats.Prgba64 || source.Format == PixelFormats.Rgba128Float
            || source.Format == PixelFormats.Prgba128Float || source.Format == PixelFormats.Rgb128Float;
        if (gray || color)
        {
            var channels = gray ? 1 : 3;
            thumbnail = new FormatConvertedBitmap(thumbnail, gray ? PixelFormats.Gray16 : PixelFormats.Rgb48, null, 0);
            var pixels = new ushort[thumbnail.PixelWidth * thumbnail.PixelHeight * channels];
            thumbnail.CopyPixels(pixels, thumbnail.PixelWidth * channels * 2, 0);
            var bytes = PreviewStretch.Render(pixels, background, clipping);
            thumbnail = BitmapSource.Create(thumbnail.PixelWidth, thumbnail.PixelHeight, 96, 96,
                gray ? PixelFormats.Gray8 : PixelFormats.Rgb24, null, bytes, thumbnail.PixelWidth * channels);
        }
        thumbnail.Freeze();
        return thumbnail;
    }
}
