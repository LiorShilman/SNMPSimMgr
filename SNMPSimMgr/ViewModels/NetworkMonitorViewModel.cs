using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SNMPSimMgr.Models;

namespace SNMPSimMgr.ViewModels;

public partial class NetworkMonitorViewModel : ObservableObject
{
    private readonly SimulatorViewModel _simulator;

    public ObservableCollection<ClientConnection> Connections { get; } = new();
    public ObservableCollection<TrafficEntry> TrafficEntries { get; } = new();
    public ObservableCollection<SimulatorDeviceStatus> ActiveSimulators => _simulator.ActiveSimulators;

    [ObservableProperty] private int _totalRequests;
    [ObservableProperty] private int _clientCount;

    public NetworkMonitorViewModel(SimulatorViewModel simulator)
    {
        _simulator = simulator;

        _simulator.TrafficReceived += (deviceName, op, oid, val, sourceIp) =>
        {
            OnTrafficReceived(deviceName, op, oid, val, sourceIp);
        };
    }

    private void OnTrafficReceived(string deviceName, string op, string oid, string val, string sourceIp)
    {
        bool isInjection = sourceIp == "injection";
        string displayIp = isInjection ? $"INJECTION → {deviceName}" : sourceIp;

        // Track connection node (both clients and injection sources)
        var client = Connections.FirstOrDefault(c => c.IpAddress == displayIp);
        if (client == null)
        {
            client = new ClientConnection
            {
                IpAddress = displayIp,
                IsInjection = isInjection
            };
            Connections.Add(client);
            ClientCount = Connections.Count;
        }

        client.RequestCount++;
        client.LastOperation = op;
        client.LastSeen = DateTime.Now.ToString("HH:mm:ss");
        client.TargetDevice = deviceName;
        client.IsActive = true;
        _ = ResetActiveAsync(client);

        // Add traffic entry
        TrafficEntries.Insert(0, new TrafficEntry
        {
            Time = DateTime.Now.ToString("HH:mm:ss"),
            SourceIp = isInjection ? "INJECT" : sourceIp,
            DeviceName = deviceName,
            Operation = op,
            Oid = oid,
            Value = val
        });

        while (TrafficEntries.Count > 500)
            TrafficEntries.RemoveAt(TrafficEntries.Count - 1);

        TotalRequests++;
    }

    private async Task ResetActiveAsync(ClientConnection client)
    {
        await Task.Delay(500);
        App.Current.Dispatcher.Invoke(() => client.IsActive = false);
    }

    [RelayCommand]
    private void ClearTraffic()
    {
        TrafficEntries.Clear();
        Connections.Clear();
        TotalRequests = 0;
        ClientCount = 0;
    }
}
