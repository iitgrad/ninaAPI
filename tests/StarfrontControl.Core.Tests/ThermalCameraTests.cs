using StarfrontSimulation;
using Xunit;

namespace StarfrontControl.Core.Tests;

public sealed class ThermalCameraTests
{
    [Fact]
    public void CoolingAndWarmingFollowElapsedTimeWithoutReadFrequencyDependence()
    {
        double seconds = 0;
        var frequent = new ThermalModel(() => seconds);
        var sparse = new ThermalModel(() => seconds);
        frequent.Target = sparse.Target = -10;
        frequent.Cooling = sparse.Cooling = true;
        Assert.Equal(15, frequent.Temperature);
        Assert.Equal(100, frequent.Power);
        for (var i = 1; i <= 60; i++) { seconds = i; _ = frequent.Temperature; }
        Assert.Equal(sparse.Temperature, frequent.Temperature, 8);
        Assert.InRange(frequent.Temperature, -9, -8);
        seconds = 180;
        Assert.InRange(frequent.Temperature, -10, -9.9);
        Assert.InRange(frequent.Power, 62, 64);
        frequent.Cooling = false;
        Assert.Equal(0, frequent.Power);
        seconds = 240;
        Assert.InRange(frequent.Temperature, 13, 15);
    }

    [Fact]
    public void ImpossibleSetPointSaturatesAtPhysicalDeltaAndInvalidInputsAreRejected()
    {
        double seconds = 0;
        var model = new ThermalModel(() => seconds) { Target = -40, Cooling = true };
        seconds = 10000;
        Assert.Equal(-25, model.Temperature, 6);
        Assert.Equal(100, model.Power);
        Assert.Throws<ArgumentOutOfRangeException>(() => model.Target = double.NaN);
        Assert.Throws<ArgumentOutOfRangeException>(() => model.Target = -41);
    }
}
