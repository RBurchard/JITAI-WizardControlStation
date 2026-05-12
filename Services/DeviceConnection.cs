using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
        var hostAndPort = address.Contains(':') ? address : $"{address}:8080";
        _baseUrl = $"http://{hostAndPort}";
        _wsUrl = $"ws://{hostAndPort}/stream";

        try
        {
            var resp = await _http.GetAsync($"{_baseUrl}/status");
            resp.EnsureSuccessStatusCode();
            IsConnected = true;
            StatusChanged?.Invoke("Connected");
            _ = StartWebSocketAsync();
            return true;
        }
        catch (Exception ex)
        {
            IsConnected = false;
            StatusChanged?.Invoke($"Failed: {ex.Message}");
            return false;
        }
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
                        if (batch != null) DataReceived?.Invoke(batch);
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

    public void Disconnect()
    {
        _wsCts?.Cancel();
        IsConnected = false;
        StatusChanged?.Invoke("Disconnected");
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
