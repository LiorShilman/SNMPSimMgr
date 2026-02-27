using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SNMPSimMgr.Models;
using SNMPSimMgr.Services;

namespace SNMPSimMgr.ViewModels;

public partial class SimulatorViewModel : ObservableObject
{
    private readonly DeviceProfileStore _store;
    private readonly TrapGeneratorService _trapGenerator;
    private readonly DeviceListViewModel _deviceList;
    private readonly SnmpRecorderService _recorder;
    private readonly MibStore _mibStore;
    private readonly ConcurrentDictionary<string, SnmpSimulatorService> _simulators = new();

    public event Action<string, string, string, string, string>? TrafficReceived; // deviceName, op, oid, val, sourceIp

    public ObservableCollection<string> LogEntries { get; } = new();
    public ObservableCollection<SimulatorDeviceStatus> ActiveSimulators { get; } = new();
    public ObservableCollection<string> TrafficLog { get; } = new();
    public ObservableCollection<QueryResultItem> QueryResults { get; } = new();
    public ObservableCollection<string> SessionList { get; } = new();

    [ObservableProperty] private string _simulatorListenIp = "0.0.0.0";
    [ObservableProperty] private int _simulatorPort = 10161;
    [ObservableProperty] private string _trapTargetIp = "127.0.0.1";
    [ObservableProperty] private int _trapTargetPort = 162;
    [ObservableProperty] private string _queryOid = "1.3.6.1.2.1.1.1.0";
    [ObservableProperty] private string _setValue = "";
    [ObservableProperty] private string _setValueType = "OctetString";
    [ObservableProperty] private SimulatorDeviceStatus? _selectedSimulator;
    [ObservableProperty] private bool _isQuerying;
    [ObservableProperty] private bool _isInjecting;
    [ObservableProperty] private string _injectionStatus = "";
    [ObservableProperty] private string? _selectedSessionName;

    public DeviceProfile? SelectedDevice => _deviceList.SelectedDevice;

    public SimulatorViewModel(
        DeviceProfileStore store,
        TrapGeneratorService trapGenerator,
        DeviceListViewModel deviceList,
        SnmpRecorderService recorder,
        MibStore mibStore)
    {
        _store = store;
        _trapGenerator = trapGenerator;
        _deviceList = deviceList;
        _recorder = recorder;
        _mibStore = mibStore;

        _trapGenerator.LogMessage += msg => App.Current.Dispatcher.Invoke(() =>
            LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] {msg}"));

        _deviceList.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName == nameof(DeviceListViewModel.SelectedDevice))
            {
                OnPropertyChanged(nameof(SelectedDevice));
                await RefreshSessionList();
            }
        };
    }

    public async Task RefreshSessionList()
    {
        SessionList.Clear();
        var device = SelectedDevice;
        if (device == null) return;

        var names = await _store.ListSessionNamesAsync(device);
        foreach (var name in names)
            SessionList.Add(name);

        if (SessionList.Count > 0)
            SelectedSessionName = SessionList[0];
    }

    /// <summary>Resolve OID to MibDefinition by trying exact match then stripping trailing segments.</summary>
    private MibDefinition? ResolveOidDef(string oid)
    {
        if (_mibStore.LoadedOids.TryGetValue(oid, out var def))
            return def;

        var current = oid;
        for (int i = 0; i < 3; i++)
        {
            var lastDot = current.LastIndexOf('.');
            if (lastDot <= 0) break;
            current = current[..lastDot];
            if (_mibStore.LoadedOids.TryGetValue(current, out def))
                return def;
        }
        return null;
    }

    /// <summary>Resolve OID to MIB name, e.g. "1.3.6.1.2.1.1.1.0" → "sysDescr".</summary>
    private string ResolveOidName(string oid) => ResolveOidDef(oid)?.Name ?? "";

    private DeviceProfile? BuildTempDevice()
    {
        if (SelectedSimulator == null) return null;
        var device = _deviceList.Devices.FirstOrDefault(d => d.Id == SelectedSimulator.DeviceId);
        return new DeviceProfile
        {
            IpAddress = "127.0.0.1",
            Port = SelectedSimulator.Port,
            Version = SnmpVersionOption.V2c,
            Community = device?.Community ?? "public"
        };
    }

    [RelayCommand]
    private async Task StartSimulator()
    {
        var device = SelectedDevice;
        if (device == null) return;

        if (_simulators.ContainsKey(device.Id))
        {
            LogEntries.Add($"Simulator for {device.Name} is already running.");
            return;
        }

        var walkData = await _store.LoadWalkDataAsync(device);
        if (walkData.Count == 0)
        {
            LogEntries.Add($"No walk data for {device.Name}. Record first!");
            return;
        }

        // Load MIB definitions for OID name resolution in traffic log
        await _mibStore.LoadForDeviceAsync(device);

        var sim = new SnmpSimulatorService();
        sim.LoadMibData(walkData, device.Name);

        sim.LogMessage += msg => App.Current.Dispatcher.Invoke(() =>
        {
            // Enrich OIDs in log messages with MIB names
            var enriched = Regex.Replace(msg, @"(\d+(?:\.\d+){5,})", m =>
            {
                var name = ResolveOidName(m.Value);
                return string.IsNullOrEmpty(name) ? m.Value : $"{m.Value} ({name})";
            });
            LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] [{device.Name}] {enriched}");
        });

        sim.RequestReceived += (op, oid, val, sourceIp) => App.Current.Dispatcher.Invoke(() =>
        {
            var name = ResolveOidName(oid);
            var nameTag = string.IsNullOrEmpty(name) ? "" : $" ({name})";
            TrafficLog.Add($"[{DateTime.Now:HH:mm:ss}] {sourceIp} → {device.Name} | {op} {oid}{nameTag} {val}");
            while (TrafficLog.Count > 500)
                TrafficLog.RemoveAt(0);

            // Only broadcast individual GET/SET to Angular (not GETNEXT/GETBULK to avoid flooding during WALK)
            if (op == "GET" || op == "SET")
                TrafficReceived?.Invoke(device.Name, op, oid, val, sourceIp);
        });

        var port = SimulatorPort;
        var listenIp = SimulatorListenIp;
        sim.Start(port, device.Community, listenIp);

        _simulators[device.Id] = sim;
        device.Status = DeviceStatus.Simulating;

        ActiveSimulators.Add(new SimulatorDeviceStatus
        {
            DeviceId = device.Id,
            DeviceName = device.Name,
            ListenIp = listenIp,
            Port = port,
            OidCount = walkData.Count,
            OriginalIp = device.IpAddress,
            OriginalPort = device.Port
        });

        SimulatorPort++; // Next device gets next port
    }

    [RelayCommand]
    private void StopSimulator()
    {
        var device = SelectedDevice;
        if (device == null) return;

        if (_simulators.TryRemove(device.Id, out var sim))
        {
            sim.Stop();
            sim.Dispose();
            device.Status = DeviceStatus.Idle;

            var status = ActiveSimulators.FirstOrDefault(s => s.DeviceId == device.Id);
            if (status != null)
                ActiveSimulators.Remove(status);
        }
    }

    [RelayCommand]
    private void StopAll()
    {
        foreach (var kvp in _simulators)
        {
            kvp.Value.Stop();
            kvp.Value.Dispose();
        }
        _simulators.Clear();
        ActiveSimulators.Clear();
        LogEntries.Add("All simulators stopped.");
    }

    /// <summary>
    /// Inject a recorded session into the selected simulator.
    /// Values update dynamically over time, looping the recording.
    /// </summary>
    [RelayCommand]
    private async Task InjectSession()
    {
        var device = SelectedDevice;
        if (device == null)
        {
            LogEntries.Add("Select a device first.");
            return;
        }

        if (!_simulators.TryGetValue(device.Id, out var sim))
        {
            LogEntries.Add($"No running simulator for {device.Name}. Start it first!");
            return;
        }

        if (string.IsNullOrEmpty(SelectedSessionName))
        {
            LogEntries.Add("Select a session to inject.");
            return;
        }

        var session = await _store.LoadSessionAsync(device, SelectedSessionName);
        if (session == null || session.Frames.Count == 0)
        {
            LogEntries.Add($"No recorded session '{SelectedSessionName}' for {device.Name}.");
            return;
        }

        sim.InjectionProgress += (current, total) => App.Current.Dispatcher.Invoke(() =>
        {
            InjectionStatus = $"Frame {current}/{total}";
        });

        sim.InjectionFrameApplied += (frameNum, records) => App.Current.Dispatcher.Invoke(() =>
        {
            QueryResults.Clear();
            foreach (var r in records)
            {
                QueryResults.Add(new QueryResultItem
                {
                    Time = DateTime.Now.ToString("HH:mm:ss"),
                    Operation = $"INJ#{frameNum}",
                    Oid = r.Oid,
                    ValueType = r.ValueType,
                    Value = r.Value,
                    IsSuccess = true
                });
            }

            // Log injection traffic with device → clients direction
            TrafficLog.Add($"[{DateTime.Now:HH:mm:ss}] {device.Name} → Clients | INJ#{frameNum} {records.Count} OIDs");
            while (TrafficLog.Count > 500)
                TrafficLog.RemoveAt(0);

            TrafficReceived?.Invoke(device.Name, $"INJ#{frameNum}", $"{records.Count} OIDs", $"Frame {frameNum}", "injection");
        });

        sim.StartInjection(session);
        IsInjecting = true;
        InjectionStatus = $"Injecting {session.Frames.Count} frames...";
        LogEntries.Add($"Injection started for {device.Name} — {session.Frames.Count} frames, interval {session.IntervalSeconds}s");
    }

    [RelayCommand]
    private void StopInjection()
    {
        var device = SelectedDevice;
        if (device != null && _simulators.TryGetValue(device.Id, out var sim))
        {
            sim.StopInjection();
        }
        IsInjecting = false;
        InjectionStatus = "Stopped";
    }

    [RelayCommand]
    private async Task PlayTraps()
    {
        var device = SelectedDevice;
        if (device == null) return;

        var traps = await _store.LoadTrapsAsync(device);
        if (traps.Count == 0)
        {
            LogEntries.Add($"No recorded traps for {device.Name}.");
            return;
        }

        foreach (var trap in traps)
        {
            await _trapGenerator.SendTrapAsync(trap, TrapTargetIp, TrapTargetPort);
        }
    }

    [RelayCommand]
    private async Task QueryGet()
    {
        var tempDevice = BuildTempDevice();
        if (tempDevice == null || string.IsNullOrWhiteSpace(QueryOid)) return;

        IsQuerying = true;
        try
        {
            var result = await _recorder.GetSingleAsync(tempDevice, QueryOid);
            if (result != null)
            {
                QueryResults.Insert(0, new QueryResultItem
                {
                    Time = DateTime.Now.ToString("HH:mm:ss"),
                    Operation = "GET",
                    Oid = result.Oid,
                    ValueType = result.ValueType,
                    Value = result.Value,
                    IsSuccess = true
                });

                // Auto-detect SET value type from GET response
                if (!string.IsNullOrEmpty(result.ValueType))
                {
                    SetValueType = result.ValueType;
                    SetValue = result.Value;
                }

                // Broadcast GET result so Angular panel updates
                if (SelectedSimulator != null)
                    TrafficReceived?.Invoke(SelectedSimulator.DeviceName, "GET", result.Oid, result.Value, "127.0.0.1");
            }
            else
            {
                QueryResults.Insert(0, new QueryResultItem
                {
                    Time = DateTime.Now.ToString("HH:mm:ss"),
                    Operation = "GET",
                    Oid = QueryOid,
                    Value = "No response",
                    IsSuccess = false
                });
            }
        }
        catch (Exception ex)
        {
            QueryResults.Insert(0, new QueryResultItem
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                Operation = "GET",
                Oid = QueryOid,
                Value = $"Error: {ex.Message}",
                IsSuccess = false
            });
        }
        finally
        {
            IsQuerying = false;
        }
    }

    [RelayCommand]
    private async Task QuerySet()
    {
        var tempDevice = BuildTempDevice();
        if (tempDevice == null || string.IsNullOrWhiteSpace(QueryOid)) return;

        IsQuerying = true;
        try
        {
            var success = await _recorder.SetAsync(tempDevice, QueryOid, SetValue, SetValueType);

            QueryResults.Insert(0, new QueryResultItem
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                Operation = "SET",
                Oid = QueryOid,
                ValueType = SetValueType,
                Value = success ? SetValue : "SET failed",
                IsSuccess = success
            });

            // Broadcast SET traffic so Angular receives it
            if (success && SelectedSimulator != null)
            {
                var setName = ResolveOidName(QueryOid);
                var setNameTag = string.IsNullOrEmpty(setName) ? "" : $" ({setName})";
                TrafficLog.Add($"[{DateTime.Now:HH:mm:ss}] WPF → {SelectedSimulator.DeviceName} | SET {QueryOid}{setNameTag} {SetValue}");
                while (TrafficLog.Count > 500)
                    TrafficLog.RemoveAt(0);

                TrafficReceived?.Invoke(SelectedSimulator.DeviceName, "SET", QueryOid, SetValue, "127.0.0.1");
            }

            if (success)
            {
                // Verify with GET
                var verify = await _recorder.GetSingleAsync(tempDevice, QueryOid);
                if (verify != null)
                {
                    QueryResults.Insert(0, new QueryResultItem
                    {
                        Time = DateTime.Now.ToString("HH:mm:ss"),
                        Operation = "VERIFY",
                        Oid = verify.Oid,
                        ValueType = verify.ValueType,
                        Value = verify.Value,
                        IsSuccess = true
                    });

                    // Broadcast verified value so Angular panel updates immediately
                    if (SelectedSimulator != null)
                        TrafficReceived?.Invoke(SelectedSimulator.DeviceName, "SET", verify.Oid, verify.Value, "127.0.0.1");
                }
            }
        }
        catch (Exception ex)
        {
            QueryResults.Insert(0, new QueryResultItem
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                Operation = "SET",
                Oid = QueryOid,
                Value = $"Error: {ex.Message}",
                IsSuccess = false
            });
        }
        finally
        {
            IsQuerying = false;
        }
    }

    [RelayCommand]
    private async Task QueryWalk()
    {
        var tempDevice = BuildTempDevice();
        if (tempDevice == null || string.IsNullOrWhiteSpace(QueryOid)) return;

        IsQuerying = true;
        QueryResults.Clear();

        try
        {
            var results = await _recorder.WalkDeviceAsync(tempDevice);
            var prefix = QueryOid.TrimEnd('.', '0');
            var filtered = results
                .Where(r => r.Oid.StartsWith(prefix))
                .ToList();

            foreach (var r in filtered.Take(500))
            {
                QueryResults.Add(new QueryResultItem
                {
                    Time = DateTime.Now.ToString("HH:mm:ss"),
                    Operation = "WALK",
                    Oid = r.Oid,
                    ValueType = r.ValueType,
                    Value = r.Value,
                    IsSuccess = true
                });
            }

            if (filtered.Count > 500)
            {
                QueryResults.Add(new QueryResultItem
                {
                    Time = DateTime.Now.ToString("HH:mm:ss"),
                    Operation = "INFO",
                    Oid = "",
                    Value = $"... and {filtered.Count - 500} more OIDs",
                    IsSuccess = true
                });
            }

            LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] Walk complete — {filtered.Count} OIDs from {SelectedSimulator?.DeviceName}");
        }
        catch (Exception ex)
        {
            QueryResults.Add(new QueryResultItem
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                Operation = "WALK",
                Oid = QueryOid,
                Value = $"Error: {ex.Message}",
                IsSuccess = false
            });
        }
        finally
        {
            IsQuerying = false;
        }
    }

    [RelayCommand]
    private void ClearQueryResults()
    {
        QueryResults.Clear();
    }

    /// <summary>
    /// Called when user double-clicks a Traffic Log entry. Parses OID and fills the query field.
    /// Format: [HH:mm:ss] source → device | OP OID (name) value
    /// </summary>
    [RelayCommand]
    private void SelectTrafficEntry(string? entry)
    {
        if (string.IsNullOrWhiteSpace(entry)) return;

        // Format: [HH:mm:ss] source → device | OP OID (name) value
        var match = Regex.Match(entry, @"\|\s+\w+\s+([\d\.]+)(?:\s+\(\w+\))?\s*(.*)");
        if (match.Success)
        {
            var oid = match.Groups[1].Value;
            var value = match.Groups[2].Value.Trim();
            QueryOid = oid;

            if (!string.IsNullOrEmpty(value))
                SetValue = value;

            // Resolve SNMP type from MIB definitions
            var def = ResolveOidDef(oid);
            if (def != null && !string.IsNullOrEmpty(def.Syntax))
                SetValueType = MapMibSyntaxToSnmpType(def.Syntax);
        }
    }

    /// <summary>Map MIB SYNTAX (e.g. "INTEGER", "DisplayString") to SNMP type name.</summary>
    private static string MapMibSyntaxToSnmpType(string syntax)
    {
        var s = syntax.Split('(')[0].Split('{')[0].Trim().ToUpperInvariant();
        return s switch
        {
            "INTEGER" or "INTEGER32" => "Integer32",
            "COUNTER" or "COUNTER32" => "Counter32",
            "COUNTER64" => "Counter64",
            "GAUGE" or "GAUGE32" or "UNSIGNED32" => "Gauge32",
            "TIMETICKS" => "TimeTicks",
            "IPADDRESS" => "IpAddress",
            "OBJECT IDENTIFIER" => "ObjectIdentifier",
            "DISPLAYSTRING" or "OCTET STRING" or "OCTETSTRING" => "OctetString",
            _ => "OctetString"
        };
    }

    /// <summary>
    /// Called when user double-clicks a Query Results row. Copies OID and value to query fields.
    /// </summary>
    [RelayCommand]
    private void SelectQueryResult(QueryResultItem? item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Oid)) return;

        QueryOid = item.Oid;
        if (!string.IsNullOrEmpty(item.ValueType))
            SetValueType = item.ValueType;
        if (!string.IsNullOrEmpty(item.Value))
            SetValue = item.Value;
    }
}

public class SimulatorDeviceStatus
{
    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string ListenIp { get; set; } = "0.0.0.0";
    public int Port { get; set; }
    public int OidCount { get; set; }
    public string OriginalIp { get; set; } = string.Empty;
    public int OriginalPort { get; set; }
}
