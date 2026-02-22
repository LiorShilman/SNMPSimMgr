using System.Net;
using System.Net.Sockets;
using SnmpSharpNet;
using SNMPSimMgr.Models;

namespace SNMPSimMgr.Services;

/// <summary>
/// Scans a subnet range for SNMP-enabled devices by sending sysDescr GET to each IP.
/// </summary>
public class DiscoveryService
{
    private const int ScanTimeout = 1500;  // ms per IP
    private const int ScanRetries = 0;     // no retries for speed
    private const string SysDescrOid = "1.3.6.1.2.1.1.1.0";
    private const string SysNameOid = "1.3.6.1.2.1.1.5.0";

    public event Action<string>? LogMessage;
    public event Action<int, int>? ProgressChanged;

    /// <summary>
    /// Scan a range of IPs for SNMP devices.
    /// </summary>
    public async Task<List<DiscoveredDevice>> ScanRangeAsync(
        string baseIp, int startHost, int endHost,
        string community = "public", int port = 161,
        CancellationToken ct = default)
    {
        var results = new List<DiscoveredDevice>();
        var parts = baseIp.Split('.');
        if (parts.Length != 4)
            throw new ArgumentException("Invalid base IP format. Use x.x.x.x");

        var prefix = $"{parts[0]}.{parts[1]}.{parts[2]}";
        int total = endHost - startHost + 1;
        int current = 0;

        Log($"Scanning {prefix}.{startHost}-{endHost} ({total} addresses)...");

        // Scan in parallel batches for speed
        var semaphore = new SemaphoreSlim(20); // 20 concurrent scans
        var tasks = new List<Task>();

        for (int i = startHost; i <= endHost; i++)
        {
            ct.ThrowIfCancellationRequested();
            var ip = $"{prefix}.{i}";

            tasks.Add(Task.Run(async () =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    var result = ProbeDevice(ip, port, community);
                    if (result != null)
                    {
                        lock (results) results.Add(result);
                        Log($"  Found: {ip} — {result.SysDescr}");
                    }
                }
                finally
                {
                    semaphore.Release();
                    var done = Interlocked.Increment(ref current);
                    ProgressChanged?.Invoke(done, total);
                }
            }, ct));
        }

        await Task.WhenAll(tasks);
        Log($"Scan complete — {results.Count} devices found.");
        return results.OrderBy(d => d.IpAddress).ToList();
    }

    private DiscoveredDevice? ProbeDevice(string ip, int port, string community)
    {
        try
        {
            var target = new UdpTarget(
                IPAddress.Parse(ip), port, ScanTimeout, ScanRetries);

            try
            {
                var param = new AgentParameters(new OctetString(community))
                {
                    Version = SnmpVersion.Ver2
                };

                var pdu = new Pdu(PduType.Get);
                pdu.VbList.Add(SysDescrOid);
                pdu.VbList.Add(SysNameOid);

                var result = target.Request(pdu, param);
                if (result == null || result.Pdu.ErrorStatus != 0)
                    return null;

                var sysDescr = result.Pdu.VbList[0].Value?.ToString() ?? "";
                var sysName = result.Pdu.VbList[1].Value?.ToString() ?? "";

                if (string.IsNullOrEmpty(sysDescr) && string.IsNullOrEmpty(sysName))
                    return null;

                return new DiscoveredDevice
                {
                    IpAddress = ip,
                    Port = port,
                    SysName = sysName,
                    SysDescr = sysDescr,
                    Community = community
                };
            }
            finally
            {
                target.Close();
            }
        }
        catch
        {
            return null; // unreachable or not SNMP
        }
    }

    private void Log(string msg) => LogMessage?.Invoke(msg);
}

public class DiscoveredDevice
{
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; } = 161;
    public string SysName { get; set; } = string.Empty;
    public string SysDescr { get; set; } = string.Empty;
    public string Community { get; set; } = "public";
    public bool IsSelected { get; set; } = true;
}
