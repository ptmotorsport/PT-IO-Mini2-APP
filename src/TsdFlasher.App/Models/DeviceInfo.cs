namespace IOMini2Tool.Models;

public sealed class DeviceInfo
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string? VidPid { get; init; }

    public string? UsbSerial { get; init; }
}