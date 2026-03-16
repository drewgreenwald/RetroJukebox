using RetroJukebox.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace RetroJukebox.Services;

public enum RepeatMode { None, One, All }

public class PlaylistService : INotifyPropertyChanged
{
    private readonly ObservableCollection<Track> _queue = [];
    private int _currentIndex = -1;
    private bool _shuffle;
    private RepeatMode _repeatMode = RepeatMode.None;
    private readonly string _sessionPath;
    private readonly Random _rng = new();

    public ObservableCollection<Track> Queue => _queue;
    public int CurrentIndex { get => _currentIndex; private set { _currentIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentTrack)); } }
    public Track? CurrentTrack => _currentIndex >= 0 && _currentIndex < _queue.Count ? _queue[_currentIndex] : null;

    public bool Shuffle
    {
        get => _shuffle;
        set { _shuffle = value; OnPropertyChanged(); }
    }

    public RepeatMode RepeatMode
    {
        get => _repeatMode;
        set { _repeatMode = value; OnPropertyChanged(); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;

    public PlaylistService()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RetroJukebox");
        Directory.CreateDirectory(appData);
        _sessionPath = Path.Combine(appData, "session.json");
    }

    // ── Queue management ──────────────────────────────────────────────────
    public void Enqueue(Track track)
    {
        _queue.Add(track);
        if (_currentIndex < 0) CurrentIndex = 0;
    }

    public void EnqueueRange(IEnumerable<Track> tracks)
    {
        bool first = _queue.Count == 0;
        foreach (var t in tracks) _queue.Add(t);
        if (first && _queue.Count > 0) CurrentIndex = 0;
    }

    public void InsertNext(Track track)
    {
        var insertAt = Math.Min(_currentIndex + 1, _queue.Count);
        _queue.Insert(insertAt, track);
    }

    public void Clear()
    {
        _queue.Clear();
        CurrentIndex = -1;
    }

    public void Remove(Track track)
    {
        var idx = _queue.IndexOf(track);
        if (idx < 0) return;
        _queue.RemoveAt(idx);
        if (idx < _currentIndex) CurrentIndex--;
        else if (idx == _currentIndex) CurrentIndex = Math.Min(_currentIndex, _queue.Count - 1);
    }

    public void MoveTrack(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || newIndex < 0 || oldIndex >= _queue.Count || newIndex >= _queue.Count) return;
        _queue.Move(oldIndex, newIndex);
        if (_currentIndex == oldIndex) CurrentIndex = newIndex;
    }

    // ── Navigation ────────────────────────────────────────────────────────
    public Track? PlayAt(int index)
    {
        if (index < 0 || index >= _queue.Count) return null;
        CurrentIndex = index;
        return _queue[index];
    }

    public Track? Next()
    {
        if (_queue.Count == 0) return null;

        if (_repeatMode == RepeatMode.One)
            return CurrentTrack;

        if (_shuffle)
        {
            var next = _rng.Next(_queue.Count);
            return PlayAt(next);
        }

        var nextIdx = _currentIndex + 1;
        if (nextIdx >= _queue.Count)
        {
            if (_repeatMode == RepeatMode.All)
                return PlayAt(0);
            return null; // end of queue
        }

        return PlayAt(nextIdx);
    }

    public Track? Previous()
    {
        if (_queue.Count == 0) return null;
        var prevIdx = _currentIndex - 1;
        if (prevIdx < 0) prevIdx = _repeatMode == RepeatMode.All ? _queue.Count - 1 : 0;
        return PlayAt(prevIdx);
    }

    public bool HasNext()
    {
        if (_queue.Count == 0) return false;
        if (_shuffle || _repeatMode != RepeatMode.None) return true;
        return _currentIndex < _queue.Count - 1;
    }

    // ── Session persistence ───────────────────────────────────────────────
    public void SaveSession()
    {
        try
        {
            var session = new SessionData(
                _queue.Select(t => t.FilePath).ToList(),
                _currentIndex,
                App.AudioService.Position.TotalSeconds,
                _shuffle,
                (int)_repeatMode);
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(session, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(_sessionPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PlaylistService] Save session error: {ex.Message}");
        }
    }

    public void LoadSession()
    {
        if (!File.Exists(_sessionPath)) return;
        try
        {
            var json = File.ReadAllText(_sessionPath);
            var session = Newtonsoft.Json.JsonConvert.DeserializeObject<SessionData>(json);
            if (session == null) return;

            _shuffle = session.Shuffle;
            _repeatMode = (RepeatMode)session.RepeatMode;

            foreach (var path in session.Queue)
            {
                if (!File.Exists(path)) continue;
                var track = App.LibraryService.Tracks.FirstOrDefault(t =>
                    string.Equals(t.FilePath, path, StringComparison.OrdinalIgnoreCase))
                    ?? App.LibraryService.ReadMetadata(path);
                _queue.Add(track);
            }

            CurrentIndex = Math.Min(session.CurrentIndex, _queue.Count - 1);
            // Restore position is handled by MainViewModel after UI is ready
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[PlaylistService] Load session error: {ex.Message}");
        }
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private record SessionData(List<string> Queue, int CurrentIndex,
        double PositionSeconds, bool Shuffle, int RepeatMode);
}
