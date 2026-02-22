using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SNMPSimMgr.Models;
using SNMPSimMgr.Services;

namespace SNMPSimMgr.ViewModels;

public partial class ScenarioViewModel : ObservableObject
{
    private readonly SimulatorViewModel _simulator;
    private CancellationTokenSource? _playCts;

    public ObservableCollection<ScenarioEvent> Events { get; } = new();

    [ObservableProperty] private string _scenarioName = "New Scenario";
    [ObservableProperty] private string _scenarioDescription = string.Empty;
    [ObservableProperty] private bool _scenarioLoop;
    [ObservableProperty] private bool _isPlaying;
    [ObservableProperty] private string _playStatus = string.Empty;
    [ObservableProperty] private int _currentEventIndex = -1;
    [ObservableProperty] private int _elapsedSeconds;

    // New event form
    [ObservableProperty] private int _newDelay;
    [ObservableProperty] private string _newLabel = string.Empty;
    [ObservableProperty] private string _newOid = string.Empty;
    [ObservableProperty] private string _newValue = string.Empty;
    [ObservableProperty] private string _newValueType = "Integer32";

    // Presets
    public ObservableCollection<string> ValueTypes { get; } = new()
    {
        "Integer32", "OctetString", "Counter32", "Counter64", "Gauge32", "TimeTicks"
    };

    public ScenarioViewModel(SimulatorViewModel simulator)
    {
        _simulator = simulator;
    }

    [RelayCommand]
    private void AddEvent()
    {
        if (string.IsNullOrWhiteSpace(NewOid)) return;

        var evt = new ScenarioEvent
        {
            DelaySeconds = NewDelay,
            Label = string.IsNullOrWhiteSpace(NewLabel) ? $"Set {NewOid}" : NewLabel,
            Oid = NewOid,
            Value = NewValue,
            ValueType = NewValueType
        };

        // Insert in order
        var idx = Events.Count;
        for (int i = 0; i < Events.Count; i++)
        {
            if (Events[i].DelaySeconds > evt.DelaySeconds)
            {
                idx = i;
                break;
            }
        }
        Events.Insert(idx, evt);

        // Reset form, advance delay
        NewLabel = string.Empty;
        NewOid = string.Empty;
        NewValue = string.Empty;
        NewDelay = evt.DelaySeconds + 10;
    }

    [RelayCommand]
    private void RemoveEvent(ScenarioEvent? evt)
    {
        if (evt != null) Events.Remove(evt);
    }

    [RelayCommand]
    private void AddPresetInterfaceDown()
    {
        var baseDelay = NewDelay;
        Events.Add(new ScenarioEvent
        {
            DelaySeconds = baseDelay,
            Label = "Interface eth0 goes DOWN",
            Oid = "1.3.6.1.2.1.2.2.1.8.1",
            Value = "2",
            ValueType = "Integer32"
        });
        Events.Add(new ScenarioEvent
        {
            DelaySeconds = baseDelay + 30,
            Label = "Interface eth0 comes UP",
            Oid = "1.3.6.1.2.1.2.2.1.8.1",
            Value = "1",
            ValueType = "Integer32"
        });
        NewDelay = baseDelay + 60;
    }

    [RelayCommand]
    private void AddPresetHighCpu()
    {
        var baseDelay = NewDelay;
        Events.Add(new ScenarioEvent
        {
            DelaySeconds = baseDelay,
            Label = "CPU spike to 95%",
            Oid = "1.3.6.1.2.1.25.3.3.1.2.1",
            Value = "95",
            ValueType = "Integer32"
        });
        Events.Add(new ScenarioEvent
        {
            DelaySeconds = baseDelay + 60,
            Label = "CPU returns to normal (15%)",
            Oid = "1.3.6.1.2.1.25.3.3.1.2.1",
            Value = "15",
            ValueType = "Integer32"
        });
        NewDelay = baseDelay + 90;
    }

    [RelayCommand]
    private async Task PlayScenario()
    {
        if (Events.Count == 0 || _simulator.ActiveSimulators.Count == 0)
        {
            PlayStatus = "No events or no active simulator!";
            return;
        }

        IsPlaying = true;
        _playCts = new CancellationTokenSource();
        ElapsedSeconds = 0;
        CurrentEventIndex = -1;

        try
        {
            do
            {
                PlayStatus = "Playing scenario...";
                var sortedEvents = Events.OrderBy(e => e.DelaySeconds).ToList();
                int lastDelay = 0;

                for (int i = 0; i < sortedEvents.Count; i++)
                {
                    _playCts.Token.ThrowIfCancellationRequested();

                    var evt = sortedEvents[i];
                    int waitSeconds = evt.DelaySeconds - lastDelay;

                    if (waitSeconds > 0)
                    {
                        for (int s = 0; s < waitSeconds; s++)
                        {
                            _playCts.Token.ThrowIfCancellationRequested();
                            await Task.Delay(1000, _playCts.Token);
                            ElapsedSeconds++;
                            PlayStatus = $"T+{ElapsedSeconds}s — waiting for: {evt.Label}";
                        }
                    }

                    // Apply event to all active simulators
                    App.Current.Dispatcher.Invoke(() =>
                    {
                        CurrentEventIndex = i;
                        PlayStatus = $"T+{ElapsedSeconds}s — {evt.Label}";

                        foreach (var simStatus in _simulator.ActiveSimulators)
                        {
                            var sim = GetSimulatorByDeviceId(simStatus.DeviceId);
                            sim?.SetValue(evt.Oid, evt.Value);
                        }
                    });

                    lastDelay = evt.DelaySeconds;
                }

                if (!ScenarioLoop) break;

                PlayStatus = "Loop — restarting scenario...";
                ElapsedSeconds = 0;
                await Task.Delay(2000, _playCts.Token);

            } while (ScenarioLoop);

            PlayStatus = "Scenario complete";
        }
        catch (OperationCanceledException)
        {
            PlayStatus = "Scenario stopped";
        }
        finally
        {
            IsPlaying = false;
            CurrentEventIndex = -1;
        }
    }

    [RelayCommand]
    private void StopScenario()
    {
        _playCts?.Cancel();
    }

    private SnmpSimulatorService? GetSimulatorByDeviceId(string deviceId)
    {
        // Access via reflection since _simulators is private
        var field = typeof(SimulatorViewModel)
            .GetField("_simulators", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field?.GetValue(_simulator) is System.Collections.Concurrent.ConcurrentDictionary<string, SnmpSimulatorService> dict)
        {
            dict.TryGetValue(deviceId, out var sim);
            return sim;
        }
        return null;
    }

    [RelayCommand]
    private async Task SaveScenario()
    {
        var scenario = new SimulationScenario
        {
            Name = ScenarioName,
            Description = ScenarioDescription,
            Loop = ScenarioLoop,
            Events = Events.ToList()
        };

        var dialog = new SaveFileDialog
        {
            Filter = "Scenario (*.scenario.json)|*.scenario.json",
            FileName = ScenarioName.Replace(" ", "_")
        };

        if (dialog.ShowDialog() != true) return;

        var json = JsonSerializer.Serialize(scenario, new JsonSerializerOptions { WriteIndented = true });
        await System.IO.File.WriteAllTextAsync(dialog.FileName, json);
        PlayStatus = $"Saved: {System.IO.Path.GetFileName(dialog.FileName)}";
    }

    [RelayCommand]
    private async Task LoadScenario()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Scenario (*.scenario.json)|*.scenario.json|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog() != true) return;

        var json = await System.IO.File.ReadAllTextAsync(dialog.FileName);
        var scenario = JsonSerializer.Deserialize<SimulationScenario>(json);
        if (scenario == null) return;

        ScenarioName = scenario.Name;
        ScenarioDescription = scenario.Description;
        ScenarioLoop = scenario.Loop;
        Events.Clear();
        foreach (var evt in scenario.Events)
            Events.Add(evt);

        PlayStatus = $"Loaded: {scenario.Name} ({Events.Count} events)";
    }

    [RelayCommand]
    private void ClearEvents()
    {
        Events.Clear();
        NewDelay = 0;
        PlayStatus = string.Empty;
    }
}
