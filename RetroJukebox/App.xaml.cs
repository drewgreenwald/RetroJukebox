using System.Windows;
using System.Windows.Threading;
using RetroJukebox.Models;
using RetroJukebox.Services;

namespace RetroJukebox;

public partial class App : Application
{
    public static AudioService AudioService { get; private set; } = null!;
    public static LibraryService LibraryService { get; private set; } = null!;
    public static PlaylistService PlaylistService { get; private set; } = null!;
    public static SettingsService SettingsService { get; private set; } = null!;
    public static SavedPlaylistService SavedPlaylistService { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
            ShowFatalError("AppDomain", ex.ExceptionObject as Exception);

        DispatcherUnhandledException += (s, ex) =>
        {
            ShowFatalError("Dispatcher", ex.Exception);
            ex.Handled = true;
        };

        TaskScheduler.UnobservedTaskException += (s, ex) =>
        {
            System.Diagnostics.Debug.WriteLine($"[Task] {ex.Exception}");
            ex.SetObserved();
        };

        base.OnStartup(e);

        // ── Initialise lightweight services synchronously ─────────────────
        try { SettingsService  = new SettingsService(); }
        catch (Exception ex) { ShowFatalError("SettingsService", ex); return; }

        try { AudioService = new AudioService(SettingsService); }
        catch (Exception ex) { ShowFatalError("AudioService", ex); return; }

        try { PlaylistService = new PlaylistService(); }
        catch (Exception ex) { ShowFatalError("PlaylistService", ex); return; }

        try { SavedPlaylistService = new SavedPlaylistService(); }
        catch (Exception ex) { ShowFatalError("SavedPlaylistService", ex); return; }

        // ── LibraryService: create instance now (opens DB) but load tracks async ──
        try { LibraryService = new LibraryService(); }
        catch (Exception ex) { ShowFatalError("LibraryService", ex); return; }

        // ── Show the window immediately ───────────────────────────────────
        var mainWindow = new Views.MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();

        // ── Load library + restore session in background after window is up ──
        Dispatcher.InvokeAsync(async () =>
        {
            await LibraryService.LoadLibraryAsync();

            try { PlaylistService.LoadSession(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[App] Session restore failed: {ex.Message}");
            }

            // Notify MainViewModel that library is ready
            if (mainWindow.DataContext is ViewModels.MainViewModel vm)
                vm.OnLibraryLoaded();

            // ── Handle files passed on the command line (e.g. "Open with...") ──
            // Do this after library+session load so the queue is fully initialised
            if (e.Args.Length > 0)
                OpenCommandLineFiles(e.Args, mainWindow);

        }, DispatcherPriority.Background);
    }

    private static void OpenCommandLineFiles(string[] args, Views.MainWindow mainWindow)
    {
        Track? firstTrack = null;

        foreach (var path in args)
        {
            if (!System.IO.File.Exists(path)) continue;
            if (!Track.IsSupported(path)) continue;

            var track = LibraryService.ReadMetadata(path);
            track.IsTransient = true;
            PlaylistService.Enqueue(track);
            firstTrack ??= track;
        }

        if (firstTrack is null) return;

        var idx = PlaylistService.Queue.IndexOf(firstTrack);
        PlaylistService.PlayAt(idx);
        AudioService.Play(firstTrack);

        if (mainWindow.DataContext is ViewModels.MainViewModel vm)
            vm.CurrentTrack = firstTrack;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { PlaylistService?.SaveSession(); } catch { }
        try { AudioService?.Dispose(); } catch { }
        base.OnExit(e);
    }

    private static void ShowFatalError(string context, Exception? ex)
    {
        var msg = $"Startup error in {context}:\n\n{ex?.GetType().Name}: {ex?.Message}\n\n{ex?.InnerException?.Message}";
        MessageBox.Show(msg, "RetroJukebox — Startup Error",
            MessageBoxButton.OK, MessageBoxImage.Error);
        Current?.Shutdown(1);
    }
}
