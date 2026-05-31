using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using OcdWizard.ControlStation.Models;

namespace OcdWizard.ControlStation.Services;

public sealed class UsbDeviceSource : IAsyncDisposable
{
    private readonly AdbTransport _adb;
    private readonly Dictionary<string, int> _allocatedPorts = new();
    private CancellationTokenSource? _cts;
    private int _nextPort = ProtocolPaths.HTTP_PORT;

    public event Action<DiscoveredDevice>? DeviceAdded;
    public event Action<DiscoveredDevice>? DeviceRemoved;
    public event Action<string>? Log;

    public ObservableCollection<DiscoveredDevice> Devices { get; } = new();

    public UsbDeviceSource(AdbTransport adb)
    {
        _adb = adb;
        _adb.Log += s => Log?.Invoke(s);
    }

    public void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        _ = WatchLoopAsync(_cts.Token);
    }

    private async Task WatchLoopAsync(CancellationToken ct)
    {
        await foreach (var devices in _adb.WatchDevicesAsync(ct))
        {
            var byCurrent = new Dictionary<string, DiscoveredDevice>();
            foreach (var d in Devices)
                if (d.Transport == DeviceTransport.Usb && d.AdbSerial != null)
                    byCurrent[d.AdbSerial] = d;
            var newSerials = new HashSet<string>();
            foreach (var ad in devices)
            {
                if (ad.State != "device") continue;
                newSerials.Add(ad.Serial);
                if (byCurrent.ContainsKey(ad.Serial)) continue;
                var hostPort = AllocatePort(ad.Serial);
                var ok = await _adb.ForwardAsync(ad.Serial, hostPort, ProtocolPaths.HTTP_PORT, ct);
                if (!ok) continue;
                var d = new DiscoveredDevice(
                    Name: string.IsNullOrEmpty(ad.Model) ? ad.Serial : ad.Model,
                    Address: "127.0.0.1",
                    Port: hostPort,
                    Transport: DeviceTransport.Usb,
                    AdbSerial: ad.Serial);
                Devices.Add(d);
                DeviceAdded?.Invoke(d);
            }
            foreach (var (serial, dev) in byCurrent)
            {
                if (newSerials.Contains(serial)) continue;
                Devices.Remove(dev);
                await _adb.RemoveForwardAsync(serial, dev.Port, ct);
                _allocatedPorts.Remove(serial);
                DeviceRemoved?.Invoke(dev);
            }
        }
    }

    private int AllocatePort(string serial)
    {
        if (_allocatedPorts.TryGetValue(serial, out var p)) return p;
        var port = _nextPort++;
        _allocatedPorts[serial] = port;
        return port;
    }

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        foreach (var d in Devices)
            if (d.AdbSerial != null) await _adb.RemoveForwardAsync(d.AdbSerial, d.Port);
        Devices.Clear();
        _cts?.Dispose();
        _cts = null;
    }
}
