using System;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using OcdWizard.ControlStation.ViewModels;

namespace OcdWizard.ControlStation.Views;

public partial class ReplayView : UserControl
{
    private TimelineControl? _timeline;
    private TextBox? _timeEntry;
    private ReplayViewModel? _vm;

    public ReplayView()
    {
        InitializeComponent();
        _timeline = this.FindControl<TimelineControl>("Timeline");
        _timeEntry = this.FindControl<TextBox>("TimeEntry");

        if (_timeline != null)
            _timeline.SeekRequested += abs => _vm?.SeekToAbsoluteFromTimeline(abs);

        AttachedToVisualTree += (_, _) => Bind(DataContext as ReplayViewModel);
        DataContextChanged += (_, _) => Bind(DataContext as ReplayViewModel);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Bind(ReplayViewModel? vm)
    {
        if (ReferenceEquals(_vm, vm)) return;
        if (_vm != null) _vm.TimelineChanged -= RefreshTimeline;
        _vm = vm;
        if (_vm != null)
        {
            _vm.OwnerWindow = TopLevel.GetTopLevel(this) as Window;
            _vm.TimelineChanged += RefreshTimeline;
            RefreshTimeline();
        }
    }

    private void RefreshTimeline()
    {
        if (_vm != null) _timeline?.SetMarkers(_vm.AutoMarkers, _vm.UserMarkers);
    }

    private void OnTimeEntryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; GoToEnteredTime(); }
    }

    private void OnGoToTimeClick(object? sender, RoutedEventArgs e) => GoToEnteredTime();

    private void GoToEnteredTime()
    {
        if (_vm == null || _timeEntry?.Text is not string text) return;
        if (TryParseRelative(text, out var ms)) _vm.SeekToRelativeMs(ms);
    }

    /// <summary>Parse <c>mm:ss.mmm</c>, <c>ss.mmm</c> or <c>hh:mm:ss.mmm</c> into ms from the start.</summary>
    private static bool TryParseRelative(string text, out long ms)
    {
        ms = 0;
        text = text.Trim();
        if (text.Length == 0) return false;
        var parts = text.Split(':');
        double seconds;
        long minutes = 0, hours = 0;
        try
        {
            if (parts.Length == 1)
                seconds = double.Parse(parts[0], CultureInfo.InvariantCulture);
            else if (parts.Length == 2)
            {
                minutes = long.Parse(parts[0], CultureInfo.InvariantCulture);
                seconds = double.Parse(parts[1], CultureInfo.InvariantCulture);
            }
            else
            {
                hours = long.Parse(parts[0], CultureInfo.InvariantCulture);
                minutes = long.Parse(parts[1], CultureInfo.InvariantCulture);
                seconds = double.Parse(parts[2], CultureInfo.InvariantCulture);
            }
        }
        catch { return false; }
        ms = (long)Math.Round((((hours * 60) + minutes) * 60 + seconds) * 1000);
        return ms >= 0;
    }
}
