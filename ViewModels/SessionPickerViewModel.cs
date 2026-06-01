using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OcdWizard.ControlStation.Database;
using OcdWizard.ControlStation.Models;

namespace OcdWizard.ControlStation.ViewModels;

public partial class SessionPickerViewModel : ObservableObject
{
    private readonly TaskCompletionSource<SessionSummary?> _tcs = new();

    public ObservableCollection<SessionSummaryRow> Sessions { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private SessionSummaryRow? _selectedSession;

    public Task<SessionSummary?> Result => _tcs.Task;

    public SessionPickerViewModel(DatabaseService db, string? preSelectId = null)
    {
        _ = LoadAsync(db, preSelectId);
    }

    private async Task LoadAsync(DatabaseService db, string? preSelectId)
    {
        var sessions = await db.LoadAllSessionsAsync();
        foreach (var s in sessions)
        {
            var start = DateTimeOffset.FromUnixTimeMilliseconds(s.StartedAt).ToLocalTime();
            var duration = s.EndedAt.HasValue
                ? FormatDuration(s.EndedAt.Value - s.StartedAt)
                : "ongoing";
            var row = new SessionSummaryRow(
                s,
                $"{start:yyyy-MM-dd HH:mm}",
                duration);
            Sessions.Add(row);
            if (preSelectId != null && s.Id == preSelectId)
                SelectedSession = row;
        }
        if (SelectedSession == null && Sessions.Count > 0)
            SelectedSession = Sessions[0];
    }

    private static string FormatDuration(long ms)
    {
        var ts = TimeSpan.FromMilliseconds(ms);
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}h {ts.Minutes}m"
            : $"{ts.Minutes}m {ts.Seconds}s";
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        _tcs.TrySetResult(SelectedSession?.Summary);
    }

    private bool CanConfirm() => SelectedSession != null;

    [RelayCommand]
    private void Cancel()
    {
        _tcs.TrySetResult(null);
    }
}

public record SessionSummaryRow(SessionSummary Summary, string StartDisplay, string DurationDisplay);
