using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OcdWizard.ControlStation.Controls;
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
        new(null, "Welcome to the JITAI Wizard Control Station 👋",
            "This quick tour points out each part of the cockpit you'll use to run a session. " +
            "Use Next / Back to step through it, or Skip to jump straight in — you can reopen it " +
            "any time with the Tutorial button."),
        new("TutSessionName", "Name your session",
            "Give each recording session a clear name (e.g. the participant code). It's stored " +
            "alongside every data row you later export."),
        new("TutWatchToggle", "One or two watches",
            "Switch between a single participant and a side-by-side dual-participant layout when " +
            "two watches are streaming at once."),
        new("TutConnection", "Connect a watch",
            "Type the phone's IP, or press Scan to auto-discover devices. Pick one from the list, " +
            "then Connect to start streaming its live data."),
        new("TutScan", "Scan for devices",
            "Scan now finds both Wi-Fi devices broadcasting on the network and any phone attached " +
            "over USB debugging — a USB connection is folded in, not overwritten."),
        new("TutMotion", "Motion & rotation tracker",
            "A live 3D model of the wrist plus the reconstructed motion trail. The little heart up " +
            "here beats in time with the participant's current BPM, so you can read heart rate at a " +
            "glance while watching their movement."),
        new("TutSensors", "Live sensors",
            "The raw real-time read-outs — heart rate and per-axis accelerometer deltas — straight " +
            "off the watch."),
        new("TutRunner", "Experiment runner",
            "Drive the experiment on the watch: start, pause, step between events and follow the " +
            "live countdown for the active task."),
        new("TutIntervention", "Send an intervention",
            "Manually fire a notification or mini-game (trivia, Simon Says, …) to the watch — every " +
            "intervention you trigger is logged as research data."),
        new("TutStartSession", "Start & stop recording",
            "When you're set up, Start Session begins recording and Stop Session ends it. Notes you " +
            "type during a run are saved against the session."),
        new("TutExport", "Export your data",
            "Export any session to CSV for analysis. The trivia question bank is managed from the " +
            "Trivia button right next to it."),
        new("TutTutorialBtn", "That's the tour! 🚀",
            "You're ready to run a session. Press this Tutorial button whenever you'd like to see " +
            "these tips again. Good luck!"),
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
}
