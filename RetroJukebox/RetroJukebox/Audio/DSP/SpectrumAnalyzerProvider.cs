using NAudio.Wave;
using System;
using System.Numerics;

namespace RetroJukebox.Audio.DSP;

/// <summary>
/// Passthrough ISampleProvider that captures audio for real-time FFT spectrum analysis.
/// Audio is NOT modified. Uses a Hann window to reduce spectral leakage.
/// GetSpectrumData() is thread-safe and returns a snapshot of the latest magnitude bins.
/// </summary>
public sealed class SpectrumAnalyzerProvider : ISampleProvider
{
    public const int FftSize = 2048; // Must be a power of 2

    private readonly ISampleProvider _source;
    private readonly float[] _circularBuffer;
    private readonly float[] _hannWindow;
    private readonly object  _lock = new();

    private int     _writePos;
    private float[] _latestMagnitudes = new float[FftSize / 2];

    public WaveFormat WaveFormat => _source.WaveFormat;

    public SpectrumAnalyzerProvider(ISampleProvider source)
    {
        _source         = source;
        _circularBuffer = new float[FftSize];
        _hannWindow     = new float[FftSize];

        // Precompute Hann window
        for (int i = 0; i < FftSize; i++)
            _hannWindow[i] = (float)(0.5 * (1 - Math.Cos(2 * Math.PI * i / (FftSize - 1))));
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read     = _source.Read(buffer, offset, count);
        int channels = _source.WaveFormat.Channels;

        for (int i = 0; i < read; i += channels)
        {
            // Mix all channels to mono for analysis
            float mono = 0f;
            for (int ch = 0; ch < channels && i + ch < read; ch++)
                mono += buffer[offset + i + ch];
            mono /= channels;

            _circularBuffer[_writePos] = mono;
            _writePos = (_writePos + 1) % FftSize;

            if (_writePos == 0)
                ComputeFFT();
        }
        return read;
    }

    private void ComputeFFT()
    {
        var fft = new Complex[FftSize];
        for (int i = 0; i < FftSize; i++)
            fft[i] = new Complex(_circularBuffer[i] * _hannWindow[i], 0);

        FFT(fft);

        var mag = new float[FftSize / 2];
        for (int i = 0; i < FftSize / 2; i++)
            mag[i] = (float)fft[i].Magnitude;

        lock (_lock) { _latestMagnitudes = mag; }
    }

    /// <summary>Returns a thread-safe snapshot of the current FFT magnitude bins (FftSize/2 values).</summary>
    public float[] GetSpectrumData()
    {
        lock (_lock) { return (float[])_latestMagnitudes.Clone(); }
    }

    // Cooley–Tukey in-place FFT (radix-2 DIT)
    private static void FFT(Complex[] buf)
    {
        int n = buf.Length;

        // Bit-reversal permutation
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) (buf[i], buf[j]) = (buf[j], buf[i]);
        }

        // Butterfly stages
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang  = -2 * Math.PI / len;
            var    wLen = new Complex(Math.Cos(ang), Math.Sin(ang));
            for (int i = 0; i < n; i += len)
            {
                var w = Complex.One;
                for (int j = 0; j < len / 2; j++)
                {
                    var u = buf[i + j];
                    var v = buf[i + j + len / 2] * w;
                    buf[i + j]           = u + v;
                    buf[i + j + len / 2] = u - v;
                    w *= wLen;
                }
            }
        }
    }
}
