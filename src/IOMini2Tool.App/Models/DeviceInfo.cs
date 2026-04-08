namespace IOMini2Tool.Models;

public sealed class DeviceInfo
{
    public string Id { get; init; } = string.Empty;

    public string DisplayName { get; init; } = string.Empty;

    public string? VidPid { get; init; }

    public string? UsbSerial { get; init; }
    
    // Protocol-related fields from hello message
    public int ProtocolVersion { get; init; }
    
    public int FirmwareVersion { get; init; }
    
    public int CanMode { get; init; }
    
    public string CanModeName { get; init; } = string.Empty;
    
    public string[] Capabilities { get; init; } = Array.Empty<string>();
    
    public int AnalogChannels { get; init; }
    
    public int DigitalIn { get; init; }
    
    public int DigitalOut { get; init; }
}
