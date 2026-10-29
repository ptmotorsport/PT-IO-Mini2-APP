using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;
using IOMini2Tool.Models;

namespace IOMini2Tool.Services;

public sealed class DeviceDiscoveryService
{
    private static readonly Regex VidPidRegex = new("VID_([0-9A-F]{4})&PID_([0-9A-F]{4})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex UsbSerialRegex = new(@"USB\\VID_[0-9A-F]{4}&PID_[0-9A-F]{4}(?:&[^\\]+)?\\([^\\]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IReadOnlyList<DeviceInfo> GetComPorts()
    {
        return SerialPort.GetPortNames()
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .Select(static name => new DeviceInfo { Id = name, DisplayName = name })
            .ToList();
    }

    public IReadOnlyList<DeviceInfo> GetDfuDevices()
    {
        var signedDrivers = GetSignedDrivers();
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, DeviceID, PNPDeviceID, Service, Status, ConfigManagerErrorCode, HardwareID FROM Win32_PnPEntity");
        using var collection = searcher.Get();
        var devices = new List<DeviceInfo>();

        foreach (ManagementObject item in collection)
        {
            using (item)
            {
                var name = ReadString(item, "Name");
                var deviceId = ReadString(item, "DeviceID");
                var pnpDeviceId = ReadString(item, "PNPDeviceID");
                var service = ReadString(item, "Service");
                var hardwareIds = item["HardwareID"] as string[] ?? Array.Empty<string>();

                if (!LooksLikeDfu(name, deviceId, pnpDeviceId, service, hardwareIds))
                {
                    continue;
                }

                var normalizedId = string.IsNullOrWhiteSpace(pnpDeviceId) ? deviceId : pnpDeviceId;
                var vidPid = ExtractVidPid(normalizedId);
                var usbSerial = ExtractUsbSerial(normalizedId);
                var driver = signedDrivers.TryGetValue(normalizedId, out var foundDriver) ? foundDriver : null;
                devices.Add(new DeviceInfo
                {
                    Id = normalizedId,
                    DisplayName = BuildDisplayName(name, normalizedId, vidPid, usbSerial),
                    VidPid = vidPid,
                    UsbSerial = usbSerial,
                    DriverService = service,
                    DriverProvider = driver?.Provider ?? string.Empty,
                    DriverSigner = driver?.Signer ?? string.Empty,
                    DriverVersion = driver?.Version ?? string.Empty,
                    DriverInfName = driver?.InfName ?? string.Empty,
                    DriverIsSigned = driver?.IsSigned,
                    PnpStatus = ReadString(item, "Status"),
                    ConfigManagerErrorCode = ReadInt(item, "ConfigManagerErrorCode"),
                    HardwareIds = hardwareIds
                });
            }
        }

        return devices
            .OrderBy(static device => device.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool TryValidateWinUsbBinding(DeviceInfo device, out string error)
    {
        if (!string.Equals(device.DriverService, "WinUSB", StringComparison.OrdinalIgnoreCase))
        {
            error = $"The selected device is bound to '{(string.IsNullOrWhiteSpace(device.DriverService) ? "no reported service" : device.DriverService)}', not WinUSB. Install/repair the correct signed driver or ask IT to bind the vendor-approved WinUSB driver.";
            return false;
        }

        if (!string.Equals(device.PnpStatus, "OK", StringComparison.OrdinalIgnoreCase)
            || device.ConfigManagerErrorCode != 0)
        {
            error = $"Windows reports a driver/device problem (status={device.PnpStatus}; configError={device.ConfigManagerErrorCode?.ToString() ?? "unknown"}). Resolve it in Device Manager before flashing.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static Dictionary<string, SignedDriverInfo> GetSignedDrivers()
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT DeviceID, DriverProviderName, Signer, DriverVersion, InfName, IsSigned FROM Win32_PnPSignedDriver");
        using var collection = searcher.Get();
        var drivers = new Dictionary<string, SignedDriverInfo>(StringComparer.OrdinalIgnoreCase);

        foreach (ManagementObject item in collection)
        {
            using (item)
            {
                var deviceId = ReadString(item, "DeviceID");
                if (string.IsNullOrWhiteSpace(deviceId))
                {
                    continue;
                }

                drivers[deviceId] = new SignedDriverInfo(
                    ReadString(item, "DriverProviderName"),
                    ReadString(item, "Signer"),
                    ReadString(item, "DriverVersion"),
                    ReadString(item, "InfName"),
                    item["IsSigned"] is bool isSigned ? isSigned : null);
            }
        }

        return drivers;
    }

    private static string ReadString(ManagementBaseObject item, string propertyName)
    {
        return item[propertyName] as string ?? string.Empty;
    }

    private static int? ReadInt(ManagementBaseObject item, string propertyName)
    {
        return item[propertyName] is { } value ? Convert.ToInt32(value) : null;
    }

    private static bool LooksLikeDfu(string name, string deviceId, string pnpDeviceId, string service, IEnumerable<string> hardwareIds)
    {
        var haystack = string.Join(" ", name, deviceId, pnpDeviceId, service);
        return haystack.Contains("dfu", StringComparison.OrdinalIgnoreCase)
               || (service.Contains("winusb", StringComparison.OrdinalIgnoreCase)
                   && VidPidRegex.IsMatch(haystack))
               || hardwareIds.Any(ArduinoRenesasDriverPackage.IsSupportedHardwareId);
    }

    private static string? ExtractVidPid(string identifier)
    {
        var match = VidPidRegex.Match(identifier);
        return match.Success
            ? $"{match.Groups[1].Value}:{match.Groups[2].Value}".ToLowerInvariant()
            : null;
    }

    private static string? ExtractUsbSerial(string identifier)
    {
        var match = UsbSerialRegex.Match(identifier);
        return match.Success && !string.IsNullOrWhiteSpace(match.Groups[1].Value)
            ? match.Groups[1].Value.Trim()
            : null;
    }

    private static string BuildDisplayName(string name, string identifier, string? vidPid, string? usbSerial)
    {
        var productName = string.IsNullOrWhiteSpace(name) ? identifier : name;
        var usbIdentity = usbSerial ?? identifier.Split('\\').LastOrDefault();
        var identityText = string.IsNullOrWhiteSpace(usbIdentity) ? string.Empty : $"; {usbIdentity}";
        return $"{productName} ({vidPid ?? "unknown VID:PID"}{identityText})";
    }

    private sealed record SignedDriverInfo(string Provider, string Signer, string Version, string InfName, bool? IsSigned);
}
