using System.IO;
using System.IO.Compression;
using System.Text.Json;
using SNMPSimMgr.Models;

namespace SNMPSimMgr.Services;

/// <summary>
/// Exports/imports all device profiles + data as a .snmpsim zip package.
/// </summary>
public class PackageService
{
    private static readonly string DataRoot = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Data");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Export all devices and their data to a .snmpsim zip file.
    /// </summary>
    public async Task ExportAsync(string outputPath, List<DeviceProfile> devices)
    {
        // Create temp staging directory
        var tempDir = Path.Combine(Path.GetTempPath(), $"snmpsim_export_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Write profiles
            var profilesJson = JsonSerializer.Serialize(devices, JsonOptions);
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
                Version = "1.0"
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
    /// Returns the list of imported device profiles.
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
