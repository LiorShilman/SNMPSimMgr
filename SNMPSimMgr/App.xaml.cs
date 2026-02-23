using System.Windows;
using SNMPSimMgr.Services;
using SNMPSimMgr.ViewModels;
using SNMPSimMgr.Views;

namespace SNMPSimMgr;

public partial class App : Application
{
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
        var simulatorVm = new SimulatorViewModel(store, trapGenerator, deviceListVm, recorder);
        var demoService = new DemoDataService(store);
        var monitorVm = new NetworkMonitorViewModel(simulatorVm);
        var mibBrowserVm = new MibBrowserViewModel(store, deviceListVm, mibStore);
        var scenarioVm = new ScenarioViewModel(simulatorVm);
        var mainVm = new MainViewModel(deviceListVm, recorderVm, simulatorVm, demoService, monitorVm, mibBrowserVm, scenarioVm);

        // Load data and show window
        var mainWindow = new MainWindow { DataContext = mainVm };
        mainWindow.Show();

        _ = deviceListVm.LoadAsync();
    }
}
