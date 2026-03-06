using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace SNMPSimMgr.Models
{
    /// <summary>
    /// A scenario defines a timeline of OID value changes
    /// that can be played on a running simulator to simulate faults,
    /// load changes, interface failures, etc.
    /// </summary>
    public class SimulationScenario
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<ScenarioEvent>  Events { get; set; } = new List<ScenarioEvent>();
        public bool Loop { get; set; }
    }

    /// <summary>
    /// A single event in the scenario timeline.
    /// At the specified time offset, the given OID value is changed.
    /// </summary>
    public class ScenarioEvent
    {
        /// <summary>Seconds from scenario start when this event fires.</summary>
        public int DelaySeconds { get; set; }

        /// <summary>Descriptive label for the event.</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>OID to modify.</summary>
        public string Oid { get; set; } = string.Empty;

        /// <summary>New value to set.</summary>
        public string Value { get; set; } = string.Empty;

        /// <summary>SNMP value type (Integer32, OctetString, etc.).</summary>
        public string ValueType { get; set; } = "OctetString";
    }
}
