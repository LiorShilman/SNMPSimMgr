using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SNMPSimMgr.Models;
using SNMPSimMgr.Services;

namespace SNMPSimMgr.ViewModels;

public partial class MibBrowserViewModel : ObservableObject
{
    private readonly DeviceProfileStore _store;
    private readonly DeviceListViewModel _deviceList;
    private List<SnmpRecord> _allRecords = new();

    public ObservableCollection<OidTreeNode> RootNodes { get; } = new();
    public ObservableCollection<SnmpRecord> FlatRecords { get; } = new();

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private int _totalOids;
    [ObservableProperty] private string _loadedDeviceName = string.Empty;
    [ObservableProperty] private OidTreeNode? _selectedNode;
    [ObservableProperty] private bool _showTree = true;

    // Well-known OID names for display
    private static readonly Dictionary<string, string> KnownOids = new()
    {
        ["1.3"] = "org",
        ["1.3.6"] = "dod",
        ["1.3.6.1"] = "internet",
        ["1.3.6.1.2"] = "mgmt",
        ["1.3.6.1.2.1"] = "mib-2",
        ["1.3.6.1.2.1.1"] = "system",
        ["1.3.6.1.2.1.1.1"] = "sysDescr",
        ["1.3.6.1.2.1.1.2"] = "sysObjectID",
        ["1.3.6.1.2.1.1.3"] = "sysUpTime",
        ["1.3.6.1.2.1.1.4"] = "sysContact",
        ["1.3.6.1.2.1.1.5"] = "sysName",
        ["1.3.6.1.2.1.1.6"] = "sysLocation",
        ["1.3.6.1.2.1.1.7"] = "sysServices",
        ["1.3.6.1.2.1.2"] = "interfaces",
        ["1.3.6.1.2.1.2.1"] = "ifNumber",
        ["1.3.6.1.2.1.2.2"] = "ifTable",
        ["1.3.6.1.2.1.2.2.1.1"] = "ifIndex",
        ["1.3.6.1.2.1.2.2.1.2"] = "ifDescr",
        ["1.3.6.1.2.1.2.2.1.3"] = "ifType",
        ["1.3.6.1.2.1.2.2.1.5"] = "ifSpeed",
        ["1.3.6.1.2.1.2.2.1.6"] = "ifPhysAddress",
        ["1.3.6.1.2.1.2.2.1.7"] = "ifAdminStatus",
        ["1.3.6.1.2.1.2.2.1.8"] = "ifOperStatus",
        ["1.3.6.1.2.1.2.2.1.10"] = "ifInOctets",
        ["1.3.6.1.2.1.2.2.1.16"] = "ifOutOctets",
        ["1.3.6.1.2.1.3"] = "at",
        ["1.3.6.1.2.1.4"] = "ip",
        ["1.3.6.1.2.1.5"] = "icmp",
        ["1.3.6.1.2.1.6"] = "tcp",
        ["1.3.6.1.2.1.7"] = "udp",
        ["1.3.6.1.2.1.11"] = "snmp",
        ["1.3.6.1.2.1.25"] = "host",
        ["1.3.6.1.2.1.31"] = "ifMIB",
        ["1.3.6.1.2.1.47"] = "entityMIB",
        ["1.3.6.1.4"] = "private",
        ["1.3.6.1.4.1"] = "enterprises",
    };

    public MibBrowserViewModel(DeviceProfileStore store, DeviceListViewModel deviceList)
    {
        _store = store;
        _deviceList = deviceList;

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
            LoadedDeviceName = string.Empty;
            return;
        }

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

    private void ApplyFilter()
    {
        FlatRecords.Clear();
        var filter = SearchText.Trim();

        foreach (var record in _allRecords)
        {
            if (string.IsNullOrEmpty(filter) ||
                record.Oid.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                record.Value.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                (record.Name?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true))
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

        bool selfMatch = node.Oid.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                          node.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                          (node.Value?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true);

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

                var name = KnownOids.TryGetValue(currentPath, out var knownName)
                    ? knownName
                    : parts[i];

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
    }

    private void CollapseChains(OidTreeNode node)
    {
        // If a node has exactly one child and no value, merge them
        while (node.Children.Count == 1 && !node.IsLeaf)
        {
            var child = node.Children[0];
            if (child.IsLeaf) break;

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

    [RelayCommand]
    private void ToggleView()
    {
        ShowTree = !ShowTree;
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
