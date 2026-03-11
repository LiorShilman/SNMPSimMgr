using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Text.Json;
using SNMPSimMgr.Models;

namespace SNMPSimMgr.Services
{
    public class DeviceProfileStore
    {
        private static readonly string DataRoot = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Data");

        private static readonly string SchemasRoot = Path.Combine(DataRoot, "schemas");

        private static readonly JsonSerializerOptions  JsonOptions = new JsonSerializerOptions() {
            WriteIndented = true
        };

        public DeviceProfileStore()
        {
            Directory.CreateDirectory(DataRoot);
            Directory.CreateDirectory(SchemasRoot);
        }

        private string ProfilesPath => Path.Combine(DataRoot, "devices.json");

        /// <summary>
        /// Resolves a path that may be relative (to DataRoot) or absolute.
        /// Relative paths like "schemas/ACU.json" or "MIBs/IF-MIB.mib" resolve from Data/.
        /// </summary>
        public static string ResolvePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (Path.IsPathRooted(path)) return path;
            return Path.GetFullPath(Path.Combine(DataRoot, path));
        }

        /// <summary>
        /// Converts an absolute path to a relative path (from DataRoot) if it falls under Data/.
        /// Otherwise returns the original absolute path.
        /// </summary>
        public static string ToRelativePath(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath)) return absolutePath;
            if (!Path.IsPathRooted(absolutePath)) return absolutePath; // already relative

            var dataRootFull = DataRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                               + Path.DirectorySeparatorChar;
            if (absolutePath.StartsWith(dataRootFull, StringComparison.OrdinalIgnoreCase))
            {
                return absolutePath.Substring(dataRootFull.Length).Replace('\\', '/');
            }

            // Check parent of DataRoot (e.g. exe directory) for MIBs folder at app level
            var appRoot = Path.GetDirectoryName(DataRoot)
                          .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                          + Path.DirectorySeparatorChar;
            if (absolutePath.StartsWith(appRoot, StringComparison.OrdinalIgnoreCase))
            {
                return absolutePath.Substring(appRoot.Length).Replace('\\', '/');
            }

            return absolutePath; // outside app directory — keep absolute
        }

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
            var profiles = JsonSerializer.Deserialize<List<DeviceProfile>>(json, JsonOptions) ?? new List<DeviceProfile>();

            foreach (var device in profiles)
            {
                // Resolve relative paths to absolute for runtime use
                device.SchemaPath = ResolvePath(device.SchemaPath);
                device.MibFilePaths = device.MibFilePaths
                    .Select(p => ResolvePath(p))
                    .ToList();

                // Auto-detect schema JSON files for devices without SchemaPath
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
            // Convert absolute paths to relative before saving (portable JSON)
            var toSave = profiles.Select(d => new DeviceProfile
            {
                Id = d.Id,
                Name = d.Name,
                IpAddress = d.IpAddress,
                Port = d.Port,
                Version = d.Version,
                Community = d.Community,
                V3Credentials = d.V3Credentials,
                Status = d.Status,
                IddFields = d.IddFields,
                SchemaPath = ToRelativePath(d.SchemaPath),
                MibFilePaths = d.MibFilePaths
                    .Select(p => ToRelativePath(p))
                    .ToList()
            }).ToList();

            var json = JsonSerializer.Serialize(toSave, JsonOptions);
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
            if (!File.Exists(path)) return new List<SnmpRecord>();
            var json = await Task.Run(() => File.ReadAllText(path));
            return JsonSerializer.Deserialize<List<SnmpRecord>>(json, JsonOptions) ?? new List<SnmpRecord>();
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
            if (!File.Exists(path)) return new List<TrapRecord>();
            var json = await Task.Run(() => File.ReadAllText(path));
            return JsonSerializer.Deserialize<List<TrapRecord>>(json, JsonOptions) ?? new List<TrapRecord>();
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
            if (!File.Exists(path)) return new List<TrapScenario>();
            var json = await Task.Run(() => File.ReadAllText(path));
            return JsonSerializer.Deserialize<List<TrapScenario>>(json, JsonOptions) ?? new List<TrapScenario>();
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

        public async Task<RecordedSession> LoadSessionAsync(DeviceProfile device, string sessionName)
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
}
