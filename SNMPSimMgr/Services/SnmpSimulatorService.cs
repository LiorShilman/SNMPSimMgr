using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using SnmpSharpNet;
using SNMPSimMgr.Models;

namespace SNMPSimMgr.Services
{
    public class SnmpSimulatorService : IDisposable
    {
        private UdpClient _listener;
        private CancellationTokenSource _cts;
        private CancellationTokenSource _injectionCts;
        private Task _listenTask;

        private SortedDictionary<string, SnmpRecord> _mibData = new SortedDictionary<string, SnmpRecord>(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, string>  _dynamicValues = new ConcurrentDictionary<string, string>();

        // V3 support
        private SnmpV3Credentials _v3Credentials;
        private OctetString _engineId;
        private int _engineBoots = 1;
        private int _engineTime = 0;

        public event Action<string> LogMessage;
        public event Action<string, string, string, string> RequestReceived; // op, oid, val, sourceIp
        public event Action<int, int> InjectionProgress; // currentFrame, totalFrames
        public event Action<int, List<SnmpRecord>> InjectionFrameApplied; // frameIndex, records

        public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;
        public bool IsInjecting => _injectionCts != null && !_injectionCts.IsCancellationRequested;
        public int Port { get; private set; }
        public string DeviceName { get; private set; } = string.Empty;

        public void LoadMibData(List<SnmpRecord> walkData, string deviceName)
        {
            DeviceName = deviceName;
            _mibData = new SortedDictionary<string, SnmpRecord>(StringComparer.Ordinal);
            foreach (var record in walkData)
                _mibData[record.Oid] = record;
            Log($"Loaded {_mibData.Count} OIDs for device '{deviceName}'.");
        }

        public void Start(int port, string community = "public", string listenIp = "0.0.0.0",
            SnmpV3Credentials v3Credentials = null)
        {
            if (IsRunning) return;

            Port = port;
            _v3Credentials = v3Credentials;

            // Generate a unique engine ID for V3
            if (_v3Credentials != null)
            {
                var idBytes = new byte[12];
                idBytes[0] = 0x80; // enterprise format
                var rng = new Random();
                rng.NextBytes(idBytes);
                _engineId = new OctetString(idBytes);
                _engineBoots = 1;
                _engineTime = 0;
                Log($"Simulator '{DeviceName}' V3 enabled (user: {_v3Credentials.Username})");
            }

            var ip = IPAddress.Parse(listenIp);
            _cts = new CancellationTokenSource();
            _listener = new UdpClient(new IPEndPoint(ip, port));
            Log($"Simulator '{DeviceName}' listening on {listenIp}:{port}...");
            _listenTask = Task.Run(() => ListenLoop(community, _cts.Token));
        }

        public void Stop()
        {
            if (!IsRunning) return;

            _cts?.Cancel();
            _listener?.Close();
            _listener?.Dispose();
            _listener = null;
            _cts = null;
            Log($"Simulator '{DeviceName}' stopped.");
        }

        public void SetValue(string oid, string value)
        {
            _dynamicValues[oid] = value;
            Log($"SET {oid} = {value}");
        }

        private string GetValue(string oid)
        {
            if (_dynamicValues.TryGetValue(oid, out var dynVal))
                return dynVal;
            if (_mibData.TryGetValue(oid, out var record))
                return record.Value;
            return string.Empty;
        }

        private byte GetValueType(string oid)
        {
            if (_mibData.TryGetValue(oid, out var record))
                return SnmpTypeHelper.StringToType(record.ValueType);
            return SnmpTypeHelper.OctetString;
        }

        private async Task ListenLoop(string community, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var receiveTask = _listener!.ReceiveAsync();
                    var cancelTask = Task.Delay(Timeout.Infinite, ct);
                    var completed = await Task.WhenAny(receiveTask, cancelTask);
                    if (completed == cancelTask) break;
                    var result = receiveTask.Result;
                    _ = Task.Run(() => HandleRequest(result.Buffer, result.RemoteEndPoint, community));
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex)
                {
                    Log($"Error receiving: {ex.Message}");
                }
            }
        }

        private void HandleRequest(byte[] data, IPEndPoint remoteEp, string expectedCommunity)
        {
            try
            {
                int ver = SnmpPacket.GetProtocolVersion(data, data.Length);

                if (ver == (int)SnmpVersion.Ver2)
                    HandleV2cRequest(data, remoteEp, expectedCommunity);
                else if (ver == (int)SnmpVersion.Ver3 && _v3Credentials != null)
                    HandleV3Request(data, remoteEp);
                else
                    Log($"Unsupported SNMP version {ver} from {remoteEp}");
            }
            catch (Exception ex)
            {
                Log($"Error handling request from {remoteEp}: {ex.Message}");
            }
        }

        private void HandleV2cRequest(byte[] data, IPEndPoint remoteEp, string expectedCommunity)
        {
            var request = new SnmpV2Packet();
            request.decode(data, data.Length);

            if (request.Community.ToString() != expectedCommunity)
            {
                Log($"Bad community string from {remoteEp}");
                return;
            }

            var response = new SnmpV2Packet();
            response.Community.Set(expectedCommunity);
            response.Pdu.RequestId = request.Pdu.RequestId;

            var sourceIp = remoteEp.Address.ToString();

            switch (request.Pdu.Type)
            {
                case PduType.Get:
                    HandleGet(request.Pdu, response.Pdu, sourceIp);
                    break;
                case PduType.GetNext:
                    HandleGetNext(request.Pdu, response.Pdu, sourceIp);
                    break;
                case PduType.GetBulk:
                    HandleGetBulk(request.Pdu, response.Pdu, sourceIp);
                    break;
                case PduType.Set:
                    HandleSet(request.Pdu, response.Pdu, sourceIp);
                    break;
                default:
                    Log($"Unsupported PDU type: {request.Pdu.Type}");
                    return;
            }

            response.Pdu.Type = PduType.Response;
            var responseBytes = response.encode();
            _listener?.Send(responseBytes, responseBytes.Length, remoteEp);
        }

        private void HandleV3Request(byte[] data, IPEndPoint remoteEp)
        {
            // Decode as noAuthNoPriv first to read USM headers
            var request = new SnmpV3Packet();
            request.decode(data, data.Length);

            var sourceIp = remoteEp.Address.ToString();

            // Phase 1: Discovery — empty security name or no engine ID
            if (request.USM.SecurityName.Length == 0 ||
                request.USM.EngineId.Length == 0)
            {
                SendV3Discovery(request, remoteEp);
                return;
            }

            // Verify username
            if (request.USM.SecurityName.ToString() != _v3Credentials.Username)
            {
                Log($"V3 bad username '{request.USM.SecurityName}' from {remoteEp}");
                return;
            }

            // Phase 2: Re-decode with auth/priv so the library verifies + decrypts
            var authRequest = new SnmpV3Packet();
            var authProto = _v3Credentials.AuthProtocol == Models.AuthProtocol.MD5
                ? AuthenticationDigests.MD5 : AuthenticationDigests.SHA1;
            var privProto = _v3Credentials.PrivProtocol == Models.PrivProtocol.DES
                ? PrivacyProtocols.DES : PrivacyProtocols.AES128;

            var userBytes = System.Text.Encoding.UTF8.GetBytes(_v3Credentials.Username);
            var authPwdBytes = System.Text.Encoding.UTF8.GetBytes(_v3Credentials.AuthPassword);
            var privPwdBytes = !string.IsNullOrEmpty(_v3Credentials.PrivPassword)
                ? System.Text.Encoding.UTF8.GetBytes(_v3Credentials.PrivPassword) : null;

            if (privPwdBytes != null)
            {
                authRequest.authPriv(userBytes, authPwdBytes, authProto, privPwdBytes, privProto);
            }
            else
            {
                authRequest.authNoPriv(userBytes, authPwdBytes, authProto);
            }

            try
            {
                authRequest.decode(data, data.Length);
            }
            catch (SnmpAuthenticationException)
            {
                Log($"V3 authentication failed from {remoteEp}");
                return;
            }
            catch (SnmpPrivacyException)
            {
                Log($"V3 decryption failed from {remoteEp}");
                return;
            }

            // Build response PDU (reuse same HandleGet/Set/etc.)
            var responsePdu = new Pdu();
            responsePdu.RequestId = authRequest.Pdu.RequestId;

            switch (authRequest.Pdu.Type)
            {
                case PduType.Get:
                    HandleGet(authRequest.Pdu, responsePdu, sourceIp);
                    break;
                case PduType.GetNext:
                    HandleGetNext(authRequest.Pdu, responsePdu, sourceIp);
                    break;
                case PduType.GetBulk:
                    HandleGetBulk(authRequest.Pdu, responsePdu, sourceIp);
                    break;
                case PduType.Set:
                    HandleSet(authRequest.Pdu, responsePdu, sourceIp);
                    break;
                default:
                    Log($"V3 unsupported PDU type: {authRequest.Pdu.Type}");
                    return;
            }
            responsePdu.Type = PduType.Response;

            // Build and send V3 response packet
            var response = new SnmpV3Packet();
            if (!string.IsNullOrEmpty(_v3Credentials.PrivPassword))
            {
                response.authPriv(userBytes, authPwdBytes, authProto, privPwdBytes, privProto);
            }
            else
            {
                response.authNoPriv(userBytes, authPwdBytes, authProto);
            }

            response.USM.EngineId.Set(_engineId);
            response.USM.EngineBoots = _engineBoots;
            response.USM.EngineTime = _engineTime;

            // Copy PDU VBs into ScopedPdu
            response.ScopedPdu.Type = PduType.Response;
            response.ScopedPdu.RequestId = responsePdu.RequestId;
            foreach (var vb in responsePdu.VbList)
                response.ScopedPdu.VbList.Add(vb.Oid, vb.Value);

            var responseBytes = response.encode();
            _listener?.Send(responseBytes, responseBytes.Length, remoteEp);
        }

        private void SendV3Discovery(SnmpV3Packet request, IPEndPoint remoteEp)
        {
            var response = new SnmpV3Packet();
            response.USM.EngineId.Set(_engineId);
            response.USM.EngineBoots = _engineBoots;
            response.USM.EngineTime = _engineTime;
            response.ScopedPdu.Type = PduType.Report;
            response.ScopedPdu.RequestId = request.Pdu.RequestId;
            // RFC 3414: usmStatsUnknownEngineIDs.0
            response.ScopedPdu.VbList.Add(
                new Oid("1.3.6.1.6.3.15.1.1.4.0"), new Integer32(1));

            var respBytes = response.encode();
            _listener?.Send(respBytes, respBytes.Length, remoteEp);
            Log($"V3 discovery from {remoteEp}");
        }

        private void HandleGet(Pdu request, Pdu response, string sourceIp)
        {
            int found = 0, missing = 0;
            bool isBatch = request.VbList.Count > 1;

            foreach (var vb in request.VbList)
            {
                var oid = vb.Oid.ToString();
                if (_mibData.ContainsKey(oid) || _dynamicValues.ContainsKey(oid))
                {
                    var val = GetValue(oid);
                    var vbType = GetValueType(oid);
                    response.VbList.Add(vb.Oid, SnmpTypeHelper.CreateValue(vbType, val));
                    found++;

                    if (!isBatch)
                    {
                        RequestReceived.Invoke("GET", oid, val, sourceIp);
                        Log($"GET {oid} → {val}");
                    }
                }
                else
                {
                    response.VbList.Add(vb.Oid, new NoSuchInstance());
                    missing++;

                    if (!isBatch)
                    {
                        RequestReceived.Invoke("GET", oid, string.Empty, sourceIp);
                        Log($"GET {oid} → NoSuchInstance");
                    }
                }
            }

            // Batch: single summary log + event to avoid flooding UI
            if (isBatch)
            {
                var firstOid = request.VbList[0].Oid.ToString();
                Log($"GET batch ({found + missing} OIDs) {firstOid}.. → {found} ok, {missing} missing");
                RequestReceived.Invoke("GET", $"{firstOid} (+{found + missing - 1})", $"{found} values", sourceIp);
            }
        }

        private void HandleGetNext(Pdu request, Pdu response, string sourceIp)
        {
            foreach (var vb in request.VbList)
            {
                var requestedOid = vb.Oid.ToString();

                var nextOid = FindNextOid(requestedOid);
                if (nextOid != null)
                {
                    var val = GetValue(nextOid);
                    var vbType = GetValueType(nextOid);
                    response.VbList.Add(new Oid(nextOid), SnmpTypeHelper.CreateValue(vbType, val));
                    RequestReceived.Invoke("GETNEXT", nextOid, val, sourceIp);
                    Log($"GETNEXT {requestedOid} → {nextOid} = {val}");
                }
                else
                {
                    response.VbList.Add(vb.Oid, new EndOfMibView());
                    RequestReceived.Invoke("GETNEXT", requestedOid, string.Empty, sourceIp);
                    Log($"GETNEXT {requestedOid} → EndOfMibView");
                }
            }
        }

        private void HandleGetBulk(Pdu request, Pdu response, string sourceIp)
        {
            int maxRepetitions = request.MaxRepetitions > 0 ? request.MaxRepetitions : 10;

            foreach (var vb in request.VbList)
            {
                var currentOid = vb.Oid.ToString();

                for (int i = 0; i < maxRepetitions; i++)
                {
                    var nextOid = FindNextOid(currentOid);
                    if (nextOid == null)
                    {
                        response.VbList.Add(new Oid(currentOid), new EndOfMibView());
                        break;
                    }

                    var val = GetValue(nextOid);
                    var vbType = GetValueType(nextOid);
                    response.VbList.Add(new Oid(nextOid), SnmpTypeHelper.CreateValue(vbType, val));
                    RequestReceived.Invoke("GETBULK", nextOid, val, sourceIp);
                    currentOid = nextOid;
                }

                Log($"GETBULK {vb.Oid} → {response.VbList.Count} items");
            }
        }

        private void HandleSet(Pdu request, Pdu response, string sourceIp)
        {
            foreach (var vb in request.VbList)
            {
                var oid = vb.Oid.ToString();
                var val = vb.Value.ToString() ?? string.Empty;
                RequestReceived.Invoke("SET", oid, val, sourceIp);

                _dynamicValues[oid] = val;

                if (!_mibData.ContainsKey(oid))
                {
                    _mibData[oid] = new SnmpRecord() {
                        Oid = oid,
                        Value = val,
                        ValueType = SnmpTypeHelper.TypeToString(vb.Value.Type)
                    };
                }

                response.VbList.Add(vb.Oid, vb.Value);
                Log($"SET {oid} = {val}");
            }
        }

        private string FindNextOid(string currentOid)
        {
            bool found = false;
            foreach (var key in _mibData.Keys)
            {
                if (found) return key;
                if (string.Compare(key, currentOid, StringComparison.Ordinal) > 0)
                    return key;
                if (key == currentOid)
                    found = true;
            }
            return null;
        }

        /// <summary>
        /// Inject a recorded session — replays OID value changes over time.
        /// The simulator must already be running. Values update in real-time
        /// and loop back to the start when the recording ends.
        /// </summary>
        public void StartInjection(RecordedSession session)
        {
            StopInjection();

            if (session.Frames.Count == 0)
            {
                Log("No frames in session to inject.");
                return;
            }

            _injectionCts = new CancellationTokenSource();
            var ct = _injectionCts.Token;

            Task.Run(async () =>
            {
                Log($"Injection started — {session.Frames.Count} frames, looping playback...");

                while (!ct.IsCancellationRequested)
                {
                    long startTick = System.Diagnostics.Stopwatch.GetTimestamp() * 1000 / System.Diagnostics.Stopwatch.Frequency;

                    for (int i = 0; i < session.Frames.Count; i++)
                    {
                        if (ct.IsCancellationRequested) break;

                        var frame = session.Frames[i];

                        // Wait until the right time for this frame
                        long targetMs = frame.ElapsedMs;
                        long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() * 1000 / System.Diagnostics.Stopwatch.Frequency - startTick;
                        if (targetMs > elapsed)
                        {
                            try { await Task.Delay((int)(targetMs - elapsed), ct); }
                            catch (OperationCanceledException) { break; }
                        }

                        // Apply all OID values from this frame
                        foreach (var record in frame.Records)
                        {
                            _dynamicValues[record.Oid] = record.Value;

                            if (!_mibData.ContainsKey(record.Oid))
                            {
                                _mibData[record.Oid] = new SnmpRecord() {
                                    Oid = record.Oid,
                                    Value = record.Value,
                                    ValueType = record.ValueType
                                };
                            }
                        }

                        InjectionProgress.Invoke(i + 1, session.Frames.Count);
                        InjectionFrameApplied.Invoke(i + 1, frame.Records);
                        Log($"Injection frame {i + 1}/{session.Frames.Count} applied — {frame.Records.Count} OIDs updated");
                    }

                    if (!ct.IsCancellationRequested)
                        Log("Injection loop complete, restarting...");
                }

                Log("Injection stopped.");
            }, ct);
        }

        public void StopInjection()
        {
            _injectionCts?.Cancel();
            _injectionCts = null;
        }

        private void Log(string msg) => LogMessage.Invoke(msg);

        public void Dispose()
        {
            StopInjection();
            Stop();
            GC.SuppressFinalize(this);
        }
    }
}
