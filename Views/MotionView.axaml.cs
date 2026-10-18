using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using OcdWizard.ControlStation.Models;
using OcdWizard.ControlStation.ViewModels;

namespace OcdWizard.ControlStation.Views;

public partial class MotionView : UserControl
{
    private MotionVisualizer? _visualizer;
    private IMotionSource? _vm;
    private bool _suppressRemapEvents;

    // ComboBox/CheckBox refs populated in InitializeComponent
    private ComboBox? _cbSrcX, _cbSrcY, _cbSrcZ;
    private CheckBox? _ckFlipX, _ckFlipY, _ckFlipZ;

    public MotionView()
    {
        InitializeComponent();
        _visualizer = this.FindControl<MotionVisualizer>("Visualizer");
        _cbSrcX  = this.FindControl<ComboBox>("CbSrcX");
        _cbSrcY  = this.FindControl<ComboBox>("CbSrcY");
        _cbSrcZ  = this.FindControl<ComboBox>("CbSrcZ");
        _ckFlipX = this.FindControl<CheckBox>("CkFlipX");
        _ckFlipY = this.FindControl<CheckBox>("CkFlipY");
        _ckFlipZ = this.FindControl<CheckBox>("CkFlipZ");

        // Populate axis dropdowns
        foreach (var cb in new[] { _cbSrcX, _cbSrcY, _cbSrcZ })
            if (cb != null)
                cb.ItemsSource = MotionVisualizer.AxisNames;

        // Set defaults to match MotionReconstructor defaults (X, -Z, -Y)
        SyncControlsFromVisualizer();

        AttachedToVisualTree += (_, _) => Subscribe(DataContext as IMotionSource);
        DetachedFromVisualTree += (_, _) => Subscribe(null);
        DataContextChanged += (_, _) => Subscribe(DataContext as IMotionSource);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void Subscribe(IMotionSource? source)
    {
        if (ReferenceEquals(_vm, source)) return;
        if (_vm != null)
        {
            _vm.MotionSamplesReceived -= OnMotionSamples;
            _vm.MotionResetRequested -= OnMotionReset;
        }
        _vm = source;
        if (_vm != null)
        {
            _vm.MotionSamplesReceived += OnMotionSamples;
            _vm.MotionResetRequested += OnMotionReset;
        }
    }

    private void OnMotionSamples(IReadOnlyList<WatchDataSnapshot> samples)
        => _visualizer?.EnqueueSamples(samples);

    // Replay seeking backwards clears velocity + trail so they re-warm cleanly. This must NOT be the
    // user-facing Reset(), which re-zeroes the pose to the current one and discards older samples.
    private void OnMotionReset() => _visualizer?.ResetForSeek();

    private void OnResetVelocityClick(object? sender, RoutedEventArgs e) => _visualizer?.ResetVelocity();
    private void OnResetClick(object? sender, RoutedEventArgs e) => _visualizer?.Reset();

    // ── Preset buttons ────────────────────────────────────────────────────

    private void OnPreset0(object? sender, RoutedEventArgs e) => ApplyPreset("Default (Y,-X,Z)");
    private void OnPreset1(object? sender, RoutedEventArgs e) => ApplyPreset("(X,-Z,-Y)");
    private void OnPreset2(object? sender, RoutedEventArgs e) => ApplyPreset("(X, Y,-Z)");
    private void OnPreset3(object? sender, RoutedEventArgs e) => ApplyPreset("(Y, X,-Z)");

    private void ApplyPreset(string name)
    {
        _visualizer?.ApplyPreset(name);
        SyncControlsFromVisualizer();
    }

    // ── Per-axis dropdowns / flip checkboxes ──────────────────────────────

    // Called by both ComboBox SelectionChanged and CheckBox IsCheckedChanged.
    // Avalonia fires different event arg types so we use a shared implementation.
    private void OnRemapChanged(object? sender, RoutedEventArgs e) => PushControlsToVisualizer();
    private void OnRemapChanged(object? sender, SelectionChangedEventArgs e) => PushControlsToVisualizer();

    private void PushControlsToVisualizer()
    {
        if (_suppressRemapEvents || _visualizer == null) return;
        if (_cbSrcX?.SelectedIndex is int sx and >= 0) _visualizer.RemapSrcX = sx;
        if (_cbSrcY?.SelectedIndex is int sy and >= 0) _visualizer.RemapSrcY = sy;
        if (_cbSrcZ?.SelectedIndex is int sz and >= 0) _visualizer.RemapSrcZ = sz;
        _visualizer.RemapFlipX = _ckFlipX?.IsChecked == true;
        _visualizer.RemapFlipY = _ckFlipY?.IsChecked == true;
        _visualizer.RemapFlipZ = _ckFlipZ?.IsChecked == true;
    }

    private void SyncControlsFromVisualizer()
    {
        if (_visualizer == null) return;
        _suppressRemapEvents = true;
        if (_cbSrcX != null) _cbSrcX.SelectedIndex = _visualizer.RemapSrcX;
        if (_cbSrcY != null) _cbSrcY.SelectedIndex = _visualizer.RemapSrcY;
        if (_cbSrcZ != null) _cbSrcZ.SelectedIndex = _visualizer.RemapSrcZ;
        if (_ckFlipX != null) _ckFlipX.IsChecked = _visualizer.RemapFlipX;
        if (_ckFlipY != null) _ckFlipY.IsChecked = _visualizer.RemapFlipY;
        if (_ckFlipZ != null) _ckFlipZ.IsChecked = _visualizer.RemapFlipZ;
        _suppressRemapEvents = false;
    }
}
