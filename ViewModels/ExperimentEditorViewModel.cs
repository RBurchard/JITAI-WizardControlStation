using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OcdWizard.ControlStation.Localization;
using OcdWizard.ControlStation.Models;
using OcdWizard.ControlStation.Services;

namespace OcdWizard.ControlStation.ViewModels;

/// <summary>
/// Renders the localized label for a ComboBox item whose bound/serialized value is a fixed
/// English wire token (e.g. "RandomWait", "LOCK_PICKING", "Yes/No", "VIBRATION1"). The
/// ComboBox's ItemsSource/SelectedItem keep holding that wire token completely unchanged this
/// converter only supplies what gets drawn on screen, via each ComboBox's ItemTemplate. See the
/// "Editor_*_Display" resources in Strings.resx for the label text itself.
///
/// Bound as a two-input MultiBinding: values[0] is the wire value (an empty-path binding to the
/// item itself), values[1] is <see cref="Loc.LanguageTag"/> included purely so the MultiBinding
/// re-evaluates, and the visible label re-localizes live, whenever the app language changes. This
/// mirrors what <c>{loc:T}</c> gives every other label; a plain single-value converter would only
/// refresh a ComboBox's closed-state text when its SelectedItem changes, not on a language switch.
/// ConverterParameter selects which wire-value family to resolve against (see ResolveKey).
/// </summary>
public sealed class WireValueDisplayConverter : IMultiValueConverter
{
    public static readonly WireValueDisplayConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var wire = values.Count > 0 ? values[0] as string : null;
        return Loc.Get(ResolveKey(parameter as string, wire));
    }

    private static string ResolveKey(string? category, string? wire)
    {
        // "None" is a shared sentinel across every option list that has it (GameType, PhoneTask).
        if (wire == "None") return "Editor_NoneOption_Display";

        return (category, wire) switch
        {
            ("EventKind", "Timed") => "Editor_EventKind_Timed_Display",
            ("EventKind", "RandomWait") => "Editor_EventKind_RandomWait_Display",
            ("EventKind", "WaitForAction") => "Editor_EventKind_WaitForAction_Display",
            ("EventKind", "WaitForTrigger") => "Editor_EventKind_WaitForTrigger_Display",
            ("EventKind", "WaitForPrevious") => "Editor_EventKind_WaitForPrevious_Display",

            ("TriggerKind", "Manual") => "Editor_TriggerKind_Manual",
            ("TriggerKind", "RapidMovement") => "Editor_TriggerKind_RapidMovement",
            ("TriggerKind", "RepeatingMovement") => "Editor_TriggerKind_RepeatingMovement",
            ("TriggerKind", "HeartRate") => "Editor_TriggerKind_HeartRate",

            ("InterventionType", "Text") => "Editor_InterventionType_Text_Display",
            ("InterventionType", "Timer") => "Editor_InterventionType_Timer_Display",
            ("InterventionType", "Yes/No") => "Editor_InterventionType_YesNo_Display",

            ("Notification", "VIBRATION1") => "Editor_Notification_Vibration1_Display",
            ("Notification", "VIBRATION2") => "Editor_Notification_Vibration2_Display",
            ("Notification", "SOUND") => "Editor_Notification_Sound_Display",
            ("Notification", "VIBRATION_SOUND") => "Editor_Notification_VibrationSound_Display",
            ("Notification", "VIBRATION_SOUND2") => "Editor_Notification_VibrationSound2_Display",
            ("Notification", "ANNOY_VIB") => "Editor_Notification_AnnoyVib_Display",
            ("Notification", "ANNOY_SOUND") => "Editor_Notification_AnnoySound_Display",
            ("Notification", "ANNOY_VIBRATION_SOUND") => "Editor_Notification_AnnoyVibrationSound_Display",

            ("GameType", "LOCK_PICKING") => "Editor_GameType_LockPicking_Display",
            ("GameType", "SIMON_SAYS") => "Editor_GameType_SimonSays_Display",
            ("GameType", "STAND_STILL") => "Editor_GameType_StandStill_Display",
            ("GameType", "TRIVIA") => "Editor_GameType_Trivia_Display",

            ("PhoneTask", "DRAW") => "Editor_PhoneTask_Draw",
            ("PhoneTask", "CAMERA") => "Editor_PhoneTask_Camera",

            // Defensive fallback: never blank, never throws shows the raw wire value if a new
            // token is ever added to a Kinds/*Options list without a matching resx entry.
            _ => wire ?? string.Empty
        };
    }
}

public partial class ExperimentEventEditModel : ObservableObject
{
    [ObservableProperty] private string _id = Guid.NewGuid().ToString("N").Substring(0, 8);
    [ObservableProperty] private string _type = Loc.Get("Editor_DefaultEventType");
    [ObservableProperty] private float _duration = 5f;
    [ObservableProperty] private string _kindLabel = "Timed";
    [ObservableProperty] private float _randomMin = 1f;
    [ObservableProperty] private float _randomMax = 5f;
    [ObservableProperty] private string _waitForActionId = "";
    [ObservableProperty] private string _waitForTriggerId = "";
    [ObservableProperty] private string _indexLabel = "";

    // Runtime-only display state, set by the runner while an experiment is running. Not part of
    // the experiment definition: excluded from ToDto() and skipped by change-driven auto-sync.
    [ObservableProperty] private bool _isCurrent;
    [ObservableProperty] private string _remainingDisplay = "";

    // Intervention / distraction config
    [ObservableProperty] private bool _hasIntervention = false;
    [ObservableProperty] private string _interventionTypeLabel = "Text";
    [ObservableProperty] private string _notificationLabel = "VIBRATION1";
    [ObservableProperty] private string _interventionMessage = "";
    [ObservableProperty] private int _interventionDurationSecs = 10;
    [ObservableProperty] private string _gameTypeLabel = "None";
    [ObservableProperty] private string _phoneTaskLabel = "None";
    private string _interventionId = Guid.NewGuid().ToString("N").Substring(0, 8);

    partial void OnGameTypeLabelChanged(string value)
    {
        if (value != "None" && PhoneTaskLabel != "None") PhoneTaskLabel = "None";
    }

    partial void OnPhoneTaskLabelChanged(string value)
    {
        if (value != "None" && GameTypeLabel != "None") GameTypeLabel = "None";
    }

    public static IReadOnlyList<string> Kinds { get; } =
        new[] { "Timed", "RandomWait", "WaitForAction", "WaitForTrigger", "WaitForPrevious" };

    public static IReadOnlyList<string> InterventionTypeOptions { get; } =
        new[] { "Text", "Timer", "Yes/No" };

    public static IReadOnlyList<string> NotificationOptions { get; } =
        new[] { "VIBRATION1", "VIBRATION2", "SOUND", "VIBRATION_SOUND", "VIBRATION_SOUND2",
                "ANNOY_VIB", "ANNOY_SOUND", "ANNOY_VIBRATION_SOUND" };

    public static IReadOnlyList<string> GameTypeOptions { get; } =
        new[] { "None", "LOCK_PICKING", "SIMON_SAYS", "TRIVIA", "STAND_STILL" };

    public static IReadOnlyList<string> PhoneTaskOptions { get; } =
        new[] { "None", "DRAW", "CAMERA" };

    public EventKindDto ToKindDto() => KindLabel switch
    {
        "RandomWait"      => new RandomWaitKindDto(RandomMin, RandomMax),
        "WaitForAction"   => new WaitForActionKindDto(WaitForActionId),
        "WaitForTrigger"  => new WaitForTriggerKindDto(WaitForTriggerId),
        "WaitForPrevious" => new WaitForPreviousKindDto(),
        _                 => new TimedKindDto()
    };

    public ExperimentEventDto ToDto()
    {
        InterventionDto? intervention = null;
        string? gameType = null;
        if (HasIntervention)
        {
            var gt = GameTypeLabel == "None" ? null : GameTypeLabel;
            var pt = PhoneTaskLabel == "None" ? null : PhoneTaskLabel;
            intervention = new InterventionDto(_interventionId, InterventionTypeLabel, NotificationLabel,
                InterventionMessage, InterventionDurationSecs, gt, pt);
            gameType = gt;
        }
        return new(Id, Duration, Type, intervention, gameType, ToKindDto());
    }

    public static ExperimentEventEditModel FromDto(ExperimentEventDto e)
    {
        var m = new ExperimentEventEditModel
        {
            Id = e.Id,
            Type = e.Type,
            Duration = e.Duration,
            KindLabel = e.Kind switch
            {
                RandomWaitKindDto _      => "RandomWait",
                WaitForActionKindDto _   => "WaitForAction",
                WaitForTriggerKindDto _  => "WaitForTrigger",
                WaitForPreviousKindDto _ => "WaitForPrevious",
                _                        => "Timed"
            }
        };
        if (e.Intervention != null)
        {
            m.HasIntervention = true;
            m._interventionId = e.Intervention.Id;
            m.InterventionTypeLabel = e.Intervention.Type is "Text" or "Timer" or "Yes/No"
                ? e.Intervention.Type : "Text";
            m.NotificationLabel = e.Intervention.Notification is { Length: > 0 } n ? n : "VIBRATION1";
            m.InterventionMessage = e.Intervention.Message ?? "";
            m.InterventionDurationSecs = e.Intervention.DurationSeconds > 0 ? e.Intervention.DurationSeconds : 10;
            m.GameTypeLabel = e.Intervention.GameType ?? "None";
            m.PhoneTaskLabel = e.Intervention.PhoneTaskType ?? "None";
        }
        switch (e.Kind)
        {
            case RandomWaitKindDto r: m.RandomMin = r.MinSec; m.RandomMax = r.MaxSec; break;
            case WaitForActionKindDto a: m.WaitForActionId = a.ActionId; break;
            case WaitForTriggerKindDto t: m.WaitForTriggerId = t.TriggerId; break;
        }
        return m;
    }
}

public partial class TriggerEditModel : ObservableObject
{
    [ObservableProperty] private string _id = Guid.NewGuid().ToString("N").Substring(0, 8);
    [ObservableProperty] private string _name = Loc.Get("Editor_DefaultTriggerName");
    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private string _kindLabel = "Manual";
    [ObservableProperty] private float _accelThreshold = 2.5f;
    [ObservableProperty] private float _minHz = 1f;
    [ObservableProperty] private float _maxHz = 4f;
    [ObservableProperty] private float _sustainSec = 3f;
    [ObservableProperty] private int _bpm = 110;
    [ObservableProperty] private bool _above = true;

    public static IReadOnlyList<string> Kinds { get; } =
        new[] { "Manual", "RapidMovement", "RepeatingMovement", "HeartRate" };

    public TriggerKindDto ToKindDto() => KindLabel switch
    {
        "RapidMovement" => new RapidMovementKindDto(AccelThreshold),
        "RepeatingMovement" => new RepeatingMovementKindDto(MinHz, MaxHz, SustainSec),
        "HeartRate" => new HeartRateKindDto(Bpm, Above, SustainSec),
        _ => new ManualKindDto()
    };

    public TriggerDto ToDto() => new(Id, Name, ToKindDto(), Enabled);

    public static TriggerEditModel FromDto(TriggerDto t)
    {
        var m = new TriggerEditModel
        {
            Id = t.Id,
            Name = t.Name,
            Enabled = t.Enabled,
            KindLabel = t.Kind switch
            {
                RapidMovementKindDto _ => "RapidMovement",
                RepeatingMovementKindDto _ => "RepeatingMovement",
                HeartRateKindDto _ => "HeartRate",
                _ => "Manual"
            }
        };
        switch (t.Kind)
        {
            case RapidMovementKindDto r: m.AccelThreshold = r.AccelThreshold; break;
            case RepeatingMovementKindDto rep: m.MinHz = rep.MinHz; m.MaxHz = rep.MaxHz; m.SustainSec = rep.SustainSec; break;
            case HeartRateKindDto hr: m.Bpm = hr.Bpm; m.Above = hr.Above; m.SustainSec = hr.SustainSec; break;
        }
        return m;
    }
}

public partial class ExperimentEditorViewModel : ObservableObject
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly ExperimentSyncService _sync;
    [ObservableProperty] private string _experimentId = Guid.NewGuid().ToString();
    [ObservableProperty] private string _name = Loc.Get("Editor_UntitledExperiment");
    [ObservableProperty] private string _notes = "";

    // Watch microgame difficulty, carried on the experiment so it travels with the condition.
    // Held as four flat properties rather than a nested object because Avalonia two-way binds
    // to plain properties far more simply, and they are recombined in ToDto().
    [ObservableProperty] private int _simonDifficulty = 2;
    [ObservableProperty] private int _simonRounds = 4;
    [ObservableProperty] private int _lockDifficulty = 2;
    [ObservableProperty] private string _lockPenalty = "STUN";

    /// <summary>Penalty values offered by the picker, in ascending severity.</summary>
    public IReadOnlyList<string> LockPenaltyOptions => MicrogameSettingsDto.PenaltyOptions;

    private long _version = 1L;
    private bool _suppressNotify = false;

    /// <summary>
    /// Raised after any call to <see cref="LoadFromDto"/> so consumers can reset
    /// experiment-run state (e.g. the tracker overlay) on a clean switch.
    /// Not fired for the initial seed in the constructor.
    /// </summary>
    public event Action? ExperimentLoaded;

    public ObservableCollection<ExperimentEventEditModel> Events { get; } = new();
    public ObservableCollection<TriggerEditModel> Triggers { get; } = new();

    public IReadOnlyList<string> KindOptions => ExperimentEventEditModel.Kinds;
    public IReadOnlyList<string> TriggerKindOptions => TriggerEditModel.Kinds;
    public IReadOnlyList<string> InterventionTypeOptions => ExperimentEventEditModel.InterventionTypeOptions;
    public IReadOnlyList<string> NotificationOptions => ExperimentEventEditModel.NotificationOptions;
    public IReadOnlyList<string> GameTypeOptions => ExperimentEventEditModel.GameTypeOptions;
    public IReadOnlyList<string> PhoneTaskOptions => ExperimentEventEditModel.PhoneTaskOptions;

    private bool _initializingFromConstructor = true;

    public ExperimentEditorViewModel(ExperimentSyncService sync)
    {
        _sync = sync;
        _sync.RemoteUpdated += dto => Avalonia.Threading.Dispatcher.UIThread.Post(() => LoadFromDto(dto));
        // Seed a fresh editor with the bundled default. A later phone Pull overwrites this,
        // and the phone's fresh experiment is the same bundled default, so they match.
        LoadFromDto(DefaultExperiment.Load());
        _initializingFromConstructor = false;
    }

    public void LoadFromDto(ExperimentDto dto)
    {
        _suppressNotify = true;
        try
        {
            // Unsubscribe from old models before clearing to prevent leaks.
            foreach (var m in Events) m.PropertyChanged -= OnItemPropertyChanged;
            foreach (var t in Triggers) t.PropertyChanged -= OnItemPropertyChanged;

            ExperimentId = dto.Id; Name = dto.Name; Notes = dto.Notes; _version = dto.Version;

            var game = (dto.GameSettings ?? new MicrogameSettingsDto()).Sanitized();
            SimonDifficulty = game.SimonDifficulty;
            SimonRounds = game.SimonRounds;
            LockDifficulty = game.LockDifficulty;
            LockPenalty = game.LockPenalty;

            Events.Clear();
            foreach (var e in dto.Events ?? new())
            {
                var m = ExperimentEventEditModel.FromDto(e);
                m.PropertyChanged += OnItemPropertyChanged;
                Events.Add(m);
            }

            Triggers.Clear();
            foreach (var t in dto.Triggers ?? new())
            {
                var m = TriggerEditModel.FromDto(t);
                m.PropertyChanged += OnItemPropertyChanged;
                Triggers.Add(m);
            }

            RefreshIndices();
        }
        finally
        {
            _suppressNotify = false;
        }

        // Notify consumers that the experiment changed so they can reset live-run state.
        // Skipped for the initial constructor seed nothing is running yet.
        if (!_initializingFromConstructor)
            ExperimentLoaded?.Invoke();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Runtime highlight/countdown changes are display-only never push them to the phone.
        if (e.PropertyName is nameof(ExperimentEventEditModel.IsCurrent)
                           or nameof(ExperimentEventEditModel.RemainingDisplay)) return;
        if (!_suppressNotify) NotifyChanged();
    }

    partial void OnSimonDifficultyChanged(int value) { if (!_suppressNotify) NotifyChanged(); }
    partial void OnSimonRoundsChanged(int value) { if (!_suppressNotify) NotifyChanged(); }
    partial void OnLockDifficultyChanged(int value) { if (!_suppressNotify) NotifyChanged(); }
    partial void OnLockPenaltyChanged(string value) { if (!_suppressNotify) NotifyChanged(); }

    private void RefreshIndices()
    {
        for (int i = 0; i < Events.Count; i++)
            Events[i].IndexLabel = $"{i + 1}";
    }

    public ExperimentDto ToDto() => new(
        ExperimentId, Name, _version, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Notes,
        Events.Select(e => e.ToDto()).ToList(),
        Triggers.Select(t => t.ToDto()).ToList(),
        new MicrogameSettingsDto(SimonDifficulty, SimonRounds, LockDifficulty, LockPenalty).Sanitized());

    [RelayCommand]
    private void AddEvent()
    {
        var m = new ExperimentEventEditModel();
        m.PropertyChanged += OnItemPropertyChanged;
        Events.Add(m);
        RefreshIndices();
        NotifyChanged();
    }

    [RelayCommand]
    private void RemoveEvent(ExperimentEventEditModel? e)
    {
        if (e == null) return;
        e.PropertyChanged -= OnItemPropertyChanged;
        Events.Remove(e);
        RefreshIndices();
        NotifyChanged();
    }

    [RelayCommand]
    private void MoveEventUp(ExperimentEventEditModel? e)
    {
        if (e == null) return;
        var idx = Events.IndexOf(e);
        if (idx <= 0) return;
        Events.Move(idx, idx - 1);
        RefreshIndices();
        NotifyChanged();
    }

    [RelayCommand]
    private void MoveEventDown(ExperimentEventEditModel? e)
    {
        if (e == null) return;
        var idx = Events.IndexOf(e);
        if (idx < 0 || idx >= Events.Count - 1) return;
        Events.Move(idx, idx + 1);
        RefreshIndices();
        NotifyChanged();
    }

    [RelayCommand]
    private void AddTrigger()
    {
        var t = new TriggerEditModel();
        t.PropertyChanged += OnItemPropertyChanged;
        Triggers.Add(t);
    }

    [RelayCommand]
    private void RemoveTrigger(TriggerEditModel? t)
    {
        if (t == null) return;
        t.PropertyChanged -= OnItemPropertyChanged;
        Triggers.Remove(t);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        // Cancel any pending auto-push so it can't overwrite the explicit push with a stale snapshot.
        _sync.CancelPendingAutoSync();
        await _sync.PushAsync(ToDto());
    }

    [RelayCommand]
    private async Task PullAsync() => await _sync.PullAsync();

    [RelayCommand]
    private async Task ExportExperimentAsync(Window? owner)
    {
        if (owner == null) return;
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Loc.Get("Editor_ExportExperimentDialogTitle"),
            SuggestedFileName = $"{SanitizeFileName(Name)}.json",
            FileTypeChoices = [new FilePickerFileType(Loc.Get("Common_FileType_ExperimentJson")) { Patterns = ["*.json"] }]
        });
        if (file?.Path.LocalPath is string path)
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(ToDto(), JsonOpts));
    }

    [RelayCommand]
    private async Task ImportExperimentAsync(Window? owner)
    {
        if (owner == null) return;
        // Open in the Assets folder beside the executable, where the bundled
        // experiment templates ship, so they are one click away.
        var assetsPath = Path.Combine(AppContext.BaseDirectory, "Assets");
        var startFolder = Directory.Exists(assetsPath)
            ? await owner.StorageProvider.TryGetFolderFromPathAsync(assetsPath)
            : null;
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc.Get("Editor_ImportExperimentDialogTitle"),
            AllowMultiple = false,
            SuggestedStartLocation = startFolder,
            FileTypeFilter = [new FilePickerFileType(Loc.Get("Common_FileType_ExperimentJson")) { Patterns = ["*.json"] }]
        });
        if (files.Count == 0 || files[0].Path.LocalPath is not string path) return;
        var dto = JsonSerializer.Deserialize<ExperimentDto>(await File.ReadAllTextAsync(path), JsonOpts);
        if (dto == null) return;
        LoadFromDto(dto);
        // Push the imported experiment to the phone immediately so both sides match.
        _sync.CancelPendingAutoSync();
        await _sync.PushAsync(ToDto());
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray())
            .Replace(' ', '_').TrimStart('_');
        return string.IsNullOrEmpty(sanitized) ? "experiment" : sanitized;
    }

    public void NotifyChanged() => _sync.NotifyEdited(ToDto());
}
