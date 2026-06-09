using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OcdWizard.ControlStation.Database;
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

    [ObservableProperty] private string _sessionName = "New Session";
    [ObservableProperty] private string _sessionNotes = "";
    [ObservableProperty] private bool _sessionActive;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private bool _adbMissing;
    [ObservableProperty] private bool _dualParticipantMode = false;
    public string AdbDownloadUrl => _adb.DownloadUrl;

    public ParticipantViewModel Participant1 { get; }
    public ParticipantViewModel Participant2 { get; }
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
        if (_adb.TryLocate())
            _usbSource.Start();
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        await _db.InitializeAsync();
        StatusMessage = "Database ready";
    }

    [RelayCommand]
    private async Task StartSessionAsync()
    {
        _activeSessionId = Guid.NewGuid().ToString();
        SessionNotes = "";
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.InsertSessionAsync(_activeSessionId, now, SessionName);
        await Participant1.SetSessionAsync(_activeSessionId, SessionName);
        await Participant2.SetSessionAsync(_activeSessionId, SessionName);
        SessionActive = true;
        StatusMessage = $"Session started: {_activeSessionId[..8]}";
    }

    [RelayCommand]
    private async Task SaveSessionNotesAsync()
    {
        if (_activeSessionId == null) { StatusMessage = "No active session for notes"; return; }
        await _db.UpdateSessionNotesAsync(_activeSessionId, SessionNotes);
        StatusMessage = "Session notes saved";
    }

    [RelayCommand]
    private async Task StopSessionAsync()
    {
        if (_activeSessionId == null) return;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.EndSessionAsync(_activeSessionId, now);
        SessionActive = false;
        StatusMessage = "Session ended";
    }

    [RelayCommand]
    private async Task ExportAsync(Window? owner)
    {
        if (owner == null) return;

        var sessions = await _db.LoadAllSessionsAsync();
        if (sessions.Count == 0)
        {
            StatusMessage = "No sessions in database";
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
            Title = "Export Session CSV",
            SuggestedFileName = $"{SanitizeFileName(sessionName)}_{DateTime.Now:yyyyMMdd_HHmm}.csv",
            FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }]
        });

        if (file?.Path.LocalPath is string path)
        {
            await _csv.ExportAsync(sessionId, path);
            StatusMessage = $"Exported to {Path.GetFileName(path)}";
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
