using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using SNMPSimMgr.Models;

namespace SNMPSimMgr.Services
{
    public class DemoDataService
    {
        private static readonly Random  _random = new Random();
        private readonly DeviceProfileStore _store;

        public DemoDataService(DeviceProfileStore store)
        {
            _store = store;
        }

        public async Task<List<DeviceProfile>> CreateDemoDataAsync()
        {
            var devices = new List<DeviceProfile>() {
                CreateSwitch(),
                CreateRouter(),
                CreateAccessPoint(),
                CreateSuperDevice()
            };

            // Save profiles
            await _store.SaveProfilesAsync(devices);

            // Save walk data for each
            await _store.SaveWalkDataAsync(devices[0], GenerateSwitchWalkData());
            await _store.SaveWalkDataAsync(devices[1], GenerateRouterWalkData());
            await _store.SaveWalkDataAsync(devices[2], GenerateApWalkData());
            await _store.SaveWalkDataAsync(devices[3], GenerateSuperDeviceWalkData());

            // Save sample traps
            await _store.SaveTrapsAsync(devices[0], GenerateSampleTraps("192.168.1.10"));
            await _store.SaveTrapsAsync(devices[1], GenerateSampleTraps("192.168.1.1"));
            await _store.SaveTrapsAsync(devices[3], GenerateSuperDeviceTraps());

            return devices;
        }

        private static DeviceProfile CreateSwitch() => new DeviceProfile()
        {
            Name = "Demo-Switch-Floor2",
            IpAddress = "192.168.1.10",
            Port = 161,
            Version = SnmpVersionOption.V2c,
            Community = "public"
        };

        private static DeviceProfile CreateRouter() => new DeviceProfile()
        {
            Name = "Demo-Router-Main",
            IpAddress = "192.168.1.1",
            Port = 161,
            Version = SnmpVersionOption.V2c,
            Community = "public"
        };

        private static DeviceProfile CreateAccessPoint() => new DeviceProfile()
        {
            Name = "Demo-AP-Lobby",
            IpAddress = "192.168.1.50",
            Port = 161,
            Version = SnmpVersionOption.V3,
            Community = "public",
            V3Credentials = new SnmpV3Credentials() {
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

        private static DeviceProfile CreateSuperDevice()
        {
            // Resolve MIB file paths — walk up from exe dir to find MIBs/ folder
            var mibPaths = new List<string>();
            var mibDir = FindMibsDirectory();

            if (mibDir != null)
            {
                var mibFiles = new[] { "SUPER-DEVICE-MIB.txt", "SNMPv2-SMI.txt", "SNMPv2-TC.txt", "SNMPv2-CONF.txt" };
                foreach (var f in mibFiles)
                {
                    var path = Path.Combine(mibDir, f);
                    if (File.Exists(path)) mibPaths.Add(path);
                }
            }

            return new DeviceProfile
            {
                Name = "Super-Device-Lab",
                IpAddress = "10.0.0.100",
                Port = 161,
                Version = SnmpVersionOption.V2c,
                Community = "supertest",
                MibFilePaths = mibPaths
            };
        }

        /// <summary>
        /// Walk up from the exe directory to find the MIBs/ folder.
        /// Handles: bin/Debug/net472 → project → solution root.
        /// </summary>
        private static string FindMibsDirectory()
        {
            var dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 6; i++)
            {
                var candidate = Path.Combine(dir, "MIBs");
                if (Directory.Exists(candidate))
                    return candidate;
                var parent = Directory.GetParent(dir);
                if (parent == null) break;
                dir = parent.FullName;
            }
            return null;
        }

        private static List<SnmpRecord> GenerateSuperDeviceWalkData()
        {
            var data = new List<SnmpRecord>();
            const string SD = "1.3.6.1.4.1.99999"; // superDevice

            // ── sdInfo (.1) — Device Info Scalars ──
            data.Add(R($"{SD}.1.1.0", "SuperDevice-01", "OctetString"));          // sdDeviceName
            data.Add(R($"{SD}.1.2.0", "SD-9000X", "OctetString"));               // sdModel
            data.Add(R($"{SD}.1.3.0", "4.2.1-build.2847", "OctetString"));       // sdFirmwareVersion
            data.Add(R($"{SD}.1.4.0", "SD9K-2026-A1B2C3", "OctetString"));       // sdSerialNumber
            data.Add(R($"{SD}.1.5.0", "AA:BB:CC:DD:EE:01", "OctetString"));      // sdMacAddress
            data.Add(R($"{SD}.1.6.0", "8765432", "TimeTicks"));                   // sdUptime (~24h)
            data.Add(R($"{SD}.1.7.0", "Lab Admin <lab@test.local>", "OctetString")); // sdContact
            data.Add(R($"{SD}.1.8.0", "Lab Room B, Rack 5, U22", "OctetString")); // sdLocation
            data.Add(R($"{SD}.1.9.0", "10.0.0.100", "IpAddress"));               // sdManagementIp
            data.Add(R($"{SD}.1.10.0", "Super Device for comprehensive SNMP testing — all types, tables, and controls.", "OctetString")); // sdDescription

            // ── sdStatus (.2) — Status & Counters ──
            data.Add(R($"{SD}.2.1.0", "1", "Integer32"));             // sdOperState = running(1)
            data.Add(R($"{SD}.2.2.0", "37", "Gauge32"));              // sdCpuUsage = 37%
            data.Add(R($"{SD}.2.3.0", "16384", "Unsigned32"));        // sdMemoryTotal = 16GB
            data.Add(R($"{SD}.2.4.0", "11520", "Unsigned32"));        // sdMemoryUsed = ~11GB
            data.Add(R($"{SD}.2.5.0", "70", "Gauge32"));              // sdMemoryUsagePercent = 70%
            data.Add(R($"{SD}.2.6.0", "512000", "Unsigned32"));       // sdDiskTotal = 500GB
            data.Add(R($"{SD}.2.7.0", "204800", "Unsigned32"));       // sdDiskUsed = 200GB
            data.Add(R($"{SD}.2.8.0", "42", "Integer32"));            // sdTemperature = 42°C
            data.Add(R($"{SD}.2.9.0", "1", "Integer32"));             // sdPowerSupplyStatus = ok(1)
            data.Add(R($"{SD}.2.10.0", "1", "Integer32"));            // sdFanStatus = ok(1)
            data.Add(R($"{SD}.2.11.0", "2", "Integer32"));            // sdAlarmActive = false(2) TruthValue
            data.Add(R($"{SD}.2.12.0", "98765432100", "Counter64"));  // sdTotalBytesIn
            data.Add(R($"{SD}.2.13.0", "54321098700", "Counter64"));  // sdTotalBytesOut
            data.Add(R($"{SD}.2.14.0", "45678901", "Counter32"));     // sdTotalPacketsIn
            data.Add(R($"{SD}.2.15.0", "23456789", "Counter32"));     // sdTotalPacketsOut
            data.Add(R($"{SD}.2.16.0", "342", "Gauge32"));            // sdActiveConnections
            data.Add(R($"{SD}.2.17.0", "3", "Integer32"));            // sdLastRebootReason = userRequest(3)

            // ── sdConfig (.3) — Writable Config Scalars ──
            data.Add(R($"{SD}.3.1.0", "super-device", "OctetString"));       // sdHostname
            data.Add(R($"{SD}.3.2.0", "lab.test.local", "OctetString"));     // sdDomainName
            data.Add(R($"{SD}.3.3.0", "8.8.8.8", "IpAddress"));             // sdDnsServerPrimary
            data.Add(R($"{SD}.3.4.0", "8.8.4.4", "IpAddress"));             // sdDnsServerSecondary
            data.Add(R($"{SD}.3.5.0", "216.239.35.0", "IpAddress"));        // sdNtpServer
            data.Add(R($"{SD}.3.6.0", "Asia/Jerusalem", "OctetString"));     // sdTimezone
            data.Add(R($"{SD}.3.7.0", "6", "Integer32"));                    // sdLogLevel = info(6)
            data.Add(R($"{SD}.3.8.0", "3", "Integer32"));                    // sdLogDestination = syslog(3)
            data.Add(R($"{SD}.3.9.0", "10.0.0.50", "IpAddress"));           // sdSyslogServer
            data.Add(R($"{SD}.3.10.0", "1000", "Integer32"));               // sdMaxConnections
            data.Add(R($"{SD}.3.11.0", "300", "Integer32"));                 // sdSessionTimeout = 5min
            data.Add(R($"{SD}.3.12.0", "1", "Integer32"));                   // sdSnmpEnabled = true(1)
            data.Add(R($"{SD}.3.13.0", "1", "Integer32"));                   // sdSshEnabled = true(1)
            data.Add(R($"{SD}.3.14.0", "1", "Integer32"));                   // sdHttpsEnabled = true(1)
            data.Add(R($"{SD}.3.15.0", "2", "Integer32"));                   // sdTelnetEnabled = false(2)
            data.Add(R($"{SD}.3.16.0", "Welcome to SuperDevice Lab. Authorized access only.", "OctetString")); // sdBannerMotd

            // ── sdIfTable (.4.1) — 8 interfaces of various types ──
            string ifPfx = $"{SD}.4.1";
            var interfaces = new[]
            {
                //  idx, name,         descr,                  type, speed,  mtu,  admin,oper, ip,               mask,           duplex, autoNeg, vlan
                (1,  "ge0/0",    "WAN Uplink",             3,    1000,  1500, 1, 1, "10.0.0.100",     "255.255.255.0",   2, 1, 1),
                (2,  "ge0/1",    "LAN Core",               3,    1000,  9000, 1, 1, "192.168.1.1",    "255.255.255.0",   2, 1, 10),
                (3,  "ge0/2",    "Server Farm",            3,    1000,  9000, 1, 1, "192.168.10.1",   "255.255.255.0",   2, 1, 20),
                (4,  "fe0/0",    "Management",             2,    100,   1500, 1, 1, "172.16.0.1",     "255.255.255.0",   2, 1, 99),
                (5,  "te0/0",    "10G Storage Link",       4,    10000, 9216, 1, 1, "10.10.10.1",     "255.255.255.252", 2, 0, 30),
                (6,  "wlan0",    "WiFi 2.4GHz",            5,    300,   1500, 1, 1, "0.0.0.0",        "0.0.0.0",         3, 1, 1),
                (7,  "lo0",      "Loopback",               6,    0,     65535,1, 1, "127.0.0.1",      "255.0.0.0",       0, 0, 0),
                (8,  "vlan100",  "VLAN 100 - Guest",       7,    1000,  1500, 2, 2, "10.100.0.1",     "255.255.255.0",   0, 0, 100),
            };

            foreach (var iface in interfaces)
            {
                var i = iface.Item1;
                long inOctets = iface.Item8 == 1 ? _random.Next(1_000_000, 500_000_000) : 0;
                long outOctets = iface.Item8 == 1 ? _random.Next(1_000_000, 500_000_000) : 0;
                int inErrors = iface.Item8 == 1 ? _random.Next(0, 50) : 0;
                int outErrors = iface.Item8 == 1 ? _random.Next(0, 20) : 0;

                data.Add(R($"{ifPfx}.1.{i}", i.ToString(), "Integer32"));                        // sdIfIndex
                data.Add(R($"{ifPfx}.2.{i}", iface.Item2, "OctetString"));                       // sdIfName
                data.Add(R($"{ifPfx}.3.{i}", iface.Item3, "OctetString"));                       // sdIfDescr
                data.Add(R($"{ifPfx}.4.{i}", iface.Item4.ToString(), "Integer32"));               // sdIfType
                data.Add(R($"{ifPfx}.5.{i}", iface.Item5.ToString(), "Gauge32"));                 // sdIfSpeed
                data.Add(R($"{ifPfx}.6.{i}", iface.Item6.ToString(), "Integer32"));               // sdIfMtu
                data.Add(R($"{ifPfx}.7.{i}", iface.Item7.ToString(), "Integer32"));               // sdIfAdminStatus
                data.Add(R($"{ifPfx}.8.{i}", iface.Item8.ToString(), "Integer32"));               // sdIfOperStatus
                data.Add(R($"{ifPfx}.9.{i}", $"AA:BB:CC:DD:{i:X2}:01", "OctetString"));          // sdIfMacAddress
                data.Add(R($"{ifPfx}.10.{i}", iface.Item9, "IpAddress"));                         // sdIfIpAddress
                data.Add(R($"{ifPfx}.11.{i}", iface.Item10, "IpAddress"));                        // sdIfSubnetMask
                data.Add(R($"{ifPfx}.12.{i}", inOctets.ToString(), "Counter32"));                  // sdIfInOctets
                data.Add(R($"{ifPfx}.13.{i}", outOctets.ToString(), "Counter32"));                 // sdIfOutOctets
                data.Add(R($"{ifPfx}.14.{i}", inErrors.ToString(), "Counter32"));                  // sdIfInErrors
                data.Add(R($"{ifPfx}.15.{i}", outErrors.ToString(), "Counter32"));                 // sdIfOutErrors
                data.Add(R($"{ifPfx}.16.{i}", (inOctets / 80).ToString(), "Counter32"));           // sdIfInPackets
                data.Add(R($"{ifPfx}.17.{i}", (outOctets / 80).ToString(), "Counter32"));          // sdIfOutPackets
                data.Add(R($"{ifPfx}.18.{i}", iface.Item11.ToString(), "Integer32"));              // sdIfDuplex
                data.Add(R($"{ifPfx}.19.{i}", iface.Item12.ToString(), "Integer32"));              // sdIfAutoNeg (TruthValue)
                data.Add(R($"{ifPfx}.20.{i}", iface.Item13.ToString(), "Integer32"));              // sdIfVlanId
            }

            // ── sdSensorTable (.5.1) — 6 sensors ──
            string senPfx = $"{SD}.5.1";
            var sensors = new[]
            {
                //  idx, name,              type, value, units,   status, threshLow, threshHigh
                (1, "CPU Temperature",    1,    42,    "°C",    1, 0,   85),
                (2, "Inlet Temperature",  1,    28,    "°C",    1, 5,   45),
                (3, "Exhaust Temperature",1,    38,    "°C",    2, 5,   40),   // warning!
                (4, "Humidity",           2,    55,    "%RH",   1, 20,  80),
                (5, "PSU Voltage",        3,    12,    "V",     1, 11,  13),
                (6, "Fan 1 Speed",        4,    4200,  "RPM",   1, 2000,6000),
                (7, "Fan 2 Speed",        4,    4150,  "RPM",   1, 2000,6000),
                (8, "PSU Power",          5,    185,   "W",     1, 0,   500),
            };

            foreach (var s in sensors)
            {
                data.Add(R($"{senPfx}.1.{s.Item1}", s.Item1.ToString(), "Integer32"));     // sdSensorIndex
                data.Add(R($"{senPfx}.2.{s.Item1}", s.Item2, "OctetString"));              // sdSensorName
                data.Add(R($"{senPfx}.3.{s.Item1}", s.Item3.ToString(), "Integer32"));     // sdSensorType
                data.Add(R($"{senPfx}.4.{s.Item1}", s.Item4.ToString(), "Integer32"));     // sdSensorValue
                data.Add(R($"{senPfx}.5.{s.Item1}", s.Item5, "OctetString"));              // sdSensorUnits
                data.Add(R($"{senPfx}.6.{s.Item1}", s.Item6.ToString(), "Integer32"));     // sdSensorStatus
                data.Add(R($"{senPfx}.7.{s.Item1}", s.Item7.ToString(), "Integer32"));     // sdSensorThresholdLow
                data.Add(R($"{senPfx}.8.{s.Item1}", s.Item8.ToString(), "Integer32"));     // sdSensorThresholdHigh
            }

            // ── sdUserTable (.6.1) — 5 users with read-create + RowStatus ──
            string usrPfx = $"{SD}.6.1";
            var users = new[]
            {
                //  idx, name,      role, enabled, logins, lastIp
                (1, "admin",     1,    1,       523,    "10.0.0.5"),
                (2, "operator1", 2,    1,       187,    "10.0.0.12"),
                (3, "viewer",    3,    1,       45,     "192.168.1.100"),
                (4, "guest",     4,    2,       3,      "192.168.1.200"),  // disabled
                (5, "auditor",   5,    1,       12,     "172.16.0.50"),
            };

            foreach (var u in users)
            {
                data.Add(R($"{usrPfx}.1.{u.Item1}", u.Item1.ToString(), "Integer32"));     // sdUserIndex
                data.Add(R($"{usrPfx}.2.{u.Item1}", u.Item2, "OctetString"));              // sdUserName
                data.Add(R($"{usrPfx}.3.{u.Item1}", u.Item3.ToString(), "Integer32"));     // sdUserRole
                data.Add(R($"{usrPfx}.4.{u.Item1}", u.Item4.ToString(), "Integer32"));     // sdUserEnabled
                data.Add(R($"{usrPfx}.5.{u.Item1}", u.Item5.ToString(), "Counter32"));     // sdUserLoginCount
                data.Add(R($"{usrPfx}.6.{u.Item1}", u.Item6, "IpAddress"));                // sdUserLastLoginIp
                data.Add(R($"{usrPfx}.7.{u.Item1}", "1", "Integer32"));                    // sdUserRowStatus = active(1)
            }

            // ── sdVlanTable (.7.1) — 5 VLANs ──
            string vlPfx = $"{SD}.7.1";
            var vlans = new[]
            {
                //  id,  name,           status, memberPorts, taggedPorts
                (1,   "Default",       1,      "1,4,6",     ""),
                (10,  "LAN-Core",      1,      "2",         "5"),
                (20,  "ServerFarm",    1,      "3",         "5"),
                (30,  "Storage",       1,      "5",         ""),
                (99,  "Management",    1,      "4",         ""),
                (100, "Guest-WiFi",    2,      "",          "2,3"),   // suspended
            };

            foreach (var v in vlans)
            {
                data.Add(R($"{vlPfx}.1.{v.Item1}", v.Item1.ToString(), "Integer32"));      // sdVlanId
                data.Add(R($"{vlPfx}.2.{v.Item1}", v.Item2, "OctetString"));               // sdVlanName
                data.Add(R($"{vlPfx}.3.{v.Item1}", v.Item3.ToString(), "Integer32"));       // sdVlanStatus
                data.Add(R($"{vlPfx}.4.{v.Item1}", v.Item4, "OctetString"));               // sdVlanMemberPorts
                data.Add(R($"{vlPfx}.5.{v.Item1}", v.Item5, "OctetString"));               // sdVlanTaggedPorts
                data.Add(R($"{vlPfx}.6.{v.Item1}", "1", "Integer32"));                     // sdVlanRowStatus = active(1)
            }

            // ── sdDioTable (.8.1) — 8 DIO channels ──
            string dioPfx = $"{SD}.8.1";
            var dios = new[]
            {
                //  idx, name,              dir,   state, mode,  trigger, pulseW, pull, inv, alarm, counter, lastChange
                (1, "Door Sensor",       1,     1,     1,     3,       0,      1,   0,   1,     47,      123456),
                (2, "Motion Detector",   1,     0,     1,     1,       0,      1,   0,   1,     312,     234567),
                (3, "Window Contact",    1,     1,     1,     3,       0,      2,   0,   1,     5,       345678),
                (4, "Alarm Relay",       2,     0,     1,     0,       0,      0,   0,   0,     0,       456789),
                (5, "LED Indicator",     2,     1,     2,     0,       500,    0,   0,   0,     0,       567890),
                (6, "Buzzer",            2,     0,     3,     0,       200,    0,   0,   0,     0,       678901),
                (7, "Counter Input",     1,     0,     5,     1,       0,      1,   1,   0,     98765,   789012),
                (8, "Freq Analyzer",     1,     0,     6,     3,       0,      0,   0,   0,     543210,  890123),
            };

            foreach (var d in dios)
            {
                data.Add(R($"{dioPfx}.1.{d.Item1}", d.Item1.ToString(), "Integer32"));     // sdDioIndex
                data.Add(R($"{dioPfx}.2.{d.Item1}", d.Item2, "OctetString"));              // sdDioName
                data.Add(R($"{dioPfx}.3.{d.Item1}", d.Item3.ToString(), "Integer32"));     // sdDioDirection
                data.Add(R($"{dioPfx}.4.{d.Item1}", d.Item4.ToString(), "Integer32"));     // sdDioState
                data.Add(R($"{dioPfx}.5.{d.Item1}", d.Item5.ToString(), "Integer32"));     // sdDioMode
                data.Add(R($"{dioPfx}.6.{d.Item1}", d.Item6.ToString(), "Integer32"));     // sdDioTrigger
                data.Add(R($"{dioPfx}.7.{d.Item1}", d.Item7.ToString(), "Integer32"));     // sdDioPulseWidth
                data.Add(R($"{dioPfx}.8.{d.Item1}", d.Item8.ToString(), "Integer32"));     // sdDioPullResistor
                data.Add(R($"{dioPfx}.9.{d.Item1}", d.Item9 == 0 ? "2" : "1", "Integer32")); // sdDioInverted (TruthValue)
                data.Add(R($"{dioPfx}.10.{d.Item1}", d.Item10 == 0 ? "2" : "1", "Integer32")); // sdDioAlarmEnabled
                data.Add(R($"{dioPfx}.11.{d.Item1}", d.Item11.ToString(), "Counter32"));   // sdDioCounter
                data.Add(R($"{dioPfx}.12.{d.Item1}", d.Item12.ToString(), "TimeTicks"));   // sdDioLastChange
            }

            return data;
        }

        private static List<TrapRecord> GenerateSuperDeviceTraps()
        {
            const string SD = "1.3.6.1.4.1.99999";
            return new List<TrapRecord>
            {
                new TrapRecord()
                {
                    Timestamp = DateTime.UtcNow.AddHours(-2),
                    Oid = $"{SD}.10.1", // sdDeviceReboot
                    SourceIp = "10.0.0.100",
                    VariableBindings = new List<SnmpRecord>() {
                        R($"{SD}.1.1.0", "SuperDevice-01", "OctetString"),
                        R($"{SD}.2.17.0", "3", "Integer32"), // userRequest
                    }
                },
                new TrapRecord()
                {
                    Timestamp = DateTime.UtcNow.AddMinutes(-45),
                    Oid = $"{SD}.10.2", // sdTemperatureAlarm
                    SourceIp = "10.0.0.100",
                    VariableBindings = new List<SnmpRecord>() {
                        R($"{SD}.5.1.2.3", "Exhaust Temperature", "OctetString"),
                        R($"{SD}.5.1.4.3", "38", "Integer32"),
                        R($"{SD}.5.1.8.3", "40", "Integer32"),
                    }
                },
                new TrapRecord()
                {
                    Timestamp = DateTime.UtcNow.AddMinutes(-30),
                    Oid = $"{SD}.10.3", // sdInterfaceStateChange
                    SourceIp = "10.0.0.100",
                    VariableBindings = new List<SnmpRecord>() {
                        R($"{SD}.4.1.1.8", "8", "Integer32"),
                        R($"{SD}.4.1.2.8", "vlan100", "OctetString"),
                        R($"{SD}.4.1.8.8", "2", "Integer32"), // down
                    }
                },
                new TrapRecord()
                {
                    Timestamp = DateTime.UtcNow.AddMinutes(-15),
                    Oid = $"{SD}.10.4", // sdAuthenticationFailure
                    SourceIp = "10.0.0.100",
                    VariableBindings = new List<SnmpRecord>() {
                        R($"{SD}.6.1.2.4", "guest", "OctetString"),
                        R($"{SD}.6.1.6.4", "192.168.1.200", "IpAddress"),
                    }
                },
                new TrapRecord()
                {
                    Timestamp = DateTime.UtcNow.AddMinutes(-5),
                    Oid = $"{SD}.10.5", // sdConfigChanged
                    SourceIp = "10.0.0.100",
                    VariableBindings = new List<SnmpRecord>() {
                        R($"{SD}.1.1.0", "SuperDevice-01", "OctetString"),
                        R($"{SD}.6.1.2.1", "admin", "OctetString"),
                    }
                },
                new TrapRecord()
                {
                    Timestamp = DateTime.UtcNow.AddMinutes(-2),
                    Oid = $"{SD}.10.6", // sdDioTriggerEvent
                    SourceIp = "10.0.0.100",
                    VariableBindings = new List<SnmpRecord>() {
                        R($"{SD}.8.1.1.1", "1", "Integer32"),
                        R($"{SD}.8.1.2.1", "Door Sensor", "OctetString"),
                        R($"{SD}.8.1.4.1", "1", "Integer32"), // high
                        R($"{SD}.8.1.6.1", "3", "Integer32"), // bothEdges
                    }
                },
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
                new TrapRecord()
                {
                    Timestamp = DateTime.UtcNow.AddMinutes(-30),
                    Oid = "1.3.6.1.6.3.1.1.5.3", // linkDown
                    SourceIp = sourceIp,
                    VariableBindings = new List<SnmpRecord>() {
                        R("1.3.6.1.2.1.2.2.1.1.5", "5", "Integer32"),
                        R("1.3.6.1.2.1.2.2.1.7.5", "2", "Integer32"),
                        R("1.3.6.1.2.1.2.2.1.8.5", "2", "Integer32"),
                    }
                },
                new TrapRecord()
                {
                    Timestamp = DateTime.UtcNow.AddMinutes(-15),
                    Oid = "1.3.6.1.6.3.1.1.5.4", // linkUp
                    SourceIp = sourceIp,
                    VariableBindings = new List<SnmpRecord>() {
                        R("1.3.6.1.2.1.2.2.1.1.5", "5", "Integer32"),
                        R("1.3.6.1.2.1.2.2.1.7.5", "1", "Integer32"),
                        R("1.3.6.1.2.1.2.2.1.8.5", "1", "Integer32"),
                    }
                },
                new TrapRecord()
                {
                    Timestamp = DateTime.UtcNow.AddMinutes(-5),
                    Oid = "1.3.6.1.4.1.9.9.43.2.0.1", // Cisco config change
                    SourceIp = sourceIp,
                    VariableBindings = new List<SnmpRecord>() {
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

        private static SnmpRecord R(string oid, string value, string type) => new SnmpRecord()
        {
            Oid = oid,
            Value = value,
            ValueType = type
        };
    }
}
