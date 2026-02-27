using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;
using Newtonsoft.Json;
using SNMPSimMgr.Models;
using SNMPSimMgr.Services;
using SNMPSimMgr.ViewModels;

namespace SNMPSimMgr.Hubs;

[HubName("snmpHub")]
public class SnmpHub : Hub
{
    // Static service references — set once during App startup.
    // SignalR 2.0 creates a new Hub instance per request; statics are the
    // standard pattern when there is no DI container.
    public static SnmpRecorderService? Recorder { get; set; }
    public static MibPanelExportService? ExportService { get; set; }
    public static DeviceProfileStore? Store { get; set; }
    public static SimulatorViewModel? SimulatorVm { get; set; }
    public static DeviceListViewModel? DeviceListVm { get; set; }
    public static MibStore? MibStoreRef { get; set; }

    // ── Schema cache ─────────────────────────────────────────────────
    // Avoids re-parsing MIBs and rebuilding the schema on every device switch.
    // Key = deviceId, Value = (cacheKey based on MIB file paths, cached schema).
    private static readonly Dictionary<string, (string cacheKey, MibPanelSchema schema)> SchemaCache = new();

    private static string BuildCacheKey(DeviceProfile device)
    {
        // Sorted MIB file paths → deterministic key. If user adds/removes MIBs, key changes.
        var paths = device.MibFilePaths?.OrderBy(p => p) ?? Enumerable.Empty<string>();
        return string.Join("|", paths);
    }

    /// <summary>Invalidate cached schema for a device (call when MIBs change).</summary>
    public static void InvalidateSchemaCache(string deviceId)
    {
        lock (SchemaCache) { SchemaCache.Remove(deviceId); }
    }

    // ── Server methods (called by Angular) ──────────────────────────

    /// <summary>Send an SNMP SET to a running simulator.</summary>
    public async Task<SetResult> SendSet(string deviceId, string oid, string value, string valueType)
    {
        try
        {
            if (Store == null || Recorder == null)
                return new SetResult { Success = false, Message = "Services not initialized" };

            var profiles = await Store.LoadProfilesAsync();
            var device = profiles.FirstOrDefault(d => d.Id == deviceId);
            if (device == null)
                return new SetResult { Success = false, Message = $"Device {deviceId} not found" };

            // Locate the running simulator's local port
            var sim = SimulatorVm?.ActiveSimulators.FirstOrDefault(s => s.DeviceId == deviceId);
            if (sim == null)
                return new SetResult { Success = false, Message = "Simulator not running for this device" };

            var target = new DeviceProfile
            {
                IpAddress = "127.0.0.1",
                Port = sim.Port,
                Version = SnmpVersionOption.V2c,
                Community = device.Community
            };

            var success = await Recorder.SetAsync(target, oid, value, valueType);
            return new SetResult
            {
                Success = success,
                Message = success ? "SET acknowledged" : "SET failed"
            };
        }
        catch (Exception ex)
        {
            return new SetResult { Success = false, Message = ex.Message };
        }
    }

    /// <summary>Build and return a full MIB panel schema for a device.</summary>
    public async Task<object?> RequestSchema(string deviceId)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[SnmpHub] RequestSchema called for: {deviceId}");

            if (Store == null || ExportService == null || MibStoreRef == null)
            {
                System.Diagnostics.Debug.WriteLine($"[SnmpHub] Service null — Store={Store != null}, Export={ExportService != null}, MibStore={MibStoreRef != null}");
                return null;
            }

            var profiles = await Store.LoadProfilesAsync();
            var device = profiles.FirstOrDefault(d => d.Id == deviceId);
            if (device == null)
            {
                System.Diagnostics.Debug.WriteLine($"[SnmpHub] Device not found: {deviceId}. Known: {string.Join(", ", profiles.Select(p => p.Id))}");
                return null;
            }

            // Check schema cache — skip MIB reload + schema build if MIBs haven't changed
            var cacheKey = BuildCacheKey(device);
            lock (SchemaCache)
            {
                if (SchemaCache.TryGetValue(deviceId, out var cached) && cached.cacheKey == cacheKey)
                {
                    System.Diagnostics.Debug.WriteLine($"[SnmpHub] Schema cache HIT for {device.Name} ({cached.schema.TotalFields} fields)");
                    return cached.schema;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[SnmpHub] Schema cache MISS for {device.Name}, loading MIBs...");

            // Skip UI updates (ObservableCollection) — avoids Dispatcher deadlocks
            await MibStoreRef.LoadForDeviceAsync(device, updateUI: false);

            System.Diagnostics.Debug.WriteLine("[SnmpHub] Building schema...");
            var schema = await ExportService.BuildSchemaAsync(device);
            System.Diagnostics.Debug.WriteLine($"[SnmpHub] Schema built: {schema?.TotalFields} fields, {schema?.Modules?.Count} modules");

            // Store in cache
            if (schema != null)
                lock (SchemaCache) { SchemaCache[deviceId] = (cacheKey, schema); }

            return schema;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SnmpHub] RequestSchema ERROR: {ex}");
            return null;
        }
    }

    /// <summary>Batch GET all scalar OIDs and return updated values.</summary>
    public async Task<Dictionary<string, string>> RequestRefresh(string deviceId)
    {
        var result = new Dictionary<string, string>();
        try
        {
            System.Diagnostics.Debug.WriteLine($"[SnmpHub] RequestRefresh called for: {deviceId}");

            if (Store == null || Recorder == null || ExportService == null || MibStoreRef == null)
            {
                System.Diagnostics.Debug.WriteLine("[SnmpHub] RequestRefresh: services null");
                return result;
            }

            var profiles = await Store.LoadProfilesAsync();
            var device = profiles.FirstOrDefault(d => d.Id == deviceId);
            if (device == null) { System.Diagnostics.Debug.WriteLine("[SnmpHub] RequestRefresh: device not found"); return result; }

            var sim = SimulatorVm?.ActiveSimulators.FirstOrDefault(s => s.DeviceId == deviceId);
            if (sim == null) { System.Diagnostics.Debug.WriteLine("[SnmpHub] RequestRefresh: simulator not running"); return result; }

            var target = new DeviceProfile
            {
                IpAddress = "127.0.0.1",
                Port = sim.Port,
                Version = SnmpVersionOption.V2c,
                Community = device.Community
            };

            // Use cached schema for OID collection — avoid re-parsing MIBs
            MibPanelSchema? schema = null;
            var cacheKey = BuildCacheKey(device);
            bool cacheHit = false;
            lock (SchemaCache)
            {
                if (SchemaCache.TryGetValue(deviceId, out var cached) && cached.cacheKey == cacheKey)
                {
                    schema = cached.schema;
                    cacheHit = true;
                }
            }

            if (!cacheHit)
            {
                System.Diagnostics.Debug.WriteLine("[SnmpHub] RequestRefresh: schema cache MISS, rebuilding...");

                // Skip UI updates (ObservableCollection) — avoids Dispatcher deadlocks
                await MibStoreRef.LoadForDeviceAsync(device, updateUI: false);

                schema = await ExportService.BuildSchemaAsync(device);
                if (schema != null)
                    lock (SchemaCache) { SchemaCache[deviceId] = (cacheKey, schema); }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[SnmpHub] RequestRefresh: schema cache HIT, skipping MIB reload");
            }

            if (schema == null) return result;

            // Collect all OIDs: scalars + table cells
            var allOids = new List<string>();

            // Scalar OIDs (append .0 for instance)
            allOids.AddRange(schema.Modules
                .SelectMany(m => m.Scalars)
                .Select(f => f.Oid.EndsWith(".0") ? f.Oid : f.Oid + ".0"));

            // Table cell OIDs: column.oid + "." + row.index
            foreach (var module in schema.Modules)
            foreach (var table in module.Tables)
            foreach (var row in table.Rows)
            foreach (var col in table.Columns)
                allOids.Add(col.Oid + "." + row.Index);

            System.Diagnostics.Debug.WriteLine($"[SnmpHub] RequestRefresh: fetching {allOids.Count} OIDs from port {sim.Port}");

            // Batch GET in groups of 20 — tolerate per-batch failures
            for (int i = 0; i < allOids.Count; i += 20)
            {
                try
                {
                    var batch = allOids.Skip(i).Take(20);
                    var records = await Recorder.GetMultipleAsync(target, batch);
                    foreach (var r in records)
                        result[r.Oid] = r.Value;
                }
                catch (Exception batchEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[SnmpHub] Batch GET failed (offset {i}): {batchEx.Message}");
                }
            }

            System.Diagnostics.Debug.WriteLine($"[SnmpHub] RequestRefresh: got {result.Count} values");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SnmpHub] RequestRefresh ERROR: {ex}");
        }
        return result;
    }

    /// <summary>Send an IDD SET (non-SNMP) to WPF for handling.</summary>
    public Task<SetResult> SendIddSet(string deviceId, string fieldId, string value)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[SnmpHub] SendIddSet: device={deviceId}, field={fieldId}, value={value}");

            if (SimulatorVm == null)
                return Task.FromResult(new SetResult { Success = false, Message = "SimulatorVM not initialized" });

            // Fire event — WPF handles the actual IDD communication
            SimulatorVm.RaiseIddSet(deviceId, fieldId, value);

            return Task.FromResult(new SetResult
            {
                Success = true,
                Message = "IDD SET dispatched"
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new SetResult { Success = false, Message = ex.Message });
        }
    }

    /// <summary>List all known devices with their simulator status.</summary>
    public async Task<List<DeviceInfo>> GetDevices()
    {
        if (Store == null) return new List<DeviceInfo>();

        var profiles = await Store.LoadProfilesAsync();
        return profiles.Select(d =>
        {
            var sim = SimulatorVm?.ActiveSimulators.FirstOrDefault(s => s.DeviceId == d.Id);
            return new DeviceInfo
            {
                Id = d.Id,
                Name = d.Name,
                IpAddress = d.IpAddress,
                Port = d.Port,
                IsSimulating = sim != null,
                SimulatorPort = sim?.Port ?? 0
            };
        }).ToList();
    }

    // ── Connection lifecycle ────────────────────────────────────────

    public override Task OnConnected()
    {
        System.Diagnostics.Debug.WriteLine($"[SignalR] Client connected: {Context.ConnectionId}");
        return base.OnConnected();
    }

    public override Task OnDisconnected(bool stopCalled)
    {
        System.Diagnostics.Debug.WriteLine($"[SignalR] Client disconnected: {Context.ConnectionId}");
        return base.OnDisconnected(stopCalled);
    }

    // ── Static broadcast helpers (called from WPF event handlers) ──

    public static void BroadcastTraffic(string deviceName, string op, string oid, string value, string sourceIp)
    {
        System.Diagnostics.Debug.WriteLine($"[SignalR] BroadcastTraffic: {op} {oid} = '{value}' (device={deviceName}, src={sourceIp})");
        var context = GlobalHost.ConnectionManager.GetHubContext<SnmpHub>();
        context.Clients.All.onTrafficReceived(new
        {
            deviceName,
            operation = op,
            oid,
            value,
            sourceIp,
            timestamp = DateTime.UtcNow
        });
    }

    public static void BroadcastTrap(TrapRecord trap)
    {
        var context = GlobalHost.ConnectionManager.GetHubContext<SnmpHub>();
        context.Clients.All.onTrapReceived(new
        {
            oid = trap.Oid,
            sourceIp = trap.SourceIp,
            timestamp = trap.Timestamp,
            variableBindings = trap.VariableBindings.Select(v => new
            {
                oid = v.Oid,
                value = v.Value,
                valueType = v.ValueType
            })
        });
    }

    public static void BroadcastDeviceStatus(string deviceId, string deviceName, string status)
    {
        var context = GlobalHost.ConnectionManager.GetHubContext<SnmpHub>();
        context.Clients.All.onDeviceStatusChanged(new
        {
            deviceId,
            deviceName,
            status,
            timestamp = DateTime.UtcNow
        });
    }

    public static void BroadcastMibUpdate(string deviceId, Dictionary<string, string> updatedValues)
    {
        var context = GlobalHost.ConnectionManager.GetHubContext<SnmpHub>();
        context.Clients.All.onMibUpdated(new
        {
            deviceId,
            values = updatedValues,
            timestamp = DateTime.UtcNow
        });
    }
}

// ── DTOs ────────────────────────────────────────────────────────

public class SetResult
{
    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; } = string.Empty;
}

public class DeviceInfo
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("ipAddress")]
    public string IpAddress { get; set; } = string.Empty;

    [JsonProperty("port")]
    public int Port { get; set; }

    [JsonProperty("isSimulating")]
    public bool IsSimulating { get; set; }

    [JsonProperty("simulatorPort")]
    public int SimulatorPort { get; set; }
}
