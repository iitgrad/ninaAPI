using System;
using System.Diagnostics;

namespace StarfrontSimulation;

/// First-order simulated sensor/cooler. Updated on reads; no timer, queue or history.
public sealed class ThermalModel
{
    private readonly object gate = new();
    private readonly Func<double> clock;
    private double last;
    private double temperature = 15;
    private double target = -10;
    private bool cooling;
    public ThermalModel(Func<double>? seconds = null)
    {
        var watch = Stopwatch.StartNew();
        clock = seconds ?? (() => watch.Elapsed.TotalSeconds);
        last = clock();
    }
    private void Advance()
    {
        var now = clock();
        var elapsed = Math.Max(0, now - last);
        last = now;
        var equilibrium = cooling ? Math.Max(-25, Math.Min(15, target)) : 15;
        temperature = equilibrium + (temperature - equilibrium) * Math.Exp(-elapsed / 20);
    }
    public double Temperature { get { lock (gate) { Advance(); return temperature; } } }
    public double Target
    {
        get { lock (gate) return target; }
        set
        {
            if (!double.IsFinite(value) || value < -40 || value > 30) throw new ArgumentOutOfRangeException(nameof(value));
            lock (gate) { Advance(); target = value; }
        }
    }
    public bool Cooling
    {
        get { lock (gate) return cooling; }
        set { lock (gate) { Advance(); cooling = value; } }
    }
    public double Power
    {
        get
        {
            lock (gate)
            {
                Advance();
                return cooling ? Math.Clamp((15 - target) / 40 * 100 + (temperature - target) * 3, 0, 100) : 0;
            }
        }
    }
}
