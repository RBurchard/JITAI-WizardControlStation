using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OcdWizard.ControlStation.Database;
using OcdWizard.ControlStation.Localization;
using OcdWizard.ControlStation.Services;
using OcdWizard.ControlStation.Views;

namespace OcdWizard.ControlStation.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly CsvExportService _csv;
    private readonly AdbTransport _adb = new();
    private readonly UsbDeviceSource _usbSource;
    private string? _activeSessionId;
    private string? _lastUsedSessionName;
    private bool _sessionAutoStarted;
    private bool _endingAutoSession;

    /// <summary>Set by MainWindow after construction so auto-end-of-experiment can prompt for a save name.</summary>
    public Window? OwnerWindow { get; set; }

    [ObservableProperty] private string _sessionName = Loc.Get("Main_DefaultSessionName");
    [ObservableProperty] private string _sessionNotes = "";
    [ObservableProperty] private bool _sessionActive;
    [ObservableProperty] private bool _isStartingSession;
    [ObservableProperty] private string _statusMessage = Loc.Get("Main_StatusReady");
    [ObservableProperty] private bool _adbMissing;
    [ObservableProperty] private bool _dualParticipantMode = false;
    // Top-level mode: false = live "Experiment Mode", true = "Replay Mode" (CSV playback).
    [ObservableProperty] private bool _isReplayMode;
    public string AdbDownloadUrl => _adb.DownloadUrl;

    public ParticipantViewModel Participant1 { get; }
    public ParticipantViewModel Participant2 { get; }
    public ReplayViewModel Replay { get; } = new();
    public UsbDeviceSource UsbSource => _usbSource;

    private static string DbPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OcdWizard", "data.db");

    public MainViewModel()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        _db = new DatabaseService(DbPath);
        _csv = new CsvExportService(_db);
        Participant1 = new ParticipantViewModel("P1", _db);
        Participant2 = new ParticipantViewModel("P2", _db);
        _adb.AdbMissing += () => Avalonia.Threading.Dispatcher.UIThread.Post(() => AdbMissing = true);
        _usbSource = new UsbDeviceSource(_adb);
        _usbSource.DeviceAdded += d => Avalonia.Threading.Dispatcher.UIThread.Post(() => {
            Participant1.AddDiscoveredDevice(d);
            Participant2.AddDiscoveredDevice(d);
        });
        _usbSource.DeviceRemoved += d => Avalonia.Threading.Dispatcher.UIThread.Post(() => {
            Participant1.RemoveDiscoveredDevice(d);
            Participant2.RemoveDiscoveredDevice(d);
        });
        // Let a manual Scan fold in the live USB devices instead of overwriting them.
        Participant1.UsbDeviceProvider = () => _usbSource.Devices.ToList();
        Participant2.UsbDeviceProvider = () => _usbSource.Devices.ToList();
        // Starting an experiment without an active session orphans its data (it can never be
        // found again in the Session Picker / export) — auto-start one if needed, and auto-end
        // it once every run has stopped, so a forgotten "Start Session" never loses data.
        Participant1.EnsureSessionStarted = EnsureSessionStartedAsync;
        Participant2.EnsureSessionStarted = EnsureSessionStartedAsync;
        Participant1.RunLifecycle.RunStopped += _ => OnExperimentRunStopped();
        Participant2.RunLifecycle.RunStopped += _ => OnExperimentRunStopped();
        if (_adb.TryLocate())
            _usbSource.Start();

        _lastUsedSessionName = SessionNameState.LoadLastUsedName();
        if (!string.IsNullOrWhiteSpace(_lastUsedSessionName))
            SessionName = _lastUsedSessionName;

        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        await _db.InitializeAsync();
        StatusMessage = Loc.Get("Main_StatusDatabaseReady");
    }

    /// <summary>
    /// Invoked by a participant before dispatching "start"/"auto" to the phone. No-ops if a
    /// session is already running; otherwise starts one and remembers that it was implicit, so
    /// it can be auto-ended (with a save-name prompt) once the experiment stops, without
    /// touching sessions the researcher started themselves.
    /// </summary>
    private async Task EnsureSessionStartedAsync()
    {
        if (SessionActive) return;
        await StartSessionAsync();
        _sessionAutoStarted = true;
    }

    [RelayCommand]
    private async Task StartSessionAsync()
    {
        _sessionAutoStarted = false;
        IsStartingSession = true;
        try
        {
            _activeSessionId = Guid.NewGuid().ToString();
            SessionNotes = "";
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await _db.InsertSessionAsync(_activeSessionId, now, SessionName);
            await Participant1.SetSessionAsync(_activeSessionId, SessionName);
            await Participant2.SetSessionAsync(_activeSessionId, SessionName);
            SessionActive = true;
            StatusMessage = Loc.Format("Main_StatusSessionStarted", _activeSessionId[..8]);
        }
        finally
        {
            IsStartingSession = false;
        }
    }

    [RelayCommand]
    private async Task SaveSessionNotesAsync()
    {
        if (_activeSessionId == null) { StatusMessage = Loc.Get("Main_StatusNoActiveSessionForNotes"); return; }
        await _db.UpdateSessionNotesAsync(_activeSessionId, SessionNotes);
        StatusMessage = Loc.Get("Main_StatusSessionNotesSaved");
    }

    [RelayCommand]
    private async Task StopSessionAsync()
    {
        if (_activeSessionId == null) return;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.EndSessionAsync(_activeSessionId, now);
        SessionNameState.SaveLastUsedName(SessionName);
        _lastUsedSessionName = SessionName;
        SessionActive = false;
        StatusMessage = Loc.Get("Main_StatusSessionEnded");
    }

    // RunStopped fires on the WebSocket receive thread — hop to the UI thread before touching
    // observable state or showing a dialog.
    private void OnExperimentRunStopped() =>
        Dispatcher.UIThread.Post(() => _ = EndAutoSessionIfIdleAsync());

    /// <summary>
    /// Once every participant's run has stopped, auto-ends a session that was itself
    /// auto-started (sessions the researcher started manually are left running, since they may
    /// be deliberately running several experiment passes under one session). If the name is
    /// still the default or unchanged from last time, prompts for a real one first.
    /// </summary>
    private async Task EndAutoSessionIfIdleAsync()
    {
        if (_endingAutoSession) return;
        if (!_sessionAutoStarted || !SessionActive || _activeSessionId == null) return;
        if (Participant1.RunLifecycle.CurrentRunId != null) return;
        if (Participant2.RunLifecycle.CurrentRunId != null) return;

        _endingAutoSession = true;
        try
        {
            var nameUnchanged = SessionName == Loc.Get("Main_DefaultSessionName") || SessionName == _lastUsedSessionName;
            if (nameUnchanged && OwnerWindow != null)
            {
                var chosen = await SessionSaveNameDialog.ShowAsync(OwnerWindow, SessionName);
                if (!string.IsNullOrWhiteSpace(chosen))
                    SessionName = chosen.Trim();
            }

            await _db.UpdateSessionNameAsync(_activeSessionId, SessionName);
            await StopSessionAsync();
            StatusMessage = Loc.Get("Main_StatusSessionAutoEnded");
        }
        finally
        {
            _endingAutoSession = false;
        }
    }

    [RelayCommand]
    private async Task ExportAsync(Window? owner)
    {
        if (owner == null) return;

        var sessions = await _db.LoadAllSessionsAsync();
        if (sessions.Count == 0)
        {
            StatusMessage = Loc.Get("Main_StatusNoSessionsInDatabase");
            return;
        }

        string sessionId;
        string sessionName;

        if (sessions.Count == 1 && sessions[0].Id == _activeSessionId)
        {
            // Only one session and it's the active one — skip the picker
            sessionId = sessions[0].Id;
            sessionName = sessions[0].Name;
        }
        else
        {
            var vm = new SessionPickerViewModel(_db, _activeSessionId);
            var picked = await SessionPickerDialog.ShowAsync(owner, vm);
            if (picked == null) return;
            sessionId = picked.Id;
            sessionName = picked.Name;
        }

        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Loc.Get("Main_ExportSessionCsvDialogTitle"),
            SuggestedFileName = $"{SanitizeFileName(sessionName)}_{DateTime.Now:yyyyMMdd_HHmm}.csv",
            FileTypeChoices = [new FilePickerFileType(Loc.Get("Common_FileType_Csv")) { Patterns = ["*.csv"] }]
        });

        if (file?.Path.LocalPath is string path)
        {
            await _csv.ExportAsync(sessionId, path);
            StatusMessage = Loc.Format("Main_StatusExportedTo", Path.GetFileName(path));
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray())
            .Replace(' ', '_')
            .TrimStart('_');
    }

    [RelayCommand]
    private async Task OpenTriviaEditorAsync(Window? owner)
    {
        var vm = new TriviaEditorViewModel(_db, []);
        await vm.LoadAsync();
        var dialog = new TriviaEditorDialog { DataContext = vm };
        if (owner != null)
            await dialog.ShowDialog(owner);
    }
}
