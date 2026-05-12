using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OcdWizard.ControlStation.Database;
using OcdWizard.ControlStation.Models;
using OcdWizard.ControlStation.Services;

namespace OcdWizard.ControlStation.ViewModels;

public partial class ParticipantViewModel : ObservableObject
{
    private readonly DatabaseService _db;
    private readonly DeviceConnection _device = new();
    private string? _participantId;
    private string? _sessionId;

    public string Label { get; }

    [ObservableProperty] private string _connectionStatus = "Not connected";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private float _currentBpm;
    [ObservableProperty] private float _movementLevel;
    [ObservableProperty] private string _ipAddress = "";
    [ObservableProperty] private string _selectedGameType = "None";
    [ObservableProperty] private string _selectedNotificationType = "VIBRATION1";

    public ObservableCollection<string> EventLog { get; } = [];

    public ParticipantViewModel(string label, DatabaseService db)
    {
        Label = label;
        _db = db;
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
            AppendLog($"Connected to {IpAddress}");
        }
    }

    [RelayCommand]
    private void Disconnect()
    {
        _device.Disconnect();
        AppendLog("Disconnected");
    }

    [RelayCommand]
    private async Task TriggerAsync()
    {
        var gameType = SelectedGameType == "None" ? null : SelectedGameType;
        var req = new InterventionRequest(SelectedNotificationType, GameType: gameType);
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

    public void SetSession(string sessionId)
    {
        _sessionId = sessionId;
        _participantId = Guid.NewGuid().ToString();
    }

    private void OnDataReceived(WatchDataBatch batch)
    {
        _ = Task.Run(async () =>
        {
            if (batch.Snapshots.Count > 0)
            {
                var last = batch.Snapshots[^1];
                CurrentBpm = last.HeartRate;
                var mag = MathF.Sqrt(last.AccelX * last.AccelX + last.AccelY * last.AccelY + last.AccelZ * last.AccelZ);
                MovementLevel = Math.Clamp((mag - 9.8f) / 5f, 0f, 1f);
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

    public void Dispose() => _device.Dispose();
}
