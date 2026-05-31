using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using OcdWizard.ControlStation.Database;
using OcdWizard.ControlStation.Models;

namespace OcdWizard.ControlStation.Services;

public sealed class LogSyncService : IDisposable
{
    private readonly DatabaseService _db;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private CancellationTokenSource? _cts;
    private string? _baseUrl;
    private string? _wsUrl;
    private long _lastSeenId;
    private readonly string _logsDir;

    public event Action<SessionLogEntryDto>? LogReceived;
    public event Action<WsEnvelopeDto>? EnvelopeReceived;
    public event Action<string>? Status;

    public ObservableCollection<SessionLogEntryDto> RecentLogs { get; } = new();
    private const int MaxRecent = 500;

    public LogSyncService(DatabaseService db)
    {
        _db = db;
        _logsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OcdWizard", "logs");
        Directory.CreateDirectory(_logsDir);
    }

    public void Start(string baseUrl)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        var b = new UriBuilder(baseUrl) { Scheme = baseUrl.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws", Path = ProtocolPaths.EVENTS_WS };
        _wsUrl = b.Uri.ToString();
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _ = WsLoopAsync(_cts.Token);
    }

    public void Stop() { _cts?.Cancel(); _cts = null; }

    private async Task WsLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var ws = new ClientWebSocket();
                await ws.ConnectAsync(new Uri(_wsUrl!), ct);
                Status?.Invoke("logs-ws connected");
                var buffer = new byte[64 * 1024];
                while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var res = await ws.ReceiveAsync(buffer, ct);
                    if (res.MessageType == WebSocketMessageType.Text)
                    {
                        var text = Encoding.UTF8.GetString(buffer, 0, res.Count);
                        var env = JsonSerializer.Deserialize<WsEnvelopeDto>(text);
                        if (env == null) continue;
                        EnvelopeReceived?.Invoke(env);
                        if (env.Type == WsEnvelopeDto.TYPE_LOG)
                        {
                            var entry = JsonSerializer.Deserialize<SessionLogEntryDto>(env.PayloadJson);
                            if (entry != null) await IngestAsync(entry);
                        }
                    }
                    else if (res.MessageType == WebSocketMessageType.Close) break;
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { Status?.Invoke($"logs-ws error: {ex.Message}"); }

            try
            {
                await PollOnceAsync(ct);
                await Task.Delay(3000, ct);
            }
            catch (OperationCanceledException) { return; }
        }
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        if (_baseUrl == null) return;
        try
        {
            var url = $"{_baseUrl}{ProtocolPaths.LOGS}?since={_lastSeenId}";
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return;
            var body = await resp.Content.ReadAsStringAsync(ct);
            var page = JsonSerializer.Deserialize<SessionLogPageDto>(body);
            if (page == null) return;
            foreach (var e in page.Entries) await IngestAsync(e);
        }
        catch (Exception ex) { Status?.Invoke($"logs-poll error: {ex.Message}"); }
    }

    private async Task IngestAsync(SessionLogEntryDto entry)
    {
        if (entry.Id <= _lastSeenId) return;
        _lastSeenId = entry.Id;
        await _db.InsertLogAsync(entry);
        AppendToTsv(entry);
        LogReceived?.Invoke(entry);
        Dispatcher.UIThread.Post(() =>
        {
            RecentLogs.Add(entry);
            while (RecentLogs.Count > MaxRecent) RecentLogs.RemoveAt(0);
        });
    }

    private void AppendToTsv(SessionLogEntryDto e)
    {
        try
        {
            var name = e.RunId ?? "no-run";
            var path = Path.Combine(_logsDir, $"{name}.tsv");
            var line = $"{e.Id}\t{e.Ts}\t{e.Source}\t{e.Level}\t{e.Kind}\t{e.PayloadJson.Replace('\n', ' ').Replace('\r', ' ')}\n";
            File.AppendAllText(path, line);
        }
        catch { }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _http.Dispose();
    }
}
