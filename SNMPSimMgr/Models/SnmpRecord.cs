using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json.Serialization;

namespace SNMPSimMgr.Models
{
    public class SnmpRecord
    {
        public string Oid { get; set; } = string.Empty;
        public string Name { get; set; }
        public string Value { get; set; } = string.Empty;
        public string ValueType { get; set; } = string.Empty;
    }

    public class QueryResultItem
    {
        public string Time { get; set; } = string.Empty;
        public string Operation { get; set; } = string.Empty;
        public string Oid { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ValueType { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public bool IsSuccess { get; set; }
    }

    public class TrafficRecord
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public TrafficDirection Direction { get; set; }
        public SnmpOperation Operation { get; set; }
        public string Oid { get; set; } = string.Empty;
        public string RequestValue { get; set; }
        public string ResponseValue { get; set; }
        public string ResponseType { get; set; }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum TrafficDirection { Request, Response }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SnmpOperation { Get, GetNext, GetBulk, Set, Walk }

    /// <summary>
    /// Result of a safe walk — always contains collected records even if the walk ended early.
    /// </summary>
    public class WalkResult
    {
        public List<SnmpRecord> Records { get; set; } = new List<SnmpRecord>();
        /// <summary>True if the walk reached endOfMibView normally.</summary>
        public bool WalkCompleted { get; set; }
        /// <summary>Non-null if the walk ended early (timeout, stuck, error).</summary>
        public string EndReason { get; set; }
    }
}
