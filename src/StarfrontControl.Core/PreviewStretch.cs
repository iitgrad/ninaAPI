namespace StarfrontControl.Core;

/// <summary>Display-only midtones stretch, before a 16-bit thumbnail loses precision to JPEG.</summary>
public static class PreviewStretch
{
    public static byte[] Render(ReadOnlySpan<ushort> pixels, double targetBackground = 0.2, double blackClipping = -2.8)
    {
        if (pixels.IsEmpty) return [];
        if (pixels.Length > 800 * 800 * 3) throw new ArgumentOutOfRangeException(nameof(pixels));
        if (!double.IsFinite(targetBackground) || targetBackground <= 0 || targetBackground >= 1)
            throw new ArgumentOutOfRangeException(nameof(targetBackground));
        if (!double.IsFinite(blackClipping) || blackClipping > 0 || blackClipping < -20)
            throw new ArgumentOutOfRangeException(nameof(blackClipping));

        var histogram = new int[65536];
        foreach (var pixel in pixels) histogram[pixel]++;
        var median = Percentile(histogram, pixels.Length);
        var deviations = new int[65536];
        for (var value = 0; value < histogram.Length; value++)
            deviations[Math.Abs(value - median)] += histogram[value];
        var mad = Percentile(deviations, pixels.Length);

        // A one-ADU floor keeps flat low-noise frames finite. Statistics and the
        // transfer are computed only on the bounded thumbnail, never the host FITS.
        var shadows = Math.Max(0, median + blackClipping * 1.4826 * Math.Max(1, mad)) / 65535.0;
        var normalizedMedian = median / 65535.0;
        var balance = Transfer(targetBackground, Math.Clamp(normalizedMedian - shadows, 1.0 / 65535, 1 - 1.0 / 65535));
        var map = new byte[65536];
        for (var value = 0; value < map.Length; value++)
            map[value] = (byte)Math.Clamp((int)Math.Round(255 * Transfer(balance, value / 65535.0 - shadows)), 0, 255);
        var result = new byte[pixels.Length];
        for (var index = 0; index < pixels.Length; index++) result[index] = map[pixels[index]];
        return result;
    }

    private static int Percentile(int[] histogram, int count)
    {
        var cumulative = 0;
        for (var index = 0; index < histogram.Length; index++)
            if ((cumulative += histogram[index]) > (count - 1) / 2) return index;
        return histogram.Length - 1;
    }

    private static double Transfer(double balance, double value) => value <= 0 ? 0 : value >= 1 ? 1
        : (balance - 1) * value / ((2 * balance - 1) * value - balance);
}
