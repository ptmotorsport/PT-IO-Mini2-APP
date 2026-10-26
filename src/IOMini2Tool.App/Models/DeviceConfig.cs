namespace IOMini2Tool.Models;

/// <summary>
/// Device configuration (matches protocol config response)
/// </summary>
public sealed class DeviceConfig
{
    public int CanSpeed { get; init; }
    
    public int TxBaseId { get; init; }
    
    public int RxBaseId { get; init; }
    
    public int TxRate { get; init; }
    
    public int RxTimeout { get; init; }
    
    public int CanMode { get; init; }
    
    public byte SafeMask { get; init; }
    
    public byte ActiveMask { get; init; }
    
    public byte InputPullupMask { get; init; }

    /// <summary>
    /// AV1-4 external 1k pullup switch state (DTM2 only). 0 on Mini2 and on older DTM2
    /// firmware that predates this field.
    /// </summary>
    public byte AnalogPullupMask { get; init; }

    /// <summary>
    /// Per-channel app-vs-CAN override masks (bit=1: app value wins, CAN cannot change that
    /// channel's parameter). Auto-reset to per-mode defaults by firmware on CAN mode switch.
    /// </summary>
    public byte DutyOverrideMask { get; init; }
    public byte FreqOverrideMask { get; init; }
    public byte SafeOverrideMask { get; init; }
    public byte ActiveOverrideMask { get; init; }
    
    public int DiDebounceMs { get; init; }
    
    public int[] OutFreq { get; init; } = new int[8];
}
