using System.IO;
using System.IO.Compression;
using System.Text.Json;
using SNMPSimMgr.Models;

namespace SNMPSimMgr.Services;

/// <summary>
/// Exports/imports all device profiles + data + MIB files as a .snmpsim zip package.
/// </summary>
public class PackageService
{
    private static readonly string DataRoot = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Data");

    private static readonly string MibDir = Path.Combine(DataRoot, "mibs");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Export all devices and their data to a .snmpsim zip file.
    /// Includes MIB files referenced by device profiles.
    /// </summary>
    public async Task ExportAsync(string outputPath, List<DeviceProfile> devices)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"snmpsim_export_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Collect and copy all MIB files into mibs/ folder
            var mibsDir = Path.Combine(tempDir, "mibs");
            Directory.CreateDirectory(mibsDir);

            // Track filename mapping to handle duplicates and build relative paths
            var copiedMibs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // absolutePath → relative path

            foreach (var device in devices)
            {
                foreach (var mibPath in device.MibFilePaths)
                {
                    if (copiedMibs.ContainsKey(mibPath)) continue;
                    if (!File.Exists(mibPath)) continue;

                    var fileName = Path.GetFileName(mibPath);

                    // Handle duplicate filenames from different paths
                    var destName = fileName;
                    int counter = 1;
                    while (File.Exists(Path.Combine(mibsDir, destName)))
                    {
                        destName = $"{Path.GetFileNameWithoutExtension(fileName)}_{counter}{Path.GetExtension(fileName)}";
                        counter++;
                    }

                    File.Copy(mibPath, Path.Combine(mibsDir, destName));
                    copiedMibs[mibPath] = $"mibs/{destName}";
                }
            }

            // Create export copies of device profiles with relative MIB paths
            var exportDevices = new List<DeviceProfile>();
            foreach (var device in devices)
            {
                var exportDevice = new DeviceProfile
                {
                    Id = device.Id,
                    Name = device.Name,
                    IpAddress = device.IpAddress,
                    Port = device.Port,
                    Version = device.Version,
                    Community = device.Community,
                    V3Credentials = device.V3Credentials,
                    Status = device.Status,
                    MibFilePaths = device.MibFilePaths
                        .Where(p => copiedMibs.ContainsKey(p))
                        .Select(p => copiedMibs[p])
                        .ToList()
                };
                exportDevices.Add(exportDevice);
            }

            var profilesJson = JsonSerializer.Serialize(exportDevices, JsonOptions);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "devices.json"), profilesJson);

            // Copy each device's data folder
            foreach (var device in devices)
            {
                var safeName = string.Join("_", device.Name.Split(Path.GetInvalidFileNameChars()));
                var sourceDir = Path.Combine(DataRoot, safeName);
                if (!Directory.Exists(sourceDir)) continue;

                var destDir = Path.Combine(tempDir, safeName);
                CopyDirectory(sourceDir, destDir);
            }

            // Write metadata
            var metadata = new PackageMetadata
            {
                ExportDate = DateTime.Now,
                DeviceCount = devices.Count,
                Version = "1.1"
            };
            var metaJson = JsonSerializer.Serialize(metadata, JsonOptions);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "package.json"), metaJson);

            // Create zip
            if (File.Exists(outputPath))
                File.Delete(outputPath);
            ZipFile.CreateFromDirectory(tempDir, outputPath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    /// <summary>
    /// Import devices and data from a .snmpsim zip file.
    /// Extracts MIB files and updates device profiles with new absolute paths.
    /// </summary>
    public async Task<List<DeviceProfile>> ImportAsync(string packagePath)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"snmpsim_import_{Guid.NewGuid():N}");

        try
        {
            ZipFile.ExtractToDirectory(packagePath, tempDir);

            // Read profiles
            var profilesPath = Path.Combine(tempDir, "devices.json");
            if (!File.Exists(profilesPath))
                throw new InvalidOperationException("Invalid package: devices.json not found.");

            var json = await File.ReadAllTextAsync(profilesPath);
            var devices = JsonSerializer.Deserialize<List<DeviceProfile>>(json, JsonOptions) ?? new();

            // Assign new IDs to avoid conflicts
            foreach (var device in devices)
                device.Id = Guid.NewGuid().ToString("N");

            // Extract MIB files to Data/mibs/ and update paths
            var importedMibsDir = Path.Combine(tempDir, "mibs");
            if (Directory.Exists(importedMibsDir))
            {
                Directory.CreateDirectory(MibDir);

                foreach (var device in devices)
                {
                    var updatedPaths = new List<string>();
                    foreach (var relativePath in device.MibFilePaths)
                    {
                        // relativePath is like "mibs/IF-MIB.mib"
                        var fileName = Path.GetFileName(relativePath);
                        var sourcePath = Path.Combine(tempDir, relativePath.Replace('/', Path.DirectorySeparatorChar));

                        if (!File.Exists(sourcePath)) continue;

                        var destPath = Path.Combine(MibDir, fileName);

                        // Don't overwrite existing MIB files with same name
                        if (!File.Exists(destPath))
                            File.Copy(sourcePath, destPath);

                        updatedPaths.Add(destPath);
                    }
                    device.MibFilePaths = updatedPaths;
                }
            }

            // Copy data folders
            foreach (var device in devices)
            {
                var safeName = string.Join("_", device.Name.Split(Path.GetInvalidFileNameChars()));
                var sourceDir = Path.Combine(tempDir, safeName);
                if (!Directory.Exists(sourceDir)) continue;

                var destDir = Path.Combine(DataRoot, safeName);
                Directory.CreateDirectory(destDir);
                CopyDirectory(sourceDir, destDir);
            }

            return devices;
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    private static void CopyDirectory(string source, string dest)
    {
        Directory.CreateDirectory(dest);

        foreach (var file in Directory.GetFiles(source))
        {
            var destFile = Path.Combine(dest, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            var destSubDir = Path.Combine(dest, Path.GetFileName(dir));
            CopyDirectory(dir, destSubDir);
        }
    }
}

public class PackageMetadata
{
    public DateTime ExportDate { get; set; }
    public int DeviceCount { get; set; }
    public string Version { get; set; } = "1.0";
}
