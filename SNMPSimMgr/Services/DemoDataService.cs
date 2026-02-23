using SNMPSimMgr.Models;

namespace SNMPSimMgr.Services;

public class DemoDataService
{
    private static readonly Random _random = new();
    private readonly DeviceProfileStore _store;

    public DemoDataService(DeviceProfileStore store)
    {
        _store = store;
    }

    public async Task<List<DeviceProfile>> CreateDemoDataAsync()
    {
        var devices = new List<DeviceProfile>
        {
            CreateSwitch(),
            CreateRouter(),
            CreateAccessPoint()
        };

        // Save profiles
        await _store.SaveProfilesAsync(devices);

        // Save walk data for each
        await _store.SaveWalkDataAsync(devices[0], GenerateSwitchWalkData());
        await _store.SaveWalkDataAsync(devices[1], GenerateRouterWalkData());
        await _store.SaveWalkDataAsync(devices[2], GenerateApWalkData());

        // Save sample traps
        await _store.SaveTrapsAsync(devices[0], GenerateSampleTraps("192.168.1.10"));
        await _store.SaveTrapsAsync(devices[1], GenerateSampleTraps("192.168.1.1"));

        return devices;
    }

    private static DeviceProfile CreateSwitch() => new()
    {
        Name = "Demo-Switch-Floor2",
        IpAddress = "192.168.1.10",
        Port = 161,
        Version = SnmpVersionOption.V2c,
        Community = "public"
    };

    private static DeviceProfile CreateRouter() => new()
    {
        Name = "Demo-Router-Main",
        IpAddress = "192.168.1.1",
        Port = 161,
        Version = SnmpVersionOption.V2c,
        Community = "public"
    };

    private static DeviceProfile CreateAccessPoint() => new()
    {
        Name = "Demo-AP-Lobby",
        IpAddress = "192.168.1.50",
        Port = 161,
        Version = SnmpVersionOption.V3,
        Community = "public",
        V3Credentials = new SnmpV3Credentials
        {
            Username = "admin",
            AuthProtocol = AuthProtocol.SHA,
            AuthPassword = "demo12345",
            PrivProtocol = PrivProtocol.AES,
            PrivPassword = "demo12345"
        }
    };

    private static List<SnmpRecord> GenerateSwitchWalkData()
    {
        var data = new List<SnmpRecord>();

        // System group
        data.AddRange(SystemGroup(
            "Cisco IOS Software, C2960 Software (C2960-LANBASEK9-M), Version 15.0(2)SE, RELEASE SOFTWARE",
            "1.3.6.1.4.1.9.1.716",
            "Switch-Floor2.local",
            "Server Room, Rack 3, Unit 12",
            "IT Department <it@company.com>",
            3_456_789));

        // Interfaces - 24 port switch + 2 uplinks
        for (int i = 1; i <= 26; i++)
        {
            string name = i <= 24 ? $"FastEthernet0/{i}" : $"GigabitEthernet0/{i - 24}";
            string alias = i <= 24 ? $"Port {i}" : $"Uplink {i - 24}";
            int speed = i <= 24 ? 100_000_000 : 1_000_000_000;
            int status = (i % 5 == 0) ? 2 : 1; // every 5th port is down
            long inOctets = status == 1 ? _random.Next(100_000, 50_000_000) : 0;
            long outOctets = status == 1 ? _random.Next(100_000, 50_000_000) : 0;

            data.AddRange(InterfaceEntry(i, name, alias, speed, status, inOctets, outOctets));
        }

        // IP address table
        data.Add(R("1.3.6.1.2.1.4.20.1.1.192.168.1.10", "192.168.1.10", "IpAddress"));
        data.Add(R("1.3.6.1.2.1.4.20.1.2.192.168.1.10", "1", "Integer32"));
        data.Add(R("1.3.6.1.2.1.4.20.1.3.192.168.1.10", "255.255.255.0", "IpAddress"));

        // Entity (physical inventory)
        data.Add(R("1.3.6.1.2.1.47.1.1.1.1.2.1", "WS-C2960-24TC-L", "OctetString"));
        data.Add(R("1.3.6.1.2.1.47.1.1.1.1.7.1", "WS-C2960-24TC-L", "OctetString"));
        data.Add(R("1.3.6.1.2.1.47.1.1.1.1.8.1", "V05", "OctetString"));
        data.Add(R("1.3.6.1.2.1.47.1.1.1.1.11.1", "FCW1234A5BC", "OctetString"));
        data.Add(R("1.3.6.1.2.1.47.1.1.1.1.13.1", "Cisco Systems, Inc.", "OctetString"));

        return data;
    }

    private static List<SnmpRecord> GenerateRouterWalkData()
    {
        var data = new List<SnmpRecord>();

        // System group
        data.AddRange(SystemGroup(
            "Cisco IOS Software, ISR Software (ISR4321/K9-UNIVERSALK9-M), Version 16.9.4, RELEASE SOFTWARE",
            "1.3.6.1.4.1.9.1.2497",
            "Router-Main.local",
            "Server Room, Rack 1, Unit 1",
            "Network Team <net@company.com>",
            12_345_678));

        // 4 interfaces: WAN, LAN, Management, Loopback
        string[] names = { "GigabitEthernet0/0/0", "GigabitEthernet0/0/1", "Management0/0", "Loopback0" };
        string[] aliases = { "WAN - ISP Link", "LAN - Internal", "MGMT", "Loopback" };
        int[] speeds = { 1_000_000_000, 1_000_000_000, 100_000_000, 0 };

        for (int i = 0; i < 4; i++)
        {
            data.AddRange(InterfaceEntry(i + 1, names[i], aliases[i], speeds[i], 1,
                _random.Next(1_000_000, 100_000_000),
                _random.Next(1_000_000, 100_000_000)));
        }

        // Routing table entries
        data.Add(R("1.3.6.1.2.1.4.21.1.1.0.0.0.0", "0.0.0.0", "IpAddress"));
        data.Add(R("1.3.6.1.2.1.4.21.1.7.0.0.0.0", "10.0.0.1", "IpAddress"));
        data.Add(R("1.3.6.1.2.1.4.21.1.1.192.168.1.0", "192.168.1.0", "IpAddress"));
        data.Add(R("1.3.6.1.2.1.4.21.1.7.192.168.1.0", "0.0.0.0", "IpAddress"));
        data.Add(R("1.3.6.1.2.1.4.21.1.1.10.0.0.0", "10.0.0.0", "IpAddress"));
        data.Add(R("1.3.6.1.2.1.4.21.1.7.10.0.0.0", "0.0.0.0", "IpAddress"));

        // BGP peers
        data.Add(R("1.3.6.1.2.1.15.3.1.2.10.0.0.1", "6", "Integer32")); // established
        data.Add(R("1.3.6.1.2.1.15.3.1.7.10.0.0.1", "65001", "Integer32"));
        data.Add(R("1.3.6.1.2.1.15.3.1.9.10.0.0.1", "64512", "Integer32"));

        // IP addresses
        data.Add(R("1.3.6.1.2.1.4.20.1.1.192.168.1.1", "192.168.1.1", "IpAddress"));
        data.Add(R("1.3.6.1.2.1.4.20.1.1.10.0.0.2", "10.0.0.2", "IpAddress"));

        // CPU + Memory (Cisco specific)
        data.Add(R("1.3.6.1.4.1.9.9.109.1.1.1.1.3.1", "23", "Integer32"));  // CPU 1min
        data.Add(R("1.3.6.1.4.1.9.9.109.1.1.1.1.4.1", "18", "Integer32"));  // CPU 5min
        data.Add(R("1.3.6.1.4.1.9.9.48.1.1.1.5.1", "734425088", "Integer32")); // memUsed
        data.Add(R("1.3.6.1.4.1.9.9.48.1.1.1.6.1", "314572800", "Integer32")); // memFree

        return data;
    }

    private static List<SnmpRecord> GenerateApWalkData()
    {
        var data = new List<SnmpRecord>();

        // System group
        data.AddRange(SystemGroup(
            "Ubiquiti UniFi AP-AC-Pro, Version 4.3.28.11361",
            "1.3.6.1.4.1.41112.1.6",
            "AP-Lobby.local",
            "Lobby Ceiling, above reception",
            "IT <it@company.com>",
            987_654));

        // 3 interfaces: eth0, ath0 (2.4GHz), ath1 (5GHz)
        string[] names = { "eth0", "ath0", "ath1" };
        string[] aliases = { "Ethernet", "WiFi 2.4GHz", "WiFi 5GHz" };
        int[] speeds = { 1_000_000_000, 300_000_000, 867_000_000 };

        for (int i = 0; i < 3; i++)
        {
            data.AddRange(InterfaceEntry(i + 1, names[i], aliases[i], speeds[i], 1,
                _random.Next(500_000, 20_000_000),
                _random.Next(500_000, 20_000_000)));
        }

        // Wireless clients (Ubiquiti-specific OIDs)
        data.Add(R("1.3.6.1.4.1.41112.1.6.1.2.1.1.1", "12", "Integer32"));  // connected clients 2.4GHz
        data.Add(R("1.3.6.1.4.1.41112.1.6.1.2.1.1.2", "8", "Integer32"));   // connected clients 5GHz
        data.Add(R("1.3.6.1.4.1.41112.1.6.1.2.1.2.1", "-65", "Integer32")); // avg RSSI 2.4GHz
        data.Add(R("1.3.6.1.4.1.41112.1.6.1.2.1.2.2", "-58", "Integer32")); // avg RSSI 5GHz
        data.Add(R("1.3.6.1.4.1.41112.1.6.1.2.1.3.1", "CompanyWiFi", "OctetString")); // SSID
        data.Add(R("1.3.6.1.4.1.41112.1.6.1.2.1.3.2", "CompanyWiFi-5G", "OctetString"));
        data.Add(R("1.3.6.1.4.1.41112.1.6.1.2.1.4.1", "6", "Integer32"));   // channel 2.4GHz
        data.Add(R("1.3.6.1.4.1.41112.1.6.1.2.1.4.2", "36", "Integer32"));  // channel 5GHz

        return data;
    }

    private static List<SnmpRecord> SystemGroup(
        string sysDescr, string sysOid, string sysName,
        string sysLocation, string sysContact, int uptime)
    {
        return new List<SnmpRecord>
        {
            R("1.3.6.1.2.1.1.1.0", sysDescr, "OctetString"),
            R("1.3.6.1.2.1.1.2.0", sysOid, "ObjectIdentifier"),
            R("1.3.6.1.2.1.1.3.0", uptime.ToString(), "TimeTicks"),
            R("1.3.6.1.2.1.1.4.0", sysContact, "OctetString"),
            R("1.3.6.1.2.1.1.5.0", sysName, "OctetString"),
            R("1.3.6.1.2.1.1.6.0", sysLocation, "OctetString"),
            R("1.3.6.1.2.1.1.7.0", "72", "Integer32"), // services
        };
    }

    private static List<SnmpRecord> InterfaceEntry(
        int index, string name, string alias, int speed,
        int status, long inOctets, long outOctets)
    {
        string pfx = $"1.3.6.1.2.1.2.2.1";
        string pfx2 = $"1.3.6.1.2.1.31.1.1.1";
        return new List<SnmpRecord>
        {
            R($"{pfx}.1.{index}", index.ToString(), "Integer32"),       // ifIndex
            R($"{pfx}.2.{index}", name, "OctetString"),                  // ifDescr
            R($"{pfx}.3.{index}", "6", "Integer32"),                     // ifType (ethernet)
            R($"{pfx}.4.{index}", "1500", "Integer32"),                  // ifMtu
            R($"{pfx}.5.{index}", speed.ToString(), "Gauge32"),          // ifSpeed
            R($"{pfx}.6.{index}", GenerateMac(index), "OctetString"),    // ifPhysAddress
            R($"{pfx}.7.{index}", status.ToString(), "Integer32"),       // ifAdminStatus
            R($"{pfx}.8.{index}", status.ToString(), "Integer32"),       // ifOperStatus
            R($"{pfx}.10.{index}", inOctets.ToString(), "Counter32"),    // ifInOctets
            R($"{pfx}.11.{index}", (inOctets / 100).ToString(), "Counter32"),  // ifInUcastPkts
            R($"{pfx}.14.{index}", (status == 1 ? "0" : "0"), "Counter32"),    // ifInErrors
            R($"{pfx}.16.{index}", outOctets.ToString(), "Counter32"),   // ifOutOctets
            R($"{pfx}.17.{index}", (outOctets / 100).ToString(), "Counter32"), // ifOutUcastPkts
            R($"{pfx}.20.{index}", "0", "Counter32"),                    // ifOutErrors
            R($"{pfx2}.1.{index}", name, "OctetString"),                 // ifName
            R($"{pfx2}.18.{index}", alias, "OctetString"),               // ifAlias
        };
    }

    private static List<TrapRecord> GenerateSampleTraps(string sourceIp)
    {
        return new List<TrapRecord>
        {
            new()
            {
                Timestamp = DateTime.UtcNow.AddMinutes(-30),
                Oid = "1.3.6.1.6.3.1.1.5.3", // linkDown
                SourceIp = sourceIp,
                VariableBindings = new List<SnmpRecord>
                {
                    R("1.3.6.1.2.1.2.2.1.1.5", "5", "Integer32"),
                    R("1.3.6.1.2.1.2.2.1.7.5", "2", "Integer32"),
                    R("1.3.6.1.2.1.2.2.1.8.5", "2", "Integer32"),
                }
            },
            new()
            {
                Timestamp = DateTime.UtcNow.AddMinutes(-15),
                Oid = "1.3.6.1.6.3.1.1.5.4", // linkUp
                SourceIp = sourceIp,
                VariableBindings = new List<SnmpRecord>
                {
                    R("1.3.6.1.2.1.2.2.1.1.5", "5", "Integer32"),
                    R("1.3.6.1.2.1.2.2.1.7.5", "1", "Integer32"),
                    R("1.3.6.1.2.1.2.2.1.8.5", "1", "Integer32"),
                }
            },
            new()
            {
                Timestamp = DateTime.UtcNow.AddMinutes(-5),
                Oid = "1.3.6.1.4.1.9.9.43.2.0.1", // Cisco config change
                SourceIp = sourceIp,
                VariableBindings = new List<SnmpRecord>
                {
                    R("1.3.6.1.4.1.9.9.43.1.1.6.1.3.1", "3", "Integer32"), // running
                    R("1.3.6.1.4.1.9.9.43.1.1.6.1.5.1", "admin", "OctetString"),
                }
            }
        };
    }

    private static string GenerateMac(int index)
    {
        return $"AA:BB:CC:00:00:{index:X2}";
    }

    private static SnmpRecord R(string oid, string value, string type) => new()
    {
        Oid = oid,
        Value = value,
        ValueType = type
    };
}
