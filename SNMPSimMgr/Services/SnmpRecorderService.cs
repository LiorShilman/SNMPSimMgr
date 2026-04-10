using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SnmpSharpNet;
using SNMPSimMgr.Models;

namespace SNMPSimMgr.Services
{
    public class SnmpRecorderService
    {
        private const int DefaultTimeout = 10000;
        private const int DefaultRetries = 2;
        // Walk uses shorter timeout so cancellation (Stop & Save) responds quickly
        // without needing to force-close the socket (which causes NRE in SnmpSharpNet)
        private const int WalkTimeout = 2000;
        private const int WalkRetries = 1;
        private const int MaxConsecutiveSkips = 10;
        private const int StaleWalkTimeoutMs = 30000; // 30s without new OID → walk is stuck
        private const int MaxWalkDurationMs = 300000; // 5 min hard cap per walk

        /// <summary>
        /// Active simulator endpoints: device ID → (listenIp, port).
        /// When a device is simulating, GET/SET/WALK should be routed here instead of the real device address.
        /// </summary>
        public static ConcurrentDictionary<string, (string Ip, int Port)>  SimulatorEndpoints { get; } = new ConcurrentDictionary<string, (string Ip, int Port)>();

        /// <summary>
        /// Returns a DeviceProfile pointing to the simulator if one is active, otherwise the original device.
        /// </summary>
        public static DeviceProfile ResolveTarget(DeviceProfile device)
        {
            if (SimulatorEndpoints.TryGetValue(device.Id, out var ep))
            {
                return new DeviceProfile
                {
                    Id = device.Id,
                    Name = device.Name,
                    IpAddress = ep.Ip == "0.0.0.0" ? "127.0.0.1" : ep.Ip,
                    Port = ep.Port,
                    Version = device.Version,
                    Community = device.Community,
                    V3Credentials = device.V3Credentials
                };
            }
            return device;
        }

        public event Action<string> LogMessage;
        public event Action<int> ProgressChanged;

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
            return await Task.Run(() => WalkDevice(device, "1.3.6.1", ct), ct);
        }

        public async Task<List<SnmpRecord>> WalkSubtreeAsync(
            DeviceProfile device,
            string rootOid,
            CancellationToken ct = default)
        {
            return await Task.Run(() => WalkDevice(device, rootOid, ct), ct);
        }

        /// <summary>
        /// Walk multiple subtrees and return combined results.
        /// Each OID in the list is walked as a subtree (supports tables with not-accessible parent OIDs).
        /// Much faster than a full walk when you only need specific branches.
        /// </summary>
        public async Task<List<SnmpRecord>> WalkSubtreesAsync(
            DeviceProfile device,
            IEnumerable<string> rootOids,
            CancellationToken ct = default)
        {
            var allResults = new List<SnmpRecord>();
            var oidList = rootOids.ToList();

            Log($"Selective walk on {device.Name}: {oidList.Count} subtree(s)");

            for (int i = 0; i < oidList.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var results = await Task.Run(() => WalkDevice(device, oidList[i], ct), ct);
                allResults.AddRange(results);
                Log($"  Subtree {i + 1}/{oidList.Count} ({oidList[i]}): {results.Count} OIDs");
            }

            Log($"Selective walk complete: {allResults.Count} total OIDs from {oidList.Count} subtree(s)");
            return allResults;
        }

        /// <summary>
        /// Perform a walk that always returns collected results — even on timeout, stuck, or cancellation.
        /// WalkCompleted is false if the walk ended prematurely.
        /// </summary>
        public async Task<WalkResult> WalkDeviceSafeAsync(
            DeviceProfile device,
            CancellationToken ct = default)
        {
            return await Task.Run(() => WalkDeviceSafe(device, "1.3.6.1", ct), ct).ConfigureAwait(false);
        }

        private WalkResult WalkDeviceSafe(DeviceProfile device, string rootOid, CancellationToken ct)
        {
            var results = new List<SnmpRecord>();
            bool completed = false;
            string endReason = null;

            Log($"Starting SNMP Walk on {device.Name} ({device.IpAddress}) from {rootOid}...");

            try
            {
                // Hard cap: create a linked CTS that fires after MaxWalkDurationMs
                using (var walkTimeCts = new CancellationTokenSource(MaxWalkDurationMs))
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, walkTimeCts.Token))
                {
                    if (device.Version == SnmpVersionOption.V2c)
                        WalkV2c(device, rootOid, results, linked.Token);
                    else
                        WalkV3(device, rootOid, results, linked.Token);
                }

                completed = true;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Walk timeout (hard cap) — not user cancellation
                endReason = $"Walk timed out after {MaxWalkDurationMs / 1000}s";
                Log($"  {endReason} — saving {results.Count} OIDs collected so far");
            }
            catch (OperationCanceledException)
            {
                // User cancellation — still return partial results
                endReason = "Walk cancelled by user";
                Log($"  {endReason} — saving {results.Count} OIDs collected so far");
            }
            catch (Exception ex)
            {
                endReason = $"Walk error: {ex.Message}";
                Log($"  {endReason} — saving {results.Count} OIDs collected so far");
            }

            Log(completed
                ? $"Walk complete. Captured {results.Count} OIDs."
                : $"Walk ended early ({endReason}). Captured {results.Count} OIDs.");

            return new WalkResult
            {
                Records = results,
                WalkCompleted = completed,
                EndReason = endReason
            };
        }

        private List<SnmpRecord> WalkDevice(DeviceProfile device, string rootOid, CancellationToken ct)
        {
            // Legacy method — delegates to safe walk and throws on user cancellation
            var result = WalkDeviceSafe(device, rootOid, ct);
            if (!result.WalkCompleted && ct.IsCancellationRequested)
                throw new OperationCanceledException();
            return result.Records;
        }

        private void WalkV2c(DeviceProfile device, string rootOidStr, List<SnmpRecord> results, CancellationToken ct)
        {
            // Use shorter timeout for walk so cancellation is responsive
            // (no socket-close callback — avoids NRE inside SnmpSharpNet)
            var target = new UdpTarget(
                ResolveAddress(device.IpAddress), device.Port, WalkTimeout, WalkRetries);

            var param = new AgentParameters(SnmpVersion.Ver2, new OctetString(device.Community));
            var rootOid = new Oid(rootOidStr);
            var lastOid = rootOid;
            int count = 0;
            int consecutiveSkips = 0;
            int sameOidCount = 0;
            var staleSw = System.Diagnostics.Stopwatch.StartNew(); // time since last NEW oid

            try
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    // Stale walk detection — no new OID for too long
                    if (staleSw.ElapsedMilliseconds > StaleWalkTimeoutMs)
                    {
                        Log($"  No new OID for {StaleWalkTimeoutMs / 1000}s — walk appears stalled, ending");
                        break;
                    }

                    var pdu = new Pdu(PduType.GetNext);
                    pdu.VbList.Add(lastOid);

                    SnmpV2Packet response = null;
                    try
                    {
                        response = (SnmpV2Packet)target.Request(pdu, param);
                    }
                    catch (Exception ex)
                    {
                        Log($"  Request error at {lastOid}: {ex.Message}");
                    }

                    ct.ThrowIfCancellationRequested();

                    if (response == null || response.Pdu.ErrorStatus != 0)
                    {
                        // Timeout or error — try to skip to the next branch
                        var nextOid = SkipToNextBranch(lastOid, rootOid);
                        if (nextOid == null || ++consecutiveSkips > MaxConsecutiveSkips)
                        {
                            Log(consecutiveSkips > MaxConsecutiveSkips
                                ? $"  Too many consecutive skips ({MaxConsecutiveSkips}), ending walk"
                                : $"  No more branches to skip to, ending walk");
                            break;
                        }
                        Log($"  Timeout at {lastOid}, skipping to {nextOid}");
                        lastOid = nextOid;
                        continue;
                    }

                    consecutiveSkips = 0; // reset on success
                    var vb = response.Pdu.VbList[0];

                    if (vb.Oid == null ||
                        vb.Value.Type == SnmpConstants.SMI_ENDOFMIBVIEW ||
                        !vb.Oid.ToString().StartsWith(rootOid.ToString()))
                        break;

                    // Detect stuck OID — device returns same OID repeatedly
                    if (vb.Oid.ToString() == lastOid.ToString())
                    {
                        sameOidCount++;
                        if (sameOidCount > 3)
                        {
                            Log($"  Stuck at OID {lastOid} ({sameOidCount} repeats), skipping branch");
                            var nextOid = SkipToNextBranch(lastOid, rootOid);
                            if (nextOid == null || ++consecutiveSkips > MaxConsecutiveSkips)
                            {
                                Log($"  No more branches to skip to, ending walk");
                                break;
                            }
                            lastOid = nextOid;
                            sameOidCount = 0;
                            continue;
                        }
                        continue;
                    }
                    sameOidCount = 0;
                    staleSw.Restart(); // got a new OID — reset stale timer

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

        private void WalkV3(DeviceProfile device, string rootOidStr, List<SnmpRecord> results, CancellationToken ct)
        {
            var target = new UdpTarget(
                ResolveAddress(device.IpAddress), device.Port, WalkTimeout, WalkRetries);

            var param = BuildV3Params(target, device);

            var rootOid = new Oid(rootOidStr);
            var lastOid = rootOid;
            int count = 0;
            int consecutiveSkips = 0;
            int sameOidCount = 0;
            var staleSw = System.Diagnostics.Stopwatch.StartNew(); // time since last NEW oid

            try
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested();

                    // Stale walk detection — no new OID for too long
                    if (staleSw.ElapsedMilliseconds > StaleWalkTimeoutMs)
                    {
                        Log($"  No new OID for {StaleWalkTimeoutMs / 1000}s — walk appears stalled, ending");
                        break;
                    }

                    var pdu = new ScopedPdu(PduType.GetNext);
                    pdu.VbList.Add(lastOid);

                    SnmpV3Packet response = null;
                    try
                    {
                        response = (SnmpV3Packet)target.Request(pdu, param);
                    }
                    catch (Exception ex)
                    {
                        Log($"  Request error at {lastOid}: {ex.Message}");
                    }

                    ct.ThrowIfCancellationRequested();

                    if (response == null || response.Pdu.ErrorStatus != 0)
                    {
                        var nextOid = SkipToNextBranch(lastOid, rootOid);
                        if (nextOid == null || ++consecutiveSkips > MaxConsecutiveSkips)
                        {
                            Log(consecutiveSkips > MaxConsecutiveSkips
                                ? $"  Too many consecutive skips ({MaxConsecutiveSkips}), ending walk"
                                : $"  No more branches to skip to, ending walk");
                            break;
                        }
                        Log($"  Timeout at {lastOid}, skipping to {nextOid}");
                        lastOid = nextOid;
                        continue;
                    }

                    consecutiveSkips = 0;
                    var vb = response.Pdu.VbList[0];

                    if (vb.Oid == null ||
                        vb.Value.Type == SnmpConstants.SMI_ENDOFMIBVIEW ||
                        !vb.Oid.ToString().StartsWith(rootOid.ToString()))
                        break;

                    // Detect stuck OID — device returns same OID repeatedly
                    if (vb.Oid.ToString() == lastOid.ToString())
                    {
                        sameOidCount++;
                        if (sameOidCount > 3)
                        {
                            Log($"  Stuck at OID {lastOid} ({sameOidCount} repeats), skipping branch");
                            var nextOid = SkipToNextBranch(lastOid, rootOid);
                            if (nextOid == null || ++consecutiveSkips > MaxConsecutiveSkips)
                            {
                                Log($"  No more branches to skip to, ending walk");
                                break;
                            }
                            lastOid = nextOid;
                            sameOidCount = 0;
                            continue;
                        }
                        continue;
                    }
                    sameOidCount = 0;
                    staleSw.Restart(); // got a new OID — reset stale timer

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

        /// <summary>
        /// When a GETNEXT times out at a given OID, skip to the next sibling branch.
        /// For example: 1.3.6.1.4.1.2544.1.12.13.1.2.3 → tries 1.3.6.1.4.1.2544.1.12.14
        /// by progressively stripping the last component and incrementing,
        /// until we find a branch still under rootOid.
        /// </summary>
        private Oid SkipToNextBranch(Oid stuckOid, Oid rootOid)
        {
            var parts = stuckOid.ToString().Split('.');
            var rootLen = rootOid.ToString().Split('.').Length;

            // Try progressively shorter OIDs: strip last component, increment
            for (int len = parts.Length - 1; len > rootLen; len--)
            {
                var parentParts = new string[len];
                Array.Copy(parts, parentParts, len);

                // Increment last component to jump to next sibling branch
                if (uint.TryParse(parentParts[len - 1], out uint last))
                {
                    parentParts[len - 1] = (last + 1).ToString();
                    return new Oid(string.Join(".", parentParts));
                }
            }

            return null; // No more branches under rootOid
        }

        public async Task<SnmpRecord> GetSingleAsync(DeviceProfile device, string oid)
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

        private SnmpRecord GetV2c(UdpTarget target, DeviceProfile device, string oid)
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

        private SnmpRecord GetV3(UdpTarget target, DeviceProfile device, string oid)
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
                Value = vb.Value.ToString() ?? string.Empty,
                ValueType = SnmpTypeToString(vb.Value.Type)
            };
        }

        private static string SnmpTypeToString(byte type) => SnmpTypeHelper.TypeToString(type);

        private void Log(string msg) => LogMessage.Invoke(msg);
    }
}
