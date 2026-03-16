using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RetroJukebox.Controls;

/// <summary>
/// Custom WPF control that renders a waveform and a playhead, and raises a
/// <see cref="Seek"/> routed event when the user clicks or drags.
/// </summary>
public sealed class WaveformScrubber : Control
{
    static WaveformScrubber()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(WaveformScrubber),
            new FrameworkPropertyMetadata(typeof(WaveformScrubber)));
    }

    // ── Dependency Properties ─────────────────────────────────────────────

    public static readonly DependencyProperty WaveformDataProperty =
        DependencyProperty.Register(
            nameof(WaveformData),
            typeof((float Min, float Max)[]),
            typeof(WaveformScrubber),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(
            nameof(Progress),
            typeof(double),
            typeof(WaveformScrubber),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty WaveColorProperty =
        DependencyProperty.Register(
            nameof(WaveColor), typeof(Brush), typeof(WaveformScrubber),
            new FrameworkPropertyMetadata(
                new SolidColorBrush(Color.FromRgb(0x44, 0x66, 0xAA)),
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PlayedColorProperty =
        DependencyProperty.Register(
            nameof(PlayedColor), typeof(Brush), typeof(WaveformScrubber),
            new FrameworkPropertyMetadata(
                new SolidColorBrush(Color.FromRgb(0xFF, 0x6A, 0x00)),
                FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PlayheadColorProperty =
        DependencyProperty.Register(
            nameof(PlayheadColor), typeof(Brush), typeof(WaveformScrubber),
            new FrameworkPropertyMetadata(Brushes.White,
                FrameworkPropertyMetadataOptions.AffectsRender));

    public (float Min, float Max)[]? WaveformData
    {
        get => ((float Min, float Max)[]?)GetValue(WaveformDataProperty);
        set => SetValue(WaveformDataProperty, value);
    }

    /// <summary>Playback position as a fraction 0..1.</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, Math.Clamp(value, 0.0, 1.0));
    }

    public Brush WaveColor
    {
        get => (Brush)GetValue(WaveColorProperty);
        set => SetValue(WaveColorProperty, value);
    }
    public Brush PlayedColor
    {
        get => (Brush)GetValue(PlayedColorProperty);
        set => SetValue(PlayedColorProperty, value);
    }
    public Brush PlayheadColor
    {
        get => (Brush)GetValue(PlayheadColorProperty);
        set => SetValue(PlayheadColorProperty, value);
    }

    // ── Seek Routed Event ─────────────────────────────────────────────────

    public static readonly RoutedEvent SeekEvent =
        EventManager.RegisterRoutedEvent(
            nameof(Seek), RoutingStrategy.Bubble,
            typeof(EventHandler<WaveformSeekEventArgs>),
            typeof(WaveformScrubber));

    public event EventHandler<WaveformSeekEventArgs> Seek
    {
        add    => AddHandler(SeekEvent, value);
        remove => RemoveHandler(SeekEvent, value);
    }

    // ── Mouse interaction ─────────────────────────────────────────────────

    private bool _dragging;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _dragging = true;
        CaptureMouse();
        FireSeek(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging && e.LeftButton == MouseButtonState.Pressed)
            FireSeek(e.GetPosition(this).X);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragging)
        {
            FireSeek(e.GetPosition(this).X);
            _dragging = false;
            ReleaseMouseCapture();
        }
        e.Handled = true;
    }

    private void FireSeek(double x)
    {
        double fraction = Math.Clamp(x / Math.Max(1, ActualWidth), 0, 1);
        RaiseEvent(new WaveformSeekEventArgs(SeekEvent, this, fraction));
    }

    // ── Rendering ─────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        // Draw background (use the Control.Background if set)
        if (Background != null)
            dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));

        var data = WaveformData;
        if (data == null || data.Length == 0)
        {
            // Draw a flat centre line when no data loaded
            var mutedPen = new Pen(WaveColor, 1) { DashStyle = DashStyles.Dash };
            dc.DrawLine(mutedPen, new Point(0, h / 2), new Point(w, h / 2));
            return;
        }

        double mid       = h / 2.0;
        double playheadX = Progress * w;
        int    points    = data.Length;
        double barW      = Math.Max(1.0, w / points);

        var playedGeom  = new StreamGeometry();
        var unplayedGeom = new StreamGeometry();

        using (var pg = playedGeom.Open())
        using (var ug = unplayedGeom.Open())
        {
            for (int i = 0; i < points; i++)
            {
                double x    = (double)i / points * w;
                double yTop = mid + data[i].Min * mid;   // Min is negative → above centre
                double yBot = mid + data[i].Max * mid;   // Max is positive → below centre

                // Ensure at least 1px tall
                if (yBot - yTop < 1.0)
                {
                    yTop = mid - 0.5;
                    yBot = mid + 0.5;
                }

                bool played = x <= playheadX;
                var ctx = played ? pg : ug;
                ctx.BeginFigure(new Point(x, yTop), true, true);
                ctx.LineTo(new Point(x + barW, yTop), false, false);
                ctx.LineTo(new Point(x + barW, yBot), false, false);
                ctx.LineTo(new Point(x,         yBot), false, false);
            }
        }

        playedGeom.Freeze();
        unplayedGeom.Freeze();

        dc.DrawGeometry(PlayedColor, null, playedGeom);
        dc.DrawGeometry(WaveColor,   null, unplayedGeom);

        // Playhead vertical line
        var pen = new Pen(PlayheadColor, 2);
        dc.DrawLine(pen, new Point(playheadX, 0), new Point(playheadX, h));
    }
}

/// <summary>Event args carrying the seek fraction (0..1).</summary>
public sealed class WaveformSeekEventArgs : RoutedEventArgs
{
    public double Fraction { get; }
    public WaveformSeekEventArgs(RoutedEvent e, object source, double fraction)
        : base(e, source) => Fraction = fraction;
}
