using System.Collections.Generic;

namespace RetroJukebox.Audio.DSP;

/// <summary>
/// Named 10-band EQ presets.
/// Band order matches EqualizerSampleProvider.BandFrequencies:
/// 32, 64, 125, 250, 500, 1k, 2k, 4k, 8k, 16k Hz — gains in dB.
/// </summary>
public static class EqPresetData
{
    public static readonly Dictionary<string, float[]> All = new()
    {
        ["Flat"]       = new float[] {  0,  0,  0,  0,  0,  0,  0,  0,  0,  0 },
        ["Rock"]       = new float[] {  5,  4,  3,  1, -1, -1,  1,  3,  4,  5 },
        ["Jazz"]       = new float[] {  4,  3,  1,  2,  0, -2, -1,  2,  3,  4 },
        ["Classical"]  = new float[] {  0,  0,  0,  0,  0,  0, -3, -4, -4, -5 },
        ["Folk"]       = new float[] {  3,  2,  1,  1,  0,  0,  0,  1,  2,  2 },
        ["Pop"]        = new float[] { -2,  0,  3,  4,  4,  3,  0, -2, -2, -2 },
        ["Electronic"] = new float[] {  5,  4,  1,  0, -2,  0,  2,  3,  4,  5 },
    };
}
