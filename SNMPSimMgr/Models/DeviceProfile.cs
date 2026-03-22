using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using SNMPSimMgr.Services;

namespace SNMPSimMgr.Models
{
    public class DeviceProfile : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private string _id = Guid.NewGuid().ToString("N");
        private string _name = string.Empty;
        private string _ipAddress = string.Empty;
        private int _port = 161;
        private SnmpVersionOption _version = SnmpVersionOption.V2c;
        private string _community = "public";
        private SnmpV3Credentials _v3Credentials;
        private List<string> _mibFilePaths = new List<string>();
        private DeviceStatus _status = DeviceStatus.Idle;
        private string _schemaPath;
        private List<IddFieldDef> _iddFields;

        public string Id { get => _id; set { if (_id != value) { _id = value; OnPropertyChanged(); } } }
        public string Name { get => _name; set { if (_name != value) { _name = value; OnPropertyChanged(); } } }
        public string IpAddress { get => _ipAddress; set { if (_ipAddress != value) { _ipAddress = value; OnPropertyChanged(); } } }
        public int Port { get => _port; set { if (_port != value) { _port = value; OnPropertyChanged(); } } }
        public SnmpVersionOption Version { get => _version; set { if (_version != value) { _version = value; OnPropertyChanged(); } } }
        public string Community { get => _community; set { if (_community != value) { _community = value; OnPropertyChanged(); } } }
        public SnmpV3Credentials V3Credentials { get => _v3Credentials; set { if (_v3Credentials != value) { _v3Credentials = value; OnPropertyChanged(); } } }
        public List<string> MibFilePaths { get => _mibFilePaths; set { if (_mibFilePaths != value) { _mibFilePaths = value; OnPropertyChanged(); } } }
        public DeviceStatus Status { get => _status; set { if (_status != value) { _status = value; OnPropertyChanged(); } } }

        /// <summary>
        /// Optional path to a pre-built MibPanelSchema JSON file.
        /// When set, RequestSchema loads this file instead of parsing MIB files.
        /// Exported from MIB Browser (SNMP) or IddPanelBuilderService (IDD).
        /// </summary>
        public string SchemaPath { get => _schemaPath; set { if (_schemaPath != value) { _schemaPath = value; OnPropertyChanged(); } } }

        /// <summary>
        /// Optional IDD (non-SNMP) field definitions. When populated, the device
        /// is treated as an IDD device and uses IddPanelBuilderService for schema.
        /// </summary>
        public List<IddFieldDef> IddFields { get => _iddFields; set { if (_iddFields != value) { _iddFields = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsIddDevice)); } } }

        /// <summary>True if this device uses IDD fields instead of SNMP MIBs.</summary>
        [JsonIgnore]
        public bool IsIddDevice => IddFields != null && IddFields.Count > 0;
    }

    public class SnmpV3Credentials
    {
        public string Username { get; set; } = string.Empty;
        public AuthProtocol AuthProtocol { get; set; } = AuthProtocol.MD5;
        public string AuthPassword { get; set; } = string.Empty;
        public PrivProtocol PrivProtocol { get; set; } = PrivProtocol.DES;
        public string PrivPassword { get; set; } = string.Empty;
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum SnmpVersionOption { V2c, V3 }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum AuthProtocol { MD5, SHA }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum PrivProtocol { DES, AES }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum DeviceStatus { Idle, Recording, Simulating }
}
