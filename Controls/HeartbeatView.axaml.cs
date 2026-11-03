using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;

namespace OcdWizard.ControlStation.Controls;

/// <summary>
/// A small heart that visibly beats in time with a live BPM value, with the rounded BPM
/// drawn on the heart itself. Purely decorative/at-a-glance: the authoritative numeric
/// read-outs still live in the raw "Live Sensors" panel.
/// </summary>
public partial class HeartbeatView : UserControl
{
    public static readonly StyledProperty<double> BpmProperty =
        AvaloniaProperty.Register<HeartbeatView, double>(nameof(Bpm));

    public double Bpm
    {
        get => GetValue(BpmProperty);
        set => SetValue(BpmProperty, value);
    }

    private readonly ScaleTransform _beat = new(1, 1);
    private readonly SolidColorBrush _fill = new(BaseColor);
    private Grid? _heartRoot;
    private Path? _heartPath;
    private TextBlock? _bpmOnHeart;
    private DispatcherTimer? _timer;
    private DateTime _lastTick;
    private double _phase;

    // Resting colour and the brighter "contracted" colour the heart flashes toward on each beat.
    private static readonly Color BaseColor = Color.Parse("#E84393");
    private static readonly Color PeakColor = Color.Parse("#FF6FB5");

    public HeartbeatView()
    {
        InitializeComponent();
        _heartRoot   = this.FindControl<Grid>("HeartRoot");
        _heartPath   = this.FindControl<Path>("HeartPath");
        _bpmOnHeart  = this.FindControl<TextBlock>("BpmOnHeart");
        if (_heartRoot != null) _heartRoot.RenderTransform = _beat;
        if (_heartPath != null) _heartPath.Fill = _fill;
        UpdateText();

        // Drive the beat from a 60fps timer only while we're on screen so a detached/off-screen
        // panel never spins a timer (mirrors MotionVisualizer's lifecycle handling).
        AttachedToVisualTree += (_, _) =>
        {
            _lastTick = DateTime.UtcNow;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += OnTick;
            _timer.Start();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _timer?.Stop();
            _timer = null;
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BpmProperty)
            UpdateText();
    }

    private void UpdateText()
    {
        if (_bpmOnHeart == null) return;
        var bpm = Bpm;
        _bpmOnHeart.Text = bpm >= 1 ? ((int)Math.Round(bpm)).ToString() : "--";
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var dt = (now - _lastTick).TotalSeconds;
        _lastTick = now;
        if (dt <= 0 || dt > 1) dt = 0.016; // guard against clock jumps / long stalls

        var bpm = Bpm;
        double pulse;
        if (bpm >= 1)
        {
            // One full beat cycle per 60/BPM seconds, clamped to a plausible HR range.
            var period = 60.0 / Math.Clamp(bpm, 30, 220);
            _phase = Wrap(_phase + dt / period);
            pulse = Beat(_phase);
        }
        else
        {
            // No reading yet a slow, faint "breathing" pulse so the widget reads as alive.
            _phase = Wrap(_phase + dt / 3.5);
            pulse = 0.25 * (0.5 - 0.5 * Math.Cos(_phase * 2 * Math.PI));
        }

        _beat.ScaleX = _beat.ScaleY = 1.0 + 0.22 * pulse;
        _fill.Color = Lerp(BaseColor, PeakColor, pulse);
    }

    private static double Wrap(double p) => p - Math.Floor(p);

    // Two quick contractions per beat ("lub-dub") that spike then decay back to rest.
    private static double Beat(double p)
    {
        double lub = Math.Exp(-Math.Pow(p / 0.07, 2));
        double dub = 0.55 * Math.Exp(-Math.Pow((p - 0.22) / 0.07, 2));
        return Math.Min(1.0, lub + dub);
    }

    private static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (byte)(a.A + (b.A - a.A) * t),
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }
}
