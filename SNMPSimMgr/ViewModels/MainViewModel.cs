using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SNMPSimMgr.Services;
using SNMPSimMgr.Views;

namespace SNMPSimMgr.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DemoDataService _demoService;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool _isDemoMode;

    public DeviceListViewModel DeviceList { get; }
    public RecorderViewModel Recorder { get; }
    public SimulatorViewModel Simulator { get; }
    public NetworkMonitorViewModel Monitor { get; }
    public MibBrowserViewModel MibBrowser { get; }
    public ScenarioViewModel Scenario { get; }

    public MainViewModel(
        DeviceListViewModel deviceList,
        RecorderViewModel recorder,
        SimulatorViewModel simulator,
        DemoDataService demoService,
        NetworkMonitorViewModel monitor,
        MibBrowserViewModel mibBrowser,
        ScenarioViewModel scenario)
    {
        DeviceList = deviceList;
        Recorder = recorder;
        Simulator = simulator;
        Monitor = monitor;
        MibBrowser = mibBrowser;
        Scenario = scenario;
        _demoService = demoService;
    }

    [RelayCommand]
    private async Task LoadDemoData()
    {
        StatusText = "Loading demo data...";

        var devices = await _demoService.CreateDemoDataAsync();

        DeviceList.Devices.Clear();
        foreach (var d in devices)
            DeviceList.Devices.Add(d);

        IsDemoMode = true;
        StatusText = $"Demo Mode — {devices.Count} devices loaded with simulated SNMP data";
    }

    [RelayCommand]
    private async Task StartDemoSimulation()
    {
        if (!IsDemoMode || DeviceList.Devices.Count == 0)
        {
            StatusText = "Load demo data first!";
            return;
        }

        StatusText = "Starting all demo simulators...";
        int port = 10161;

        foreach (var device in DeviceList.Devices)
        {
            DeviceList.SelectedDevice = device;
            Simulator.SimulatorPort = port++;
            await Simulator.StartSimulatorCommand.ExecuteAsync(null);
        }

        StatusText = $"Demo Mode — {DeviceList.Devices.Count} simulators running (ports 10161-{port - 1})";
    }

    [RelayCommand]
    private async Task StartAllAndInject()
    {
        if (DeviceList.Devices.Count == 0)
        {
            StatusText = "No devices loaded!";
            return;
        }

        StatusText = "Starting all simulators with injection...";
        int basePort = Simulator.SimulatorPort;
        int port = basePort;
        int started = 0;
        int injected = 0;

        foreach (var device in DeviceList.Devices.ToList())
        {
            DeviceList.SelectedDevice = device;
            Simulator.SimulatorPort = port++;
            await Simulator.StartSimulatorCommand.ExecuteAsync(null);

            // Check if simulator actually started
            if (Simulator.ActiveSimulators.Any(s => s.DeviceId == device.Id))
            {
                started++;

                // Try to inject first available session
                await Simulator.RefreshSessionList();
                if (Simulator.SessionList.Count > 0)
                {
                    Simulator.SelectedSessionName = Simulator.SessionList[0];
                    await Simulator.InjectSessionCommand.ExecuteAsync(null);
                    injected++;
                }
            }
        }

        StatusText = injected > 0
            ? $"▶ {started} simulators running, {injected} injections active (ports {basePort}-{basePort + started - 1})"
            : $"▶ {started} simulators running (ports {basePort}-{basePort + started - 1}) — no sessions to inject";
    }

    [RelayCommand]
    private async Task ExitDemoMode()
    {
        Simulator.StopAllCommand.Execute(null);

        var dataRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
        foreach (var device in DeviceList.Devices.ToList())
        {
            var safe = string.Join("_", device.Name.Split(Path.GetInvalidFileNameChars()));
            var folder = Path.Combine(dataRoot, safe);
            if (Directory.Exists(folder))
            {
                try { Directory.Delete(folder, true); }
                catch { }
            }
        }

        DeviceList.Devices.Clear();
        await DeviceList.SaveAsync();

        IsDemoMode = false;
        StatusText = "Ready";
    }

    [RelayCommand]
    private void ShowUserGuide()
    {
        var guide = new UserGuideWindow
        {
            Owner = App.Current.MainWindow
        };
        guide.ShowDialog();
    }
}
