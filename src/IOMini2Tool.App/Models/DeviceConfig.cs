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
    
    public int DiDebounceMs { get; init; }
    
    public int[] OutFreq { get; init; } = new int[8];
}
