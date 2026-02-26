using System.Collections.ObjectModel;
using System.IO;
using SNMPSimMgr.Models;

namespace SNMPSimMgr.Services;

public class MibStore
{
    /// <summary>All resolved OID→MibDefinition for the currently loaded device.</summary>
    public Dictionary<string, MibDefinition> LoadedOids { get; } = new();

    /// <summary>Display names of loaded MIB files for UI.</summary>
    public ObservableCollection<string> LoadedFileNames { get; } = new();

    public int TotalDefinitions => LoadedOids.Count;

    /// <summary>
    /// Load all MIB files associated with a device profile.
    /// Uses multi-file parsing so cross-MIB dependencies resolve correctly.
    /// </summary>
    public async Task LoadForDeviceAsync(DeviceProfile device)
    {
        LoadedOids.Clear();
        LoadedFileNames.Clear();

        if (device.MibFilePaths.Count == 0) return;

        // Remove paths that no longer exist on disk
        device.MibFilePaths.RemoveAll(p => !File.Exists(p));
        if (device.MibFilePaths.Count == 0) return;

        try
        {
            // Parse all files together for cross-file dependency resolution
            var results = await Task.Run(() =>
                MibParserService.ParseMultiple(device.MibFilePaths));

            foreach (var info in results)
            {
                LoadedFileNames.Add($"{info.ModuleName} ({info.DefinitionCount})");
                foreach (var def in info.Definitions)
                    LoadedOids[def.Oid] = def;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"MIB parse error: {ex.Message}");
        }
    }

    /// <summary>
    /// Parse a single MIB file (for preview/validation when user selects a file).
    /// </summary>
    public async Task<MibFileInfo> LoadMibFileAsync(string filePath)
    {
        return await Task.Run(() => MibParserService.ParseFile(filePath));
    }
}
