using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OcdWizard.ControlStation.Localization;
using OcdWizard.ControlStation.Models;
using OcdWizard.ControlStation.Services;
using OcdWizard.ControlStation.Views;

namespace OcdWizard.ControlStation.ViewModels;

/// <summary>One experiment event shown in the (optional) attached-experiment reference list.</summary>
public partial class ReplayEventRow : ObservableObject
{
    public required string IndexLabel { get; init; }
    public required string Type { get; init; }
    public required float Duration { get; init; }
    [ObservableProperty] private bool _isCurrent;
}

/// <summary>
/// Replay Mode controller: imports a CSV export, plays it back on a millisecond-precise clock,
/// drives the per-participant motion views + sensor read-outs, lets the researcher drop named
/// markers, and saves notes + markers back to a CSV (prompting overwrite vs new copy).
/// </summary>
public partial class ReplayViewModel : ObservableObject
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly DispatcherTimer _timer;
    private DateTime _lastTickUtc;

    private ReplayData? _data;
    private long _minTs, _maxTs;
    private string? _filePath;
    private bool _suppressNotesDirty;

    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private string _importedFileName = "";
    [ObservableProperty] private string _sessionName = "";
    [ObservableProperty] private string _experimentName = "";
    [ObservableProperty] private bool _hasExperiment;
    [ObservableProperty] private string _notes = "";
    [ObservableProperty] private bool _isDirty;

    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private double _playbackSpeed = 1.0;
    [ObservableProperty] private string _playbackSpeedDisplay = Loc.Format("Replay_PlaybackSpeedFormat", 1.0);
    [ObservableProperty] private long _currentTimeMs;
    [ObservableProperty] private long _durationMs;
    [ObservableProperty] private string _currentTimeDisplay = "00:00.000";
    [ObservableProperty] private string _durationDisplay = "00:00.000";

    // Overlay (best-effort experiment progress at the current time).
    [ObservableProperty] private bool _overlayActive;
    [ObservableProperty] private string _overlayTitle = "";
    [ObservableProperty] private string _overlayDetail = "";
    [ObservableProperty] private bool _hasAttachedExperiment;
    [ObservableProperty] private string _attachedExperimentName = "";

    public ObservableCollection<ReplayParticipantViewModel> Participants { get; } = new();
    public ObservableCollection<UserMarker> UserMarkers { get; } = new();
    public ObservableCollection<ReplayEventRow> ExperimentEvents { get; } = new();
    public IReadOnlyList<ReplayMarker> AutoMarkers { get; private set; } = Array.Empty<ReplayMarker>();

    public long MinTs => _minTs;
    public long MaxTs => _maxTs;
    /// <summary>Absolute epoch time of the playhead (for the timeline cursor).</summary>
    public long CurrentAbsMs => _minTs + CurrentTimeMs;
    public bool IsDualParticipant => Participants.Count > 1;

    /// <summary>Raised after a successful import or marker change so the view can refresh the timeline.</summary>
    public event Action? TimelineChanged;

    public ReplayViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += OnTick;
    }

    // ── Import ─────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task ImportCsvAsync(Window? owner)
    {
        if (owner == null) return;
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.Get("Replay_ImportCsvDialogTitle"),
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(Loc.Get("Common_FileType_SessionCsv")) { Patterns = ["*.csv"] }]
        });
        if (files.Count == 0 || files[0].Path.LocalPath is not string path) return;

        try
        {
            var text = await File.ReadAllTextAsync(path);
            var data = ReplayCsvParser.ParseText(text);
            LoadData(data, path);
        }
        catch (Exception ex)
        {
            OverlayActive = true;
            OverlayTitle = Loc.Get("Replay_OverlayImportFailed");
            OverlayDetail = ex.Message;
        }
    }

    private void LoadData(ReplayData data, string path)
    {
        Pause();
        _data = data;
        _filePath = path;
        _minTs = data.MinTs;
        _maxTs = data.MaxTs;

        ImportedFileName = Path.GetFileName(path);
        SessionName = Loc.Format("Replay_SessionNameFormat",
            string.IsNullOrWhiteSpace(data.Session.Name) ? Loc.Get("Replay_SessionUnnamed") : data.Session.Name);
        HasExperiment = data.HasExperiment;
        ExperimentName = data.Runs.FirstOrDefault()?.Name ?? "";
        _suppressNotesDirty = true;
        Notes = data.Session.Notes;
        _suppressNotesDirty = false;

        Participants.Clear();
        foreach (var p in data.Participants)
            Participants.Add(new ReplayParticipantViewModel(p));
        OnPropertyChanged(nameof(IsDualParticipant));

        UserMarkers.Clear();
        foreach (var m in data.UserMarkers.OrderBy(m => m.Ts))
        {
            m.PropertyChanged += (_, _) => IsDirty = true;
            UserMarkers.Add(m);
        }
        AutoMarkers = data.Markers;

        ExperimentEvents.Clear();
        HasAttachedExperiment = false;
        AttachedExperimentName = "";

        DurationMs = data.DurationMs;
        DurationDisplay = Fmt(DurationMs);
        HasData = Participants.Count > 0 || AutoMarkers.Count > 0;
        IsDirty = false;

        foreach (var p in Participants) p.ResetMotion();
        CurrentTimeMs = 0;
        ApplyTime(_minTs); // force the seek even if CurrentTimeMs was already 0
        CurrentTimeDisplay = Fmt(0);
        OnPropertyChanged(nameof(MinTs));
        OnPropertyChanged(nameof(MaxTs));
        OnPropertyChanged(nameof(CurrentAbsMs));
        TimelineChanged?.Invoke();
    }

    // ── Transport ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void PlayPause()
    {
        if (!HasData) return;
        if (IsPlaying) Pause();
        else
        {
            if (CurrentTimeMs >= DurationMs) CurrentTimeMs = 0;
            _lastTickUtc = DateTime.UtcNow;
            IsPlaying = true;
            _timer.Start();
        }
    }

    private void Pause()
    {
        IsPlaying = false;
        _timer.Stop();
    }

    [RelayCommand]
    private void SetSpeed(string speed)
    {
        if (double.TryParse(speed, System.Globalization.CultureInfo.InvariantCulture, out var s) && s > 0)
        {
            PlaybackSpeed = s;
            PlaybackSpeedDisplay = Loc.Format("Replay_PlaybackSpeedFormat", s);
        }
    }

    /// <summary>Seek by a signed millisecond delta (the ±1 s / ±100 ms fine-step buttons).</summary>
    [RelayCommand]
    private void Step(string deltaMs)
    {
        if (!HasData) return;
        if (long.TryParse(deltaMs, out var d)) SeekRelative(d);
    }

    /// <summary>Jump to the previous/next recorded sample of the primary participant ("+1"/"-1").</summary>
    [RelayCommand]
    private void StepSample(string dir)
    {
        if (!HasData || Participants.Count == 0) return;
        var forward = dir != "-1";
        var abs = _minTs + CurrentTimeMs;
        var next = NeighbourSampleTs(abs, forward);
        if (next is long t) SeekAbsolute(t);
    }

    private void SeekRelative(long deltaMs) => SeekAbsoluteRelative(CurrentTimeMs + deltaMs);
    private void SeekAbsolute(long absMs) => SeekAbsoluteRelative(absMs - _minTs);

    private void SeekAbsoluteRelative(long relMs)
    {
        var clamped = Math.Clamp(relMs, 0, Math.Max(0, DurationMs));
        if (clamped == CurrentTimeMs) ApplyTime(_minTs + clamped); // re-apply (e.g. StepSample landing same rel)
        else CurrentTimeMs = clamped; // triggers OnCurrentTimeMsChanged → ApplyTime
    }

    partial void OnCurrentTimeMsChanged(long value)
    {
        CurrentTimeDisplay = Fmt(value);
        OnPropertyChanged(nameof(CurrentAbsMs));
        if (_data != null) ApplyTime(_minTs + value);
    }

    /// <summary>Seek to a time given in milliseconds relative to the recording start (numeric entry).</summary>
    public void SeekToRelativeMs(long relMs) => SeekAbsoluteRelative(relMs);

    private void OnTick(object? sender, EventArgs e)
    {
        if (!IsPlaying) return;
        var now = DateTime.UtcNow;
        var wallMs = (now - _lastTickUtc).TotalMilliseconds;
        _lastTickUtc = now;
        var advance = (long)Math.Round(wallMs * PlaybackSpeed);
        if (advance <= 0) return;
        var next = CurrentTimeMs + advance;
        if (next >= DurationMs) { CurrentTimeMs = DurationMs; Pause(); }
        else CurrentTimeMs = next;
    }

    // Fan the absolute time out to every participant + recompute the overlay.
    private void ApplyTime(long absMs)
    {
        foreach (var p in Participants) p.SeekTo(absMs);
        ComputeOverlay(absMs);
    }

    // ── Markers ──────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task AddMarkerAsync(Window? owner)
    {
        if (owner == null || !HasData) return;
        var name = await MarkerNameDialog.ShowAsync(owner, ReplaySettings.RecentMarkerNames, "");
        if (string.IsNullOrWhiteSpace(name)) return;
        ReplaySettings.RememberMarkerName(name);

        var marker = new UserMarker(_minTs + CurrentTimeMs, name);
        marker.PropertyChanged += (_, _) => IsDirty = true;
        InsertOrdered(marker);
        IsDirty = true;
        TimelineChanged?.Invoke();
    }

    [RelayCommand]
    private async Task EditMarkerAsync(UserMarker? marker)
    {
        if (marker == null) return;
        var owner = OwnerWindow;
        if (owner == null) return;
        var name = await MarkerNameDialog.ShowAsync(owner, ReplaySettings.RecentMarkerNames, marker.Label);
        if (string.IsNullOrWhiteSpace(name)) return;
        ReplaySettings.RememberMarkerName(name);
        marker.Label = name; // PropertyChanged → IsDirty
        TimelineChanged?.Invoke();
    }

    [RelayCommand]
    private void RemoveMarker(UserMarker? marker)
    {
        if (marker == null) return;
        UserMarkers.Remove(marker);
        IsDirty = true;
        TimelineChanged?.Invoke();
    }

    [RelayCommand]
    private void SeekToMarker(UserMarker? marker)
    {
        if (marker == null) return;
        SeekAbsolute(marker.Ts);
    }

    public void SeekToAbsoluteFromTimeline(long absMs) => SeekAbsolute(absMs);

    private void InsertOrdered(UserMarker marker)
    {
        int idx = 0;
        while (idx < UserMarkers.Count && UserMarkers[idx].Ts <= marker.Ts) idx++;
        UserMarkers.Insert(idx, marker);
    }

    /// <summary>Set by the view so commands invoked without an explicit owner can find the window.</summary>
    public Window? OwnerWindow { get; set; }

    // ── Attach experiment JSON ────────────────────────────────────────────────────

    [RelayCommand]
    private async Task AttachExperimentJsonAsync(Window? owner)
    {
        owner ??= OwnerWindow;
        if (owner == null) return;
        var assetsPath = Path.Combine(AppContext.BaseDirectory, "Assets");
        var startFolder = Directory.Exists(assetsPath)
            ? await owner.StorageProvider.TryGetFolderFromPathAsync(assetsPath)
            : null;
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.Get("Replay_AttachExperimentDialogTitle"),
            AllowMultiple = false,
            SuggestedStartLocation = startFolder,
            FileTypeFilter = [new FilePickerFileType(Loc.Get("Common_FileType_ExperimentJson")) { Patterns = ["*.json"] }]
        });
        if (files.Count == 0 || files[0].Path.LocalPath is not string path) return;
        try
        {
            var dto = JsonSerializer.Deserialize<ExperimentDto>(await File.ReadAllTextAsync(path), JsonOpts);
            if (dto == null) return;
            ExperimentEvents.Clear();
            var events = dto.Events ?? new();
            for (int i = 0; i < events.Count; i++)
                ExperimentEvents.Add(new ReplayEventRow
                {
                    IndexLabel = $"{i + 1}",
                    Type = string.IsNullOrWhiteSpace(events[i].Type) ? Loc.Format("Replay_EventFallbackName", i + 1) : events[i].Type,
                    Duration = events[i].Duration
                });
            HasAttachedExperiment = ExperimentEvents.Count > 0;
            AttachedExperimentName = dto.Name;
            ComputeOverlay(_minTs + CurrentTimeMs);
        }
        catch (Exception ex)
        {
            OverlayActive = true;
            OverlayTitle = Loc.Get("Replay_OverlayExperimentLoadFailed");
            OverlayDetail = ex.Message;
        }
    }

    // ── Save ───────────────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task SaveAsync(Window? owner)
    {
        owner ??= OwnerWindow;
        if (owner == null || _data == null) return;

        var choice = await SaveTargetDialog.ShowAsync(owner, ImportedFileName);
        if (choice == SaveTargetDialog.Result.Cancel) return;

        string targetPath;
        if (choice == SaveTargetDialog.Result.Overwrite && _filePath != null)
        {
            targetPath = _filePath;
        }
        else
        {
            var suggested = Path.GetFileNameWithoutExtension(ImportedFileName);
            var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = Loc.Get("Replay_SaveAnnotatedCsvDialogTitle"),
                SuggestedFileName = $"{suggested}_annotated.csv",
                FileTypeChoices = [new FilePickerFileType(Loc.Get("Common_FileType_SessionCsv")) { Patterns = ["*.csv"] }]
            });
            if (file?.Path.LocalPath is not string p) return;
            targetPath = p;
        }

        var text = ReplayCsvWriter.Build(_data, Notes, UserMarkers.ToList());
        await File.WriteAllTextAsync(targetPath, text);

        // Re-read so the loaded file path + raw lines stay in sync for the next round-trip save.
        var reloaded = ReplayCsvParser.Parse(ReplayCsvParser.SplitLines(text));
        _data = reloaded;
        _filePath = targetPath;
        ImportedFileName = Path.GetFileName(targetPath);
        IsDirty = false;
    }

    // ── Overlay computation (best-effort) ────────────────────────────────────────

    private void ComputeOverlay(long absMs)
    {
        if (_data == null) { OverlayActive = false; return; }

        var run = _data.Runs.FirstOrDefault(r => absMs >= r.StartedAtMs && (r.EndedAtMs == null || absMs <= r.EndedAtMs));
        var lastMarker = AutoMarkers.Where(m => m.Ts <= absMs).OrderBy(m => m.Ts).LastOrDefault();

        if (run != null)
        {
            OverlayActive = true;
            OverlayTitle = HasAttachedExperiment ? CurrentEventName(absMs - run.StartedAtMs) : Loc.Format("Replay_OverlayRunTitle", run.Name);
            OverlayDetail = lastMarker?.Detail ?? lastMarker?.Label ?? Loc.Get("Replay_OverlayExperimentRunning");
        }
        else if (lastMarker != null && absMs - lastMarker.Ts < 15000)
        {
            OverlayActive = true;
            OverlayTitle = lastMarker.Label;
            OverlayDetail = lastMarker.Detail ?? "";
        }
        else
        {
            OverlayActive = false;
            OverlayTitle = "";
            OverlayDetail = "";
        }
    }

    // Approximate: assumes events ran back-to-back at their nominal durations from run start.
    private string CurrentEventName(long elapsedMs)
    {
        if (ExperimentEvents.Count == 0) return "";
        double acc = 0;
        int current = -1;
        var elapsedSec = elapsedMs / 1000.0;
        for (int i = 0; i < ExperimentEvents.Count; i++)
        {
            acc += Math.Max(0, ExperimentEvents[i].Duration);
            if (elapsedSec <= acc) { current = i; break; }
        }
        if (current < 0) current = ExperimentEvents.Count - 1;
        for (int i = 0; i < ExperimentEvents.Count; i++)
            ExperimentEvents[i].IsCurrent = i == current;
        return ExperimentEvents[current].Type;
    }

    partial void OnNotesChanged(string value)
    {
        if (!_suppressNotesDirty) IsDirty = true;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private long? NeighbourSampleTs(long absMs, bool forward)
    {
        // Use the primary participant's series for sample-accurate stepping.
        var p = _data?.Participants.FirstOrDefault();
        if (p == null || p.Samples.Count == 0) return null;
        var samples = p.Samples;
        if (forward)
        {
            foreach (var s in samples) if (s.Timestamp > absMs) return s.Timestamp;
            return null;
        }
        long? prev = null;
        foreach (var s in samples) { if (s.Timestamp < absMs) prev = s.Timestamp; else break; }
        return prev;
    }

    private static string Fmt(long ms)
    {
        if (ms < 0) ms = 0;
        var t = TimeSpan.FromMilliseconds(ms);
        return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}.{t.Milliseconds:000}";
    }
}
