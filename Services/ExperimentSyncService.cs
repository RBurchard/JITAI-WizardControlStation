using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using OcdWizard.ControlStation.Database;
using OcdWizard.ControlStation.Models;

namespace OcdWizard.ControlStation.Services;

public sealed class ExperimentSyncService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly DatabaseService _db;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private CancellationTokenSource? _debounceCts;
    private string? _baseUrl;
    private string _lastSyncedEtag = "";

    public event Action<ExperimentDto, ExperimentDto>? ConflictDetected;
    public event Action<ExperimentDto>? RemoteUpdated;
    public event Action<string>? Log;

    public ExperimentSyncService(DatabaseService db)
    {
        _db = db;
    }

    public void SetEndpoint(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _lastSyncedEtag = "";
    }

    public async Task PullAsync(CancellationToken ct = default)
    {
        if (_baseUrl == null) return;
        try
        {
            using var resp = await _http.GetAsync($"{_baseUrl}{ProtocolPaths.EXPERIMENT}", ct);
            if (!resp.IsSuccessStatusCode) return;
            var body = await resp.Content.ReadAsStringAsync(ct);
            var env = JsonSerializer.Deserialize<ExperimentEnvelopeDto>(body, JsonOpts);
            if (env == null) return;

            var local = await _db.LoadExperimentAsync(env.Experiment.Id);
            var remoteNewerThanLocal = local == null || env.Experiment.UpdatedAt > local.Value.UpdatedAt;
            var localChangedSinceLastSync = local != null && _lastSyncedEtag != "" && _lastSyncedEtag != env.Etag;

            if (remoteNewerThanLocal && localChangedSinceLastSync && local != null)
            {
                var localDto = JsonSerializer.Deserialize<ExperimentDto>(local.Value.Json, JsonOpts);
                if (localDto != null) ConflictDetected?.Invoke(localDto, env.Experiment);
                return;
            }

            await _db.UpsertExperimentAsync(
                env.Experiment.Id,
                env.Experiment.Name,
                JsonSerializer.Serialize(env.Experiment, JsonOpts),
                env.Experiment.Version,
                env.Experiment.UpdatedAt,
                source: "phone");
            _lastSyncedEtag = env.Etag;
            RemoteUpdated?.Invoke(env.Experiment);
            Log?.Invoke($"Pulled experiment {env.Experiment.Name} etag={env.Etag}");
        }
        catch (Exception ex) { Log?.Invoke($"Pull failed: {ex.Message}"); }
    }

    public async Task<bool> PushAsync(ExperimentDto experiment, string? ifMatch = null, CancellationToken ct = default)
    {
        if (_baseUrl == null) return false;
        try
        {
            var req = new ExperimentPutRequestDto(experiment, ifMatch ?? (_lastSyncedEtag.Length > 0 ? _lastSyncedEtag : null));
            var json = JsonSerializer.Serialize(req, JsonOpts);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var resp = await _http.PutAsync($"{_baseUrl}{ProtocolPaths.EXPERIMENT}", content, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            var env = JsonSerializer.Deserialize<ExperimentEnvelopeDto>(body, JsonOpts);
            if (resp.StatusCode == System.Net.HttpStatusCode.Conflict && env != null)
            {
                ConflictDetected?.Invoke(experiment, env.Experiment);
                return false;
            }
            if (!resp.IsSuccessStatusCode) return false;
            if (env != null)
            {
                _lastSyncedEtag = env.Etag;
                await _db.UpsertExperimentAsync(env.Experiment.Id, env.Experiment.Name,
                    JsonSerializer.Serialize(env.Experiment, JsonOpts), env.Experiment.Version, env.Experiment.UpdatedAt, "pc");
                // Do NOT fire RemoteUpdated after push — the ControlStation is the source of
                // truth for the experiment definition. Reloading from the phone's response
                // would clear any UI selections that the phone may not round-trip identically.
                // Only PullAsync fires RemoteUpdated to reload the UI.
                Log?.Invoke($"Pushed experiment {env.Experiment.Name} etag={env.Etag}");
            }
            return true;
        }
        catch (Exception ex) { Log?.Invoke($"Push failed: {ex.Message}"); return false; }
    }

    public void CancelPendingAutoSync() => _debounceCts?.Cancel();

    public void NotifyEdited(ExperimentDto experiment)
    {
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var token = _debounceCts.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), token);
                if (!token.IsCancellationRequested) await PushAsync(experiment, ct: token);
            }
            catch (TaskCanceledException) { }
        });
    }

    public void Dispose()
    {
        _debounceCts?.Cancel();
        _http.Dispose();
    }
}
