using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Data.Converters;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OcdWizard.ControlStation.Database;
using OcdWizard.ControlStation.Localization;
using OcdWizard.ControlStation.Models;
using OcdWizard.ControlStation.Services;
using Avalonia.Threading;

namespace OcdWizard.ControlStation.ViewModels;

/// <summary>
/// Formats a single value through a resx template (e.g. "{0:F0} bpm", "ID: {0}") via
/// <see cref="Loc.Format"/>. Bound as a two-input MultiBinding — values[0] is the value to format,
/// values[1] is <see cref="Loc.LanguageTag"/> purely to force re-evaluation, and therefore a live
/// re-localized label, on a language switch (a plain single-value converter would only refresh
/// when the formatted value itself changes). ConverterParameter is the resx key of the template.
/// </summary>
public sealed class LocalizedFormatConverter : IMultiValueConverter
{
    public static readonly LocalizedFormatConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        Loc.Format((string)parameter!, values.Count > 0 ? values[0] : null);
}

public partial class ParticipantViewModel : ObservableObject, IMotionSource
{
    private readonly DatabaseService _db;
    private readonly DeviceConnection _device = new();
    public ExperimentSyncService ExperimentSync { get; }
    public LogSyncService LogSync { get; }
    public RunLifecycleService RunLifecycle { get; }
    public ExperimentEditorViewModel ExperimentEditor { get; }
    private string? _participantId;
    private string? _sessionId;
    private bool _suppressParticipantPush;
    private CancellationTokenSource? _participantPushCts;

    public string Label { get; }
    public string? CurrentRunId => RunLifecycle.CurrentRunId;
    [ObservableProperty] private string _participantKey = "";

    public IReadOnlyList<string> GameTypes { get; } =
        ["None", "STAND_STILL", "TRIVIA", "SIMON_SAYS", "LOCK_PICKING"];
    public IReadOnlyList<string> NotificationTypes { get; } =
        ["VIBRATION1", "VIBRATION2", "SOUND", "VIBRATION_SOUND"];

    [ObservableProperty] private string _connectionStatus = Loc.Get("Participant_StatusNotConnected");
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private float _currentBpm;
    [ObservableProperty] private float _accelX;
    [ObservableProperty] private float _accelY;
    [ObservableProperty] private float _accelZ;
    [ObservableProperty] private float _rawAccelX;
    [ObservableProperty] private float _rawAccelY;
    [ObservableProperty] private float _rawAccelZ;
    [ObservableProperty] private float _rotW = 1f;
    [ObservableProperty] private float _rotX;
    [ObservableProperty] private float _rotY;
    [ObservableProperty] private float _rotZ;
    [ObservableProperty] private long _lastSnapshotTs;
    private float _prevAccelX = float.NaN;
    private float _prevAccelY = float.NaN;
    private float _prevAccelZ = float.NaN;
    [ObservableProperty] private string _ipAddress = "";
    [ObservableProperty] private string _selectedGameType = "None";
    [ObservableProperty] private string _selectedNotificationType = "VIBRATION1";
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private DiscoveredDevice? _selectedDevice;
    [ObservableProperty] private bool _isTrivia;
    [ObservableProperty] private string _triviaQuestionText = "";
    [ObservableProperty] private string _triviaAnswerA = "";
    [ObservableProperty] private string _triviaAnswerB = "";
    [ObservableProperty] private string _triviaAnswerC = "";
    [ObservableProperty] private string _triviaAnswerD = "";
    [ObservableProperty] private string _triviaCorrectAnswer = "A";

    // ── Live experiment progress (driven by EngineStateSnapshot over the events WS) ──
    [ObservableProperty] private string _currentEventName = "";
    [ObservableProperty] private string _countdownDisplay = "";
    [ObservableProperty] private bool _hasActiveEvent;

    private readonly DispatcherTimer _countdownTimer;
    private int _engineEventIndex = -1;
    private float _localElapsed;
    private float? _randomWaitRemaining;
    private bool _engineRunning;

    public ObservableCollection<string> EventLog { get; } = [];
    public ObservableCollection<DiscoveredDevice> DiscoveredDevices { get; } = [];

    /// <summary>
    /// Supplies the USB devices currently managed by the always-on ADB watcher so a manual
    /// Wi-Fi <see cref="ScanAsync"/> can fold them in instead of wiping them. Set by MainViewModel.
    /// </summary>
    public Func<IReadOnlyList<DiscoveredDevice>>? UsbDeviceProvider { get; set; }

    /// <summary>
    /// Lets MainViewModel start the session on demand. Invoked before dispatching "start"/"auto"
    /// so an experiment begun without an active session never orphans its data (no-ops if a
    /// session is already running).
    /// </summary>
    public Func<Task>? EnsureSessionStarted { get; set; }

    /// <summary>
    /// Raised once per received batch with the full ordered snapshot series so the
    /// motion view can reconstruct at the watch's true sample rate (~50 Hz) rather
    /// than sampling only the latest value. Fired from a background thread.
    /// </summary>
    public event Action<IReadOnlyList<WatchDataSnapshot>>? MotionSamplesReceived;

    // Part of IMotionSource; only the replay source ever seeks backward, so the live panel
    // never raises this. Present so MotionView can bind to the shared interface.
#pragma warning disable CS0067
    public event Action? MotionResetRequested;
#pragma warning restore CS0067

    partial void OnSelectedDeviceChanged(DiscoveredDevice? value)
    {
        if (value != null)
            IpAddress = $"{value.Address}:{value.Port}";
    }

    partial void OnSelectedGameTypeChanged(string value) => IsTrivia = value == "TRIVIA";

    // When the researcher edits the participant name/ID, push it to the phone (debounced)
    // so both sides stay in sync. Programmatic assignments set _suppressParticipantPush.
    partial void OnParticipantKeyChanged(string value)
    {
        if (_suppressParticipantPush) return;
        SchedulePushParticipant(value);
    }

    private void SchedulePushParticipant(string value)
    {
        _participantPushCts?.Cancel();
        _participantPushCts = new CancellationTokenSource();
        var token = _participantPushCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(600, token);
                if (token.IsCancellationRequested) return;
                _participantId ??= Guid.NewGuid().ToString();
                await _device.PostParticipantAsync(_participantId, _sessionId ?? "", value, IpAddress);
                if (_sessionId != null)
                    await _db.UpsertParticipantAsync(_participantId, _sessionId, Label, IpAddress, value);
                AppendLog(Loc.Format("Participant_LogParticipantSetTo", value));
            }
            catch (TaskCanceledException) { }
            catch (Exception ex) { AppendLog(Loc.Format("Participant_LogParticipantPushFailed", ex.Message)); }
        });
    }

    public ParticipantViewModel(string label, DatabaseService db)
    {
        Label = label;
        _db = db;
        ExperimentSync = new ExperimentSyncService(_db);
        LogSync = new LogSyncService(_db);
        RunLifecycle = new RunLifecycleService(_db);
        ExperimentEditor = new ExperimentEditorViewModel(ExperimentSync);
        ExperimentSync.Log += AppendLog;
        LogSync.EnvelopeReceived += env => _ = RunLifecycle.OnEnvelopeAsync(env);
        RunLifecycle.StateChanged += OnEngineStateChanged;
        // Reset the tracker overlay whenever the loaded experiment changes so stale
        // run state (highlighted event, countdown, "NOW RUNNING" banner) never bleeds
        // into a different experiment or a freshly-connected device session.
        ExperimentEditor.ExperimentLoaded += () => Dispatcher.UIThread.Post(ResetOverlayState);
        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) => OnCountdownTick();
        _device.DataReceived += OnDataReceived;
        _device.StatusChanged += s =>
        {
            ConnectionStatus = s;
            IsConnected = _device.IsConnected;
        };
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(IpAddress)) return;
        ConnectionStatus = Loc.Get("Participant_StatusConnecting");
        var ok = await _device.ConnectAsync(IpAddress);
        if (ok)
        {
            // A fresh connection means whatever the engine was showing before is now stale.
            ResetOverlayState();
            _participantId ??= Guid.NewGuid().ToString();
            _suppressParticipantPush = true;
            if (string.IsNullOrEmpty(ParticipantKey)) ParticipantKey = _participantId;
            _suppressParticipantPush = false;
            var baseUrl = IpAddress.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? IpAddress
                : $"http://{(IpAddress.Contains(':') ? IpAddress : IpAddress + ":8080")}";
            ExperimentSync.SetEndpoint(baseUrl);
            LogSync.Start(baseUrl);
            RunLifecycle.Bind(baseUrl, _participantId, _sessionId);
            await ExperimentSync.PullAsync();
            AppendLog(Loc.Format("Participant_LogConnectedTo", IpAddress));

            // Reconcile the participant identity with the phone (bidirectional sync): if the
            // phone already has a participant set and we don't, adopt it.
            var phoneLabel = await _device.GetParticipantLabelAsync();
            if (!string.IsNullOrWhiteSpace(phoneLabel) &&
                (string.IsNullOrWhiteSpace(ParticipantKey) || ParticipantKey == _participantId))
            {
                _suppressParticipantPush = true;
                ParticipantKey = phoneLabel!;
                _suppressParticipantPush = false;
                AppendLog(Loc.Format("Participant_LogAdoptedParticipantFromPhone", phoneLabel));
            }

            // Push our participant to the phone so it agrees with the Control Station.
            if (_sessionId != null)
                await _db.UpsertParticipantAsync(_participantId, _sessionId, Label, IpAddress, ParticipantKey);
            await _device.PostParticipantAsync(_participantId, _sessionId ?? "", ParticipantKey, IpAddress);
        }
    }

    [RelayCommand]
    private void Disconnect()
    {
        _device.Disconnect();
        LogSync.Stop();
        AppendLog(Loc.Get("Common_Disconnected"));
    }

    [RelayCommand]
    private async Task StartExperimentAsync()
    {
        if (EnsureSessionStarted != null) await EnsureSessionStarted();
        ResetOverlayState();
        await RunLifecycle.DispatchAsync("start");
    }

    [RelayCommand]
    private async Task StartAutoAsync()
    {
        if (EnsureSessionStarted != null) await EnsureSessionStarted();
        ResetOverlayState();
        await RunLifecycle.DispatchAsync("auto");
    }

    [RelayCommand]
    private async Task PauseExperimentAsync() => await RunLifecycle.DispatchAsync("pause");

    [RelayCommand]
    private async Task StopExperimentAsync() => await RunLifecycle.DispatchAsync("stop");

    [RelayCommand]
    private async Task NextEventAsync() => await RunLifecycle.DispatchAsync("next");

    [RelayCommand]
    private async Task PrevEventAsync() => await RunLifecycle.DispatchAsync("prev");

    [RelayCommand]
    private async Task PushExperimentAsync(ExperimentDto experiment) => await ExperimentSync.PushAsync(experiment);

    [RelayCommand]
    private async Task PullExperimentAsync() => await ExperimentSync.PullAsync();

    [RelayCommand]
    private async Task TriggerAsync()
    {
        var gameType = SelectedGameType == "None" ? null : SelectedGameType;

        TriviaQuestion? trivia = null;
        if (SelectedGameType == "TRIVIA")
        {
            var idx = TriviaCorrectAnswer switch { "B" => 1, "C" => 2, "D" => 3, _ => 0 };
            trivia = new TriviaQuestion
            {
                Id = 1,
                Question = TriviaQuestionText,
                Answers = [TriviaAnswerA, TriviaAnswerB, TriviaAnswerC, TriviaAnswerD],
                CorrectIndex = idx
            };
        }

        var req = new InterventionRequest(SelectedNotificationType, GameType: gameType, TriviaQuestion: trivia);
        var ok = await _device.TriggerInterventionAsync(req);
        AppendLog(ok ? Loc.Format("Participant_LogTriggered", SelectedNotificationType, gameType ?? "") : Loc.Get("Participant_LogTriggerFailed"));

        // Record the intervention whenever the researcher fires one, regardless of whether
        // the wear delivery succeeded — the action itself is research data we want to keep.
        if (_participantId != null && _sessionId != null)
        {
            await _db.InsertInterventionAsync(
                Guid.NewGuid().ToString(), _participantId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                gameType, SelectedNotificationType);
        }
        else
        {
            AppendLog(Loc.Get("Participant_LogInterventionNotRecorded"));
        }
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        var req = new InterventionRequest("CANCEL");
        await _device.TriggerInterventionAsync(req);
        AppendLog(Loc.Get("Participant_LogSentCancel"));
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        IsScanning = true;
        AppendLog(Loc.Get("Participant_LogScanning"));

        // USB devices are tracked live by the ADB watcher, so a manual scan must NOT wipe them
        // (previously DiscoveredDevices.Clear() removed any USB-connected phone entirely). Only
        // the Wi-Fi-discovered entries are refreshed here.
        Dispatcher.UIThread.Post(() =>
        {
            for (int i = DiscoveredDevices.Count - 1; i >= 0; i--)
                if (DiscoveredDevices[i].Transport == DeviceTransport.Wifi)
                    DiscoveredDevices.RemoveAt(i);
        });

        var wifi = await DeviceDiscovery.ScanAsync(timeoutMs: 3000);
        var usb = UsbDeviceProvider?.Invoke() ?? (IReadOnlyList<DiscoveredDevice>)Array.Empty<DiscoveredDevice>();

        Dispatcher.UIThread.Post(() =>
        {
            foreach (var d in wifi)
                if (!DiscoveredDevices.Contains(d))
                    DiscoveredDevices.Add(d);
            // Fold USB connections back in so Scan reflects everything reachable, even if the
            // watcher's add event hasn't repopulated the list yet.
            foreach (var u in usb)
                if (!DiscoveredDevices.Contains(u))
                    DiscoveredDevices.Add(u);
        });

        AppendLog(wifi.Count + usb.Count > 0
            ? Loc.Format("Participant_LogFoundDevices", wifi.Count, usb.Count)
            : Loc.Get("Participant_LogNoDevicesFound"));
        IsScanning = false;
    }

    public async Task SetSessionAsync(string sessionId, string participantName)
    {
        _sessionId = sessionId;
        _participantId = Guid.NewGuid().ToString();
        _suppressParticipantPush = true;
        ParticipantKey = string.IsNullOrWhiteSpace(participantName) ? _participantId : participantName;
        _suppressParticipantPush = false;
        await _db.UpsertParticipantAsync(_participantId, sessionId, Label, IpAddress, ParticipantKey);
        // Keep the run-lifecycle context in sync so experiment runs started after the
        // session begins are linked to the correct session/participant (e.g. when the
        // device was connected before the session was started).
        RunLifecycle.SetContext(_participantId, _sessionId);
        // Inform the phone of the participant for this session (no-op if not connected).
        await _device.PostParticipantAsync(_participantId, sessionId, ParticipantKey, IpAddress);
    }

    public void AddDiscoveredDevice(DiscoveredDevice device)
    {
        if (!DiscoveredDevices.Contains(device))
            DiscoveredDevices.Add(device);
    }

    public void RemoveDiscoveredDevice(DiscoveredDevice device)
    {
        DiscoveredDevices.Remove(device);
        if (SelectedDevice == device) SelectedDevice = null;
    }

    private void OnDataReceived(WatchDataBatch batch)
    {
        // DB insert runs first — completely isolated so nothing else can block it.
        if (_participantId != null)
        {
            var pid = _participantId;
            _ = Task.Run(async () =>
            {
                try { await _db.InsertWatchDataBatchAsync(pid, batch); }
                catch (Exception ex) { AppendLog(Loc.Format("Participant_LogWatchDataInsertFailed", ex.Message)); }
            });
        }

        // UI updates are best-effort — a crash here can never affect data recording.
        if (batch.Snapshots?.Count > 0)
        {
            var last = batch.Snapshots[^1];
            Dispatcher.UIThread.Post(() =>
            {
                CurrentBpm = last.HeartRate;
                if (!float.IsNaN(_prevAccelX))
                {
                    AccelX = last.AccelX - _prevAccelX;
                    AccelY = last.AccelY - _prevAccelY;
                    AccelZ = last.AccelZ - _prevAccelZ;
                }
                _prevAccelX = last.AccelX;
                _prevAccelY = last.AccelY;
                _prevAccelZ = last.AccelZ;
                RawAccelX = last.AccelX;
                RawAccelY = last.AccelY;
                RawAccelZ = last.AccelZ;
                RotW = last.RotationW;
                RotX = last.RotationX;
                RotY = last.RotationY;
                RotZ = last.RotationZ;
                LastSnapshotTs = last.Timestamp;
            });
            try { MotionSamplesReceived?.Invoke(batch.Snapshots); }
            catch { }
        }
    }

    // Clears all live tracker state so the overlay starts from scratch. Safe to call from any
    // thread — always posts to the UI thread internally.
    private void ResetOverlayState()
    {
        Dispatcher.UIThread.Post(() =>
        {
            _engineRunning = false;
            _localElapsed = 0f;
            _randomWaitRemaining = null;
            _countdownTimer.Stop();
            SetCurrentEventIndex(-1);
        });
    }

    // Surfaces the engine's live state (which event is active + how long it's been running) so
    // the operator sees where the experiment is and a per-task countdown — for auto and manual runs.
    private void OnEngineStateChanged(EngineStateSnapshotDto snap)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _engineRunning = snap.Status == "RUNNING";
            _localElapsed = snap.ElapsedSecInEvent;
            _randomWaitRemaining = snap.RandomWaitRemainingSec;
            var active = snap.Status is "RUNNING" or "PAUSED";
            SetCurrentEventIndex(active ? snap.CurrentEventIndex : -1);
            UpdateCountdown();
            // Run the local 1 Hz ticker between snapshots for a smooth countdown; snapshots
            // (also ~1 Hz) keep it accurate. Don't restart it each snapshot or it never ticks.
            if (_engineRunning) { if (!_countdownTimer.IsEnabled) _countdownTimer.Start(); }
            else _countdownTimer.Stop();
        });
    }

    private void OnCountdownTick()
    {
        if (!_engineRunning) { _countdownTimer.Stop(); return; }
        _localElapsed += 1f;
        if (_randomWaitRemaining is float r) _randomWaitRemaining = Math.Max(0f, r - 1f);
        UpdateCountdown();
    }

    private void SetCurrentEventIndex(int index)
    {
        var events = ExperimentEditor.Events;
        for (int i = 0; i < events.Count; i++)
        {
            var isCur = i == index;
            if (events[i].IsCurrent != isCur) events[i].IsCurrent = isCur;
            if (!isCur && events[i].RemainingDisplay.Length > 0) events[i].RemainingDisplay = "";
        }
        _engineEventIndex = index;
        CurrentEventName = index >= 0 && index < events.Count ? events[index].Type : "";
        HasActiveEvent = index >= 0;
        if (index < 0) CountdownDisplay = "";
    }

    private void UpdateCountdown()
    {
        var events = ExperimentEditor.Events;
        if (_engineEventIndex < 0 || _engineEventIndex >= events.Count) { CountdownDisplay = ""; return; }
        var m = events[_engineEventIndex];
        string display = m.KindLabel switch
        {
            "Timed" => $"{Math.Max(0, (int)Math.Ceiling(m.Duration - _localElapsed))}s",
            "RandomWait" => $"~{Math.Max(0, (int)Math.Ceiling(_randomWaitRemaining ?? 0f))}s",
            "WaitForAction" or "WaitForTrigger" or "WaitForPrevious" => "⏳",
            _ => $"{(int)_localElapsed}s"
        };
        CountdownDisplay = display;
        m.RemainingDisplay = display;
    }

    private void AppendLog(string message)
    {
        var entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
        Avalonia.Threading.Dispatcher.UIThread.Post(() => EventLog.Add(entry));
    }

    public void Dispose()
    {
        _countdownTimer.Stop();
        RunLifecycle.StateChanged -= OnEngineStateChanged;
        _device.Dispose();
        ExperimentSync.Dispose();
        LogSync.Dispose();
        RunLifecycle.Dispose();
    }
}
