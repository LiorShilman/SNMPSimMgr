namespace SNMPSimMgr.Models;

/// <summary>
/// Container for all recorded data of a single device.
/// </summary>
public class DeviceData
{
    public string DeviceId { get; set; } = string.Empty;
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
    public List<SnmpRecord> WalkData { get; set; } = new();
    public List<TrafficRecord> TrafficLog { get; set; } = new();
    public List<TrapRecord> Traps { get; set; } = new();
}
