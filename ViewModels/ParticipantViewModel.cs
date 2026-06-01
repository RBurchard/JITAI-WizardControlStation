using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OcdWizard.ControlStation.Database;
using OcdWizard.ControlStation.Models;
using OcdWizard.ControlStation.Services;
using Avalonia.Threading;

namespace OcdWizard.ControlStation.ViewModels;

public partial class ParticipantViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly DeviceConnection _device = new();
    public ExperimentSyncService ExperimentSync { get; }
    public LogSyncService LogSync { get; }
    public RunLifecycleService RunLifecycle { get; }
    public ExperimentEditorViewModel ExperimentEditor { get; }
    private string? _participantId;
    private string? _sessionId;

    public string Label { get; }
    public string? CurrentRunId => RunLifecycle.CurrentRunId;
    [ObservableProperty] private string _participantKey = "";

    public IReadOnlyList<string> GameTypes { get; } =
        ["None", "STAND_STILL", "TRIVIA", "SIMON_SAYS", "LOCK_PICKING"];
    public IReadOnlyList<string> NotificationTypes { get; } =
        ["VIBRATION1", "VIBRATION2", "SOUND", "VIBRATION_SOUND"];

    [ObservableProperty] private string _connectionStatus = "Not connected";
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

    public ObservableCollection<string> EventLog { get; } = [];
    public ObservableCollection<DiscoveredDevice> DiscoveredDevices { get; } = [];

    /// <summary>
    /// Raised once per received batch with the full ordered snapshot series so the
    /// motion view can reconstruct at the watch's true sample rate (~50 Hz) rather
    /// than sampling only the latest value. Fired from a background thread.
    /// </summary>
    public event Action<IReadOnlyList<WatchDataSnapshot>>? MotionSamplesReceived;

    partial void OnSelectedDeviceChanged(DiscoveredDevice? value)
    {
        if (value != null)
            IpAddress = $"{value.Address}:{value.Port}";
    }

    partial void OnSelectedGameTypeChanged(string value) => IsTrivia = value == "TRIVIA";

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
        ConnectionStatus = "Connecting...";
        var ok = await _device.ConnectAsync(IpAddress);
        if (ok)
        {
            _participantId ??= Guid.NewGuid().ToString();
            if (string.IsNullOrEmpty(ParticipantKey)) ParticipantKey = _participantId;
            var baseUrl = IpAddress.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? IpAddress
                : $"http://{(IpAddress.Contains(':') ? IpAddress : IpAddress + ":8080")}";
            ExperimentSync.SetEndpoint(baseUrl);
            LogSync.Start(baseUrl);
            RunLifecycle.Bind(baseUrl, _participantId, _sessionId);
            await ExperimentSync.PullAsync();
            AppendLog($"Connected to {IpAddress}");
        }
    }

    [RelayCommand]
    private void Disconnect()
    {
        _device.Disconnect();
        LogSync.Stop();
        AppendLog("Disconnected");
    }

    [RelayCommand]
    private async Task StartExperimentAsync() => await RunLifecycle.DispatchAsync("start");

    [RelayCommand]
    private async Task StartAutoAsync() => await RunLifecycle.DispatchAsync("auto");

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
        AppendLog(ok ? $"Triggered {SelectedNotificationType} {gameType ?? ""}" : "Trigger failed");

        if (ok && _participantId != null && _sessionId != null)
        {
            await _db.InsertInterventionAsync(
                Guid.NewGuid().ToString(), _participantId,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                gameType, SelectedNotificationType);
        }
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        var req = new InterventionRequest("CANCEL");
        await _device.TriggerInterventionAsync(req);
        AppendLog("Sent CANCEL");
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        IsScanning = true;
        AppendLog("Scanning for JITAI Wizard devices…");
        Dispatcher.UIThread.Post(() => DiscoveredDevices.Clear());

        var devices = await DeviceDiscovery.ScanAsync(timeoutMs: 3000);

        Dispatcher.UIThread.Post(() =>
        {
            foreach (var d in devices)
                DiscoveredDevices.Add(d);
        });

        AppendLog(devices.Count > 0
            ? $"Found {devices.Count} device(s)"
            : "No devices found — ensure phone app is running on the same network");
        IsScanning = false;
    }

    public void SetSession(string sessionId)
    {
        _sessionId = sessionId;
        _participantId = Guid.NewGuid().ToString();
        if (string.IsNullOrEmpty(ParticipantKey)) ParticipantKey = _participantId;
        _ = _db.UpsertParticipantAsync(_participantId, sessionId, Label, IpAddress, ParticipantKey);
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
        _ = Task.Run(async () =>
        {
            if (batch.Snapshots.Count > 0)
            {
                var last = batch.Snapshots[^1];
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

                // Hand the full series to the motion view for high-rate reconstruction.
                MotionSamplesReceived?.Invoke(batch.Snapshots);
            }

            if (_participantId != null)
                await _db.InsertWatchDataBatchAsync(_participantId, batch);
        });
    }

    private void AppendLog(string message)
    {
        var entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
        Avalonia.Threading.Dispatcher.UIThread.Post(() => EventLog.Add(entry));
    }

    public void Dispose()
    {
        _device.Dispose();
        ExperimentSync.Dispose();
        LogSync.Dispose();
        RunLifecycle.Dispose();
    }
}
