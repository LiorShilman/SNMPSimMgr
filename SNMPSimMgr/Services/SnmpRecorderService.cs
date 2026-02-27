using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SnmpSharpNet;
using SNMPSimMgr.Models;

namespace SNMPSimMgr.Services;

public class SnmpRecorderService
{
    private const int DefaultTimeout = 10000;
    private const int DefaultRetries = 2;
    // Walk uses shorter timeout so cancellation (Stop & Save) responds quickly
    // without needing to force-close the socket (which causes NRE in SnmpSharpNet)
    private const int WalkTimeout = 2000;
    private const int WalkRetries = 1;

    public event Action<string>? LogMessage;
    public event Action<int>? ProgressChanged;

    private static IPAddress ResolveAddress(string hostOrIp)
    {
        if (IPAddress.TryParse(hostOrIp, out var ip))
            return ip;

        try
        {
            var addresses = Dns.GetHostAddresses(hostOrIp);
            return addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                ?? addresses.First();
        }
        catch (SocketException)
        {
            throw new Exception($"Cannot resolve hostname '{hostOrIp}'. Check the address and your network connection.");
        }
    }

    public async Task<List<SnmpRecord>> WalkDeviceAsync(
        DeviceProfile device,
        CancellationToken ct = default)
    {
        return await Task.Run(() => WalkDevice(device, ct), ct);
    }

    private List<SnmpRecord> WalkDevice(DeviceProfile device, CancellationToken ct)
    {
        var results = new List<SnmpRecord>();
        Log($"Starting SNMP Walk on {device.Name} ({device.IpAddress})...");

        try
        {
            if (device.Version == SnmpVersionOption.V2c)
                WalkV2c(device, results, ct);
            else
                WalkV3(device, results, ct);
        }
        catch (OperationCanceledException)
        {
            Log("Walk cancelled.");
            throw;
        }
        catch (Exception ex)
        {
            Log($"Walk error: {ex.Message}");
            throw;
        }

        Log($"Walk complete. Captured {results.Count} OIDs.");
        return results;
    }

    private void WalkV2c(DeviceProfile device, List<SnmpRecord> results, CancellationToken ct)
    {
        // Use shorter timeout for walk so cancellation is responsive
        // (no socket-close callback — avoids NRE inside SnmpSharpNet)
        var target = new UdpTarget(
            ResolveAddress(device.IpAddress), device.Port, WalkTimeout, WalkRetries);

        var param = new AgentParameters(SnmpVersion.Ver2, new OctetString(device.Community));
        var rootOid = new Oid("1.3.6.1");
        var lastOid = rootOid;
        int count = 0;

        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var pdu = new Pdu(PduType.GetNext);
                pdu.VbList.Add(lastOid);

                var response = target.Request(pdu, param);

                // Check cancellation after each request completes
                ct.ThrowIfCancellationRequested();

                if (response == null || response.Pdu.ErrorStatus != 0)
                    break;

                var vb = response.Pdu.VbList[0];

                if (vb.Oid == null ||
                    vb.Value.Type == SnmpConstants.SMI_ENDOFMIBVIEW ||
                    !vb.Oid.ToString().StartsWith(rootOid.ToString()))
                    break;

                results.Add(VbToRecord(vb));

                count++;
                if (count % 100 == 0)
                {
                    Log($"  ...captured {count} OIDs (current: {vb.Oid})");
                    ProgressChanged?.Invoke(count);
                }

                lastOid = vb.Oid;
            }
        }
        finally
        {
            try { target.Close(); } catch { }
        }
    }

    private void WalkV3(DeviceProfile device, List<SnmpRecord> results, CancellationToken ct)
    {
        var target = new UdpTarget(
            ResolveAddress(device.IpAddress), device.Port, WalkTimeout, WalkRetries);

        var param = BuildV3Params(target, device);

        var rootOid = new Oid("1.3.6.1");
        var lastOid = rootOid;
        int count = 0;

        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var pdu = new ScopedPdu(PduType.GetNext);
                pdu.VbList.Add(lastOid);

                var response = target.Request(pdu, param);

                ct.ThrowIfCancellationRequested();

                if (response == null || response.Pdu.ErrorStatus != 0)
                    break;

                var vb = response.Pdu.VbList[0];

                if (vb.Oid == null ||
                    vb.Value.Type == SnmpConstants.SMI_ENDOFMIBVIEW ||
                    !vb.Oid.ToString().StartsWith(rootOid.ToString()))
                    break;

                results.Add(VbToRecord(vb));

                count++;
                if (count % 100 == 0)
                {
                    Log($"  ...captured {count} OIDs (current: {vb.Oid})");
                    ProgressChanged?.Invoke(count);
                }

                lastOid = vb.Oid;
            }
        }
        finally
        {
            try { target.Close(); } catch { }
        }
    }

    public async Task<SnmpRecord?> GetSingleAsync(DeviceProfile device, string oid)
    {
        return await Task.Run(() =>
        {
            var target = new UdpTarget(
                ResolveAddress(device.IpAddress), device.Port, DefaultTimeout, DefaultRetries);

            try
            {
                if (device.Version == SnmpVersionOption.V3)
                    return GetV3(target, device, oid);

                return GetV2c(target, device, oid);
            }
            finally
            {
                target.Close();
            }
        });
    }

    public async Task<List<SnmpRecord>> GetMultipleAsync(DeviceProfile device, IEnumerable<string> oids)
    {
        return await Task.Run(() =>
        {
            var target = new UdpTarget(
                ResolveAddress(device.IpAddress), device.Port, DefaultTimeout, DefaultRetries);

            try
            {
                if (device.Version == SnmpVersionOption.V3)
                    return GetMultipleV3(target, device, oids);

                return GetMultipleV2c(target, device, oids);
            }
            finally
            {
                target.Close();
            }
        });
    }

    private SnmpRecord? GetV2c(UdpTarget target, DeviceProfile device, string oid)
    {
        var param = new AgentParameters(
            SnmpVersion.Ver2, new OctetString(device.Community));

        var pdu = new Pdu(PduType.Get);
        pdu.VbList.Add(new Oid(oid));

        var response = target.Request(pdu, param);
        if (response == null || response.Pdu.ErrorStatus != 0)
            return null;

        return VbToRecord(response.Pdu.VbList[0]);
    }

    private SnmpRecord? GetV3(UdpTarget target, DeviceProfile device, string oid)
    {
        var param = BuildV3Params(target, device);

        var pdu = new ScopedPdu(PduType.Get);
        pdu.VbList.Add(new Oid(oid));

        var response = target.Request(pdu, param);
        if (response == null || response.Pdu.ErrorStatus != 0)
            return null;

        return VbToRecord(response.Pdu.VbList[0]);
    }

    private List<SnmpRecord> GetMultipleV2c(UdpTarget target, DeviceProfile device, IEnumerable<string> oids)
    {
        var param = new AgentParameters(
            SnmpVersion.Ver2, new OctetString(device.Community));

        var pdu = new Pdu(PduType.Get);
        foreach (var oid in oids)
            pdu.VbList.Add(new Oid(oid));

        var response = target.Request(pdu, param);
        if (response == null || response.Pdu.ErrorStatus != 0)
            return new List<SnmpRecord>();

        return response.Pdu.VbList.Cast<Vb>().Select(VbToRecord).ToList();
    }

    private List<SnmpRecord> GetMultipleV3(UdpTarget target, DeviceProfile device, IEnumerable<string> oids)
    {
        var param = BuildV3Params(target, device);

        var pdu = new ScopedPdu(PduType.Get);
        foreach (var oid in oids)
            pdu.VbList.Add(new Oid(oid));

        var response = target.Request(pdu, param);
        if (response == null || response.Pdu.ErrorStatus != 0)
            return new List<SnmpRecord>();

        return response.Pdu.VbList.Cast<Vb>().Select(VbToRecord).ToList();
    }

    public async Task<bool> SetAsync(DeviceProfile device, string oid, string value, string valueType)
    {
        return await Task.Run(() =>
        {
            var target = new UdpTarget(
                ResolveAddress(device.IpAddress), device.Port, DefaultTimeout, DefaultRetries);

            try
            {
                if (device.Version == SnmpVersionOption.V3)
                    return SetV3(target, device, oid, value, valueType);

                return SetV2c(target, device, oid, value, valueType);
            }
            finally
            {
                target.Close();
            }
        });
    }

    private bool SetV2c(UdpTarget target, DeviceProfile device, string oid, string value, string valueType)
    {
        var param = new AgentParameters(
            SnmpVersion.Ver2, new OctetString(device.Community));

        var pdu = new Pdu(PduType.Set);
        var asnType = SnmpTypeHelper.CreateValue(SnmpTypeHelper.StringToType(valueType), value);
        pdu.VbList.Add(new Oid(oid), asnType);

        var response = target.Request(pdu, param);
        return response != null && response.Pdu.ErrorStatus == 0;
    }

    private bool SetV3(UdpTarget target, DeviceProfile device, string oid, string value, string valueType)
    {
        var param = BuildV3Params(target, device);

        var pdu = new ScopedPdu(PduType.Set);
        var asnType = SnmpTypeHelper.CreateValue(SnmpTypeHelper.StringToType(valueType), value);
        pdu.VbList.Add(new Oid(oid), asnType);

        var response = target.Request(pdu, param);
        return response != null && response.Pdu.ErrorStatus == 0;
    }

    private SecureAgentParameters BuildV3Params(UdpTarget target, DeviceProfile device)
    {
        var creds = device.V3Credentials
            ?? throw new InvalidOperationException("SNMPv3 requires credentials.");

        var param = new SecureAgentParameters();

        if (!target.Discovery(param))
            throw new Exception("SNMPv3 discovery failed.");

        var authProto = creds.AuthProtocol == Models.AuthProtocol.MD5
            ? AuthenticationDigests.MD5
            : AuthenticationDigests.SHA1;

        var privProto = creds.PrivProtocol == Models.PrivProtocol.DES
            ? PrivacyProtocols.DES
            : PrivacyProtocols.AES128;

        if (!string.IsNullOrEmpty(creds.PrivPassword))
            param.authPriv(creds.Username, authProto, creds.AuthPassword,
                          privProto, creds.PrivPassword);
        else
            param.authNoPriv(creds.Username, authProto, creds.AuthPassword);

        return param;
    }

    private static SnmpRecord VbToRecord(Vb vb)
    {
        return new SnmpRecord
        {
            Oid = vb.Oid.ToString(),
            Value = vb.Value?.ToString() ?? string.Empty,
            ValueType = SnmpTypeToString(vb.Value?.Type ?? 0)
        };
    }

    private static string SnmpTypeToString(byte type) => SnmpTypeHelper.TypeToString(type);

    private void Log(string msg) => LogMessage?.Invoke(msg);
}
