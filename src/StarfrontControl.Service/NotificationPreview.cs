using System.Runtime.InteropServices;
using SkiaSharp;

namespace StarfrontControl.Service;

/// <summary>Gentler display curve for notification copies of the dashboard JPEG only.</summary>
public static class NotificationPreview
{
    public const int MaximumBytes = 100000;
    public const int MaximumDimension = 800;
    // Composition of midtones transfer curves: maps 20% to 8%, preserving 0 and 1.
    // This backs off the existing display stretch without new shadow clipping.
    private static readonly byte[] Curve = Enumerable.Range(0, 256).Select(level =>
    {
        var value = level / 255d;
        const double balance = 23d / 31;
        return (byte)Math.Clamp((int)Math.Round(255 * (balance - 1) * value / ((2 * balance - 1) * value - balance)), 0, 255);
    }).ToArray();

    public static byte[]? Render(byte[] jpeg)
    {
        if (jpeg.Length is < 4 or > MaximumBytes) return null;
        using var data = SKData.CreateCopy(jpeg);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.EncodedFormat != SKEncodedImageFormat.Jpeg ||
            codec.Info.Width is < 1 or > MaximumDimension || codec.Info.Height is < 1 or > MaximumDimension) return null;
        using var bitmap = new SKBitmap(new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Opaque));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success) return null;
        var pixels = new byte[bitmap.ByteCount];
        Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
        for (var row = 0; row < bitmap.Height; row++)
            for (var col = 0; col < bitmap.Width; col++)
            {
                var offset = row * bitmap.RowBytes + col * 4;
                for (var channel = 0; channel < 3; channel++) pixels[offset + channel] = Curve[pixels[offset + channel]];
            }
        Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);
        using var image = SKImage.FromBitmap(bitmap);
        foreach (var quality in new[] { 85, 65, 45 })
        {
            using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, quality);
            if (encoded is not null && encoded.Size <= MaximumBytes) return encoded.ToArray();
        }
        return null;
    }
}
