using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using RetroJukebox.Services;

namespace RetroJukebox.Converters;

// ── Bool → Visibility ─────────────────────────────────────────────────────
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => v is Visibility.Visible;
}

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => v is not Visibility.Visible;
}

// ── Null → Visibility ─────────────────────────────────────────────────────
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value == null || (value is string s && string.IsNullOrWhiteSpace(s))
            ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => throw new NotImplementedException();
}

// ── byte[] → BitmapImage (album art) ─────────────────────────────────────
public class ByteArrayToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type t, object p, CultureInfo c)
    {
        if (value is not byte[] bytes || bytes.Length == 0) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = new MemoryStream(bytes);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => throw new NotImplementedException();
}

// ── IsPlaying → Play/Pause icon ───────────────────────────────────────────
public class PlayStateIconConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true ? "⏸" : "▶";
    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => throw new NotImplementedException();
}

// ── RepeatMode ↔ bool (for ToggleButton) ─────────────────────────────────
public class RepeatModeConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is RepeatMode mode && mode != RepeatMode.None;
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => value is true ? RepeatMode.All : RepeatMode.None;
}
