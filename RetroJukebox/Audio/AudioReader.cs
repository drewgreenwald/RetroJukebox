using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.IO;

namespace RetroJukebox.Audio;

/// <summary>
/// Unified audio file reader that handles all formats correctly, including
/// 32-bit float WAV at high sample rates (which AudioFileReader misreads).
///
/// AudioFileReader works well for MP3, OGG, FLAC, AAC and integer PCM WAV.
/// For IEEE float WAV (e.g. 32-bit float / 96kHz exported from Cubase or any DAW),
/// it miscalculates the byte-rate because it assumes integer sample sizing, causing
/// the audio to play back at the wrong speed. This wrapper detects that case and
/// uses WaveFileReader + WaveToSampleProvider instead, which handles float WAV correctly.
/// </summary>
public sealed class AudioReader : IDisposable
{
    // One of these two is active — never both
    private AudioFileReader? _afr;
    private WaveFileReader?  _wfr;

    /// <summary>The ISampleProvider to insert into the NAudio chain.</summary>
    public ISampleProvider SampleProvider { get; }

    /// <summary>Playback position (read/write).</summary>
    public TimeSpan CurrentTime
    {
        get => _afr?.CurrentTime ?? _wfr?.CurrentTime ?? TimeSpan.Zero;
        set
        {
            if (_afr != null) _afr.CurrentTime = value;
            else if (_wfr != null) _wfr.CurrentTime = value;
        }
    }

    /// <summary>Total duration of the file.</summary>
    public TimeSpan TotalTime =>
        _afr?.TotalTime ?? _wfr?.TotalTime ?? TimeSpan.Zero;

    /// <summary>File path (for diagnostics and crossfade logging).</summary>
    public string FileName { get; }

    /// <summary>
    /// Wave format of the decoded output (always IEEE float after conversion).
    /// </summary>
    public WaveFormat WaveFormat => SampleProvider.WaveFormat;

    public AudioReader(string filePath)
    {
        FileName = filePath;

        if (IsIeeeFloatWav(filePath))
        {
            // Use WaveFileReader for float WAV — AudioFileReader gets byte-rate wrong
            _wfr = new WaveFileReader(filePath);
            SampleProvider = new WaveToSampleProvider(_wfr);
        }
        else
        {
            // AudioFileReader handles everything else (MP3, FLAC, OGG, AAC, int PCM WAV)
            _afr = new AudioFileReader(filePath);
            SampleProvider = _afr;
        }
    }

    /// <summary>
    /// Detects IEEE float WAV by reading the format chunk directly.
    /// AudioEncoding 3 = IEEE_FLOAT, AudioEncoding 65534 = EXTENSIBLE (may also be float).
    /// </summary>
    private static bool IsIeeeFloatWav(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (ext != ".wav" && ext != ".wave") return false;

        try
        {
            // Peek at the WAV format chunk — first 20 bytes contain the audio format field
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var br = new BinaryReader(fs);

            // WAV header: "RIFF" (4) + filesize (4) + "WAVE" (4) = 12 bytes
            if (fs.Length < 20) return false;
            fs.Seek(12, SeekOrigin.Begin);

            // Scan for "fmt " chunk (should be right after WAVE, but be safe)
            while (fs.Position + 8 < fs.Length)
            {
                var chunkId   = new string(br.ReadChars(4));
                var chunkSize = br.ReadInt32();

                if (chunkId == "fmt " && chunkSize >= 2)
                {
                    var audioFormat = br.ReadInt16(); // 1=PCM, 3=IEEE_FLOAT, -2=EXTENSIBLE
                    // 3 = IEEE_FLOAT, 65534 (0xFFFE) = EXTENSIBLE (often float at high SR)
                    return audioFormat == 3 || audioFormat == unchecked((short)0xFFFE);
                }

                // Skip this chunk and move to next
                fs.Seek(chunkSize, SeekOrigin.Current);
            }
        }
        catch
        {
            // Any read error — fall back to AudioFileReader
        }

        return false;
    }

    public void Dispose()
    {
        _afr?.Dispose();
        _wfr?.Dispose();
    }
}
