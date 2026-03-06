using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace SNMPSimMgr.Models
{
    public class TrapRecord
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string Oid { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string ValueType { get; set; } = string.Empty;
        public string SourceIp { get; set; } = string.Empty;
        public string Enterprise { get; set; }
        public int GenericTrap { get; set; }
        public int SpecificTrap { get; set; }
        public List<SnmpRecord>  VariableBindings { get; set; } = new List<SnmpRecord>();
    }

    public class TrapScenario
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<TrapScenarioStep>  Steps { get; set; } = new List<TrapScenarioStep>();
    }

    public class TrapScenarioStep
    {
        public int DelayMs { get; set; }
        public TrapRecord  Trap { get; set; } = new TrapRecord();
    }
}
