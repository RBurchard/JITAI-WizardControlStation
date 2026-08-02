using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using OcdWizard.ControlStation.Models;

namespace OcdWizard.ControlStation.Views;

/// <summary>
/// Immediate-mode replay timeline (same drawing approach as <see cref="MotionVisualizer"/>).
/// Draws the recording track with auto-derived markers, user markers (distinct, labelled) and a
/// current-time cursor. Left click/drag scrubs; the mouse wheel zooms toward the cursor and a
/// right-drag pans, so a long recording can still be seeked to a precise millisecond.
///
/// Times are absolute epoch milliseconds. <see cref="SeekRequested"/> reports the absolute target.
/// </summary>
public sealed class TimelineControl : Control
{
    public static readonly StyledProperty<long> MinTsProperty =
        AvaloniaProperty.Register<TimelineControl, long>(nameof(MinTs));
    public static readonly StyledProperty<long> MaxTsProperty =
        AvaloniaProperty.Register<TimelineControl, long>(nameof(MaxTs));
    public static readonly StyledProperty<long> CurrentMsProperty =
        AvaloniaProperty.Register<TimelineControl, long>(nameof(CurrentMs));

    public long MinTs { get => GetValue(MinTsProperty); set => SetValue(MinTsProperty, value); }
    public long MaxTs { get => GetValue(MaxTsProperty); set => SetValue(MaxTsProperty, value); }
    public long CurrentMs { get => GetValue(CurrentMsProperty); set => SetValue(CurrentMsProperty, value); }

    public Action<long>? SeekRequested;

    private IReadOnlyList<ReplayMarker>? _autoMarkers;
    private IReadOnlyList<UserMarker>? _userMarkers;

    private double _viewStart;   // absolute ms at the left edge
    private double _viewSpan = 1; // visible ms across the width
    private bool _scrubbing;
    private bool _panning;
    private Point _lastPointer;

    private const double MinSpanMs = 50;

    private readonly ImmutableSolidColorBrush _bg = new(Color.Parse("#0E1124"));
    private readonly ImmutableSolidColorBrush _track = new(Color.Parse("#1A1F38"));
    private readonly Pen _gridPen = new(new SolidColorBrush(Color.FromArgb(40, 0xFF, 0xFF, 0xFF)), 1);
    private readonly Pen _cursorPen = new(new SolidColorBrush(Color.Parse("#FFFFFF")), 1.5);
    private readonly ImmutableSolidColorBrush _gridText = new(Color.Parse("#5A6488"));
    private readonly ImmutableSolidColorBrush _userBrush = new(Color.Parse("#F4D35E"));
    private readonly Pen _userPen = new(new SolidColorBrush(Color.Parse("#F4D35E")), 1.5);

    static TimelineControl()
    {
        AffectsRender<TimelineControl>(MinTsProperty, MaxTsProperty, CurrentMsProperty);
    }

    public TimelineControl()
    {
        Focusable = true;
        MinTsProperty.Changed.AddClassHandler<TimelineControl>((c, _) => c.ResetView());
        MaxTsProperty.Changed.AddClassHandler<TimelineControl>((c, _) => c.ResetView());
    }

    public void SetMarkers(IReadOnlyList<ReplayMarker>? auto, IReadOnlyList<UserMarker>? user)
    {
        _autoMarkers = auto;
        _userMarkers = user;
        InvalidateVisual();
    }

    private void ResetView()
    {
        _viewStart = MinTs;
        _viewSpan = Math.Max(1, MaxTs - MinTs);
        InvalidateVisual();
    }

    private double TimeToX(double tsAbs, double width) => (tsAbs - _viewStart) / _viewSpan * width;
    private double XToTime(double x, double width) => _viewStart + x / width * _viewSpan;

    // ── Pointer ────────────────────────────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pt = e.GetCurrentPoint(this);
        _lastPointer = pt.Position;
        if (pt.Properties.IsRightButtonPressed) { _panning = true; e.Pointer.Capture(this); return; }
        if (pt.Properties.IsLeftButtonPressed)
        {
            _scrubbing = true;
            e.Pointer.Capture(this);
            RaiseSeek(pt.Position.X);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        if (_scrubbing) RaiseSeek(pos.X);
        else if (_panning)
        {
            var dx = pos.X - _lastPointer.X;
            var w = Math.Max(1, Bounds.Width);
            _viewStart = ClampStart(_viewStart - dx / w * _viewSpan, _viewSpan);
            InvalidateVisual();
        }
        _lastPointer = pos;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _scrubbing = false;
        _panning = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var w = Math.Max(1, Bounds.Width);
        var x = e.GetPosition(this).X;
        var cursorTime = XToTime(x, w);
        var full = Math.Max(1, MaxTs - MinTs);
        var factor = e.Delta.Y > 0 ? 1 / 1.25 : 1.25;
        var newSpan = Math.Clamp(_viewSpan * factor, MinSpanMs, full);
        // Keep the time under the cursor pinned to the same x position.
        _viewStart = ClampStart(cursorTime - x / w * newSpan, newSpan);
        _viewSpan = newSpan;
        InvalidateVisual();
        e.Handled = true;
    }

    private double ClampStart(double start, double span) => Math.Clamp(start, MinTs, Math.Max(MinTs, MaxTs - span));

    private void RaiseSeek(double x)
    {
        var w = Math.Max(1, Bounds.Width);
        var t = (long)Math.Round(XToTime(Math.Clamp(x, 0, w), w));
        SeekRequested?.Invoke(Math.Clamp(t, MinTs, MaxTs));
    }

    // ── Render ───────────────────────────────────────────────────────────────────

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        ctx.FillRectangle(_bg, new Rect(0, 0, w, h));

        if (MaxTs <= MinTs) return;
        if (_viewSpan <= 1) ResetView();

        double trackTop = 14, trackH = Math.Max(8, h - 28);
        ctx.FillRectangle(_track, new Rect(0, trackTop, w, trackH));

        DrawTimeGrid(ctx, w, h, trackTop, trackH);

        if (_autoMarkers != null)
            foreach (var m in _autoMarkers)
                DrawTick(ctx, m.Ts, w, trackTop, trackH, AutoPen(m.Kind), 0.55);

        if (_userMarkers != null)
            foreach (var u in _userMarkers)
                DrawUserMarker(ctx, u, w, trackTop, trackH);

        // Current-time cursor.
        var cx = TimeToX(CurrentMs, w);
        if (cx >= -1 && cx <= w + 1)
        {
            ctx.DrawLine(_cursorPen, new Point(cx, 0), new Point(cx, h));
            var head = new StreamGeometry();
            using (var g = head.Open())
            {
                g.BeginFigure(new Point(cx - 5, 0), true);
                g.LineTo(new Point(cx + 5, 0));
                g.LineTo(new Point(cx, 7));
                g.EndFigure(true);
            }
            ctx.DrawGeometry(Brushes.White, null, head);
        }
    }

    private void DrawTimeGrid(DrawingContext ctx, double w, double h, double top, double trackH)
    {
        // Choose a "nice" tick step so 4–10 labels are visible across the current view.
        double targetTicks = Math.Clamp(w / 110.0, 4, 10);
        double rough = _viewSpan / targetTicks;
        double step = NiceStep(rough);
        double first = Math.Ceiling(_viewStart / step) * step;
        for (double t = first; t <= _viewStart + _viewSpan; t += step)
        {
            double x = TimeToX(t, w);
            ctx.DrawLine(_gridPen, new Point(x, top), new Point(x, top + trackH));
            var label = Fmt((long)(t - MinTs));
            ctx.DrawText(new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, Typeface.Default, 10, _gridText), new Point(x + 2, h - 13));
        }
    }

    private void DrawTick(DrawingContext ctx, long ts, double w, double top, double trackH, IPen pen, double heightFrac)
    {
        double x = TimeToX(ts, w);
        if (x < -2 || x > w + 2) return;
        double y0 = top + trackH * (1 - heightFrac);
        ctx.DrawLine(pen, new Point(x, y0), new Point(x, top + trackH));
    }

    private void DrawUserMarker(DrawingContext ctx, UserMarker u, double w, double top, double trackH)
    {
        double x = TimeToX(u.Ts, w);
        if (x < -2 || x > w + 2) return;
        ctx.DrawLine(_userPen, new Point(x, top - 6), new Point(x, top + trackH));
        var dot = new StreamGeometry();
        using (var g = dot.Open())
        {
            g.BeginFigure(new Point(x - 4, top - 6), true);
            g.LineTo(new Point(x + 4, top - 6));
            g.LineTo(new Point(x, top));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(_userBrush, null, dot);
        if (!string.IsNullOrEmpty(u.Label))
            ctx.DrawText(new FormattedText(u.Label, System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, Typeface.Default, 10, _userBrush), new Point(x + 3, top - 6));
    }

    private IPen AutoPen(ReplayMarkerKind kind) => kind switch
    {
        ReplayMarkerKind.RunStart or ReplayMarkerKind.RunEnd =>
            new Pen(new SolidColorBrush(Color.Parse("#4CC74C")), 1.3),
        ReplayMarkerKind.InterventionSent =>
            new Pen(new SolidColorBrush(Color.Parse("#F4A261")), 1.3),
        ReplayMarkerKind.Response =>
            new Pen(new SolidColorBrush(Color.Parse("#4C8CE6")), 1.3),
        ReplayMarkerKind.Distraction =>
            new Pen(new SolidColorBrush(Color.Parse("#B07CE6")), 1.3),
        _ => new Pen(new SolidColorBrush(Color.Parse("#8899BB")), 1.1),
    };

    private static double NiceStep(double rough)
    {
        if (rough <= 0) return 1000;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(rough)));
        double norm = rough / mag;
        double nice = norm <= 1 ? 1 : norm <= 2 ? 2 : norm <= 5 ? 5 : 10;
        return Math.Max(1, nice * mag);
    }

    private static string Fmt(long ms)
    {
        if (ms < 0) ms = 0;
        var t = TimeSpan.FromMilliseconds(ms);
        return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}.{t.Milliseconds:000}";
    }
}
