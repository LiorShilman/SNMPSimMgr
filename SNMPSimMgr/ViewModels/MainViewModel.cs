using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SNMPSimMgr.Hubs;
using SNMPSimMgr.Services;
using SNMPSimMgr.Views;

namespace SNMPSimMgr.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DemoDataService _demoService;
    private CancellationTokenSource? _automationCts;

    [ObservableProperty]
    private string _statusText = "Ready";

    [ObservableProperty]
    private bool _isDemoMode;

    [ObservableProperty]
    private bool _isAutomationRunning;

    public DeviceListViewModel DeviceList { get; }
    public RecorderViewModel Recorder { get; }
    public SimulatorViewModel Simulator { get; }
    public NetworkMonitorViewModel Monitor { get; }
    public MibBrowserViewModel MibBrowser { get; }
    public ScenarioViewModel Scenario { get; }
    public IddEditorViewModel IddEditor { get; }

    public MainViewModel(
        DeviceListViewModel deviceList,
        RecorderViewModel recorder,
        SimulatorViewModel simulator,
        DemoDataService demoService,
        NetworkMonitorViewModel monitor,
        MibBrowserViewModel mibBrowser,
        ScenarioViewModel scenario,
        IddEditorViewModel iddEditor)
    {
        DeviceList = deviceList;
        Recorder = recorder;
        Simulator = simulator;
        Monitor = monitor;
        MibBrowser = mibBrowser;
        Scenario = scenario;
        IddEditor = iddEditor;
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

    [RelayCommand]
    private async Task RunFullAutomation()
    {
        if (IsAutomationRunning) return;

        IsAutomationRunning = true;
        _automationCts = new CancellationTokenSource();
        var token = _automationCts.Token;

        try
        {
            // ── Phase 1: Load demo data ──
            StatusText = "⚡ Auto 1/5 — Loading demo data...";
            if (DeviceList.Devices.Count == 0)
                await LoadDemoData();
            await Task.Delay(500, token);

            // ── Phase 2: Start all simulators ──
            StatusText = "⚡ Auto 2/5 — Starting simulators...";
            int basePort = Simulator.SimulatorPort;
            int port = basePort;
            foreach (var device in DeviceList.Devices.ToList())
            {
                DeviceList.SelectedDevice = device;
                Simulator.SimulatorPort = port++;
                await Simulator.StartSimulatorCommand.ExecuteAsync(null);
            }
            int simulatorCount = Simulator.ActiveSimulators.Count;
            await Task.Delay(500, token);

            // ── Phase 3: Inject sessions ──
            StatusText = "⚡ Auto 3/5 — Injecting sessions...";
            int injected = 0;
            foreach (var device in DeviceList.Devices.ToList())
            {
                DeviceList.SelectedDevice = device;
                if (Simulator.ActiveSimulators.Any(s => s.DeviceId == device.Id))
                {
                    await Simulator.RefreshSessionList();
                    if (Simulator.SessionList.Count > 0)
                    {
                        Simulator.SelectedSessionName = Simulator.SessionList[0];
                        await Simulator.InjectSessionCommand.ExecuteAsync(null);
                        injected++;
                    }
                }
            }
            await Task.Delay(500, token);

            // ── Phase 4: Play E2E scenario (value changes + broadcasts) ──
            StatusText = "⚡ Auto 4/5 — Running E2E scenario...";
            await RunE2EScenario(token);

            // ── Phase 5: Send traps + batch MIB update ──
            StatusText = "⚡ Auto 5/5 — Sending traps & batch updates...";
            foreach (var device in DeviceList.Devices.ToList())
            {
                DeviceList.SelectedDevice = device;
                await Simulator.PlayTrapsCommand.ExecuteAsync(null);
            }

            // Broadcast batch MIB update for each active simulator
            foreach (var sim in Simulator.ActiveSimulators)
            {
                var values = new Dictionary<string, string>
                {
                    ["1.3.6.1.2.1.1.3.0"] = (Environment.TickCount / 10).ToString(),
                    ["1.3.6.1.2.1.1.5.0"] = $"{sim.DeviceName} (E2E verified)"
                };
                SnmpHub.BroadcastMibUpdate(sim.DeviceId, values);
            }
            await Task.Delay(1000, token);

            // ── Done ──
            StatusText = $"✓ Full Automation complete — {simulatorCount} simulators, {injected} injections, E2E scenario played";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Full Automation stopped";
        }
        catch (Exception ex)
        {
            StatusText = $"Automation error: {ex.Message}";
        }
        finally
        {
            IsAutomationRunning = false;
        }
    }

    private async Task RunE2EScenario(CancellationToken token)
    {
        // Built-in scenario: exercises all broadcast types with realistic network events
        var events = new[]
        {
            (delay: 0,  label: "Interface goes DOWN",        oid: "1.3.6.1.2.1.2.2.1.8.1", value: "2",                 type: "Integer32"),
            (delay: 3,  label: "CPU spike to 95%",           oid: "1.3.6.1.2.1.25.3.3.1.2.1", value: "95",             type: "Integer32"),
            (delay: 6,  label: "Temperature alert 78°C",     oid: "1.3.6.1.2.1.99.1.1.1.4.1", value: "78",             type: "Integer32"),
            (delay: 9,  label: "SET hostname (write test)",  oid: "1.3.6.1.2.1.1.5.0", value: "E2E-Automation-Test",    type: "OctetString"),
            (delay: 12, label: "Interface comes UP",         oid: "1.3.6.1.2.1.2.2.1.8.1", value: "1",                 type: "Integer32"),
            (delay: 15, label: "CPU returns to 12%",         oid: "1.3.6.1.2.1.25.3.3.1.2.1", value: "12",             type: "Integer32"),
            (delay: 18, label: "Temperature normal 42°C",    oid: "1.3.6.1.2.1.99.1.1.1.4.1", value: "42",             type: "Integer32"),
        };

        int lastDelay = 0;
        foreach (var evt in events)
        {
            token.ThrowIfCancellationRequested();

            int wait = evt.delay - lastDelay;
            if (wait > 0)
            {
                for (int s = 0; s < wait; s++)
                {
                    token.ThrowIfCancellationRequested();
                    await Task.Delay(1000, token);
                    StatusText = $"⚡ Auto 4/5 — T+{lastDelay + s + 1}s — Next: {evt.label}";
                }
            }

            // Apply to all active simulators and broadcast
            foreach (var sim in Simulator.ActiveSimulators.ToList())
            {
                Simulator.TrySetValue(sim.DeviceId, evt.oid, evt.value);
                SnmpHub.BroadcastTraffic(sim.DeviceName, "SET", evt.oid, evt.value, "automation");
            }

            StatusText = $"⚡ Auto 4/5 — T+{evt.delay}s — ✓ {evt.label}";
            lastDelay = evt.delay;
        }
    }

    [RelayCommand]
    private void StopAutomation()
    {
        _automationCts?.Cancel();
    }
}
