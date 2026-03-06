using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
namespace SNMPSimMgr.Models
{
    public class TrafficEntry
    {
        public string Time { get; set; } = string.Empty;
        public string SourceIp { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public string Operation { get; set; } = string.Empty;
        public string Oid { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}
