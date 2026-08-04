using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OcdWizard.ControlStation.Localization;
using OcdWizard.ControlStation.Models;

namespace OcdWizard.ControlStation.Services;

public class DeviceConnection : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _wsCts;
    private string? _baseUrl;
    private string? _wsUrl;
    private bool _disposed;

    public event Action<WatchDataBatch>? DataReceived;
    public event Action<string>? StatusChanged;

    public bool IsConnected { get; private set; }

    public async Task<bool> ConnectAsync(string address)
    {
        if (!TryBuildUris(address, out var baseUrl, out var wsUrl, out var error))
        {
            IsConnected = false;
            StatusChanged?.Invoke(Loc.Format("Device_StatusFailed", error));
            return false;
        }

        _baseUrl = baseUrl;
        _wsUrl = wsUrl;

        try
        {
            using var resp = await _http.GetAsync($"{_baseUrl}/status");
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                IsConnected = false;
                StatusChanged?.Invoke(Loc.Format("Device_StatusFailedHttp", (int)resp.StatusCode, resp.ReasonPhrase, FormatBody(body)));
                return false;
            }

            IsConnected = true;
            StatusChanged?.Invoke(Loc.Get("Device_StatusConnected"));
            _ = StartWebSocketAsync();
            return true;
        }
        catch (TaskCanceledException)
        {
            IsConnected = false;
            StatusChanged?.Invoke(Loc.Get("Device_StatusFailedTimeout"));
            return false;
        }
        catch (HttpRequestException ex)
        {
            IsConnected = false;
            StatusChanged?.Invoke(Loc.Format("Device_StatusFailed", ex.Message));
            return false;
        }
        catch (Exception ex)
        {
            IsConnected = false;
            StatusChanged?.Invoke(Loc.Format("Device_StatusFailed", ex.Message));
            return false;
        }
    }

    private static bool TryBuildUris(string address, out string baseUrl, out string wsUrl, out string error)
    {
        baseUrl = "";
        wsUrl = "";
        error = Loc.Get("Device_ErrorInvalidAddress");

        if (string.IsNullOrWhiteSpace(address))
        {
            error = Loc.Get("Device_ErrorAddressEmpty");
            return false;
        }

        var trimmed = address.Trim();
        var hasScheme = trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        if (!hasScheme)
        {
            var hostAndPort = trimmed.Contains(':') ? trimmed : $"{trimmed}:8080";
            trimmed = $"http://{hostAndPort}";
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var baseUri))
        {
            error = Loc.Get("Device_ErrorAddressNotValidUri");
            return false;
        }

        var baseBuilder = new UriBuilder(baseUri) { Path = "" };
        baseUrl = baseBuilder.Uri.ToString().TrimEnd('/');

        var wsBuilder = new UriBuilder(baseUri)
        {
            Scheme = baseUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? "wss" : "ws",
            Path = "/stream"
        };
        wsUrl = wsBuilder.Uri.ToString();
        return true;
    }

    private static string FormatBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";
        var compact = body.Replace("\r", " ").Replace("\n", " ").Trim();
        if (compact.Length > 200) compact = compact[..200] + "...";
        return $" - {compact}";
    }

    private async Task StartWebSocketAsync()
    {
        _wsCts?.Cancel();
        _wsCts = new CancellationTokenSource();
        var token = _wsCts.Token;

        while (!token.IsCancellationRequested)
        {
            try
            {
                _ws?.Dispose();
                _ws = new ClientWebSocket();
                if (_wsUrl != null)
                {
                    await _ws.ConnectAsync(new Uri(_wsUrl), token);
                }

                var buffer = new byte[64 * 1024];
                while (_ws.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    var result = await _ws.ReceiveAsync(buffer, token);
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        var batch = JsonSerializer.Deserialize<WatchDataBatch>(json);
                        if (batch != null)
                        {
                            try { DataReceived?.Invoke(batch); }
                            catch { /* handler errors must not kill the WebSocket loop */ }
                        }
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                        break;
                }
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                if (!token.IsCancellationRequested)
                    await Task.Delay(3000, token).ConfigureAwait(false);
            }
        }
    }

    public async Task<bool> TriggerInterventionAsync(InterventionRequest request)
    {
        if (_baseUrl == null) return false;
        try
        {
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync($"{_baseUrl}/trigger", content);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task PostParticipantAsync(string id, string sessionId, string label, string deviceIp)
    {
        if (_baseUrl == null) return;
        try
        {
            var payload = JsonSerializer.Serialize(new { id, sessionId, label, deviceIp });
            var content = new StringContent(payload, Encoding.UTF8, "application/json");
            await _http.PostAsync($"{_baseUrl}/participant", content);
        }
        catch { }
    }

    public async Task<bool> PushTriviaAsync(List<TriviaQuestion> questions)
    {
        if (_baseUrl == null) return false;
        try
        {
            var json = JsonSerializer.Serialize(questions);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync($"{_baseUrl}/trivia", content);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<string?> GetStatusAsync()
    {
        if (_baseUrl == null) return null;
        try
        {
            return await _http.GetStringAsync($"{_baseUrl}/status");
        }
        catch { return null; }
    }

    /// <summary>
    /// Reads the participant label currently set on the phone (set either from the phone
    /// UI or a prior push), so the Control Station can stay in sync. Returns null if the
    /// phone has no participant set or is unreachable.
    /// </summary>
    public async Task<string?> GetParticipantLabelAsync()
    {
        if (_baseUrl == null) return null;
        try
        {
            var json = await _http.GetStringAsync($"{_baseUrl}/participant");
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("label", out var label))
            {
                var value = label.GetString();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
            return null;
        }
        catch { return null; }
    }

    public void Disconnect()
    {
        _wsCts?.Cancel();
        IsConnected = false;
        StatusChanged?.Invoke(Loc.Get("Common_Disconnected"));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _wsCts?.Cancel();
        _ws?.Dispose();
        _http.Dispose();
    }
}
