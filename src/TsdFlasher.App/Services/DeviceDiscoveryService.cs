using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;
using IOMini2Tool.Models;

namespace IOMini2Tool.Services;

public sealed class DeviceDiscoveryService
{
    private static readonly Regex VidPidRegex = new("VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex UsbSerialRegex = new(@"USB\\VID_[0-9A-F]{4}&PID_[0-9A-F]{4}\\([^\\]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyList<DeviceInfo> GetComPorts()
    {
        var portNames = SerialPort.GetPortNames();
        var devices = new List<DeviceInfo>();

        foreach (var port in portNames.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase))
        {
            devices.Add(new DeviceInfo
            {
                Id = port,
                DisplayName = port
            });
        }

        return devices;
    }

    public IReadOnlyList<DeviceInfo> GetDfuDevices()
    {
        var result = new List<DeviceInfo>();

        ManagementObjectCollection collection;

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, DeviceID, PNPDeviceID, Service FROM Win32_PnPEntity");
            collection = searcher.Get();
        }
        catch
        {
            return result;
        }

        using (collection)
        {
            foreach (ManagementObject item in collection)
            {
                try
                {
                    var name = (item["Name"] as string) ?? string.Empty;
                    var deviceId = (item["DeviceID"] as string) ?? string.Empty;
                    var pnpDeviceId = (item["PNPDeviceID"] as string) ?? string.Empty;
                    var service = (item["Service"] as string) ?? string.Empty;

                    if (!LooksLikeDfu(name, deviceId, pnpDeviceId, service))
                    {
                        continue;
                    }

                    var normalizedId = string.IsNullOrWhiteSpace(pnpDeviceId) ? deviceId : pnpDeviceId;
                    var vidPid = ExtractVidPid(normalizedId);
                    var serial = ExtractUsbSerial(normalizedId);

                    result.Add(new DeviceInfo
                    {
                        Id = normalizedId,
                        DisplayName = string.IsNullOrWhiteSpace(name) ? normalizedId : name,
                        VidPid = vidPid,
                        UsbSerial = serial
                    });
                }
                catch
                {
                }
            }
        }

        return result
            .OrderBy(static x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool LooksLikeDfu(string name, string deviceId, string pnpDeviceId, string service)
    {
        var haystack = string.Join(" ", name, deviceId, pnpDeviceId, service).ToLowerInvariant();

        if (haystack.Contains("dfu"))
        {
            return true;
        }

        if (haystack.Contains("winusb") && (haystack.Contains("vid_") && haystack.Contains("pid_")))
        {
            return true;
        }

        return false;
    }

    private static string? ExtractVidPid(string identifier)
    {
        var match = VidPidRegex.Match(identifier);
        if (!match.Success)
        {
            return null;
        }

        return $"{match.Groups[1].Value}:{match.Groups[2].Value}".ToLowerInvariant();
    }

    private static string? ExtractUsbSerial(string identifier)
    {
        var match = UsbSerialRegex.Match(identifier);
        if (!match.Success)
        {
            return null;
        }

        var serial = match.Groups[1].Value.Trim();
        return string.IsNullOrWhiteSpace(serial) ? null : serial;
    }
}