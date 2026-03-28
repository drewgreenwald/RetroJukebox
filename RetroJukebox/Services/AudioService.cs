using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.CoreAudioApi;
using RetroJukebox.Audio.DSP;
using RetroJukebox.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace RetroJukebox.Services;

public enum PlaybackState { Stopped, Playing, Paused }
public enum OutputDeviceType { WasapiExclusive, WasapiShared, DirectSound, Asio }

public class AudioService : INotifyPropertyChanged, IDisposable
{
    // ── Output ──────────────────────────────────────────────────────────────
    private IWavePlayer? _outputDevice;
    private MixingSampleProvider? _mixer;
    private WaveFormat _mixerFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

    // ── Current track ───────────────────────────────────────────────────────
    private Audio.AudioReader? _currentReader;
    private VolumeSampleProvider? _volumeProvider;
    private FadeInOutSampleProvider? _currentFade;

    // Outgoing track during crossfade — left running in the mixer until its fade completes
    private FadeInOutSampleProvider? _outgoingFade;
    private Audio.AudioReader? _outgoingReader;

    // ── Crossfade timer ─────────────────────────────────────────────────────
    private System.Timers.Timer? _crossfadeTimer;
    private bool _crossfading;

    // ── EQ ──────────────────────────────────────────────────────────────────
    private EqualizerBand[] _eqBands = DefaultEqBands();
    private bool _eqEnabled;

    // ── DSP chain (Phase 4) ─────────────────────────────────────────────────
    private EqualizerSampleProvider? _equalizer;
    private SpectrumAnalyzerProvider? _spectrumAnalyzer;

    /// <summary>Live EQ DSP node — set band gains via this reference.</summary>
    public EqualizerSampleProvider? Equalizer => _equalizer;

    /// <summary>Returns the latest FFT magnitude snapshot; wire to SpectrumVisualizer.SpectrumDataSource.</summary>
    public Func<float[]>? SpectrumDataSource =>
        _spectrumAnalyzer is { } sa ? () => sa.GetSpectrumData() : null;

    // ── State ───────────────────────────────────────────────────────────────
    private PlaybackState _state = PlaybackState.Stopped;
    private float _volume = 0.75f;
    private Track? _currentTrack;
    private double _crossfadeDuration = 3.0;
    private bool _gaplessEnabled = true;
    private int _sampleRate = 44100;
    private OutputDeviceType _outputDeviceType = OutputDeviceType.WasapiShared;
    private string? _selectedDeviceName;

    private readonly SettingsService _settings;

    public event EventHandler? TrackEnded;
    public event PropertyChangedEventHandler? PropertyChanged;

    // ── Public properties ───────────────────────────────────────────────────
    public PlaybackState State
    {
        get => _state;
        private set { _state = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsPlaying)); }
    }
    public bool IsPlaying => _state == PlaybackState.Playing;

    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_volumeProvider != null) _volumeProvider.Volume = _volume;
            OnPropertyChanged();
        }
    }

    public Track? CurrentTrack
    {
        get => _currentTrack;
        private set { _currentTrack = value; OnPropertyChanged(); }
    }

    public TimeSpan Position
    {
        get => _currentReader?.CurrentTime ?? TimeSpan.Zero;
        set
        {
            if (_currentReader != null)
            {
                _currentReader.CurrentTime = value;
                // Crossfade timer was based on original position — reschedule from new position
                if (CrossfadeEnabled)
                    ScheduleCrossfade();
            }
        }
    }

    public TimeSpan Duration => _currentReader?.TotalTime ?? TimeSpan.Zero;

    /// <summary>
    /// True when the current reader is within 500ms of the end.
    /// False during an active crossfade handoff (crossfade timer owns that path).
    /// </summary>
    public bool IsAtEnd =>
        !_crossfading &&
        _currentReader != null &&
        _currentReader.TotalTime.TotalSeconds > 0 &&
        (_currentReader.TotalTime - _currentReader.CurrentTime).TotalMilliseconds < 500;

    public double CrossfadeDuration
    {
        get => _crossfadeDuration;
        set { _crossfadeDuration = value; OnPropertyChanged(); }
    }
    public bool CrossfadeEnabled { get; set; } = true;
    public bool GaplessEnabled
    {
        get => _gaplessEnabled;
        set { _gaplessEnabled = value; OnPropertyChanged(); }
    }
    public bool EqEnabled
    {
        get => _eqEnabled;
        set { _eqEnabled = value; PushEqGains(); OnPropertyChanged(); }
    }
    public EqualizerBand[] EqBands => _eqBands;
    public int SampleRate
    {
        get => _sampleRate;
        set { _sampleRate = value; OnPropertyChanged(); }
    }
    public OutputDeviceType OutputDeviceType
    {
        get => _outputDeviceType;
        set { _outputDeviceType = value; }
    }
    public string? SelectedDeviceName
    {
        get => _selectedDeviceName;
        set { _selectedDeviceName = value; }
    }

    // ── Constructor ─────────────────────────────────────────────────────────
    public AudioService(SettingsService settings)
    {
        _settings = settings;
        LoadSettings();
        InitOutputDevice();
    }

    // ── Output device ────────────────────────────────────────────────────────
    private void InitOutputDevice()
    {
        _outputDevice?.Stop();
        _outputDevice?.Dispose();

        _mixerFormat = WaveFormat.CreateIeeeFloatWaveFormat(_sampleRate, 2);
        _mixer = new MixingSampleProvider(_mixerFormat) { ReadFully = true };

        try
        {
            switch (_outputDeviceType)
            {
                case OutputDeviceType.WasapiExclusive:
                    _outputDevice = new WasapiOut(
                        GetWasapiDevice(_selectedDeviceName),
                        AudioClientShareMode.Exclusive, true, 100);
                    break;

                case OutputDeviceType.Asio:
                    var asioNames = AsioOut.GetDriverNames();
                    var asioName = _selectedDeviceName ?? (asioNames.Length > 0 ? asioNames[0] : null);
                    if (asioName != null)
                    {
                        _outputDevice = new AsioOut(asioName);
                    }
                    else
                    {
                        _outputDevice = new WasapiOut(
                            GetWasapiDevice(null),
                            AudioClientShareMode.Shared, true, 50);
                    }
                    break;

                default:
                    _outputDevice = new WasapiOut(
                        GetWasapiDevice(_selectedDeviceName),
                        AudioClientShareMode.Shared, true, 50);
                    break;
            }

            _outputDevice.Init(_mixer);
            _outputDevice.PlaybackStopped += OnPlaybackStopped;
            _outputDevice.Play();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioService] InitOutputDevice error: {ex.Message}");
            try
            {
                _outputDevice?.Dispose();
                _outputDevice = new WasapiOut(AudioClientShareMode.Shared, 50);
                _outputDevice.Init(_mixer);
                _outputDevice.PlaybackStopped += OnPlaybackStopped;
                _outputDevice.Play();
            }
            catch (Exception ex2)
            {
                System.Diagnostics.Debug.WriteLine($"[AudioService] Fallback init error: {ex2.Message}");
            }
        }
    }

    private MMDevice GetWasapiDevice(string? name)
    {
        using var enumerator = new MMDeviceEnumerator();
        if (!string.IsNullOrEmpty(name))
        {
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var d in devices)
                if (d.FriendlyName == name) return d;
        }
        return enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    }

    public static List<string> GetAvailableDevices()
    {
        var list = new List<string>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
            foreach (var d in devices) list.Add(d.FriendlyName);
        }
        catch { }
        return list;
    }

    public static List<string> GetAsioDevices()
    {
        try { return new List<string>(AsioOut.GetDriverNames()); }
        catch { return new List<string>(); }
    }

    // ── Playback controls ────────────────────────────────────────────────────
    public void Play(Track track)
    {
        if (CurrentTrack != null) CurrentTrack.IsPlaying = false;

        StopCurrentWithFade();

        CurrentTrack = track;
        track.IsPlaying = true;

        try
        {
            _currentReader = new Audio.AudioReader(track.FilePath);

            // Build ISampleProvider chain
            ISampleProvider provider = _currentReader.SampleProvider;

            // Resample if file SR differs from output SR (supports 96kHz, 192kHz, etc.)
            if (_currentReader.WaveFormat.SampleRate != _sampleRate)
                provider = new WdlResamplingSampleProvider(provider, _sampleRate);

            // Upmix mono to stereo
            if (provider.WaveFormat.Channels == 1)
                provider = new MonoToStereoSampleProvider(provider);

            // ── Lo-fi quality simulation ───────────────────────────────────
            var loFiRate = QualityToSampleRate(_playbackQualityKbps);
            if (loFiRate > 0 && loFiRate < provider.WaveFormat.SampleRate)
            {
                // Downsample to lo-fi rate then back up to output rate
                provider = new WdlResamplingSampleProvider(provider, loFiRate);
                provider = new WdlResamplingSampleProvider(provider, _sampleRate);
            }
            // ─────────────────────────────────────────────────────────────

            // Volume control via VolumeSampleProvider (pure ISampleProvider chain)
            _volumeProvider = new VolumeSampleProvider(provider) { Volume = _volume };
            ISampleProvider floatProvider = _volumeProvider;

            // ── Phase 4: EQ → Spectrum tap ────────────────────────────────
            _equalizer       = new EqualizerSampleProvider(floatProvider);
            _spectrumAnalyzer = new SpectrumAnalyzerProvider(_equalizer);

            // Restore current EQ band gains onto the new provider
            for (int i = 0; i < _eqBands.Length; i++)
                _equalizer.SetBandGain(i, _eqEnabled ? _eqBands[i].Gain : 0f);

            floatProvider = _spectrumAnalyzer;
            // ─────────────────────────────────────────────────────────────

            if (CrossfadeEnabled)
            {
                var fadeInMs = _crossfading
                    ? (int)(_crossfadeDuration * 1000)  // overlapping crossfade — fade in over full window
                    : 500;                               // first play / manual skip — quick fade in
                _currentFade = new FadeInOutSampleProvider(floatProvider, initiallySilent: true);
                _currentFade.BeginFadeIn(fadeInMs);
                _mixer?.AddMixerInput(_currentFade);
                ScheduleCrossfade();
            }
            else
            {
                _mixer?.AddMixerInput(floatProvider);
            }

            State = PlaybackState.Playing;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioService] Play error: {ex.Message}");
            State = PlaybackState.Stopped;
        }
    }

    public void Pause()
    {
        if (State == PlaybackState.Playing)
        {
            _outputDevice?.Pause();
            State = PlaybackState.Paused;
        }
    }

    public void Resume()
    {
        if (State == PlaybackState.Paused)
        {
            _outputDevice?.Play();
            State = PlaybackState.Playing;
        }
    }

    public void Stop()
    {
        StopCurrentWithFade();
        State = PlaybackState.Stopped;
        if (CurrentTrack != null) CurrentTrack.IsPlaying = false;
        CurrentTrack = null;
    }

    private void StopCurrentWithFade()
    {
        _crossfadeTimer?.Stop();
        _crossfadeTimer?.Dispose();
        _crossfadeTimer = null;

        _eotTimer?.Stop();
        _eotTimer?.Dispose();
        _eotTimer = null;

        if (_currentFade != null)
        {
            if (_crossfading)
            {
                // Crossfade path: the outgoing fade is already fading out via BeginFadeOut
                // called in ScheduleCrossfade. Promote it to _outgoing so it keeps playing
                // in the mixer independently. Clean up any previous outgoing track first.
                CleanupOutgoing();
                _outgoingFade   = _currentFade;
                _outgoingReader = _currentReader;

                var fadeRef   = _outgoingFade;
                var readerRef = _outgoingReader;
                var removeMs  = (int)(_crossfadeDuration * 1000) + 200;

                Task.Delay(removeMs).ContinueWith(_ =>
                {
                    _mixer?.RemoveMixerInput(fadeRef);
                    readerRef?.Dispose();
                    // Clear outgoing refs if they're still ours
                    if (_outgoingFade   == fadeRef)   _outgoingFade   = null;
                    if (_outgoingReader == readerRef) _outgoingReader = null;
                });
            }
            else
            {
                // Normal stop: quick fade out and remove
                var fadeRef   = _currentFade;
                var readerRef = _currentReader;
                _currentFade.BeginFadeOut(300);
                Task.Delay(350).ContinueWith(_ =>
                {
                    _mixer?.RemoveMixerInput(fadeRef);
                    readerRef?.Dispose();
                });
            }

            _currentFade    = null;
            _currentReader  = null;
            _volumeProvider = null;
            _crossfading    = false;  // reset so next track's timer/IsAtEnd work correctly
        }
        else if (_volumeProvider != null)
        {
            _mixer?.RemoveAllMixerInputs();
            _currentReader?.Dispose();
            _volumeProvider = null;
            _currentReader  = null;
        }
    }

    private void CleanupOutgoing()
    {
        if (_outgoingFade != null)
        {
            _mixer?.RemoveMixerInput(_outgoingFade);
            _outgoingFade = null;
        }
        _outgoingReader?.Dispose();
        _outgoingReader = null;
    }

    // ── Crossfade ─────────────────────────────────────────────────────────
    private void ScheduleCrossfade()
    {
        _crossfadeTimer?.Stop();
        _crossfadeTimer?.Dispose();
        _crossfadeTimer = null;

        if (_currentReader == null || !CrossfadeEnabled)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Crossfade] ScheduleCrossfade SKIPPED — reader={_currentReader != null}, enabled={CrossfadeEnabled}");
            return;
        }

        // Use *remaining* time from current position so seeks re-arm correctly
        var remainingMs = (_currentReader.TotalTime - _currentReader.CurrentTime).TotalMilliseconds;
        var fadeMs      = (int)(_crossfadeDuration * 1000);
        var triggerMs   = remainingMs - fadeMs;
        if (triggerMs < 50) triggerMs = 50; // already inside or past crossfade window — fire soon

        System.Diagnostics.Debug.WriteLine(
            $"[Crossfade] Scheduled — remaining={remainingMs:F0}ms  fadeMs={fadeMs}  triggerMs={triggerMs:F0}  track={System.IO.Path.GetFileName(_currentReader.FileName)}");

        _crossfadeTimer = new System.Timers.Timer(triggerMs) { AutoReset = false };
        _crossfadeTimer.Elapsed += (_, _) =>
        {
            System.Diagnostics.Debug.WriteLine("[Crossfade] Timer FIRED — starting fade-out and signalling advance");
            _crossfading = true;
            _currentFade?.BeginFadeOut(fadeMs);
            TrackEnded?.Invoke(this, EventArgs.Empty);
        };
        _crossfadeTimer.Start();
    }

    // ── End-of-track polling ──────────────────────────────────────────────
    // NOTE: Auto-advance is handled by MainViewModel's position timer on the
    // UI thread, which is simpler and avoids cross-thread races with the
    // crossfade/mixer state. TrackEnded is still raised for crossfade path.
    private System.Timers.Timer? _eotTimer; // kept for disposal safety only

    // ── Gapless / end of track ─────────────────────────────────────────────
    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (!_crossfading && State == PlaybackState.Playing)
            TrackEnded?.Invoke(this, EventArgs.Empty);
        _crossfading = false;
    }

    // ── EQ ────────────────────────────────────────────────────────────────
    public void SetEqPreset(EqPreset preset)
    {
        _eqBands = preset switch
        {
            EqPreset.Rock       => new[] { new EqualizerBand(60,6),  new EqualizerBand(170,3),  new EqualizerBand(310,0),  new EqualizerBand(600,-1), new EqualizerBand(1000,0),  new EqualizerBand(3000,2),  new EqualizerBand(6000,4),  new EqualizerBand(12000,4), new EqualizerBand(14000,4), new EqualizerBand(16000,4) },
            EqPreset.Jazz       => new[] { new EqualizerBand(60,4),  new EqualizerBand(170,2),  new EqualizerBand(310,0),  new EqualizerBand(600,0),  new EqualizerBand(1000,-1), new EqualizerBand(3000,0),  new EqualizerBand(6000,0),  new EqualizerBand(12000,1), new EqualizerBand(14000,2), new EqualizerBand(16000,3) },
            EqPreset.Classical  => new[] { new EqualizerBand(60,0),  new EqualizerBand(170,0),  new EqualizerBand(310,0),  new EqualizerBand(600,0),  new EqualizerBand(1000,0),  new EqualizerBand(3000,0),  new EqualizerBand(6000,-3), new EqualizerBand(12000,-4),new EqualizerBand(14000,-4),new EqualizerBand(16000,-5) },
            EqPreset.Folk       => new[] { new EqualizerBand(60,2),  new EqualizerBand(170,3),  new EqualizerBand(310,1),  new EqualizerBand(600,0),  new EqualizerBand(1000,0),  new EqualizerBand(3000,1),  new EqualizerBand(6000,2),  new EqualizerBand(12000,2), new EqualizerBand(14000,1), new EqualizerBand(16000,0) },
            EqPreset.Pop        => new[] { new EqualizerBand(60,-1), new EqualizerBand(170,2),  new EqualizerBand(310,3),  new EqualizerBand(600,2),  new EqualizerBand(1000,0),  new EqualizerBand(3000,-1), new EqualizerBand(6000,0),  new EqualizerBand(12000,2), new EqualizerBand(14000,3), new EqualizerBand(16000,3) },
            EqPreset.Electronic => new[] { new EqualizerBand(60,5),  new EqualizerBand(170,4),  new EqualizerBand(310,1),  new EqualizerBand(600,0),  new EqualizerBand(1000,-1), new EqualizerBand(3000,0),  new EqualizerBand(6000,2),  new EqualizerBand(12000,4), new EqualizerBand(14000,5), new EqualizerBand(16000,5) },
            _                   => DefaultEqBands()
        };
        PushEqGains();
        OnPropertyChanged(nameof(EqBands));
    }

    /// <summary>Set a single band gain and push it to the live DSP chain.</summary>
    public void SetEqBandGain(int band, float gainDb)
    {
        if (band < 0 || band >= _eqBands.Length) return;
        _eqBands[band] = new EqualizerBand(_eqBands[band].Frequency, gainDb);
        if (_eqEnabled)
            _equalizer?.SetBandGain(band, gainDb);
        OnPropertyChanged(nameof(EqBands));
    }

    private void PushEqGains()
    {
        if (_equalizer == null) return;
        for (int i = 0; i < _eqBands.Length; i++)
            _equalizer.SetBandGain(i, _eqEnabled ? _eqBands[i].Gain : 0f);
    }

    private static EqualizerBand[] DefaultEqBands() => new[]
    {
        new EqualizerBand(60,0),    new EqualizerBand(170,0),  new EqualizerBand(310,0),
        new EqualizerBand(600,0),   new EqualizerBand(1000,0), new EqualizerBand(3000,0),
        new EqualizerBand(6000,0),  new EqualizerBand(12000,0),new EqualizerBand(14000,0),
        new EqualizerBand(16000,0)
    };

    // ── Playback quality (lo-fi downsampling) ──────────────────────────────
    private int _playbackQualityKbps = 0; // 0 = full quality

    /// <summary>
    /// Simulated playback quality in kbps. 0 = full quality (no downsampling).
    /// Non-zero values downsample the source to a lower rate before upsampling back,
    /// producing the characteristic sound of low-bitrate audio.
    /// </summary>
    public int PlaybackQuality
    {
        get => _playbackQualityKbps;
        set
        {
            _playbackQualityKbps = value;
            OnPropertyChanged();
            // Rebuild the chain on the current track if one is playing
            if (CurrentTrack != null && State == PlaybackState.Playing)
            {
                var pos = Position;
                Play(CurrentTrack);
                Task.Delay(80).ContinueWith(_ => Position = pos);
            }
        }
    }

    /// <summary>Maps a kbps quality value to an equivalent intermediate sample rate.</summary>
    private int QualityToSampleRate(int kbps) => kbps switch
    {
        128 => 22050,
        64  => 11025,
        48  => 8000,
        32  => 6000,
        _   => 0   // 0 = bypass
    };

    // ── Sample rate switch ────────────────────────────────────────────────
    public void ChangeSampleRate(int newRate)
    {
        _sampleRate = newRate;
        var wasPlaying = State == PlaybackState.Playing;
        var savedTrack = CurrentTrack;
        var savedPosition = Position;

        Stop();
        InitOutputDevice();

        if (wasPlaying && savedTrack != null)
        {
            Play(savedTrack);
            Task.Delay(100).ContinueWith(_ => Position = savedPosition);
        }
    }

    // ── Settings ──────────────────────────────────────────────────────────
    private void LoadSettings()
    {
        // Migrate: if settings were saved by a pre-Phase5 build they may have
        // CrossfadeEnabled=false as the persisted default. Bump version to reset it.
        const int CurrentSettingsVersion = 2;
        var savedVersion = _settings.Get("SettingsVersion", 0);
        if (savedVersion < CurrentSettingsVersion)
        {
            // Reset crossfade to true on upgrade so users don't get silent-broken crossfade
            _settings.Set("CrossfadeEnabled",  true);
            _settings.Set("CrossfadeDuration", 3.0);
            _settings.Set("SettingsVersion",   CurrentSettingsVersion);
        }

        _volume             = _settings.Get("Volume", 0.75f);
        _sampleRate         = _settings.Get("SampleRate", 44100);
        _crossfadeDuration  = _settings.Get("CrossfadeDuration", 3.0);
        CrossfadeEnabled    = _settings.Get("CrossfadeEnabled", true);
        _gaplessEnabled     = _settings.Get("GaplessEnabled", true);
        _eqEnabled          = _settings.Get("EqEnabled", false);
        _outputDeviceType   = (OutputDeviceType)_settings.Get("OutputDeviceType", 0);
        _selectedDeviceName = _settings.Get<string?>("SelectedDeviceName", null);
    }

    public void SaveSettings()
    {
        _settings.Set("Volume",             _volume);
        _settings.Set("SampleRate",         _sampleRate);
        _settings.Set("CrossfadeDuration",  _crossfadeDuration);
        _settings.Set("CrossfadeEnabled",   CrossfadeEnabled);
        _settings.Set("GaplessEnabled",     _gaplessEnabled);
        _settings.Set("EqEnabled",          _eqEnabled);
        _settings.Set("OutputDeviceType",   (int)_outputDeviceType);
        _settings.Set("SelectedDeviceName", _selectedDeviceName ?? string.Empty);
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        SaveSettings();
        _crossfadeTimer?.Dispose();
        _eotTimer?.Dispose();
        CleanupOutgoing();
        _currentReader?.Dispose();
        _outputDevice?.Stop();
        _outputDevice?.Dispose();
    }
}

// ── Supporting types ──────────────────────────────────────────────────────
public record EqualizerBand(int Frequency, float Gain);

public enum EqPreset { Flat, Rock, Jazz, Classical, Folk, Pop, Electronic }
