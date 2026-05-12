using System;
using System.IO;
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
    private string? _activeSessionId;

    [ObservableProperty] private string _sessionName = "New Session";
    [ObservableProperty] private bool _sessionActive;
    [ObservableProperty] private string _statusMessage = "Ready";

    public ParticipantViewModel Participant1 { get; }
    public ParticipantViewModel Participant2 { get; }

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
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _db.InsertSessionAsync(_activeSessionId, now, SessionName);
        Participant1.SetSession(_activeSessionId);
        Participant2.SetSession(_activeSessionId);
        SessionActive = true;
        StatusMessage = $"Session started: {_activeSessionId[..8]}";
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
    private async Task ExportAsync(IStorageProvider? storageProvider)
    {
        if (_activeSessionId == null || storageProvider == null)
        {
            StatusMessage = "No active session to export";
            return;
        }

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Session CSV",
            SuggestedFileName = $"session_{_activeSessionId[..8]}_{DateTime.Now:yyyyMMdd_HHmm}.csv",
            FileTypeChoices = [new FilePickerFileType("CSV") { Patterns = ["*.csv"] }]
        });

        if (file?.Path.LocalPath is string path)
        {
            await _csv.ExportAsync(_activeSessionId, path);
            StatusMessage = $"Exported to {Path.GetFileName(path)}";
        }
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
