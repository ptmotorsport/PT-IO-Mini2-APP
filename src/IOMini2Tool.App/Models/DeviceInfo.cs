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

    /// <summary>
    /// Number of DPO output-voltage-sense channels (DTM2 only, via ADS1115). 0 on Mini2 and on
    /// older DTM2 firmware that predates this field.
    /// </summary>
    public int OutputVoltageChannels { get; init; }

    /// <summary>
    /// Raw "board" field from the hello message (e.g. "PT-IO-Mini2", "PT-IO-DTM2"), if the
    /// connected firmware reports one. Null on older firmware that predates this field.
    /// </summary>
    public string? Board { get; init; }

    /// <summary>
    /// Board variant. Prefers the firmware's explicit "board" hello field; falls back to a
    /// heuristic based on I/O channel counts for older firmware that doesn't report it yet.
    /// Mini2 reports 8 digital in / 8 digital out; DTM2 reports 4/4.
    /// ponytail: the fallback heuristic breaks if a future variant reuses the same counts -
    /// harmless since it's only used when the real "board" field is missing.
    /// </summary>
    public string DeviceModel => Board ?? (DigitalIn, DigitalOut) switch
    {
        (8, 8) => "PT-IO-Mini2",
        (4, 4) => "PT-IO-DTM2",
        _ => "Unknown"
    };
}
