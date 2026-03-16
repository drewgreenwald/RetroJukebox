using NAudio.Wave;
using System;

namespace RetroJukebox.Audio.DSP;

/// <summary>
/// 10-band graphic equalizer wired into the NAudio ISampleProvider chain.
/// Bands: 32, 64, 125, 250, 500, 1k, 2k, 4k, 8k, 16k Hz
/// Each band uses a peaking biquad EQ filter; gain range is ±12 dB.
/// Filters are per-channel so stereo phase coherence is maintained.
/// </summary>
public sealed class EqualizerSampleProvider : ISampleProvider
{
    public static readonly float[] BandFrequencies = { 32, 64, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
    private const float Q = 1.41421356f; // √2 → one-octave bandwidth per band

    private readonly ISampleProvider _source;
    private readonly BiquadFilter[][] _filters; // [channel][band]
    private readonly float[] _gainDb;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public EqualizerSampleProvider(ISampleProvider source)
    {
        _source  = source;
        int ch   = source.WaveFormat.Channels;
        int bands = BandFrequencies.Length;
        _gainDb  = new float[bands];

        _filters = new BiquadFilter[ch][];
        for (int c = 0; c < ch; c++)
        {
            _filters[c] = new BiquadFilter[bands];
            for (int b = 0; b < bands; b++)
                _filters[c][b] = BiquadFilter.PeakingEQ(source.WaveFormat.SampleRate, BandFrequencies[b], Q, 0f);
        }
    }

    /// <summary>Set gain (−12 to +12 dB) for one band, rebuilding its filters atomically.</summary>
    public void SetBandGain(int band, float gainDb)
    {
        gainDb = Math.Clamp(gainDb, -12f, 12f);
        _gainDb[band] = gainDb;
        for (int c = 0; c < _filters.Length; c++)
            _filters[c][band] = BiquadFilter.PeakingEQ(_source.WaveFormat.SampleRate, BandFrequencies[band], Q, gainDb);
    }

    public float GetBandGain(int band) => _gainDb[band];

    /// <summary>Apply all ten gains at once to avoid partial-update artifacts.</summary>
    public void ApplyGains(float[] gains)
    {
        for (int b = 0; b < gains.Length && b < _gainDb.Length; b++)
            SetBandGain(b, gains[b]);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read     = _source.Read(buffer, offset, count);
        int channels = _source.WaveFormat.Channels;

        for (int i = 0; i < read; i++)
        {
            int   ch     = i % channels;
            float sample = buffer[offset + i];
            foreach (var f in _filters[ch])
                sample = f.Process(sample);
            buffer[offset + i] = sample;
        }
        return read;
    }
}
