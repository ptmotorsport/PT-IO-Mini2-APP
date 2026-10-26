namespace IOMini2Tool.Models;

/// <summary>
/// Represents telemetry data from the device (matches protocol telemetry frame)
/// </summary>
public sealed class DeviceData
{
    public long Timestamp { get; init; }
    
    public int[] Analog { get; init; } = new int[8];
    
    public DigitalInputState[] Digital { get; init; } = new DigitalInputState[8];
    
    public OutputState[] Outputs { get; init; } = new OutputState[8];
    
    /// <summary>
    /// DPO1-4 output voltage sense in millivolts (DTM2 only, via ADS1115). Empty on Mini2 and
    /// on older DTM2 firmware that predates this field.
    /// </summary>
    public int[] OutputVoltage { get; init; } = Array.Empty<int>();
    
    public bool OutputVoltageReady { get; init; }
    
    public CanStatus Can { get; init; } = new();
}

/// <summary>
/// Digital input state with frequency and duty cycle measurement
/// </summary>
public sealed class DigitalInputState
{
    public bool State { get; init; }
    
    public string? Freq { get; init; }
    
    public string? Duty { get; init; }
}

/// <summary>
/// Output channel state
/// </summary>
public sealed class OutputState
{
    public string Duty { get; init; } = "0.0";
    
    public int Freq { get; init; }
    
    public bool Safe { get; init; }
    
    public bool ActiveHigh { get; init; }

    /// <summary>
    /// Last CAN-decoded duty/freq/safe/activeHigh (before any app-override revert). Only
    /// meaningfully differs from the applied value above when the matching override is set.
    /// </summary>
    public string CanDuty { get; init; } = "0.0";

    public int CanFreq { get; init; }

    public bool CanSafe { get; init; }

    public bool CanActiveHigh { get; init; }
}

/// <summary>
/// CAN bus status
/// </summary>
public sealed class CanStatus
{
    public bool Init { get; init; }
    
    public bool SafeState { get; init; }
    
    public int RxCount { get; init; }
    
    public int TxCount { get; init; }
    
    public int TxFail { get; init; }
    
    public int Mode { get; init; }
    
    public long LastRxMs { get; init; }
    
    public int RxTimeout { get; init; }

    /// <summary>
    /// Per-channel app-vs-CAN override masks (bit=1: app value wins, CAN cannot change that
    /// channel's parameter). Mirrors DeviceConfig's masks but kept fresh via telemetry.
    /// </summary>
    public byte DutyOverrideMask { get; init; }
    public byte FreqOverrideMask { get; init; }
    public byte SafeOverrideMask { get; init; }
    public byte ActiveOverrideMask { get; init; }
}
