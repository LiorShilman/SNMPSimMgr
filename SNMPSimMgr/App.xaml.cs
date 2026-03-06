using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Specialized;
using System.Windows;
using SNMPSimMgr.Hubs;
using SNMPSimMgr.Services;
using SNMPSimMgr.ViewModels;
using SNMPSimMgr.Views;

namespace SNMPSimMgr
{
    public partial class App : Application
    {
        private SignalRService _signalRService;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Create services
            var store = new DeviceProfileStore();
            var recorder = new SnmpRecorderService();
            var trapListener = new TrapListenerService();
            var trapGenerator = new TrapGeneratorService();
            var oidWatch = new OidWatchService();

            var mibStore = new MibStore();

            // Create ViewModels
            var deviceListVm = new DeviceListViewModel(store);
            var recorderVm = new RecorderViewModel(recorder, trapListener, store, deviceListVm, mibStore, oidWatch);
            var simulatorVm = new SimulatorViewModel(store, trapGenerator, deviceListVm, recorder, mibStore);
            var demoService = new DemoDataService(store);
            var monitorVm = new NetworkMonitorViewModel(simulatorVm);
            var mibExportService = new MibPanelExportService(mibStore, store);
            var mibBrowserVm = new MibBrowserViewModel(store, deviceListVm, mibStore, mibExportService, recorder);
            var scenarioVm = new ScenarioViewModel(simulatorVm);
            var iddEditorVm = new IddEditorViewModel(store, deviceListVm);
            var mainVm = new MainViewModel(deviceListVm, recorderVm, simulatorVm, demoService, monitorVm, mibBrowserVm, scenarioVm, iddEditorVm);

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
            SnmpHub.OidWatch = oidWatch;

            // When Recorder saves a session, refresh Simulator's session list
            recorderVm.SessionSaved += async () => await simulatorVm.RefreshSessionList();

            // Wire existing events → SignalR broadcasts + MIB Browser live updates + OID watches
            simulatorVm.TrafficReceived += (deviceName, op, oid, val, sourceIp) =>
            {
                if (_signalRService.IsRunning)
                    SnmpHub.BroadcastTraffic(deviceName, op, oid, val, sourceIp);

                // Update MIB Browser tree + OID watches + broadcast onOidChanged to Angular
                if (op == "SET" || op == "GET")
                {
                    Current.Dispatcher.BeginInvoke((Action)(() => mibBrowserVm.UpdateNodeValue(deviceName, oid, val)));
                    var previousValue = oidWatch.NotifyChange(oid, val);

                    // Broadcast targeted change event to Angular for client-side automation
                    if (_signalRService.IsRunning && previousValue != val)
                    {
                        var deviceId = simulatorVm.ActiveSimulators
                            .FirstOrDefault(s => s.DeviceName == deviceName)?.DeviceId ?? "";
                        SnmpHub.BroadcastOidChanged(deviceId, deviceName, oid, val, previousValue, sourceIp);
                    }
                }
            };

            // ── OID Watch examples ──
            // You can register watches in 3 ways:
            //   1. Watch(oid, callback)           — exact OID match
            //   2. WatchPrefix(prefix, callback)  — subtree match
            //   3. WatchByName(name, callback)    — by field name (SNMP or IDD)
            //
            // For WatchByName to work with SNMP devices, call oidWatch.RegisterSchema(schema)
            // after exporting/loading a schema. IDD field names work immediately (the OID IS the name).

            // ── Example 1: Watch by Name — SNMP field ──
            // Fires when sysName changes, regardless of its numeric OID
            oidWatch.WatchByName("sysName", (oid, newValue, previousValue) =>
            {
                System.Diagnostics.Debug.WriteLine($"[OidWatch] sysName changed: '{previousValue}' → '{newValue}'");
                Current.Dispatcher.BeginInvoke((Action)(() =>
                    simulatorVm.LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] AUTO: sysName changed to '{newValue}'")));
            });

            // ── Example 2: Watch by Name — IDD field with automation logic ──
            // When temperature exceeds threshold → auto-set status-led to "critical"
            oidWatch.WatchByName("temperature", (oid, newValue, previousValue) =>
            {
                if (int.TryParse(newValue, out var temp) && temp > 80)
                {
                    System.Diagnostics.Debug.WriteLine($"[OidWatch] ALERT: temperature={temp} > 80 — setting status-led to critical");
                    Current.Dispatcher.BeginInvoke((Action)(() =>
                        simulatorVm.LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] AUTO: temperature {temp}°C exceeds threshold!")));
                }
            });

            // ── Example 3: Watch by OID prefix — interface status subtree ──
            oidWatch.WatchPrefix("1.3.6.1.2.1.2.2.1.8", (oid, newValue, previousValue) =>
            {
                var status = newValue == "1" ? "UP" : "DOWN";
                System.Diagnostics.Debug.WriteLine($"[OidWatch] Interface {oid}: {previousValue} → {status}");
            });

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

            // IDD SET handler — log + broadcast updated value back to Angular + OID watch
            simulatorVm.IddSetRequested += (deviceId, fieldId, value) =>
            {
                System.Diagnostics.Debug.WriteLine($"[IDD SET] device={deviceId}, field={fieldId}, value={value}");
                Current.Dispatcher.Invoke(() =>
                    simulatorVm.LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] IDD SET: {fieldId} = {value} (device: {deviceId})"));

                // TODO: Here you would send the IDD command to your hardware.
                //       After the hardware confirms, broadcast the new value back to Angular:

                // Track IDD changes in OID watch + broadcast onOidChanged
                var previousValue = oidWatch.NotifyChange(fieldId, value);

                if (_signalRService != null && _signalRService.IsRunning)
                {
                    SnmpHub.BroadcastTraffic("IDD", "SET", fieldId, value, "localhost");

                    if (previousValue != value)
                    {
                        var deviceName = simulatorVm.ActiveSimulators
                            .FirstOrDefault(s => s.DeviceId == deviceId)?.DeviceName ?? "IDD";
                        SnmpHub.BroadcastOidChanged(deviceId, deviceName, fieldId, value, previousValue, "localhost");
                    }
                }
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
            var mainWindow = new MainWindow() { DataContext = mainVm };
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
}
