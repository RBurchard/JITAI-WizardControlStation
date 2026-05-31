using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OcdWizard.ControlStation.Database;
using OcdWizard.ControlStation.Models;

namespace OcdWizard.ControlStation.Services;

public sealed class RunLifecycleService : IDisposable
{
    private readonly DatabaseService _db;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private string? _baseUrl;
    private string? _currentRunId;
    private string? _currentDistractionId;
    private string? _participantId;
    private string? _sessionId;
    private string? _currentExperimentId;

    public event Action<string>? RunStarted;
    public event Action<string?>? RunStopped;
    public event Action<EngineStateSnapshotDto>? StateChanged;
    public event Action<string>? Log;

    public string? CurrentRunId => _currentRunId;

    public RunLifecycleService(DatabaseService db) { _db = db; }

    public void Bind(string baseUrl, string? participantId, string? sessionId)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _participantId = participantId;
        _sessionId = sessionId;
    }

    public Task<bool> DispatchAsync(string command, int? targetEventIndex = null)
    {
        if (_baseUrl == null) return Task.FromResult(false);
        var dto = new ExperimentControlDto(command, _currentRunId, targetEventIndex);
        return PostAsync(ProtocolPaths.EXPERIMENT_CONTROL, dto);
    }

    public Task<bool> FireTriggerAsync(string triggerId)
    {
        if (_baseUrl == null) return Task.FromResult(false);
        return PostAsync(ProtocolPaths.TRIGGER_FIRE, new TriggerFireRequestDto(triggerId));
    }

    private async Task<bool> PostAsync<T>(string path, T payload)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync($"{_baseUrl}{path}", content);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception ex) { Log?.Invoke($"POST {path} failed: {ex.Message}"); return false; }
    }

    public async Task OnEnvelopeAsync(WsEnvelopeDto envelope)
    {
        switch (envelope.Type)
        {
            case WsEnvelopeDto.TYPE_RUN_STARTED:
            {
                var info = JsonDocument.Parse(envelope.PayloadJson).RootElement;
                var runId = info.GetProperty("runId").GetString() ?? Guid.NewGuid().ToString();
                var experimentId = info.TryGetProperty("experimentId", out var ep) ? ep.GetString() : null;
                _currentRunId = runId;
                _currentExperimentId = experimentId;
                await _db.OpenRunAsync(runId, experimentId ?? "", _participantId, _sessionId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                RunStarted?.Invoke(runId);
                break;
            }
            case WsEnvelopeDto.TYPE_RUN_STOPPED:
            {
                if (_currentRunId != null)
                    await _db.CloseRunAsync(_currentRunId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                var prev = _currentRunId;
                _currentRunId = null;
                RunStopped?.Invoke(prev);
                break;
            }
            case WsEnvelopeDto.TYPE_INTERVENTION_STARTED:
            {
                if (_currentRunId == null) return;
                var info = JsonDocument.Parse(envelope.PayloadJson).RootElement;
                var interventionId = info.TryGetProperty("id", out var ip) ? ip.GetString() : null;
                _currentDistractionId = Guid.NewGuid().ToString();
                await _db.OpenDistractionAsync(_currentDistractionId, _currentRunId, null, interventionId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                break;
            }
            case WsEnvelopeDto.TYPE_INTERVENTION_FINISHED:
            {
                if (_currentDistractionId == null) return;
                var info = JsonDocument.Parse(envelope.PayloadJson).RootElement;
                var payload = info.TryGetProperty("payload", out var p) ? p.GetString() : null;
                long? reaction = info.TryGetProperty("reactionMs", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt64() : null;
                await _db.CloseDistractionAsync(_currentDistractionId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), payload, reaction);
                _currentDistractionId = null;
                break;
            }
            case WsEnvelopeDto.TYPE_STATE:
            {
                var snap = JsonSerializer.Deserialize<EngineStateSnapshotDto>(envelope.PayloadJson);
                if (snap != null) StateChanged?.Invoke(snap);
                break;
            }
        }
    }

    public void Dispose() { _http.Dispose(); }
}
