using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OcdWizard.ControlStation.Models;
using OcdWizard.ControlStation.Services;

namespace OcdWizard.ControlStation.ViewModels;

public partial class ExperimentEventEditModel : ObservableObject
{
    [ObservableProperty] private string _id = Guid.NewGuid().ToString("N").Substring(0, 8);
    [ObservableProperty] private string _type = "Distraction";
    [ObservableProperty] private float _duration = 5f;
    [ObservableProperty] private string _kindLabel = "Timed";
    [ObservableProperty] private float _randomMin = 1f;
    [ObservableProperty] private float _randomMax = 5f;
    [ObservableProperty] private string _waitForActionId = "";
    [ObservableProperty] private string _waitForTriggerId = "";
    [ObservableProperty] private string _indexLabel = "";

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
    [ObservableProperty] private string _name = "Trigger";
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
    private readonly ExperimentSyncService _sync;
    [ObservableProperty] private string _experimentId = Guid.NewGuid().ToString();
    [ObservableProperty] private string _name = "Untitled";
    private long _version = 1L;
    private bool _suppressNotify = false;

    public ObservableCollection<ExperimentEventEditModel> Events { get; } = new();
    public ObservableCollection<TriggerEditModel> Triggers { get; } = new();

    public IReadOnlyList<string> KindOptions => ExperimentEventEditModel.Kinds;
    public IReadOnlyList<string> TriggerKindOptions => TriggerEditModel.Kinds;
    public IReadOnlyList<string> InterventionTypeOptions => ExperimentEventEditModel.InterventionTypeOptions;
    public IReadOnlyList<string> NotificationOptions => ExperimentEventEditModel.NotificationOptions;
    public IReadOnlyList<string> GameTypeOptions => ExperimentEventEditModel.GameTypeOptions;
    public IReadOnlyList<string> PhoneTaskOptions => ExperimentEventEditModel.PhoneTaskOptions;

    public ExperimentEditorViewModel(ExperimentSyncService sync)
    {
        _sync = sync;
        _sync.RemoteUpdated += dto => Avalonia.Threading.Dispatcher.UIThread.Post(() => LoadFromDto(dto));
    }

    public void LoadFromDto(ExperimentDto dto)
    {
        _suppressNotify = true;
        try
        {
            // Unsubscribe from old models before clearing to prevent leaks.
            foreach (var m in Events) m.PropertyChanged -= OnItemPropertyChanged;
            foreach (var t in Triggers) t.PropertyChanged -= OnItemPropertyChanged;

            ExperimentId = dto.Id; Name = dto.Name; _version = dto.Version;

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
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_suppressNotify) NotifyChanged();
    }

    private void RefreshIndices()
    {
        for (int i = 0; i < Events.Count; i++)
            Events[i].IndexLabel = $"{i + 1}";
    }

    public ExperimentDto ToDto() => new(
        ExperimentId, Name, _version, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        Events.Select(e => e.ToDto()).ToList(),
        Triggers.Select(t => t.ToDto()).ToList());

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

    public void NotifyChanged() => _sync.NotifyEdited(ToDto());
}
