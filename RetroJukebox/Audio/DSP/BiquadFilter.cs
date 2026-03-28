using System;

namespace RetroJukebox.Audio.DSP;

/// <summary>
/// Direct Form II transposed biquad filter.
/// Numerically stable at high sample rates (96/192 kHz). Each instance holds
/// its own state so every channel/band gets an independent filter.
/// </summary>
public sealed class BiquadFilter
{
    private double _a0, _a1, _a2, _b1, _b2;
    private double _z1, _z2;

    private BiquadFilter(double a0, double a1, double a2, double b1, double b2)
    {
        _a0 = a0; _a1 = a1; _a2 = a2; _b1 = b1; _b2 = b2;
    }

    /// <summary>
    /// Peaking EQ filter — boosts or cuts around <paramref name="frequency"/>
    /// without altering the passband outside the band.
    /// </summary>
    public static BiquadFilter PeakingEQ(int sampleRate, float frequency, float q, float gainDb)
    {
        double A     = Math.Pow(10.0, gainDb / 40.0);   // amplitude (not power)
        double w0    = 2.0 * Math.PI * frequency / sampleRate;
        double cosW0 = Math.Cos(w0);
        double sinW0 = Math.Sin(w0);
        double alpha = sinW0 / (2.0 * q);

        double b0 =  1 + alpha * A;
        double b1 = -2 * cosW0;
        double b2 =  1 - alpha * A;
        double a0 =  1 + alpha / A;
        double a1 = -2 * cosW0;
        double a2 =  1 - alpha / A;

        return new BiquadFilter(b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
    }

    public float Process(float input)
    {
        double output = _a0 * input + _z1;
        _z1 = _a1 * input - _b1 * output + _z2;
        _z2 = _a2 * input - _b2 * output;
        return (float)output;
    }
}
