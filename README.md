# RetroJukebox — Phase 1

A MusicMatch Jukebox-inspired music player built with C# / WPF / .NET 8.

---

## Requirements

- **Visual Studio 2022 or 2026** with the **.NET Desktop Development** workload
- **.NET 8 SDK** (included with VS 2026)
- Windows 10 or 11

---

## Quick Start

1. Open `RetroJukebox.sln` in Visual Studio.
2. Right-click the solution → **Restore NuGet Packages**.
3. Set **RetroJukebox** as the startup project.
4. Press **F5** (Debug) or **Ctrl+F5** (Run without debugger).

That's it — no manual NuGet installs needed. VS will pull everything automatically.

---

## NuGet Packages (auto-restored)

| Package | Purpose |
|---|---|
| NAudio 2.2.1 | Core audio engine (MP3/WAV/FLAC/AAC/OGG/MP4) |
| NAudio.Asio 2.2.1 | ASIO support for Focusrite Scarlett and other pro interfaces |
| NAudio.WinMM 2.2.1 | Windows Multimedia audio fallback |
| TagLibSharp 2.3.0 | Read/write ID3, Vorbis, FLAC, MP4 tags + album art |
| CommunityToolkit.Mvvm 8.3.2 | ObservableObject, RelayCommand, source generators |
| Microsoft.EntityFrameworkCore.Sqlite 8.0.0 | (Wired for Phase 2 library DB) |
| Newtonsoft.Json 13.0.3 | Library/session/settings JSON persistence |

---

## Supported Audio Formats

| Format | Notes |
|---|---|
| MP3 | All bitrates, VBR/CBR |
| WAV | Including 96kHz/24-bit and 192kHz high-res |
| OGG Vorbis | Via NAudio.Vorbis |
| FLAC | Lossless, all sample rates |
| AAC / M4A | iTunes-compatible |
| MP4 | AAC audio tracks |

---

## Audio Output Options (Settings window)

| Mode | Best for |
|---|---|
| WASAPI Shared | Default Windows audio, works with all devices |
| WASAPI Exclusive | Lower latency, bypasses Windows mixer |
| ASIO | Focusrite Scarlett 18i20 and other pro interfaces |
| DirectSound | Compatibility fallback |

To use your **Focusrite Scarlett 18i20**:
1. Open **Settings**
2. Set Output Mode → **ASIO**
3. Select your Scarlett from the ASIO Device dropdown
4. Click **Apply**

---

## Sample Rate

- Select from the dropdown in the top header bar: 44.1 / 48 / 88.2 / **96 kHz** / 192 kHz
- Your 96kHz WAV files are fully supported — no downsampling
- Changing sample rate reinitializes the audio engine and resumes playback

---

## Session Persistence

On shutdown, RetroJukebox saves:
- Full play queue (file paths)
- Current track index
- Playback position
- Shuffle / repeat state

On next launch, everything is restored automatically.

---

## Data Location

All app data is stored in:
```
%APPDATA%\RetroJukebox\
  library.json    ← your music library
  session.json    ← current queue & playback state
  settings.json   ← audio device, EQ, crossfade settings
```

---

## Phase Roadmap

| Phase | Status | Contents |
|---|---|---|
| **Phase 1** | ✅ Current | Core player, UI shell, library scanning, session persistence, ASIO, EQ presets |
| **Phase 2** | 🔜 Next | SQLite library DB, full browse by artist/album/genre tree, drag-drop queue reorder |
| **Phase 3** | 🔜 | MusicBrainz online metadata fetch, Cover Art Archive album art download, bulk tag editor |
| **Phase 4** | 🔜 | Full EQ DSP pipeline (10-band), visualizer, waveform scrubber, gapless engine polish |
| **Phase 5** | 🔜 | Last.fm scrobbling, playlist import/export (M3U/PLS), mini-player mode |

---

## Known Phase 1 Limitations

- OGG playback requires the `NAudio.Vorbis` NuGet package (add manually if needed: `Install-Package NAudio.Vorbis`)
- The EQ bands are stored but DSP insertion into the NAudio chain is completed in Phase 4
- Online metadata fetch button is a placeholder (Phase 3)
- Album art display in track list coming in Phase 2

---

## Architecture Overview

```
RetroJukebox/
├── App.xaml(.cs)              — App entry, service singletons
├── Models/
│   └── Track.cs               — Track data model (INotifyPropertyChanged)
├── Services/
│   ├── AudioService.cs        — NAudio engine: playback, ASIO, crossfade, EQ, sample rate
│   ├── LibraryService.cs      — Scan dirs, read/write TagLib metadata, JSON persistence
│   ├── PlaylistService.cs     — Queue management, shuffle, repeat, session save/load
│   └── SettingsService.cs     — Key-value settings store
├── ViewModels/
│   └── MainViewModel.cs       — MVVM binding layer for MainWindow
├── Views/
│   ├── MainWindow.xaml(.cs)   — Primary UI: nav panel, track list, queue, transport
│   ├── MetadataEditorWindow   — Track tag editor
│   └── SettingsWindow         — Audio device, EQ, crossfade settings
├── Converters/
│   └── Converters.cs          — WPF value converters (bool→vis, byte[]→image, etc.)
└── Themes/
    ├── JukeboxTheme.xaml      — Color palette, brushes, gradients
    └── ControlStyles.xaml     — Button, slider, list, scrollbar styles
```
