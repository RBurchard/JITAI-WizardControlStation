using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OcdWizard.ControlStation.Services;

public enum DeviceTransport { Wifi, Usb }

public record DiscoveredDevice(string Name, string Address, int Port, DeviceTransport Transport = DeviceTransport.Wifi, string? AdbSerial = null)
{
    public override string ToString() => Transport == DeviceTransport.Usb
        ? $"USB {Name} ({AdbSerial})"
        : $"{Name} ({Address}:{Port})";
}

public static class DeviceDiscovery
{
    private const int BeaconPort = 5280;

    public static async Task<List<DiscoveredDevice>> ScanAsync(int timeoutMs = 3000)
    {
        var found = new List<DiscoveredDevice>();
        var seen = new HashSet<string>();

        using var udp = new UdpClient();
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, BeaconPort));
        udp.Client.ReceiveTimeout = 500;

        using var cts = new CancellationTokenSource(timeoutMs);
        var token = cts.Token;

        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(token);
                var json = Encoding.UTF8.GetString(result.Buffer);
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (!root.TryGetProperty("app", out var appProp) || appProp.GetString() != "JITAIWizard")
                    continue;

                var address = result.RemoteEndPoint.Address.ToString();
                var port = root.TryGetProperty("port", out var portProp) ? portProp.GetInt32() : 8080;
                var name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? address : address;
                var key = $"{address}:{port}";

                if (seen.Add(key))
                    found.Add(new DiscoveredDevice(name, address, port));
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException) { /* timeout on individual receive — keep looping */ }
            catch (JsonException) { /* malformed packet — skip */ }
        }

        return found;
    }
}
