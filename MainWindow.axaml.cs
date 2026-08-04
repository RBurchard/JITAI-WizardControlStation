using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OcdWizard.ControlStation.Controls;
using OcdWizard.ControlStation.Localization;
using OcdWizard.ControlStation.Services;
using OcdWizard.ControlStation.ViewModels;
using OcdWizard.ControlStation.Views;

namespace OcdWizard.ControlStation;

public partial class MainWindow : Window
{
    private bool _autoTutorialChecked;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();

        if (this.FindControl<TutorialOverlay>("Tutorial") is { } overlay)
            overlay.Finished += FirstRunState.MarkTutorialSeen;

        Opened += OnWindowOpened;
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        if (_autoTutorialChecked) return;
        _autoTutorialChecked = true;
        if (FirstRunState.TutorialSeen) return;
        // Defer so the participant panels are fully realised before we measure tour targets.
        Dispatcher.UIThread.Post(StartTutorial, DispatcherPriority.Background);
    }

    private void OnTutorialClick(object? sender, RoutedEventArgs e) => StartTutorial();

    private void StartTutorial() =>
        this.FindControl<TutorialOverlay>("Tutorial")?.Start(BuildTutorialSteps(), this);

    private static IReadOnlyList<TutorialStep> BuildTutorialSteps() =>
    [
        new(null, Loc.Get("Tutorial_Welcome_Title"), Loc.Get("Tutorial_Welcome_Body")),
        new("TutSessionName", Loc.Get("Tutorial_SessionName_Title"), Loc.Get("Tutorial_SessionName_Body")),
        new("TutWatchToggle", Loc.Get("Tutorial_WatchToggle_Title"), Loc.Get("Tutorial_WatchToggle_Body")),
        new("TutConnection", Loc.Get("Tutorial_Connection_Title"), Loc.Get("Tutorial_Connection_Body")),
        new("TutScan", Loc.Get("Tutorial_Scan_Title"), Loc.Get("Tutorial_Scan_Body")),
        new("TutMotion", Loc.Get("Tutorial_Motion_Title"), Loc.Get("Tutorial_Motion_Body")),
        new("TutSensors", Loc.Get("Tutorial_Sensors_Title"), Loc.Get("Tutorial_Sensors_Body")),
        new("TutRunner", Loc.Get("Tutorial_Runner_Title"), Loc.Get("Tutorial_Runner_Body")),
        new("TutIntervention", Loc.Get("Tutorial_Intervention_Title"), Loc.Get("Tutorial_Intervention_Body")),
        new("TutStartSession", Loc.Get("Tutorial_StartStop_Title"), Loc.Get("Tutorial_StartStop_Body")),
        new("TutExport", Loc.Get("Tutorial_Export_Title"), Loc.Get("Tutorial_Export_Body")),
        new("TutTutorialBtn", Loc.Get("Tutorial_Finish_Title"), Loc.Get("Tutorial_Finish_Body")),
    ];

    private async void OnManageTriviaClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            await vm.OpenTriviaEditorCommand.ExecuteAsync(this);
    }

    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            await vm.ExportCommand.ExecuteAsync(this);
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e) =>
        await SettingsDialog.ShowAsync(this);
}
