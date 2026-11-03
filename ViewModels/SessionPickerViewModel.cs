using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OcdWizard.ControlStation.Database;
using OcdWizard.ControlStation.Localization;
using OcdWizard.ControlStation.Models;

namespace OcdWizard.ControlStation.ViewModels;

/// <summary>One databank session as shown in the picker. <see cref="IsChecked"/> only matters in multi-select mode.</summary>
public partial class SessionSummaryRow : ObservableObject
{
    public required SessionSummary Summary { get; init; }
    public required string StartDisplay { get; init; }
    public required string DurationDisplay { get; init; }

    [ObservableProperty] private bool _isChecked;
}

/// <summary>
/// Picks one session (CSV export, open in Replay) or several at once (adding rows to Master
/// Notes). Multi-select uses per-row checkboxes rather than ctrl-click: the list is scrolled
/// through repeatedly while assembling a table, and a stray click must not silently discard a
/// selection built up over a dozen rows.
/// </summary>
public partial class SessionPickerViewModel : ObservableObject
{
    private readonly TaskCompletionSource<IReadOnlyList<SessionSummary>> _tcs = new();

    public ObservableCollection<SessionSummaryRow> Sessions { get; } = [];

    /// <summary>True when the picker returns a set of sessions instead of exactly one.</summary>
    public bool MultiSelect { get; }

    public string ConfirmLabel { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    private SessionSummaryRow? _selectedSession;

    /// <summary>Resolves with every chosen session; empty when the picker was cancelled.</summary>
    public Task<IReadOnlyList<SessionSummary>> ResultMany => _tcs.Task;

    /// <summary>Single-select convenience view of <see cref="ResultMany"/>.</summary>
    public Task<SessionSummary?> Result => _tcs.Task.ContinueWith(t => t.Result.FirstOrDefault());

    public SessionPickerViewModel(
        DatabaseService db,
        string? preSelectId = null,
        bool multiSelect = false,
        string? confirmLabel = null)
    {
        MultiSelect = multiSelect;
        ConfirmLabel = confirmLabel ?? Loc.Get("SessionPicker_ExportButton");
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
                : Loc.Get("SessionPicker_DurationOngoing");
            var row = new SessionSummaryRow
            {
                Summary = s,
                StartDisplay = $"{start:yyyy-MM-dd HH:mm}",
                DurationDisplay = duration
            };
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SessionSummaryRow.IsChecked)) ConfirmCommand.NotifyCanExecuteChanged();
            };
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
        if (MultiSelect)
        {
            _tcs.TrySetResult(Sessions.Where(r => r.IsChecked).Select(r => r.Summary).ToList());
            return;
        }
        _tcs.TrySetResult(SelectedSession is { } row ? [row.Summary] : []);
    }

    private bool CanConfirm() => MultiSelect ? Sessions.Any(r => r.IsChecked) : SelectedSession != null;

    [RelayCommand]
    private void Cancel() => _tcs.TrySetResult([]);

    [RelayCommand]
    private void CheckAll() { foreach (var r in Sessions) r.IsChecked = true; }

    [RelayCommand]
    private void CheckNone() { foreach (var r in Sessions) r.IsChecked = false; }
}
