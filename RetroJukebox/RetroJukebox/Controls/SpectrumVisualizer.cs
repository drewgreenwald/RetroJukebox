using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace RetroJukebox.Controls;

/// <summary>
/// Animated bar-graph spectrum analyzer rendered directly with DrawingContext.
/// Wire up <see cref="SpectrumDataSource"/> to AudioService.GetSpectrumData.
/// Runs at 60 Hz via DispatcherTimer; automatically starts/stops on Loaded/Unloaded.
/// </summary>
public sealed class SpectrumVisualizer : Control
{
    private const int    RefreshHz        = 60;
    private const int    NumBands         = 20;
    private const int    PeakHoldFrames   = 45;   // ~0.75 s at 60 Hz
    private const float  FallSpeed        = 0.012f;
    private const float  Smoothing        = 0.30f; // exponential blend factor

    private readonly DispatcherTimer _timer;
    private readonly float[] _display  = new float[NumBands];
    private readonly float[] _peak     = new float[NumBands];
    private readonly int[]   _peakTick = new int[NumBands];

    static SpectrumVisualizer()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(SpectrumVisualizer),
            new FrameworkPropertyMetadata(typeof(SpectrumVisualizer)));
    }

    public SpectrumVisualizer()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromSeconds(1.0 / RefreshHz)
        };
        _timer.Tick += (_, _) => InvalidateVisual();

        Loaded   += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    // ── Properties ────────────────────────────────────────────────────────

    /// <summary>
    /// Set to <c>AudioService.SpectrumAnalyzer.GetSpectrumData</c> from code-behind.
    /// Not a DependencyProperty because Func&lt;&gt; can't be data-bound.
    /// </summary>
    public Func<float[]>? SpectrumDataSource { get; set; }

    public static readonly DependencyProperty BarColorProperty =
        DependencyProperty.Register(nameof(BarColor), typeof(Brush), typeof(SpectrumVisualizer),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x33, 0xCC, 0xFF))));

    public static readonly DependencyProperty PeakColorProperty =
        DependencyProperty.Register(nameof(PeakColor), typeof(Brush), typeof(SpectrumVisualizer),
            new PropertyMetadata(Brushes.White));

    public static readonly DependencyProperty BarGradientTopProperty =
        DependencyProperty.Register(nameof(BarGradientTop), typeof(Brush), typeof(SpectrumVisualizer),
            new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0xFF, 0x6A, 0x00))));

    public Brush BarColor         { get => (Brush)GetValue(BarColorProperty);         set => SetValue(BarColorProperty, value); }
    public Brush PeakColor        { get => (Brush)GetValue(PeakColorProperty);        set => SetValue(PeakColorProperty, value); }
    public Brush BarGradientTop   { get => (Brush)GetValue(BarGradientTopProperty);   set => SetValue(BarGradientTopProperty, value); }

    // ── Rendering ─────────────────────────────────────────────────────────

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        double w = ActualWidth;
        double h = ActualHeight;

        if (Background != null)
            dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));

        float[]? raw = SpectrumDataSource?.Invoke();
        if (raw == null || raw.Length == 0)
        {
            DrawIdleBars(dc, w, h);
            return;
        }

        // Map FFT bins → display bands using logarithmic spacing
        int fftBins = raw.Length;
        var bandValues = new float[NumBands];
        for (int b = 0; b < NumBands; b++)
        {
            int start = (int)(fftBins * Math.Pow((double)b       / NumBands, 2.0));
            int end   = (int)(fftBins * Math.Pow((double)(b + 1) / NumBands, 2.0));
            if (end <= start) end = start + 1;

            float sum = 0f;
            for (int i = start; i < end && i < fftBins; i++) sum += raw[i];
            bandValues[b] = sum / (end - start);
        }

        // dB normalisation — floor at -60 dB
        float maxMag = 0.001f;
        foreach (var v in bandValues)
            if (v > maxMag) maxMag = v;

        double gapPx  = 2.0;
        double barW   = Math.Max(1.0, (w - gapPx * (NumBands - 1)) / NumBands);
        double peakH  = 3.0;

        // Build a gradient brush that goes orange at top → cyan at bottom
        var barBrushFrozen = MakeGradient(h);

        for (int b = 0; b < NumBands; b++)
        {
            double dB         = 20.0 * Math.Log10(bandValues[b] / maxMag + 1e-10);
            float  normalised = (float)Math.Clamp((dB + 60.0) / 60.0, 0.0, 1.0);

            // Exponential smoothing (attack + decay)
            _display[b] += (normalised - _display[b]) * Smoothing;

            // Peak hold & fall
            if (_display[b] >= _peak[b])
            {
                _peak[b]     = _display[b];
                _peakTick[b] = PeakHoldFrames;
            }
            else if (_peakTick[b] > 0)
                _peakTick[b]--;
            else
                _peak[b] = Math.Max(0f, _peak[b] - FallSpeed);

            double x     = b * (barW + gapPx);
            double barH  = _display[b] * h;
            double peakY = h - _peak[b] * h - peakH;

            // Main bar
            if (barH > 0.5)
                dc.DrawRectangle(barBrushFrozen, null, new Rect(x, h - barH, barW, barH));

            // Peak dot
            if (peakY >= 0 && peakY + peakH <= h)
                dc.DrawRectangle(PeakColor, null, new Rect(x, peakY, barW, peakH));
        }
    }

    private void DrawIdleBars(DrawingContext dc, double w, double h)
    {
        double gapPx = 2.0;
        double barW  = Math.Max(1.0, (w - gapPx * (NumBands - 1)) / NumBands);
        var    brush = new SolidColorBrush(Color.FromArgb(40, 0x33, 0xCC, 0xFF));
        brush.Freeze();
        for (int b = 0; b < NumBands; b++)
            dc.DrawRectangle(brush, null,
                new Rect(b * (barW + gapPx), h - 2, barW, 2));
    }

    private static LinearGradientBrush MakeGradient(double controlHeight)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint   = new Point(0, 1),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0xFF, 0x6A, 0x00), 0.0),  // orange top
                new GradientStop(Color.FromRgb(0x33, 0xCC, 0xFF), 1.0),  // cyan  bottom
            }
        };
        brush.Freeze();
        return brush;
    }
}
