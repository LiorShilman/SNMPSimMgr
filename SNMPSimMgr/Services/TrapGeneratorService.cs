using System.Net;
using System.Net.Sockets;
using SnmpSharpNet;
using SNMPSimMgr.Models;

namespace SNMPSimMgr.Services;

public class TrapGeneratorService
{
    public event Action<string>? LogMessage;

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

    /// <summary>
    /// Send a single trap to the specified manager.
    /// </summary>
    public async Task SendTrapAsync(TrapRecord trap, string managerIp, int managerPort = 162)
    {
        await Task.Run(() =>
        {
            var target = new UdpTarget(ResolveAddress(managerIp), managerPort, 5000, 1);

            try
            {
                var pdu = new Pdu(PduType.V2Trap);
                pdu.VbList.Add(new Oid("1.3.6.1.2.1.1.3.0"),
                    new TimeTicks((uint)(DateTime.UtcNow - DateTime.Today).TotalMilliseconds / 10));
                pdu.VbList.Add(new Oid("1.3.6.1.6.3.1.1.4.1.0"), new Oid(trap.Oid));

                foreach (var binding in trap.VariableBindings)
                {
                    pdu.VbList.Add(new Oid(binding.Oid), new OctetString(binding.Value));
                }

                var param = new AgentParameters(SnmpVersion.Ver2, new OctetString("public"));
                target.Request(pdu, param);

                Log($"Trap sent to {managerIp}:{managerPort} - OID: {trap.Oid}");
            }
            finally
            {
                target.Close();
            }
        });
    }

    /// <summary>
    /// Play back a scenario — a sequence of traps with delays.
    /// </summary>
    public async Task PlayScenarioAsync(
        TrapScenario scenario,
        string managerIp,
        int managerPort = 162,
        CancellationToken ct = default)
    {
        Log($"Playing scenario '{scenario.Name}' ({scenario.Steps.Count} steps)...");

        foreach (var step in scenario.Steps)
        {
            ct.ThrowIfCancellationRequested();

            if (step.DelayMs > 0)
                await Task.Delay(step.DelayMs, ct);

            await SendTrapAsync(step.Trap, managerIp, managerPort);
        }

        Log($"Scenario '{scenario.Name}' complete.");
    }

    private void Log(string msg) => LogMessage?.Invoke(msg);
}
