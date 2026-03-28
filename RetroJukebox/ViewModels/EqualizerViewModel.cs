using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetroJukebox.Audio.DSP;
using RetroJukebox.Services;
using System.Collections.ObjectModel;

namespace RetroJukebox.ViewModels;

public partial class EqualizerViewModel : ObservableObject
{
    private readonly AudioService _audio;

    [ObservableProperty] private bool   _isEnabled;
    [ObservableProperty] private string _selectedPreset = "Flat";

    public ObservableCollection<string>          PresetNames { get; } = new(EqPresetData.All.Keys);
    public ObservableCollection<EqBandViewModel> Bands       { get; } = new();

    // Band frequency labels matching EqualizerSampleProvider.BandFrequencies
    private static readonly string[] Labels = { "32", "64", "125", "250", "500", "1K", "2K", "4K", "8K", "16K" };

    public EqualizerViewModel(AudioService audio)
    {
        _audio     = audio;
        _isEnabled = audio.EqEnabled;

        for (int i = 0; i < Labels.Length; i++)
            Bands.Add(new EqBandViewModel(i, Labels[i], this));

        // Seed UI from whatever AudioService already has loaded
        SyncBandsFromService();
    }

    // Called by EqBandViewModel when a slider moves
    internal void OnBandChanged(int band, float gainDb)
    {
        _audio.SetEqBandGain(band, gainDb);
        // Mark preset as "Custom" when manually adjusted
        if (SelectedPreset != "Custom")
            SelectedPreset = "Custom";
    }

    partial void OnIsEnabledChanged(bool value)
    {
        _audio.EqEnabled = value;
    }

    [RelayCommand]
    private void ApplyPreset(string? presetName)
    {
        if (string.IsNullOrEmpty(presetName)) return;
        if (!EqPresetData.All.TryGetValue(presetName, out var gains)) return;

        SelectedPreset = presetName;

        // Map the 10 preset gains into AudioService bands
        var eqPreset = presetName switch
        {
            "Rock"       => EqPreset.Rock,
            "Jazz"       => EqPreset.Jazz,
            "Classical"  => EqPreset.Classical,
            "Folk"       => EqPreset.Folk,
            "Pop"        => EqPreset.Pop,
            "Electronic" => EqPreset.Electronic,
            _            => EqPreset.Flat
        };
        _audio.SetEqPreset(eqPreset);

        // Sync slider UI without re-triggering OnBandChanged
        for (int i = 0; i < gains.Length && i < Bands.Count; i++)
            Bands[i].SetSilently(gains[i]);
    }

    [RelayCommand]
    private void Reset() => ApplyPreset("Flat");

    private void SyncBandsFromService()
    {
        var bands = _audio.EqBands;
        for (int i = 0; i < bands.Length && i < Bands.Count; i++)
            Bands[i].SetSilently(bands[i].Gain);
    }
}

// ── Per-band slider VM ────────────────────────────────────────────────────

public partial class EqBandViewModel : ObservableObject
{
    private readonly int                _index;
    private readonly EqualizerViewModel _parent;
    private bool                        _silent;

    [ObservableProperty] private float  _gainDb;
    public string Label { get; }

    public EqBandViewModel(int index, string label, EqualizerViewModel parent)
    {
        _index  = index;
        _parent = parent;
        Label   = label;
    }

    partial void OnGainDbChanged(float value)
    {
        if (!_silent)
            _parent.OnBandChanged(_index, value);
    }

    /// <summary>Update the slider value without firing the EQ callback (used when loading presets).</summary>
    public void SetSilently(float value)
    {
        _silent = true;
        GainDb  = value;
        _silent = false;
    }
}
