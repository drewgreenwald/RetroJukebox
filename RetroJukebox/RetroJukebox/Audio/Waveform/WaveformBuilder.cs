using RetroJukebox.Audio;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RetroJukebox.Audio.Waveform;

/// <summary>
/// Decodes an audio file on a background thread and downsamples it to a fixed
/// number of (min, max) pairs suitable for waveform rendering.
/// Uses AudioReader so 32-bit float WAV at high sample rates is handled correctly.
/// </summary>
public static class WaveformBuilder
{
    public static Task<(float Min, float Max)[]> BuildAsync(
        string filePath,
        int pointCount = 1000,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            using var reader = new AudioReader(filePath);
            var provider     = reader.SampleProvider;
            int channels     = provider.WaveFormat.Channels;
            int sampleRate   = provider.WaveFormat.SampleRate;

            // Derive total samples from TotalTime — works for all formats including float WAV
            long totalSamples     = (long)(reader.TotalTime.TotalSeconds * sampleRate) * channels;
            long samplesPerPoint  = Math.Max(channels, totalSamples / pointCount);
            // Round down to a channel-aligned block
            samplesPerPoint = (samplesPerPoint / channels) * channels;

            var result = new (float Min, float Max)[pointCount];
            var buffer = new float[(int)Math.Max(channels, samplesPerPoint)];

            for (int p = 0; p < pointCount; p++)
            {
                ct.ThrowIfCancellationRequested();

                int read = provider.Read(buffer, 0, buffer.Length);
                if (read == 0) break;

                float min = float.MaxValue;
                float max = float.MinValue;

                for (int i = 0; i < read; i += channels)
                {
                    float mono = 0f;
                    for (int c = 0; c < channels && i + c < read; c++)
                        mono += buffer[i + c];
                    mono /= channels;
                    if (mono < min) min = mono;
                    if (mono > max) max = mono;
                }

                result[p] = (
                    min == float.MaxValue ? 0f : min,
                    max == float.MinValue ? 0f : max
                );
            }
            return result;
        }, ct);
    }
}
