using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace OcdWizard.ControlStation.Controls;

/// <summary>One step of the guided tour: a target control to spotlight (by name) and its blurb.</summary>
/// <param name="TargetName">Name of a control to highlight, or null for a centred, target-less step.</param>
public sealed record TutorialStep(string? TargetName, string Title, string Body);

/// <summary>
/// A full-window guided-tour overlay. Given an ordered list of <see cref="TutorialStep"/>s and a
/// root to search, it dims the window, spotlights each named control and shows an explanation
/// bubble beside it, with Back / Next / Skip navigation.
/// </summary>
public partial class TutorialOverlay : UserControl
{
    private readonly List<TutorialStep> _steps = new();
    private Visual? _searchRoot;
    private int _index;

    private Rectangle? _dimTop, _dimBottom, _dimLeft, _dimRight;
    private Border? _highlight, _bubble;
    private TextBlock? _stepCounter, _title, _body;
    private Button? _backBtn, _nextBtn;

    /// <summary>Raised when the tour ends (completed or skipped).</summary>
    public event Action? Finished;

    public TutorialOverlay()
    {
        InitializeComponent();
        _dimTop      = this.FindControl<Rectangle>("DimTop");
        _dimBottom   = this.FindControl<Rectangle>("DimBottom");
        _dimLeft     = this.FindControl<Rectangle>("DimLeft");
        _dimRight    = this.FindControl<Rectangle>("DimRight");
        _highlight   = this.FindControl<Border>("Highlight");
        _bubble      = this.FindControl<Border>("Bubble");
        _stepCounter = this.FindControl<TextBlock>("StepCounter");
        _title       = this.FindControl<TextBlock>("TitleBlock");
        _body        = this.FindControl<TextBlock>("BodyBlock");
        _backBtn     = this.FindControl<Button>("BackBtn");
        _nextBtn     = this.FindControl<Button>("NextBtn");
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>Begin the tour. <paramref name="searchRoot"/> is the visual tree to search for targets.</summary>
    public void Start(IReadOnlyList<TutorialStep> steps, Visual searchRoot)
    {
        if (steps.Count == 0) return;
        _steps.Clear();
        _steps.AddRange(steps);
        _searchRoot = searchRoot;
        _index = 0;
        IsVisible = true;
        Focus();
        ShowStep();
    }

    // Re-run layout whenever the overlay is resized (window resize) so the spotlight stays aligned.
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty && IsVisible)
            LayoutCurrent();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsVisible) return;
        switch (e.Key)
        {
            case Key.Escape: Finish(); e.Handled = true; break;
            case Key.Enter:
            case Key.Right: Advance(+1); e.Handled = true; break;
            case Key.Left: Advance(-1); e.Handled = true; break;
        }
    }

    private void OnSkip(object? sender, RoutedEventArgs e) => Finish();
    private void OnBack(object? sender, RoutedEventArgs e) => Advance(-1);
    private void OnNext(object? sender, RoutedEventArgs e) => Advance(+1);

    private void Advance(int delta)
    {
        var next = _index + delta;
        if (next < 0) return;
        if (next >= _steps.Count) { Finish(); return; }
        _index = next;
        ShowStep();
    }

    private void Finish()
    {
        IsVisible = false;
        Finished?.Invoke();
    }

    private void ShowStep()
    {
        var step = _steps[_index];
        if (_stepCounter != null) _stepCounter.Text = $"STEP {_index + 1} OF {_steps.Count}";
        if (_title != null) _title.Text = step.Title;
        if (_body != null) _body.Text = step.Body;
        if (_backBtn != null) _backBtn.IsEnabled = _index > 0;
        if (_nextBtn != null) _nextBtn.Content = _index >= _steps.Count - 1 ? "Done ✓" : "Next ▶";

        // The target may live inside a scrolled panel; pull it into view, then lay out after the
        // scroll/layout pass settles so its on-screen rectangle is final.
        FindTarget(step.TargetName)?.BringIntoView();
        Dispatcher.UIThread.Post(LayoutCurrent, DispatcherPriority.Loaded);
    }

    private Control? FindTarget(string? name)
    {
        if (string.IsNullOrEmpty(name) || _searchRoot == null) return null;
        foreach (var v in _searchRoot.GetVisualDescendants())
            if (v is Control c && c.Name == name && c.IsEffectivelyVisible && c.Bounds.Width > 0)
                return c;
        return null;
    }

    private void LayoutCurrent()
    {
        if (!IsVisible || _steps.Count == 0) return;
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        var target = FindTarget(_steps[_index].TargetName);
        Rect? hole = null;
        if (target != null && target.TranslatePoint(new Point(0, 0), this) is Point tl)
        {
            var r = new Rect(tl, target.Bounds.Size);
            double x = Math.Max(0, r.X), y = Math.Max(0, r.Y);
            double right = Math.Min(w, r.Right), bottom = Math.Min(h, r.Bottom);
            if (right > x && bottom > y) hole = new Rect(x, y, right - x, bottom - y);
        }

        if (hole is Rect g)
        {
            SetRect(_dimTop,    0,        0,         w,            g.Y);
            SetRect(_dimBottom, 0,        g.Bottom,  w,            h - g.Bottom);
            SetRect(_dimLeft,   0,        g.Y,       g.X,          g.Height);
            SetRect(_dimRight,  g.Right,  g.Y,       w - g.Right,  g.Height);

            const double pad = 5;
            PlaceHighlight(g.X - pad, g.Y - pad, g.Width + 2 * pad, g.Height + 2 * pad, true);
            PlaceBubbleNear(g, w, h);
        }
        else
        {
            // No (visible) target — dim the whole window and centre the bubble.
            SetRect(_dimTop, 0, 0, w, h);
            SetRect(_dimBottom, 0, 0, 0, 0);
            SetRect(_dimLeft, 0, 0, 0, 0);
            SetRect(_dimRight, 0, 0, 0, 0);
            PlaceHighlight(0, 0, 0, 0, false);
            PlaceBubbleCenter(w, h);
        }
    }

    private static void SetRect(Rectangle? r, double x, double y, double w, double h)
    {
        if (r == null) return;
        Canvas.SetLeft(r, x);
        Canvas.SetTop(r, y);
        r.Width = Math.Max(0, w);
        r.Height = Math.Max(0, h);
    }

    private void PlaceHighlight(double x, double y, double w, double h, bool visible)
    {
        if (_highlight == null) return;
        _highlight.IsVisible = visible;
        Canvas.SetLeft(_highlight, x);
        Canvas.SetTop(_highlight, y);
        _highlight.Width = Math.Max(0, w);
        _highlight.Height = Math.Max(0, h);
    }

    private void PlaceBubbleNear(Rect target, double w, double h)
    {
        if (_bubble == null) return;
        _bubble.Measure(new Size(_bubble.Width, h));
        double bw = _bubble.Width, bh = _bubble.DesiredSize.Height;
        const double gap = 16, margin = 16;

        double by;
        if (target.Bottom + gap + bh <= h - margin) by = target.Bottom + gap;            // below
        else if (target.Y - gap - bh >= margin)     by = target.Y - gap - bh;            // above
        else                                        by = Math.Clamp((h - bh) / 2, margin, Math.Max(margin, h - bh - margin));

        double bx = Math.Clamp(target.X, margin, Math.Max(margin, w - bw - margin));
        Canvas.SetLeft(_bubble, bx);
        Canvas.SetTop(_bubble, by);
    }

    private void PlaceBubbleCenter(double w, double h)
    {
        if (_bubble == null) return;
        _bubble.Measure(new Size(_bubble.Width, h));
        double bw = _bubble.Width, bh = _bubble.DesiredSize.Height;
        Canvas.SetLeft(_bubble, (w - bw) / 2);
        Canvas.SetTop(_bubble, (h - bh) / 2);
    }
}
