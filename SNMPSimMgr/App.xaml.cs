using System.Collections.Specialized;
using System.Windows;
using SNMPSimMgr.Hubs;
using SNMPSimMgr.Services;
using SNMPSimMgr.ViewModels;
using SNMPSimMgr.Views;

namespace SNMPSimMgr;

public partial class App : Application
{
    private SignalRService? _signalRService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Create services
        var store = new DeviceProfileStore();
        var recorder = new SnmpRecorderService();
        var trapListener = new TrapListenerService();
        var trapGenerator = new TrapGeneratorService();

        var mibStore = new MibStore();

        // Create ViewModels
        var deviceListVm = new DeviceListViewModel(store);
        var recorderVm = new RecorderViewModel(recorder, trapListener, store, deviceListVm);
        var simulatorVm = new SimulatorViewModel(store, trapGenerator, deviceListVm, recorder, mibStore);
        var demoService = new DemoDataService(store);
        var monitorVm = new NetworkMonitorViewModel(simulatorVm);
        var mibExportService = new MibPanelExportService(mibStore, store);
        var mibBrowserVm = new MibBrowserViewModel(store, deviceListVm, mibStore, mibExportService, recorder);
        var scenarioVm = new ScenarioViewModel(simulatorVm);
        var mainVm = new MainViewModel(deviceListVm, recorderVm, simulatorVm, demoService, monitorVm, mibBrowserVm, scenarioVm);

        // ── SignalR Setup ──
        _signalRService = new SignalRService();

        // Set static hub references (SignalR 2.0 pattern — no DI container)
        SnmpHub.Recorder = recorder;
        SnmpHub.ExportService = mibExportService;
        SnmpHub.Store = store;
        SnmpHub.SimulatorVm = simulatorVm;
        SnmpHub.DeviceListVm = deviceListVm;
        SnmpHub.MibStoreRef = mibStore;
        SnmpHub.TrapGen = trapGenerator;

        // Wire existing events → SignalR broadcasts
        simulatorVm.TrafficReceived += (deviceName, op, oid, val, sourceIp) =>
        {
            if (_signalRService.IsRunning)
                SnmpHub.BroadcastTraffic(deviceName, op, oid, val, sourceIp);
        };

        trapListener.TrapReceived += trap =>
        {
            if (_signalRService.IsRunning)
                SnmpHub.BroadcastTrap(trap);
        };

        simulatorVm.ActiveSimulators.CollectionChanged += (_, args) =>
        {
            if (!_signalRService.IsRunning) return;
            if (args.Action == NotifyCollectionChangedAction.Add && args.NewItems != null)
            {
                foreach (SimulatorDeviceStatus s in args.NewItems)
                    SnmpHub.BroadcastDeviceStatus(s.DeviceId, s.DeviceName, "Running");
            }
            if (args.Action == NotifyCollectionChangedAction.Remove && args.OldItems != null)
            {
                foreach (SimulatorDeviceStatus s in args.OldItems)
                    SnmpHub.BroadcastDeviceStatus(s.DeviceId, s.DeviceName, "Stopped");
            }
        };

        // IDD SET handler — log + broadcast updated value back to Angular
        simulatorVm.IddSetRequested += (deviceId, fieldId, value) =>
        {
            System.Diagnostics.Debug.WriteLine($"[IDD SET] device={deviceId}, field={fieldId}, value={value}");
            Current.Dispatcher.Invoke(() =>
                simulatorVm.LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] IDD SET: {fieldId} = {value} (device: {deviceId})"));

            // TODO: Here you would send the IDD command to your hardware.
            //       After the hardware confirms, broadcast the new value back to Angular:

            if (_signalRService != null && _signalRService.IsRunning)
                SnmpHub.BroadcastTraffic("IDD", "SET", fieldId, value, "localhost");
        };

        // Start SignalR server (non-fatal if it fails)
        string signalRStatus;
        try
        {
            _signalRService.Start(5050);
            signalRStatus = "SignalR server running on port 5050";
        }
        catch (Exception ex)
        {
            signalRStatus = $"SignalR failed: {ex.Message}";
            MessageBox.Show(
                $"SignalR server failed to start:\n\n{ex.Message}\n\nThe app will continue without real-time web connectivity.\n\nTip: Try running as Administrator, or run:\nnetsh http add urlacl url=http://+:5050/ user=Everyone",
                "SignalR",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        // Load data and show window
        var mainWindow = new MainWindow { DataContext = mainVm };
        mainVm.StatusText = signalRStatus;
        mainWindow.Show();

        _ = deviceListVm.LoadAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _signalRService?.Dispose();
        base.OnExit(e);
    }
}
