using System.Net;
using System.Net.Sockets;
using SnmpSharpNet;
using SNMPSimMgr.Models;

namespace SNMPSimMgr.Services;

/// <summary>
/// Listens for incoming SNMP traps on UDP 162 and records them.
/// Used during the recording phase to capture traps from real devices.
/// </summary>
public class TrapListenerService : IDisposable
{
    private UdpClient? _listener;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public event Action<string>? LogMessage;
    public event Action<TrapRecord>? TrapReceived;

    public bool IsListening => _cts != null && !_cts.IsCancellationRequested;

    public void Start(int port = 162)
    {
        if (IsListening) return;

        _cts = new CancellationTokenSource();
        _listener = new UdpClient(new IPEndPoint(IPAddress.Any, port));

        Log($"Trap listener started on UDP port {port}...");

        _listenTask = Task.Run(() => ListenLoop(_cts.Token));
    }

    public void Stop()
    {
        if (!IsListening) return;

        _cts?.Cancel();
        _listener?.Close();
        _listener?.Dispose();
        _listener = null;
        _cts = null;

        Log("Trap listener stopped.");
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await _listener!.ReceiveAsync(ct);
                ProcessTrap(result.Buffer, result.RemoteEndPoint);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception ex)
            {
                Log($"Trap listener error: {ex.Message}");
            }
        }
    }

    private void ProcessTrap(byte[] data, IPEndPoint remoteEp)
    {
        try
        {
            int ver = SnmpPacket.GetProtocolVersion(data, data.Length);

            if (ver == (int)SnmpVersion.Ver2)
            {
                var packet = new SnmpV2Packet();
                packet.decode(data, data.Length);

                if (packet.Pdu.Type != PduType.V2Trap) return;

                var trap = new TrapRecord
                {
                    Timestamp = DateTime.UtcNow,
                    SourceIp = remoteEp.Address.ToString(),
                    VariableBindings = new List<SnmpRecord>()
                };

                foreach (var vb in packet.Pdu.VbList)
                {
                    // Extract the trap OID from snmpTrapOID.0
                    if (vb.Oid.ToString() == "1.3.6.1.6.3.1.1.4.1.0")
                    {
                        trap.Oid = vb.Value.ToString() ?? string.Empty;
                    }
                    else
                    {
                        trap.VariableBindings.Add(new SnmpRecord
                        {
                            Oid = vb.Oid.ToString(),
                            Value = vb.Value?.ToString() ?? string.Empty,
                            ValueType = SnmpTypeHelper.TypeToString(vb.Value?.Type ?? SnmpTypeHelper.OctetString)
                        });
                    }
                }

                Log($"Trap from {remoteEp}: {trap.Oid} ({trap.VariableBindings.Count} bindings)");
                TrapReceived?.Invoke(trap);
            }
        }
        catch (Exception ex)
        {
            Log($"Error processing trap from {remoteEp}: {ex.Message}");
        }
    }

    private void Log(string msg) => LogMessage?.Invoke(msg);

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
