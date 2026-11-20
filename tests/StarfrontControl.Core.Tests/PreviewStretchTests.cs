using StarfrontControl.Core;
using Xunit;

namespace StarfrontControl.Core.Tests;

public class PreviewStretchTests
{
    [Fact]
    public void FaintScienceStructureSurvivesConversionToEightBits()
    {
        // All these faint levels became 1 or 2 in the old linear JPEG path.
        ushort[] input = [490, 495, 500, 505, 510, 511, 512, 515, 520, 530, 650, 65535];
        var original = input.ToArray();
        var output = PreviewStretch.Render(input);
        Assert.InRange(output[5], 40, 65);
        Assert.True(output[9] - output[5] > 15);
        Assert.True(output[10] > output[9]);
        Assert.Equal(255, output[^1]);
        Assert.Equal(original, input);
    }

    [Fact]
    public void OutlyingStarsDoNotSetTheBackgroundLevel()
    {
        var input = Enumerable.Range(0, 10000).Select(i => (ushort)(500 + i % 11)).ToArray();
        input[^1] = 65535;
        var output = PreviewStretch.Render(input);
        Array.Sort(output);
        Assert.InRange(output[5000], 45, 60);
    }

    [Fact]
    public void FlatAndBlackFramesRemainFiniteAndMonotonic()
    {
        Assert.All(PreviewStretch.Render(new ushort[100]), x => Assert.Equal(0, x));
        Assert.All(PreviewStretch.Render(Enumerable.Repeat((ushort)510, 100).ToArray()), x => Assert.InRange(x, 45, 60));
        var gradient = Enumerable.Range(0, 65536).Select(x => (ushort)x).ToArray();
        var output = PreviewStretch.Render(gradient);
        Assert.True(output.Zip(output.Skip(1)).All(pair => pair.First <= pair.Second));
    }

    [Fact]
    public void RejectsFullResolutionBuffersAndInvalidSettings()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PreviewStretch.Render(new ushort[800 * 800 * 3 + 1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => PreviewStretch.Render([510], double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => PreviewStretch.Render([510], 0.2, double.PositiveInfinity));
    }
}
