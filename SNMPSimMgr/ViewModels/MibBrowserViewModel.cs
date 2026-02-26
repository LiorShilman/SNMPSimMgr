using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SNMPSimMgr.Models;
using SNMPSimMgr.Services;

namespace SNMPSimMgr.ViewModels;

public partial class MibBrowserViewModel : ObservableObject
{
    private readonly DeviceProfileStore _store;
    private readonly DeviceListViewModel _deviceList;
    private readonly MibStore _mibStore;
    private readonly MibPanelExportService _exportService;
    private List<SnmpRecord> _allRecords = new();

    public ObservableCollection<OidTreeNode> RootNodes { get; } = new();
    public ObservableCollection<SnmpRecord> FlatRecords { get; } = new();
    public ObservableCollection<string> LoadedMibFiles => _mibStore.LoadedFileNames;

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private int _totalOids;
    [ObservableProperty] private string _loadedDeviceName = string.Empty;
    [ObservableProperty] private OidTreeNode? _selectedNode;
    [ObservableProperty] private bool _showTree = true;
    [ObservableProperty] private int _mibCount;
    [ObservableProperty] private string _mibStatus = string.Empty;
    [ObservableProperty] private bool _hasDevice;
    [ObservableProperty] private string _exportStatus = string.Empty;

    // Well-known OID names as fallback when MIB files are not loaded
    private static readonly Dictionary<string, string> KnownOids = new()
    {
        // Root hierarchy
        ["1.3"] = "org",
        ["1.3.6"] = "dod",
        ["1.3.6.1"] = "internet",
        ["1.3.6.1.2"] = "mgmt",
        ["1.3.6.1.2.1"] = "mib-2",

        // system group (.1.3.6.1.2.1.1)
        ["1.3.6.1.2.1.1"] = "system",
        ["1.3.6.1.2.1.1.1"] = "sysDescr",
        ["1.3.6.1.2.1.1.2"] = "sysObjectID",
        ["1.3.6.1.2.1.1.3"] = "sysUpTime",
        ["1.3.6.1.2.1.1.4"] = "sysContact",
        ["1.3.6.1.2.1.1.5"] = "sysName",
        ["1.3.6.1.2.1.1.6"] = "sysLocation",
        ["1.3.6.1.2.1.1.7"] = "sysServices",

        // interfaces group (.1.3.6.1.2.1.2)
        ["1.3.6.1.2.1.2"] = "interfaces",
        ["1.3.6.1.2.1.2.1"] = "ifNumber",
        ["1.3.6.1.2.1.2.2"] = "ifTable",
        ["1.3.6.1.2.1.2.2.1"] = "ifEntry",
        ["1.3.6.1.2.1.2.2.1.1"] = "ifIndex",
        ["1.3.6.1.2.1.2.2.1.2"] = "ifDescr",
        ["1.3.6.1.2.1.2.2.1.3"] = "ifType",
        ["1.3.6.1.2.1.2.2.1.4"] = "ifMtu",
        ["1.3.6.1.2.1.2.2.1.5"] = "ifSpeed",
        ["1.3.6.1.2.1.2.2.1.6"] = "ifPhysAddress",
        ["1.3.6.1.2.1.2.2.1.7"] = "ifAdminStatus",
        ["1.3.6.1.2.1.2.2.1.8"] = "ifOperStatus",
        ["1.3.6.1.2.1.2.2.1.9"] = "ifLastChange",
        ["1.3.6.1.2.1.2.2.1.10"] = "ifInOctets",
        ["1.3.6.1.2.1.2.2.1.11"] = "ifInUcastPkts",
        ["1.3.6.1.2.1.2.2.1.12"] = "ifInNUcastPkts",
        ["1.3.6.1.2.1.2.2.1.13"] = "ifInDiscards",
        ["1.3.6.1.2.1.2.2.1.14"] = "ifInErrors",
        ["1.3.6.1.2.1.2.2.1.15"] = "ifInUnknownProtos",
        ["1.3.6.1.2.1.2.2.1.16"] = "ifOutOctets",
        ["1.3.6.1.2.1.2.2.1.17"] = "ifOutUcastPkts",
        ["1.3.6.1.2.1.2.2.1.18"] = "ifOutNUcastPkts",
        ["1.3.6.1.2.1.2.2.1.19"] = "ifOutDiscards",
        ["1.3.6.1.2.1.2.2.1.20"] = "ifOutErrors",
        ["1.3.6.1.2.1.2.2.1.21"] = "ifOutQLen",
        ["1.3.6.1.2.1.2.2.1.22"] = "ifSpecific",

        // at, ip, icmp, tcp, udp, snmp groups
        ["1.3.6.1.2.1.3"] = "at",
        ["1.3.6.1.2.1.4"] = "ip",
        ["1.3.6.1.2.1.4.20"] = "ipAddrTable",
        ["1.3.6.1.2.1.4.20.1"] = "ipAddrEntry",
        ["1.3.6.1.2.1.4.20.1.1"] = "ipAdEntAddr",
        ["1.3.6.1.2.1.4.20.1.2"] = "ipAdEntIfIndex",
        ["1.3.6.1.2.1.4.20.1.3"] = "ipAdEntNetMask",
        ["1.3.6.1.2.1.4.21"] = "ipRouteTable",
        ["1.3.6.1.2.1.4.21.1"] = "ipRouteEntry",
        ["1.3.6.1.2.1.4.21.1.1"] = "ipRouteDest",
        ["1.3.6.1.2.1.4.21.1.7"] = "ipRouteNextHop",
        ["1.3.6.1.2.1.5"] = "icmp",
        ["1.3.6.1.2.1.6"] = "tcp",
        ["1.3.6.1.2.1.7"] = "udp",
        ["1.3.6.1.2.1.11"] = "snmp",
        ["1.3.6.1.2.1.15"] = "bgp",
        ["1.3.6.1.2.1.15.3"] = "bgpPeerTable",
        ["1.3.6.1.2.1.15.3.1"] = "bgpPeerEntry",
        ["1.3.6.1.2.1.15.3.1.2"] = "bgpPeerState",
        ["1.3.6.1.2.1.15.3.1.7"] = "bgpPeerRemoteAs",
        ["1.3.6.1.2.1.15.3.1.9"] = "bgpPeerLocalAs",
        ["1.3.6.1.2.1.25"] = "host",

        // ifMIB / ifXTable (.1.3.6.1.2.1.31)
        ["1.3.6.1.2.1.31"] = "ifMIB",
        ["1.3.6.1.2.1.31.1"] = "ifMIBObjects",
        ["1.3.6.1.2.1.31.1.1"] = "ifXTable",
        ["1.3.6.1.2.1.31.1.1.1"] = "ifXEntry",
        ["1.3.6.1.2.1.31.1.1.1.1"] = "ifName",
        ["1.3.6.1.2.1.31.1.1.1.6"] = "ifHCInOctets",
        ["1.3.6.1.2.1.31.1.1.1.10"] = "ifHCOutOctets",
        ["1.3.6.1.2.1.31.1.1.1.15"] = "ifHighSpeed",
        ["1.3.6.1.2.1.31.1.1.1.18"] = "ifAlias",

        // entity MIB (.1.3.6.1.2.1.47)
        ["1.3.6.1.2.1.47"] = "entityMIB",
        ["1.3.6.1.2.1.47.1"] = "entityMIBObjects",
        ["1.3.6.1.2.1.47.1.1"] = "entityPhysical",
        ["1.3.6.1.2.1.47.1.1.1"] = "entPhysicalTable",
        ["1.3.6.1.2.1.47.1.1.1.1"] = "entPhysicalEntry",
        ["1.3.6.1.2.1.47.1.1.1.1.2"] = "entPhysicalDescr",
        ["1.3.6.1.2.1.47.1.1.1.1.7"] = "entPhysicalName",
        ["1.3.6.1.2.1.47.1.1.1.1.8"] = "entPhysicalHardwareRev",
        ["1.3.6.1.2.1.47.1.1.1.1.11"] = "entPhysicalSerialNum",
        ["1.3.6.1.2.1.47.1.1.1.1.13"] = "entPhysicalMfgName",

        // private / enterprises
        ["1.3.6.1.4"] = "private",
        ["1.3.6.1.4.1"] = "enterprises",
        ["1.3.6.1.4.1.9"] = "cisco",
        ["1.3.6.1.4.1.9.9"] = "ciscoMgmt",
        ["1.3.6.1.4.1.9.9.109"] = "ciscoProcessMIB",
        ["1.3.6.1.4.1.9.9.109.1.1.1.1.3"] = "cpmCPUTotal1minRev",
        ["1.3.6.1.4.1.9.9.109.1.1.1.1.4"] = "cpmCPUTotal5minRev",
        ["1.3.6.1.4.1.9.9.48"] = "ciscoMemoryPoolMIB",
        ["1.3.6.1.4.1.9.9.48.1.1.1.5"] = "ciscoMemoryPoolUsed",
        ["1.3.6.1.4.1.9.9.48.1.1.1.6"] = "ciscoMemoryPoolFree",
        ["1.3.6.1.4.1.41112"] = "ubiquiti",
        ["1.3.6.1.4.1.41112.1.6"] = "uniFi",

        // Trap OIDs
        ["1.3.6.1.6"] = "snmpV2",
        ["1.3.6.1.6.3"] = "snmpModules",
        ["1.3.6.1.6.3.1"] = "snmpMIB",
        ["1.3.6.1.6.3.1.1.5.3"] = "linkDown",
        ["1.3.6.1.6.3.1.1.5.4"] = "linkUp",

        // Super Device MIB (.1.3.6.1.4.1.99999)
        ["1.3.6.1.4.1.99999"] = "superDevice",
        ["1.3.6.1.4.1.99999.1"] = "sdInfo",
        ["1.3.6.1.4.1.99999.2"] = "sdStatus",
        ["1.3.6.1.4.1.99999.3"] = "sdConfig",
        ["1.3.6.1.4.1.99999.4"] = "sdIfTable",
        ["1.3.6.1.4.1.99999.5"] = "sdSensorTable",
        ["1.3.6.1.4.1.99999.6"] = "sdUserTable",
        ["1.3.6.1.4.1.99999.7"] = "sdVlanTable",
        ["1.3.6.1.4.1.99999.8"] = "sdDioTable",
        ["1.3.6.1.4.1.99999.10"] = "sdNotifications",
    };

    public MibBrowserViewModel(DeviceProfileStore store, DeviceListViewModel deviceList, MibStore mibStore, MibPanelExportService exportService)
    {
        _store = store;
        _deviceList = deviceList;
        _mibStore = mibStore;
        _exportService = exportService;

        _deviceList.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName == nameof(DeviceListViewModel.SelectedDevice))
                await LoadDeviceData();
        };
    }

    public async Task LoadDeviceData()
    {
        var device = _deviceList.SelectedDevice;
        if (device == null)
        {
            RootNodes.Clear();
            FlatRecords.Clear();
            TotalOids = 0;
            MibCount = 0;
            MibStatus = string.Empty;
            LoadedDeviceName = string.Empty;
            HasDevice = false;
            _mibStore.LoadedOids.Clear();
            _mibStore.LoadedFileNames.Clear();
            return;
        }

        HasDevice = true;

        // Load device's MIB files
        await _mibStore.LoadForDeviceAsync(device);
        MibCount = _mibStore.TotalDefinitions;

        _allRecords = await _store.LoadWalkDataAsync(device);
        LoadedDeviceName = device.Name;
        TotalOids = _allRecords.Count;
        BuildTree(_allRecords);
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilter();
    }

    private string ResolveName(string oid, string fallbackSegment)
    {
        if (_mibStore.LoadedOids.TryGetValue(oid, out var def))
            return def.Name;
        if (KnownOids.TryGetValue(oid, out var known))
            return known;
        return fallbackSegment;
    }

    private void ApplyFilter()
    {
        FlatRecords.Clear();
        var filter = SearchText.Trim();

        foreach (var record in _allRecords)
        {
            if (string.IsNullOrEmpty(filter) ||
                record.Oid.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                record.Value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (record.Name?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 == true) ||
                ResolveName(record.Oid, "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                FlatRecords.Add(record);
            }
        }

        // Also highlight matching nodes in tree
        if (!string.IsNullOrEmpty(filter))
        {
            foreach (var root in RootNodes)
                ExpandMatching(root, filter);
        }
    }

    private bool ExpandMatching(OidTreeNode node, string filter)
    {
        bool childMatch = false;
        foreach (var child in node.Children)
        {
            if (ExpandMatching(child, filter))
                childMatch = true;
        }

        bool selfMatch = node.Oid.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                          node.DisplayName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                          (node.Value?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 == true);

        node.IsExpanded = childMatch;
        node.IsHighlighted = selfMatch;
        return selfMatch || childMatch;
    }

    private void BuildTree(List<SnmpRecord> records)
    {
        RootNodes.Clear();
        var nodeMap = new Dictionary<string, OidTreeNode>();

        foreach (var record in records)
        {
            var parts = record.Oid.Split('.');
            var currentPath = "";

            for (int i = 0; i < parts.Length; i++)
            {
                var parentPath = currentPath;
                currentPath = i == 0 ? parts[0] : currentPath + "." + parts[i];

                if (nodeMap.ContainsKey(currentPath))
                    continue;

                var name = ResolveName(currentPath, parts[i]);

                var isLeaf = (i == parts.Length - 1);
                var node = new OidTreeNode
                {
                    Oid = currentPath,
                    Segment = parts[i],
                    DisplayName = name,
                    Value = isLeaf ? record.Value : null,
                    ValueType = isLeaf ? record.ValueType : null,
                    IsLeaf = isLeaf
                };

                nodeMap[currentPath] = node;

                if (string.IsNullOrEmpty(parentPath) || !nodeMap.ContainsKey(parentPath))
                {
                    if (i == 0)
                        RootNodes.Add(node);
                }
                else
                {
                    nodeMap[parentPath].Children.Add(node);
                }
            }
        }

        // Collapse single-child chains for cleaner view
        foreach (var root in RootNodes)
            CollapseChains(root);

        // Label table instance nodes with descriptive names (e.g., "FastEthernet0/1" instead of "1")
        foreach (var root in RootNodes)
            LabelTableInstances(root);

        // Collapse scalar .0 instances — promote value to parent, remove the "0" child
        foreach (var root in RootNodes)
            CollapseScalarInstances(root);
    }

    private void CollapseChains(OidTreeNode node)
    {
        // If a node has exactly one child and no value, merge them
        // BUT don't collapse when both have meaningful MIB names (e.g., sdIfTable → sdIfEntry)
        while (node.Children.Count == 1 && !node.IsLeaf)
        {
            var child = node.Children[0];
            if (child.IsLeaf) break;

            // Don't collapse if both nodes have resolved MIB names (not just numbers)
            bool nodeHasName = node.DisplayName != node.Segment && !int.TryParse(node.DisplayName, out _);
            bool childHasName = child.DisplayName != child.Segment && !int.TryParse(child.DisplayName, out _);
            if (nodeHasName && childHasName)
                break;

            node.DisplayName = $"{node.DisplayName}.{child.DisplayName}";
            node.Oid = child.Oid;
            node.Segment = $"{node.Segment}.{child.Segment}";
            node.Children.Clear();
            foreach (var grandChild in child.Children)
                node.Children.Add(grandChild);
        }

        foreach (var child in node.Children)
            CollapseChains(child);
    }

    // Suffixes that indicate a "label" column for table instances
    private static readonly string[] LabelColumnSuffixes =
        { "Name", "Descr", "Alias", "Label", "Title" };

    private void LabelTableInstances(OidTreeNode node)
    {
        // A "table entry" node has multiple children (columns),
        // each column has children with numeric segments (instances)
        if (node.Children.Count >= 2 && !node.IsLeaf)
        {
            var columns = node.Children;

            // Check: do all children have LEAF children with numeric segments?
            // Key: instances must be leaves (no further children) and more than 1
            // — this distinguishes real tables (multiple rows) from scalar groups (single .0)
            bool isTableEntry = columns.All(col =>
                col.Children.Count > 1 &&
                col.Children.All(inst => IsNumericSegment(inst.Segment) && inst.Children.Count == 0));

            if (isTableEntry)
            {
                // Find label column: first check name suffix patterns, then fall back to first OctetString column
                OidTreeNode? labelColumn = null;

                // Priority 1: column whose name ends with Name/Descr/Alias (e.g., sdIfName, ifDescr, sdSensorName)
                foreach (var col in columns)
                {
                    var displayName = col.DisplayName;
                    foreach (var suffix in LabelColumnSuffixes)
                    {
                        if (displayName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
                            displayName.Length > suffix.Length) // not JUST "Name"
                        {
                            labelColumn = col;
                            break;
                        }
                    }
                    if (labelColumn != null) break;
                }

                // Priority 2: first column with OctetString leaf values (skip short MAC-like values)
                if (labelColumn == null)
                {
                    foreach (var col in columns)
                    {
                        if (col.Children.Any(inst =>
                            inst.IsLeaf &&
                            inst.ValueType == "OctetString" &&
                            !string.IsNullOrEmpty(inst.Value) &&
                            inst.Value.Length > 2))
                        {
                            labelColumn = col;
                            break;
                        }
                    }
                }

                // Remove instance children from columns — MIB tree shows Table → Entry → Columns only
                // Instance data is visible in the flat records view
                var instanceCount = columns[0].Children.Count;
                foreach (var col in columns)
                {
                    col.Children.Clear();
                    col.IsLeaf = true;
                    col.Value = $"{instanceCount} instances";
                }

                return; // Don't recurse into table columns
            }
        }

        // Recurse into children
        foreach (var child in node.Children)
            LabelTableInstances(child);
    }

    private void CollapseScalarInstances(OidTreeNode node)
    {
        // If node has exactly one child with segment "0" that is a leaf → it's a scalar .0 instance
        // Promote the value to the parent and remove the child
        if (node.Children.Count == 1 && node.Children[0].Segment == "0" && node.Children[0].IsLeaf)
        {
            var instance = node.Children[0];
            node.Value = instance.Value;
            node.ValueType = instance.ValueType;
            node.IsLeaf = true;
            node.Children.Clear();
            return;
        }

        foreach (var child in node.Children)
            CollapseScalarInstances(child);
    }

    private static bool IsNumericSegment(string segment)
    {
        // Simple numeric: "1", "26", etc.
        if (int.TryParse(segment, out _)) return true;
        // Multi-part instance index like "192.168.1.10" (IP-indexed tables)
        // These were already split into separate tree levels, so just check for int
        return false;
    }

    [RelayCommand]
    private async Task ExportPanelSchema()
    {
        var device = _deviceList.SelectedDevice;
        if (device == null)
        {
            ExportStatus = "Select a device first.";
            return;
        }

        if (_mibStore.LoadedOids.Count == 0)
        {
            ExportStatus = "Load MIB files first.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Export Panel Schema JSON",
            Filter = "JSON files (*.json)|*.json",
            FileName = $"{device.Name}_panel_schema.json"
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            ExportStatus = "Exporting...";
            await _exportService.ExportToFileAsync(device, dialog.FileName);
            ExportStatus = $"Exported {_mibStore.TotalDefinitions} fields to {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            ExportStatus = $"Export error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ToggleView()
    {
        ShowTree = !ShowTree;
    }

    [RelayCommand]
    private async Task LoadMibFile()
    {
        var device = _deviceList.SelectedDevice;
        if (device == null) return;

        var dialog = new OpenFileDialog
        {
            Title = "Load MIB File",
            Filter = "MIB files (*.mib;*.txt;*.my)|*.mib;*.txt;*.my|All files (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() != true) return;

        int loaded = 0;
        foreach (var file in dialog.FileNames)
        {
            try
            {
                var info = await _mibStore.LoadMibFileAsync(file);
                loaded++;
                MibStatus = $"Loaded {info.ModuleName} — {info.DefinitionCount} definitions";

                // Add file path to device profile if not already there
                if (!device.MibFilePaths.Contains(file, StringComparer.OrdinalIgnoreCase))
                    device.MibFilePaths.Add(file);
            }
            catch (Exception ex)
            {
                MibStatus = $"Error loading {Path.GetFileName(file)}: {ex.Message}";
            }
        }

        if (loaded > 0)
        {
            // Save device profile with updated MibFilePaths
            await _deviceList.SaveAsync();

            // Reload MIBs for this device and rebuild tree
            await _mibStore.LoadForDeviceAsync(device);
            MibCount = _mibStore.TotalDefinitions;

            if (_allRecords.Count > 0)
            {
                BuildTree(_allRecords);
                ApplyFilter();
            }
        }
    }

    [RelayCommand]
    private async Task RemoveMib(string? displayName)
    {
        if (string.IsNullOrEmpty(displayName)) return;

        var device = _deviceList.SelectedDevice;
        if (device == null) return;

        // displayName looks like "RFC1213-MIB (142)" — find matching file path by module name
        var moduleName = displayName.Contains('(')
            ? displayName[..displayName.LastIndexOf('(')].Trim()
            : displayName;

        // Find the file path that produced this module name
        string? pathToRemove = null;
        foreach (var path in device.MibFilePaths)
        {
            try
            {
                var info = await _mibStore.LoadMibFileAsync(path);
                if (info.ModuleName == moduleName)
                {
                    pathToRemove = path;
                    break;
                }
            }
            catch { }
        }

        if (pathToRemove != null)
            device.MibFilePaths.Remove(pathToRemove);

        // Save and reload
        await _deviceList.SaveAsync();
        await _mibStore.LoadForDeviceAsync(device);
        MibCount = _mibStore.TotalDefinitions;
        MibStatus = $"Removed {moduleName} — {_mibStore.TotalDefinitions} definitions remaining";

        if (_allRecords.Count > 0)
        {
            BuildTree(_allRecords);
            ApplyFilter();
        }
    }

    [RelayCommand]
    private void ExpandAllChildren(OidTreeNode? node)
    {
        if (node == null) return;
        SetExpandedRecursive(node, true);
    }

    [RelayCommand]
    private void CollapseAllChildren(OidTreeNode? node)
    {
        if (node == null) return;
        SetExpandedRecursive(node, false);
    }

    [RelayCommand]
    private void ExpandAll()
    {
        foreach (var root in RootNodes)
            SetExpandedRecursive(root, true);
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var root in RootNodes)
            SetExpandedRecursive(root, false);
    }

    private static void SetExpandedRecursive(OidTreeNode node, bool expanded)
    {
        node.IsExpanded = expanded;
        foreach (var child in node.Children)
            SetExpandedRecursive(child, expanded);
    }
}

public partial class OidTreeNode : ObservableObject
{
    public string Oid { get; set; } = string.Empty;
    public string Segment { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Value { get; set; }
    public string? ValueType { get; set; }
    public bool IsLeaf { get; set; }
    public ObservableCollection<OidTreeNode> Children { get; } = new();

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isHighlighted;

    public string Label => Value != null
        ? $"{DisplayName} ({Oid}) = {Value}"
        : $"{DisplayName} ({Oid})";

    public string TreeLabel => Value != null
        ? $"{DisplayName} = {TruncateValue(Value, 60)}"
        : DisplayName;

    private static string TruncateValue(string val, int max) =>
        val.Length > max ? val[..max] + "..." : val;
}
