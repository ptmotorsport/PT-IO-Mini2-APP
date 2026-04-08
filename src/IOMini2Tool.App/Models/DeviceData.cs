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
}
