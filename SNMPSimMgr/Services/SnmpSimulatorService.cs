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

namespace SNMPSimMgr.Services;

public class SnmpSimulatorService : IDisposable
{
    private UdpClient? _listener;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _injectionCts;
    private Task? _listenTask;

    private SortedDictionary<string, SnmpRecord> _mibData = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _dynamicValues = new();

    public event Action<string>? LogMessage;
    public event Action<string, string, string, string>? RequestReceived; // op, oid, val, sourceIp
    public event Action<int, int>? InjectionProgress; // currentFrame, totalFrames
    public event Action<int, List<SnmpRecord>>? InjectionFrameApplied; // frameIndex, records

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

    public void Start(int port, string community = "public", string listenIp = "0.0.0.0")
    {
        if (IsRunning) return;

        Port = port;
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

    private void HandleGet(Pdu request, Pdu response, string sourceIp)
    {
        foreach (var vb in request.VbList)
        {
            var oid = vb.Oid.ToString();
            RequestReceived?.Invoke("GET", oid, string.Empty, sourceIp);

            if (_mibData.ContainsKey(oid) || _dynamicValues.ContainsKey(oid))
            {
                var val = GetValue(oid);
                var vbType = GetValueType(oid);
                response.VbList.Add(vb.Oid, SnmpTypeHelper.CreateValue(vbType, val));
                Log($"GET {oid} → {val}");
            }
            else
            {
                response.VbList.Add(vb.Oid, new NoSuchInstance());
                Log($"GET {oid} → NoSuchInstance");
            }
        }
    }

    private void HandleGetNext(Pdu request, Pdu response, string sourceIp)
    {
        foreach (var vb in request.VbList)
        {
            var requestedOid = vb.Oid.ToString();
            RequestReceived?.Invoke("GETNEXT", requestedOid, string.Empty, sourceIp);

            var nextOid = FindNextOid(requestedOid);
            if (nextOid != null)
            {
                var val = GetValue(nextOid);
                var vbType = GetValueType(nextOid);
                response.VbList.Add(new Oid(nextOid), SnmpTypeHelper.CreateValue(vbType, val));
                Log($"GETNEXT {requestedOid} → {nextOid} = {val}");
            }
            else
            {
                response.VbList.Add(vb.Oid, new EndOfMibView());
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
            RequestReceived?.Invoke("GETBULK", currentOid, $"max={maxRepetitions}", sourceIp);

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
            var val = vb.Value?.ToString() ?? string.Empty;
            RequestReceived?.Invoke("SET", oid, val, sourceIp);

            _dynamicValues[oid] = val;

            if (!_mibData.ContainsKey(oid))
            {
                _mibData[oid] = new SnmpRecord
                {
                    Oid = oid,
                    Value = val,
                    ValueType = SnmpTypeHelper.TypeToString(vb.Value?.Type ?? SnmpTypeHelper.OctetString)
                };
            }

            response.VbList.Add(vb.Oid, vb.Value);
            Log($"SET {oid} = {val}");
        }
    }

    private string? FindNextOid(string currentOid)
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
                            _mibData[record.Oid] = new SnmpRecord
                            {
                                Oid = record.Oid,
                                Value = record.Value,
                                ValueType = record.ValueType
                            };
                        }
                    }

                    InjectionProgress?.Invoke(i + 1, session.Frames.Count);
                    InjectionFrameApplied?.Invoke(i + 1, frame.Records);
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

    private void Log(string msg) => LogMessage?.Invoke(msg);

    public void Dispose()
    {
        StopInjection();
        Stop();
        GC.SuppressFinalize(this);
    }
}
