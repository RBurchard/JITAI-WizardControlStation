using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OcdWizard.ControlStation.Services;

public sealed record AdbDevice(string Serial, string State, string Model);

public sealed class AdbTransport
{
    private const string AdbDownloadUrl = "https://developer.android.com/studio/releases/platform-tools";

    private string? _adbPath;
    public string? AdbPath => _adbPath;
    public string DownloadUrl => AdbDownloadUrl;

    public event Action<string>? Log;
    public event Action? AdbMissing;

    public bool TryLocate()
    {
        if (_adbPath != null && File.Exists(_adbPath)) return true;

        var candidates = new List<string>();
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            candidates.Add(Path.Combine(dir, "adb.exe"));
        }
        var androidHome = Environment.GetEnvironmentVariable("ANDROID_HOME")
            ?? Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT");
        if (!string.IsNullOrWhiteSpace(androidHome))
            candidates.Add(Path.Combine(androidHome, "platform-tools", "adb.exe"));
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localAppData))
            candidates.Add(Path.Combine(localAppData, "Android", "Sdk", "platform-tools", "adb.exe"));
        var baseDir = AppContext.BaseDirectory;
        candidates.Add(Path.Combine(baseDir, "tools", "adb", "adb.exe"));

        foreach (var c in candidates)
        {
            if (File.Exists(c)) { _adbPath = c; return true; }
        }
        AdbMissing?.Invoke();
        return false;
    }

    public async Task<List<AdbDevice>> ListDevicesAsync(CancellationToken ct = default)
    {
        var result = new List<AdbDevice>();
        if (!TryLocate()) return result;
        var output = await RunAsync(new[] { "devices", "-l" }, ct);
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("List of devices")) continue;
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            var serial = parts[0];
            var state = parts[1];
            var model = "";
            for (var i = 2; i < parts.Length; i++)
            {
                if (parts[i].StartsWith("model:")) { model = parts[i].Substring("model:".Length); break; }
            }
            result.Add(new AdbDevice(serial, state, model));
        }
        return result;
    }

    public async Task<bool> ForwardAsync(string serial, int hostPort, int devicePort, CancellationToken ct = default)
    {
        if (!TryLocate()) return false;
        var output = await RunAsync(new[] { "-s", serial, "forward", $"tcp:{hostPort}", $"tcp:{devicePort}" }, ct);
        Log?.Invoke($"adb forward {serial} {hostPort}->{devicePort}: {output.Trim()}");
        return true;
    }

    public async Task RemoveForwardAsync(string serial, int hostPort, CancellationToken ct = default)
    {
        if (!TryLocate()) return;
        await RunAsync(new[] { "-s", serial, "forward", "--remove", $"tcp:{hostPort}" }, ct);
        Log?.Invoke($"adb forward --remove {serial} tcp:{hostPort}");
    }

    public async IAsyncEnumerable<List<AdbDevice>> WatchDevicesAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default,
        int pollMs = 2000)
    {
        var last = "";
        while (!ct.IsCancellationRequested)
        {
            var devices = await ListDevicesAsync(ct);
            var key = string.Join("|", devices.ConvertAll(d => d.Serial + ":" + d.State));
            if (key != last) { last = key; yield return devices; }
            try { await Task.Delay(pollMs, ct); } catch (TaskCanceledException) { yield break; }
        }
    }

    private async Task<string> RunAsync(string[] args, CancellationToken ct)
    {
        if (_adbPath == null) return "";
        var psi = new ProcessStartInfo
        {
            FileName = _adbPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi);
            if (p == null) return "";
            var stdoutTask = p.StandardOutput.ReadToEndAsync();
            var stderrTask = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync(ct);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            return string.IsNullOrWhiteSpace(stderr) ? stdout : stdout + "\n" + stderr;
        }
        catch (Exception ex)
        {
            Log?.Invoke($"adb error: {ex.Message}");
            return "";
        }
    }
}
