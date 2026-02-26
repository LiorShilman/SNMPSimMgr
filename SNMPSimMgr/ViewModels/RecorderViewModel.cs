using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SNMPSimMgr.Models;
using SNMPSimMgr.Services;

namespace SNMPSimMgr.ViewModels;

public partial class RecorderViewModel : ObservableObject
{
    private readonly SnmpRecorderService _recorder;
    private readonly TrapListenerService _trapListener;
    private readonly DeviceProfileStore _store;
    private readonly DeviceListViewModel _deviceList;
    private CancellationTokenSource? _walkCts;
    private CancellationTokenSource? _sessionCts;
    private RecordedSession? _currentSession;

    public ObservableCollection<string> LogEntries { get; } = new();
    public ObservableCollection<TrapRecord> CapturedTraps { get; } = new();
    public ObservableCollection<QueryResultItem> GetResults { get; } = new();
    public ObservableCollection<string> SessionList { get; } = new();

    [ObservableProperty] private bool _isWalking;
    [ObservableProperty] private bool _isListeningTraps;
    [ObservableProperty] private bool _isRecordingSession;
    [ObservableProperty] private int _oidCount;
    [ObservableProperty] private int _sessionFrameCount;
    [ObservableProperty] private int _sessionInterval = 10;
    [ObservableProperty] private string _sessionName = "";
    [ObservableProperty] private string? _selectedSessionName;
    [ObservableProperty] private string _walkStatus = "Idle";
    [ObservableProperty] private int _trapListenerPort = 162;
    [ObservableProperty] private string _queryOid = "1.3.6.1.2.1.1.1.0";
    [ObservableProperty] private string _setValue = "";
    [ObservableProperty] private string _setValueType = "OctetString";
    [ObservableProperty] private bool _isQuerying;

    public DeviceProfile? SelectedDevice => _deviceList.SelectedDevice;

    public RecorderViewModel(
        SnmpRecorderService recorder,
        TrapListenerService trapListener,
        DeviceProfileStore store,
        DeviceListViewModel deviceList)
    {
        _recorder = recorder;
        _trapListener = trapListener;
        _store = store;
        _deviceList = deviceList;

        _recorder.LogMessage += msg => App.Current.Dispatcher.Invoke(() =>
        {
            LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] {msg}");
            while (LogEntries.Count > 1000)
                LogEntries.RemoveAt(0);
        });

        _recorder.ProgressChanged += count => App.Current.Dispatcher.Invoke(() =>
        {
            OidCount = count;
        });

        _trapListener.LogMessage += msg => App.Current.Dispatcher.Invoke(() =>
        {
            LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] [TRAP] {msg}");
        });

        _trapListener.TrapReceived += trap => App.Current.Dispatcher.Invoke(() =>
        {
            CapturedTraps.Add(trap);
        });

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
    }

    [RelayCommand]
    private void DeleteSession()
    {
        var device = SelectedDevice;
        if (device == null || string.IsNullOrEmpty(SelectedSessionName)) return;

        _store.DeleteSession(device, SelectedSessionName);
        SessionList.Remove(SelectedSessionName);
        SelectedSessionName = null;
        LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] Session deleted.");
    }

    [RelayCommand]
    private async Task StartWalk()
    {
        var device = SelectedDevice;
        if (device == null) return;

        IsWalking = true;
        WalkStatus = "Walking...";
        OidCount = 0;
        _walkCts = new CancellationTokenSource();

        try
        {
            var results = await _recorder.WalkDeviceAsync(device, _walkCts.Token);
            await _store.SaveWalkDataAsync(device, results);
            OidCount = results.Count;
            WalkStatus = $"Done — {results.Count} OIDs captured";
            device.Status = DeviceStatus.Idle;

            GetResults.Clear();
            foreach (var r in results)
            {
                GetResults.Add(new QueryResultItem
                {
                    Time = DateTime.Now.ToString("HH:mm:ss"),
                    Operation = "WALK",
                    Oid = r.Oid,
                    ValueType = r.ValueType,
                    Value = r.Value,
                    IsSuccess = true
                });
            }
        }
        catch (OperationCanceledException)
        {
            WalkStatus = "Cancelled";
        }
        catch (Exception ex)
        {
            WalkStatus = $"Error: {ex.Message}";
        }
        finally
        {
            IsWalking = false;
            _walkCts = null;
        }
    }

    [RelayCommand]
    private void StopWalk()
    {
        _walkCts?.Cancel();
    }

    [RelayCommand]
    private void ToggleTrapListener()
    {
        if (IsListeningTraps)
        {
            _trapListener.Stop();
            IsListeningTraps = false;
        }
        else
        {
            _trapListener.Start(TrapListenerPort);
            IsListeningTraps = true;
        }
    }

    [RelayCommand]
    private async Task SaveCapturedTraps()
    {
        var device = SelectedDevice;
        if (device == null || CapturedTraps.Count == 0) return;

        await _store.SaveTrapsAsync(device, CapturedTraps.ToList());
        LogEntries.Add($"Saved {CapturedTraps.Count} traps for {device.Name}.");
    }

    [RelayCommand]
    private async Task SnmpGet()
    {
        var device = SelectedDevice;
        if (device == null)
        {
            LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] Select a device first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(QueryOid))
        {
            LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] Enter an OID.");
            return;
        }

        IsQuerying = true;
        try
        {
            var oids = QueryOid.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(o => o.Trim()).ToArray();

            if (oids.Length == 1)
            {
                var result = await _recorder.GetSingleAsync(device, oids[0]);
                if (result != null)
                {
                    GetResults.Insert(0, new QueryResultItem
                    {
                        Time = DateTime.Now.ToString("HH:mm:ss"),
                        Operation = "GET",
                        Oid = result.Oid,
                        ValueType = result.ValueType,
                        Value = result.Value,
                        IsSuccess = true
                    });
                }
                else
                {
                    GetResults.Insert(0, new QueryResultItem
                    {
                        Time = DateTime.Now.ToString("HH:mm:ss"),
                        Operation = "GET",
                        Oid = oids[0],
                        Value = "No response",
                        IsSuccess = false
                    });
                }
            }
            else
            {
                var results = await _recorder.GetMultipleAsync(device, oids);
                foreach (var r in results)
                {
                    GetResults.Insert(0, new QueryResultItem
                    {
                        Time = DateTime.Now.ToString("HH:mm:ss"),
                        Operation = "GET",
                        Oid = r.Oid,
                        ValueType = r.ValueType,
                        Value = r.Value,
                        IsSuccess = true
                    });
                }
                if (results.Count == 0)
                {
                    GetResults.Insert(0, new QueryResultItem
                    {
                        Time = DateTime.Now.ToString("HH:mm:ss"),
                        Operation = "GET",
                        Oid = string.Join(", ", oids),
                        Value = "No response",
                        IsSuccess = false
                    });
                }
            }
        }
        catch (Exception ex)
        {
            GetResults.Insert(0, new QueryResultItem
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
    private async Task SnmpSet()
    {
        var device = SelectedDevice;
        if (device == null)
        {
            LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] Select a device first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(QueryOid) || string.IsNullOrWhiteSpace(SetValue))
        {
            LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] Enter OID and value.");
            return;
        }

        IsQuerying = true;
        try
        {
            var success = await _recorder.SetAsync(device, QueryOid, SetValue, SetValueType);

            GetResults.Insert(0, new QueryResultItem
            {
                Time = DateTime.Now.ToString("HH:mm:ss"),
                Operation = "SET",
                Oid = QueryOid,
                ValueType = SetValueType,
                Value = success ? SetValue : "SET failed",
                IsSuccess = success
            });

            if (success)
            {
                LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] SET {QueryOid} = ({SetValueType}) {SetValue} — OK");
            }
            else
            {
                LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] SET {QueryOid} — Failed");
            }
        }
        catch (Exception ex)
        {
            GetResults.Insert(0, new QueryResultItem
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
    private async Task TestConnection()
    {
        var device = SelectedDevice;
        if (device == null) return;

        try
        {
            var result = await _recorder.GetSingleAsync(device, "1.3.6.1.2.1.1.1.0");
            if (result != null)
                LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] Connection OK: sysDescr = {result.Value}");
            else
                LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] Connection failed - no response.");
        }
        catch (Exception ex)
        {
            LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] Connection error: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ClearResults()
    {
        GetResults.Clear();
    }

    /// <summary>
    /// Start recording a session: periodic walks at configurable intervals,
    /// capturing how OID values change over time.
    /// </summary>
    [RelayCommand]
    private async Task StartSessionRecording()
    {
        var device = SelectedDevice;
        if (device == null)
        {
            LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] Select a device first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(SessionName))
            SessionName = $"rec_{DateTime.Now:yyyy-MM-dd_HH-mm}";

        IsRecordingSession = true;
        SessionFrameCount = 0;
        _sessionCts = new CancellationTokenSource();

        _currentSession = new RecordedSession
        {
            Name = SessionName,
            DeviceName = device.Name,
            StartTime = DateTime.Now,
            IntervalSeconds = SessionInterval
        };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] Session recording started (every {SessionInterval}s)...");

        try
        {
            while (!_sessionCts.Token.IsCancellationRequested)
            {
                var walkSw = System.Diagnostics.Stopwatch.StartNew();
                var results = await _recorder.WalkDeviceAsync(device, _sessionCts.Token);
                walkSw.Stop();

                var frame = new RecordedFrame
                {
                    ElapsedMs = sw.ElapsedMilliseconds,
                    Records = results
                };
                _currentSession.Frames.Add(frame);
                SessionFrameCount = _currentSession.Frames.Count;
                OidCount = results.Count;

                GetResults.Clear();
                foreach (var r in results)
                {
                    GetResults.Add(new QueryResultItem
                    {
                        Time = DateTime.Now.ToString("HH:mm:ss"),
                        Operation = $"REC#{SessionFrameCount}",
                        Oid = r.Oid,
                        ValueType = r.ValueType,
                        Value = r.Value,
                        IsSuccess = true
                    });
                }

                var remaining = TimeSpan.FromSeconds(SessionInterval) - walkSw.Elapsed;
                if (remaining > TimeSpan.Zero)
                {
                    LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] Frame #{SessionFrameCount} captured — {results.Count} OIDs (walk {walkSw.Elapsed.TotalSeconds:F1}s, next in {remaining.TotalSeconds:F1}s)");
                    await Task.Delay(remaining, _sessionCts.Token);
                }
                else
                {
                    LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] Frame #{SessionFrameCount} captured — {results.Count} OIDs (walk {walkSw.Elapsed.TotalSeconds:F1}s > interval {SessionInterval}s, continuing immediately)");
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] Recording error: {ex.Message}");
        }
        finally
        {
            sw.Stop();
            IsRecordingSession = false;
        }
    }

    [RelayCommand]
    private async Task StopSessionRecording()
    {
        IsRecordingSession = false;
        _sessionCts?.Cancel();

        if (_currentSession != null && _currentSession.Frames.Count > 0)
        {
            var device = SelectedDevice;
            if (device != null)
            {
                await _store.SaveSessionAsync(device, _currentSession);
                LogEntries.Add($"[{DateTime.Now:HH:mm:ss}] Session '{_currentSession.Name}' saved — {_currentSession.Frames.Count} frames");
                await RefreshSessionList();
            }
        }

        _currentSession = null;
        _sessionCts = null;
        SessionName = "";
    }
}
