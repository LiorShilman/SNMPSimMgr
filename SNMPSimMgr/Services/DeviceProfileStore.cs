using System.IO;
using System.Text.Json;
using SNMPSimMgr.Models;

namespace SNMPSimMgr.Services;

public class DeviceProfileStore
{
    private static readonly string DataRoot = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Data");

    private static readonly string SchemasRoot = Path.Combine(DataRoot, "schemas");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public DeviceProfileStore()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(SchemasRoot);
    }

    private string ProfilesPath => Path.Combine(DataRoot, "devices.json");

    private string DeviceFolder(DeviceProfile device)
    {
        var safe = string.Join("_", device.Name.Split(Path.GetInvalidFileNameChars()));
        var path = Path.Combine(DataRoot, safe);
        Directory.CreateDirectory(path);
        return path;
    }

    public async Task<List<DeviceProfile>> LoadProfilesAsync()
    {
        if (!File.Exists(ProfilesPath))
            return new List<DeviceProfile>();

        var json = await Task.Run(() => File.ReadAllText(ProfilesPath));
        var profiles = JsonSerializer.Deserialize<List<DeviceProfile>>(json, JsonOptions) ?? new();

        // Auto-detect schema JSON files for devices without SchemaPath
        foreach (var device in profiles)
        {
            if (string.IsNullOrEmpty(device.SchemaPath))
            {
                var schemaFile = Path.Combine(SchemasRoot, device.Name + ".json");
                if (File.Exists(schemaFile))
                    device.SchemaPath = schemaFile;
            }
        }

        return profiles;
    }

    public async Task SaveProfilesAsync(List<DeviceProfile> profiles)
    {
        var json = JsonSerializer.Serialize(profiles, JsonOptions);
        await Task.Run(() => File.WriteAllText(ProfilesPath, json));
    }

    public async Task SaveWalkDataAsync(DeviceProfile device, List<SnmpRecord> records)
    {
        var path = Path.Combine(DeviceFolder(device), "walk.json");
        var json = JsonSerializer.Serialize(records, JsonOptions);
        await Task.Run(() => File.WriteAllText(path, json));
    }

    public async Task<List<SnmpRecord>> LoadWalkDataAsync(DeviceProfile device)
    {
        var path = Path.Combine(DeviceFolder(device), "walk.json");
        if (!File.Exists(path)) return new();
        var json = await Task.Run(() => File.ReadAllText(path));
        return JsonSerializer.Deserialize<List<SnmpRecord>>(json, JsonOptions) ?? new();
    }

    public async Task SaveTrapsAsync(DeviceProfile device, List<TrapRecord> traps)
    {
        var path = Path.Combine(DeviceFolder(device), "traps.json");
        var json = JsonSerializer.Serialize(traps, JsonOptions);
        await Task.Run(() => File.WriteAllText(path, json));
    }

    public async Task<List<TrapRecord>> LoadTrapsAsync(DeviceProfile device)
    {
        var path = Path.Combine(DeviceFolder(device), "traps.json");
        if (!File.Exists(path)) return new();
        var json = await Task.Run(() => File.ReadAllText(path));
        return JsonSerializer.Deserialize<List<TrapRecord>>(json, JsonOptions) ?? new();
    }

    public async Task SaveScenariosAsync(DeviceProfile device, List<TrapScenario> scenarios)
    {
        var path = Path.Combine(DeviceFolder(device), "scenarios.json");
        var json = JsonSerializer.Serialize(scenarios, JsonOptions);
        await Task.Run(() => File.WriteAllText(path, json));
    }

    public async Task<List<TrapScenario>> LoadScenariosAsync(DeviceProfile device)
    {
        var path = Path.Combine(DeviceFolder(device), "scenarios.json");
        if (!File.Exists(path)) return new();
        var json = await Task.Run(() => File.ReadAllText(path));
        return JsonSerializer.Deserialize<List<TrapScenario>>(json, JsonOptions) ?? new();
    }

    public bool HasWalkData(DeviceProfile device)
    {
        var path = Path.Combine(DeviceFolder(device), "walk.json");
        return File.Exists(path);
    }

    private string SessionsFolder(DeviceProfile device)
    {
        var path = Path.Combine(DeviceFolder(device), "sessions");
        Directory.CreateDirectory(path);
        return path;
    }

    private string SafeFileName(string name)
    {
        return string.Join("_", name.Split(Path.GetInvalidFileNameChars()));
    }

    public async Task SaveSessionAsync(DeviceProfile device, RecordedSession session)
    {
        var folder = SessionsFolder(device);
        var fileName = SafeFileName(session.Name) + ".json";
        var path = Path.Combine(folder, fileName);
        var json = JsonSerializer.Serialize(session, JsonOptions);
        await Task.Run(() => File.WriteAllText(path, json));
    }

    public async Task<RecordedSession?> LoadSessionAsync(DeviceProfile device, string sessionName)
    {
        var folder = SessionsFolder(device);
        var fileName = SafeFileName(sessionName) + ".json";
        var path = Path.Combine(folder, fileName);
        if (!File.Exists(path)) return null;
        var json = await Task.Run(() => File.ReadAllText(path));
        return JsonSerializer.Deserialize<RecordedSession>(json, JsonOptions);
    }

    public async Task<List<string>> ListSessionNamesAsync(DeviceProfile device)
    {
        var folder = SessionsFolder(device);

        // Migrate old session.json if it exists
        var oldPath = Path.Combine(DeviceFolder(device), "session.json");
        if (File.Exists(oldPath))
        {
            var json = await Task.Run(() => File.ReadAllText(oldPath));
            var oldSession = JsonSerializer.Deserialize<RecordedSession>(json, JsonOptions);
            if (oldSession != null)
            {
                if (string.IsNullOrEmpty(oldSession.Name))
                    oldSession.Name = "session_migrated";
                var newPath = Path.Combine(folder, SafeFileName(oldSession.Name) + ".json");
                await Task.Run(() => File.WriteAllText(newPath, JsonSerializer.Serialize(oldSession, JsonOptions)));
            }
            File.Delete(oldPath);
        }

        if (!Directory.Exists(folder))
            return new List<string>();

        return Directory.GetFiles(folder, "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .OrderByDescending(n => n)
            .ToList();
    }

    public void DeleteSession(DeviceProfile device, string sessionName)
    {
        var folder = SessionsFolder(device);
        var path = Path.Combine(folder, SafeFileName(sessionName) + ".json");
        if (File.Exists(path))
            File.Delete(path);
    }
}
