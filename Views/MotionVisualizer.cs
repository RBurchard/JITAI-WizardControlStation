using System;
using System.Collections.Generic;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using OcdWizard.ControlStation.Models;
using OcdWizard.ControlStation.Services;

namespace OcdWizard.ControlStation.Views;

/// <summary>
/// High-performance, immediate-mode motion view. Draws directly in
/// <see cref="Render"/> (no per-frame visual-tree allocation, unlike the old
/// Canvas-of-Lines approach that dropped below 10fps). The left half shows a 3D
/// wrist/hand model driven by the fused orientation; the right half shows the
/// reconstructed 3D motion trail. Reconstruction runs at the full incoming
/// sample rate (~50Hz); rendering runs decoupled at ~60fps.
/// </summary>
public sealed class MotionVisualizer : Control
{
    private readonly MotionReconstructor _recon = new();
    private readonly object _lock = new();
    private List<WatchDataSnapshot> _pending = new();
    private DispatcherTimer? _timer;

    // Cached drawing resources created once, reused every frame.
    private readonly ImmutableSolidColorBrush _bg = new(Color.Parse("#101020"));
    private readonly Pen _handPen = new(new SolidColorBrush(Color.Parse("#A8DADC")), 1.6);
    private readonly Pen _fingerPen = new(new SolidColorBrush(Color.Parse("#C7E9EA")), 1.4);
    private readonly ImmutableSolidColorBrush _faceFill = new(Color.FromArgb(70, 0xA8, 0xDA, 0xDC));
    private readonly ImmutableSolidColorBrush _limbFill = new(Color.FromArgb(40, 0xA8, 0xDA, 0xDC));
    private readonly ImmutableSolidColorBrush _markerFill = new(Color.Parse("#F4A261"));
    private readonly Pen _gridPen = new(new SolidColorBrush(Color.FromArgb(45, 0xFF, 0xFF, 0xFF)), 1);
    private readonly Pen _eastPen = new(new SolidColorBrush(Color.FromArgb(160, 0xE6, 0x3A, 0x3A)), 1.4);
    private readonly Pen _northPen = new(new SolidColorBrush(Color.FromArgb(160, 0x4C, 0xC7, 0x4C)), 1.4);
    private readonly Pen _upPen = new(new SolidColorBrush(Color.FromArgb(160, 0x4C, 0x8C, 0xE6)), 1.4);
    private readonly Pen _dropPen = new(new SolidColorBrush(Color.FromArgb(90, 0xE9, 0xC4, 0x6A)), 1);
    private readonly ImmutableSolidColorBrush _dotFill = new(Color.Parse("#E9C46A"));

    // Trail camera (orthographic 3/4 view of the velocity space).
    private static readonly float CamAzimuth = 0.6f;    // radians, around Up
    private static readonly float CamElevation = 0.9f;  // radians, look-down tilt
    // Max displayed velocity (m/s). Typical fast hand motion is 1-3 m/s.
    private const float TrailVMax = 2.5f;

    public MotionVisualizer()
    {
        AttachedToVisualTree += (_, _) =>
        {
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

    // ── Axis remap pass-throughs ──────────────────────────────────────────
    public static readonly string[] AxisNames = ["X", "Y", "Z"];

    public int  RemapSrcX { get => _recon.RemapSrcX; set => _recon.RemapSrcX = value; }
    public bool RemapFlipX { get => _recon.RemapFlipX; set => _recon.RemapFlipX = value; }
    public int  RemapSrcY { get => _recon.RemapSrcY; set => _recon.RemapSrcY = value; }
    public bool RemapFlipY { get => _recon.RemapFlipY; set => _recon.RemapFlipY = value; }
    public int  RemapSrcZ { get => _recon.RemapSrcZ; set => _recon.RemapSrcZ = value; }
    public bool RemapFlipZ { get => _recon.RemapFlipZ; set => _recon.RemapFlipZ = value; }

    /// <summary>Apply a named remap preset.</summary>
    public void ApplyPreset(string name)
    {
        // (SrcX,FlipX, SrcY,FlipY, SrcZ,FlipZ)
        (int sx, bool fx, int sy, bool fy, int sz, bool fz) = name switch
        {
            "Default (Y,-X,Z)"  => (1, false, 0, true,  2, false),
            "(X,-Z,-Y)"         => (0, false, 2, true,  1, true),
            "(X, Y,-Z)"         => (0, false, 1, false, 2, true),
            "(Y, X,-Z)"         => (1, false, 0, false, 2, true),
            "(Z,-Y, X)"         => (2, false, 1, true,  0, false),
            _                   => (1, false, 0, true,  2, false),
        };
        (_recon.RemapSrcX, _recon.RemapFlipX) = (sx, fx);
        (_recon.RemapSrcY, _recon.RemapFlipY) = (sy, fy);
        (_recon.RemapSrcZ, _recon.RemapFlipZ) = (sz, fz);
    }

    /// <summary>Feed a batch of snapshots (called from a background thread).</summary>
    public void EnqueueSamples(IReadOnlyList<WatchDataSnapshot> samples)
    {
        if (samples.Count == 0) return;
        lock (_lock) _pending.AddRange(samples);
    }

    /// <summary>Capture the current pose as the zero reference and clear the trail.</summary>
    public void Reset()
    {
        lock (_lock) _pending.Clear();
        _recon.Reset();
    }

    /// <summary>
    /// Replay seek: drop queued samples and clear velocity + trail so they re-warm from the samples
    /// that follow, but keep the pose reference and accept timestamps older than the last processed.
    /// </summary>
    public void ResetForSeek()
    {
        lock (_lock) _pending.Clear();
        _recon.ResetForSeek();
    }

    /// <summary>Clear velocity trail only orientation reference unchanged.</summary>
    public void ResetVelocity()
    {
        // Flush pending queue first so queued high-velocity samples can't
        // immediately replay and restore the velocity we just cleared.
        lock (_lock) _pending.Clear();
        _recon.ResetVelocity();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        List<WatchDataSnapshot>? batch = null;
        lock (_lock)
        {
            if (_pending.Count > 0)
            {
                batch = _pending;
                _pending = new List<WatchDataSnapshot>();
            }
        }
        if (batch != null)
            foreach (var s in batch) _recon.Step(s);
        // Always repaint so the velocity trail visually decays toward zero even
        // when no new samples arrive (e.g. hand held still between gestures).
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        context.FillRectangle(_bg, new Rect(0, 0, w, h));

        var halfW = w / 2;
        DrawHand(context, new Rect(0, 0, halfW, h));
        DrawTrail(context, new Rect(halfW, 0, w - halfW, h));
    }

    // ---- Left: 3D hand/wrist model --------------------------------------

    private void DrawHand(DrawingContext ctx, Rect area)
    {
        var q = _recon.Orientation;
        var cx = area.X + area.Width / 2;
        var cy = area.Y + area.Height / 2;
        var size = Math.Min(area.Width, area.Height) * 0.30;

        Point P(float x, float y, float z)
        {
            var r = Vector3.Transform(new Vector3(x, y, z), q);
            var d = 4.0 + r.Z;
            return new Point(cx + r.X * size * 3.0 / d, cy - r.Y * size * 3.0 / d);
        }

        // Forearm (toward elbow, -Y) and palm + fingers (toward hand, +Y),
        // drawn near the watch-body mid-thickness plane (z ~ 0.11).
        FillQuad(ctx, _limbFill, _handPen, P(-0.30f, -0.55f, 0.11f), P(0.30f, -0.55f, 0.11f),
                 P(0.30f, -1.65f, 0.11f), P(-0.30f, -1.65f, 0.11f));
        FillQuad(ctx, _limbFill, _handPen, P(-0.45f, 0.55f, 0.11f), P(0.45f, 0.55f, 0.11f),
                 P(0.45f, 1.10f, 0.11f), P(-0.45f, 1.10f, 0.11f));
        // Fingers
        foreach (var fx in new[] { -0.34f, -0.11f, 0.11f, 0.34f })
            ctx.DrawLine(_fingerPen, P(fx, 1.10f, 0.11f), P(fx, 1.62f, 0.11f));
        // Thumb
        ctx.DrawLine(_fingerPen, P(0.45f, 0.70f, 0.11f), P(0.78f, 1.00f, 0.11f));

        // Watch body a thin box so roll/pitch/yaw are all legible.
        var bx = new[]
        {
            P(-0.5f, -0.5f, 0f), P(0.5f, -0.5f, 0f), P(0.5f, 0.5f, 0f), P(-0.5f, 0.5f, 0f),
            P(-0.5f, -0.5f, 0.22f), P(0.5f, -0.5f, 0.22f), P(0.5f, 0.5f, 0.22f), P(-0.5f, 0.5f, 0.22f),
        };
        int[,] edges =
        {
            {0,1},{1,2},{2,3},{3,0}, {4,5},{5,6},{6,7},{7,4}, {0,4},{1,5},{2,6},{3,7}
        };
        for (int i = 0; i < edges.GetLength(0); i++)
            ctx.DrawLine(_handPen, bx[edges[i, 0]], bx[edges[i, 1]]);

        // Watch face (top, z = 0.22) filled + a 12-o'clock marker so heading reads.
        FillQuad(ctx, _faceFill, _handPen, bx[4], bx[5], bx[6], bx[7]);
        var m0 = P(-0.14f, 0.52f, 0.23f);
        var m1 = P(0.14f, 0.52f, 0.23f);
        var m2 = P(0f, 0.78f, 0.23f);
        FillTriangle(ctx, _markerFill, m0, m1, m2);
    }

    // ---- Right: velocity trail ------------------------------------------
    // The trail shows the recent history of the hand's world-frame velocity
    // (m/s). Origin = still hand. Each direction/distance from origin is a
    // real measured motion direction + speed. Decays to zero in ~500 ms.

    private void DrawTrail(DrawingContext ctx, Rect area)
    {
        var cx = area.X + area.Width / 2;
        var cy = area.Y + area.Height / 2;
        var scale = Math.Min(area.Width, area.Height) / (2 * TrailVMax + 0.5);

        // Orthographic 3/4 camera basis.
        float ca = MathF.Cos(CamAzimuth), sa = MathF.Sin(CamAzimuth);
        float ce = MathF.Cos(CamElevation), se = MathF.Sin(CamElevation);
        var right = new Vector3(ca, -sa, 0);
        var up    = new Vector3(-sa * se, -ca * se, ce);

        Point W(Vector3 v) => new(
            cx + Vector3.Dot(v, right) * scale,
            cy - Vector3.Dot(v, up)    * scale);

        // Reference grid centred on origin (zero velocity = still hand).
        const float g = TrailVMax;
        const int divs = 5;
        for (int i = 0; i <= divs; i++)
        {
            float t = -g + 2 * g * i / divs;
            ctx.DrawLine(_gridPen, W(new Vector3(t, -g, 0)), W(new Vector3(t, g, 0)));
            ctx.DrawLine(_gridPen, W(new Vector3(-g, t, 0)), W(new Vector3(g, t, 0)));
        }

        // Axis arrows from origin (1 m/s length).
        var o = Vector3.Zero;
        ctx.DrawLine(_eastPen,  W(o), W(new Vector3(1f, 0,  0)));
        ctx.DrawLine(_northPen, W(o), W(new Vector3(0,  1f, 0)));
        ctx.DrawLine(_upPen,    W(o), W(new Vector3(0,  0,  1f)));

        // Velocity trail: fades oldest (dim) → newest (bright).
        int n = _recon.TrailCount;
        if (n >= 2)
        {
            var prev = W(_recon.TrailPoint(0));
            for (int i = 1; i < n; i++)
            {
                var cur = W(_recon.TrailPoint(i));
                byte alpha = (byte)(30 + 225 * i / (n - 1));
                ctx.DrawLine(
                    new Pen(new ImmutableSolidColorBrush(Color.FromArgb(alpha, 0xF4, 0xA2, 0x61)), 1.8),
                    prev, cur);
                prev = cur;
            }
        }

        // Current velocity dot + drop line to the zero-plane for the vertical component.
        if (n >= 1)
        {
            var v = _recon.TrailPoint(n - 1);
            ctx.DrawLine(_dropPen, W(new Vector3(v.X, v.Y, 0)), W(v));
            ctx.DrawEllipse(_dotFill, null, W(v), 4, 4);
        }
    }

    // ---- Drawing helpers -------------------------------------------------

    private static void FillQuad(DrawingContext ctx, IBrush fill, IPen pen, Point a, Point b, Point c, Point d)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(a, true);
            g.LineTo(b);
            g.LineTo(c);
            g.LineTo(d);
            g.EndFigure(true);
        }
        ctx.DrawGeometry(fill, pen, geo);
    }

    private static void FillTriangle(DrawingContext ctx, IBrush fill, Point a, Point b, Point c)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(a, true);
            g.LineTo(b);
            g.LineTo(c);
            g.EndFigure(true);
        }
        ctx.DrawGeometry(fill, null, geo);
    }
}
