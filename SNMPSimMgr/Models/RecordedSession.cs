using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace SNMPSimMgr.Models
{
    /// <summary>
    /// A recorded SNMP session — a time-series of OID value changes
    /// captured by periodic walks against a real device.
    /// Can be injected into a simulator for dynamic playback.
    /// </summary>
    public class RecordedSession
    {
        public string Name { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public DateTime StartTime { get; set; }
        public int IntervalSeconds { get; set; }
        public List<RecordedFrame>  Frames { get; set; } = new List<RecordedFrame>();
    }

    /// <summary>
    /// A single snapshot frame — all OID values captured at a point in time.
    /// </summary>
    public class RecordedFrame
    {
        /// <summary>Milliseconds since recording start.</summary>
        public long ElapsedMs { get; set; }

        /// <summary>All OID values captured in this snapshot.</summary>
        public List<SnmpRecord>  Records { get; set; } = new List<SnmpRecord>();
    }
}
