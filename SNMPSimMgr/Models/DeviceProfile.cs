using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SNMPSimMgr.Models;

public class DeviceProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; } = 161;
    public SnmpVersionOption Version { get; set; } = SnmpVersionOption.V2c;
    public string Community { get; set; } = "public";
    public SnmpV3Credentials? V3Credentials { get; set; }
    public List<string> MibFilePaths { get; set; } = new();
    public DeviceStatus Status { get; set; } = DeviceStatus.Idle;
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
