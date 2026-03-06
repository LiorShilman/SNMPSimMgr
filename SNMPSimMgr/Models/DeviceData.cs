using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace SNMPSimMgr.Models
{
    /// <summary>
    /// Container for all recorded data of a single device.
    /// </summary>
    public class DeviceData
    {
        public string DeviceId { get; set; } = string.Empty;
        public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
        public List<SnmpRecord>  WalkData { get; set; } = new List<SnmpRecord>();
        public List<TrafficRecord>  TrafficLog { get; set; } = new List<TrafficRecord>();
        public List<TrapRecord>  Traps { get; set; } = new List<TrapRecord>();
    }
}
