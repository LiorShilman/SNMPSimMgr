using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SNMPSimMgr.Models;
using SNMPSimMgr.Services;

namespace SNMPSimMgr.ViewModels
{
    public partial class MibBrowserViewModel : ObservableObject
    {
        private readonly DeviceProfileStore _store;
        private readonly DeviceListViewModel _deviceList;
        private readonly MibStore _mibStore;
        private readonly MibPanelExportService _exportService;
        private readonly SnmpRecorderService _recorder;
        private List<SnmpRecord>  _allRecords = new List<SnmpRecord>();
        private Dictionary<string, OidTreeNode>  _nodeMap = new Dictionary<string, OidTreeNode>();
        private bool _needsRefresh = true;

        public ObservableCollection<OidTreeNode>  RootNodes { get; } = new ObservableCollection<OidTreeNode>();
        public ObservableCollection<SnmpRecord>  FlatRecords { get; } = new ObservableCollection<SnmpRecord>();
        public ObservableCollection<string> LoadedMibFiles => _mibStore.LoadedFileNames;

        [ObservableProperty] private string _searchText = string.Empty;
        [ObservableProperty] private int _totalOids;
        [ObservableProperty] private string _loadedDeviceName = string.Empty;
        [ObservableProperty] private OidTreeNode _selectedNode;
        [ObservableProperty] private bool _showTree = true;
        [ObservableProperty] private int _mibCount;
        [ObservableProperty] private string _mibStatus = string.Empty;
        [ObservableProperty] private bool _hasDevice;
        [ObservableProperty] private string _exportStatus = string.Empty;

        // Context menu query results
        public ObservableCollection<QueryResultItem>  TreeQueryResults { get; } = new ObservableCollection<QueryResultItem>();
        [ObservableProperty] private bool _isTreeQuerying;
        [ObservableProperty] private string _treeQueryStatus = string.Empty;

        // Panel mode properties
        [ObservableProperty] private bool _showPanel;
        [ObservableProperty] private MibPanelSchema _panelSchema;
        [ObservableProperty] private string _panelStatus = string.Empty;
        [ObservableProperty] private bool _isPanelLoading;

        // Auto-refresh
        [ObservableProperty] private bool _autoRefreshEnabled;
        [ObservableProperty] private int _autoRefreshInterval = 5; // seconds
        private DispatcherTimer _autoRefreshTimer;
        private bool _isRefreshing;

        // Panel field classification lists (for UI binding)
        public ObservableCollection<MibFieldSchema>  IdentityFields { get; } = new ObservableCollection<MibFieldSchema>();
        public ObservableCollection<MibFieldSchema>  MonitorFields { get; } = new ObservableCollection<MibFieldSchema>();
        public ObservableCollection<MibFieldSchema>  ConfigFields { get; } = new ObservableCollection<MibFieldSchema>();
        public ObservableCollection<SystemInfoItem>  SystemInfoItems { get; } = new ObservableCollection<SystemInfoItem>();
        public ObservableCollection<PanelTableItem>  PanelTables { get; } = new ObservableCollection<PanelTableItem>();

        // Well-known OID names as fallback when MIB files are not loaded
        private static readonly Dictionary<string, string>  KnownOids = new Dictionary<string, string>() {
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

        public MibBrowserViewModel(DeviceProfileStore store, DeviceListViewModel deviceList, MibStore mibStore, MibPanelExportService exportService, SnmpRecorderService recorder)
        {
            _store = store;
            _deviceList = deviceList;
            _mibStore = mibStore;
            _exportService = exportService;
            _recorder = recorder;

            _deviceList.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DeviceListViewModel.SelectedDevice))
                    _needsRefresh = true;
            };
        }

        public async Task LoadDeviceData()
        {
            var device = _deviceList.SelectedDevice;
            if (device == null)
            {
                RootNodes.Clear();
                FlatRecords.Clear();
                ClearPanelState();
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
            ClearPanelState();

            // Load device's MIB files
            await _mibStore.LoadForDeviceAsync(device);
            MibCount = _mibStore.TotalDefinitions;

            _allRecords = await _store.LoadWalkDataAsync(device);
            LoadedDeviceName = device.Name;

            // If no walk data but MIB definitions exist, generate records from MIB OIDs
            // so Tree and Flat views show the MIB structure
            if (_allRecords.Count == 0 && _mibStore.TotalDefinitions > 0)
                _allRecords = GenerateMibRecords();

            TotalOids = _allRecords.Count;
            BuildTree(_allRecords);
            ApplyFilter();
        }

        /// <summary>
        /// Called when the MIB Browser tab becomes visible. Reloads data only if the
        /// selected device changed since the last load.
        /// </summary>
        public async Task RefreshIfNeeded()
        {
            if (!_needsRefresh) return;
            _needsRefresh = false;
            await LoadDeviceData();
        }

        /// <summary>
        /// Update a tree node's displayed value when the simulator value changes externally
        /// (e.g., SET from Angular client, injection, scenario, automation).
        /// </summary>
        public void UpdateNodeValue(string deviceName, string oid, string value)
        {
            // Only update if this is the currently displayed device
            if (string.IsNullOrEmpty(LoadedDeviceName) || !deviceName.Equals(LoadedDeviceName, StringComparison.OrdinalIgnoreCase))
                return;

            // Try exact OID match
            if (_nodeMap.TryGetValue(oid, out var node) && node.IsLeaf)
            {
                node.Value = value;
                return;
            }

            // Collapsed scalar: OID "x.y.z.0" → tree node "x.y.z"
            if (oid.EndsWith(".0"))
            {
                var parentOid = oid[..^2];
                if (_nodeMap.TryGetValue(parentOid, out node) && node.IsLeaf)
                    node.Value = value;
            }
        }

        /// <summary>
        /// When no Walk data exists, create records from MIB definitions
        /// so Tree and Flat views show the MIB structure.
        /// </summary>
        private List<SnmpRecord> GenerateMibRecords()
        {
            // Only include actual data objects (those with MAX-ACCESS, i.e. OBJECT-TYPE definitions).
            // Bare OBJECT IDENTIFIER assignments (structural parents like org, dod, enterprises)
            // and special entries like zeroDotZero are excluded — they still appear as branch
            // nodes in the tree because BuildTree creates intermediate nodes from OID path segments.
            return _mibStore.LoadedOids.Values
                .Where(d => !string.IsNullOrEmpty(d.Access))
                .OrderBy(d => d.Oid, StringComparer.Ordinal)
                .Select(d => new SnmpRecord
                {
                    Oid = d.Oid,
                    Name = d.Name,
                    Value = d.Description ?? "—",
                    ValueType = d.Access ?? d.BaseType ?? "MIB"
                })
                .ToList();
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
                // Resolve MIB name for flat view display
                if (string.IsNullOrEmpty(record.Name))
                {
                    if (_mibStore.LoadedOids.TryGetValue(record.Oid, out var mibDef))
                        record.Name = mibDef.Name;
                    else if (KnownOids.TryGetValue(record.Oid, out var known))
                        record.Name = known;
                }

                if (string.IsNullOrEmpty(filter) ||
                    record.Oid.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    record.Value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (record.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 == true) ||
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
                              (node.Value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 == true);

            node.IsExpanded = childMatch;
            node.IsHighlighted = selfMatch;
            return selfMatch || childMatch;
        }

        private void BuildTree(List<SnmpRecord> records)
        {
            RootNodes.Clear();
            _nodeMap.Clear();
            var nodeMap = _nodeMap;

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
                    _mibStore.LoadedOids.TryGetValue(currentPath, out var mibDef);
                    var node = new OidTreeNode() {
                        Oid = currentPath,
                        Segment = parts[i],
                        DisplayName = name,
                        Value = isLeaf ? record.Value : null,
                        ValueType = isLeaf ? record.ValueType : null,
                        Access = mibDef?.Access,
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

            // Rebuild nodeMap from final tree (CollapseChains/CollapseScalarInstances change OIDs and remove nodes)
            _nodeMap.Clear();
            foreach (var root in RootNodes)
                RebuildNodeMap(root);

            // Expand all nodes by default
            foreach (var root in RootNodes)
                SetExpandedRecursive(root, true);
        }

        private void RebuildNodeMap(OidTreeNode node)
        {
            _nodeMap[node.Oid] = node;
            foreach (var child in node.Children)
                RebuildNodeMap(child);
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
                    OidTreeNode labelColumn = null;

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

            var dialog = new SaveFileDialog() {
                Title = "Export Panel Schema JSON",
                Filter = "JSON files (*.json)|*.json",
                FileName = $"{device.Name}_panel_schema.json"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                ExportStatus = "Exporting...";
                await _exportService.ExportToFileAsync(device, dialog.FileName);

                // Auto-save to Data/schemas/ for fast loading via SchemaPath
                var schemasDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "schemas");
                Directory.CreateDirectory(schemasDir);
                var schemaFile = Path.Combine(schemasDir, device.Name + ".json");
                if (dialog.FileName != schemaFile)
                    await Task.Run(() => File.Copy(dialog.FileName, schemaFile, overwrite: true));

                // Update device profile with SchemaPath and persist
                device.SchemaPath = schemaFile;
                var profiles = await _store.LoadProfilesAsync();
                var target = profiles.FirstOrDefault(p => p.Id == device.Id);
                if (target != null)
                {
                    target.SchemaPath = schemaFile;
                    await _store.SaveProfilesAsync(profiles);
                }

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
            if (ShowTree)
            {
                // Tree → Flat
                ShowTree = false;
                ShowPanel = false;
            }
            else if (!ShowPanel)
            {
                // Flat → Panel
                ShowTree = false;
                ShowPanel = true;
                // Always rebuild if stale (null) or if MIB count changed
                if (_mibStore.TotalDefinitions > 0 && (PanelSchema == null || PanelSchema.TotalFields == 0))
                    _ = BuildPanel();
            }
            else
            {
                // Panel → Tree
                ShowTree = true;
                ShowPanel = false;
                AutoRefreshEnabled = false;
            }
        }

        private void ClearPanelState()
        {
            PanelSchema = null;
            PanelStatus = string.Empty;
            IdentityFields.Clear();
            MonitorFields.Clear();
            ConfigFields.Clear();
            SystemInfoItems.Clear();
            PanelTables.Clear();
        }

        [RelayCommand]
        private async Task BuildPanel()
        {
            var device = _deviceList.SelectedDevice;
            if (device == null)
            {
                PanelStatus = "Select a device first.";
                return;
            }

            if (_mibStore.TotalDefinitions == 0)
            {
                PanelStatus = "Load MIB files first.";
                return;
            }

            try
            {
                IsPanelLoading = true;
                PanelStatus = "Building panel schema...";

                PanelSchema = await _exportService.BuildSchemaAsync(device);

                ClassifyFields(PanelSchema);

                PanelStatus = $"Panel loaded — {PanelSchema.TotalFields} fields from {PanelSchema.Modules.Count} modules";
            }
            catch (Exception ex)
            {
                PanelStatus = $"Panel error: {ex.Message}";
            }
            finally
            {
                IsPanelLoading = false;
            }
        }

        [RelayCommand]
        private async Task PanelRefresh()
        {
            var device = _deviceList.SelectedDevice;
            if (device == null || PanelSchema == null) return;

            try
            {
                IsPanelLoading = true;
                PanelStatus = "Refreshing values...";

                // Collect all scalar OIDs with .0 suffix
                var scalarOids = PanelSchema.Modules
                    .SelectMany(m => m.Scalars)
                    .Select(f => f.Oid.EndsWith(".0") ? f.Oid : f.Oid + ".0")
                    .ToList();

                if (scalarOids.Count > 0)
                {
                    // Batch GET in groups of 20 (SNMP packet size limit)
                    var allResults = new List<SnmpRecord>();
                    for (int i = 0; i < scalarOids.Count; i += 20)
                    {
                        var batch = scalarOids.Skip(i).Take(20);
                        var results = await _recorder.GetMultipleAsync(device, batch);
                        allResults.AddRange(results);
                    }

                    // Update field values
                    var lookup = allResults.ToDictionary(r => r.Oid, r => r);
                    foreach (var module in PanelSchema.Modules)
                    {
                        foreach (var field in module.Scalars)
                        {
                            var oid = field.Oid.EndsWith(".0") ? field.Oid : field.Oid + ".0";
                            if (lookup.TryGetValue(oid, out var record))
                            {
                                field.CurrentValue = record.Value;
                                field.CurrentValueType = record.ValueType;
                            }
                        }
                    }

                    // Re-classify to update UI collections
                    ClassifyFields(PanelSchema);
                }

                PanelStatus = $"Refreshed {scalarOids.Count} values";
            }
            catch (Exception ex)
            {
                PanelStatus = $"Refresh error: {ex.Message}";
            }
            finally
            {
                IsPanelLoading = false;
            }
        }

        [RelayCommand]
        private async Task PanelSetField(MibFieldSchema field)
        {
            var device = _deviceList.SelectedDevice;
            if (device == null || field == null) return;

            try
            {
                var oid = field.Oid.EndsWith(".0") ? field.Oid : field.Oid + ".0";
                var valueType = field.CurrentValueType ?? field.BaseType;
                var value = field.CurrentValue ?? "";

                PanelStatus = $"Setting {field.Name}...";

                var success = await _recorder.SetAsync(device, oid, value, valueType);

                if (success)
                {
                    PanelStatus = $"Set {FriendlyName(field.Name)} = {value}";
                }
                else
                {
                    PanelStatus = $"SET failed for {field.Name} — device may not support writes";
                }
            }
            catch (Exception ex)
            {
                PanelStatus = $"SET error: {ex.Message}";
            }
        }

        // ── Auto-Refresh ──

        partial void OnAutoRefreshEnabledChanged(bool value)
        {
            if (value)
                StartAutoRefresh();
            else
                StopAutoRefresh();
        }

        partial void OnAutoRefreshIntervalChanged(int value)
        {
            if (_autoRefreshTimer != null && AutoRefreshEnabled)
            {
                _autoRefreshTimer.Interval = TimeSpan.FromSeconds(Math.Max(value, 2));
            }
        }

        private void StartAutoRefresh()
        {
            StopAutoRefresh();
            _autoRefreshTimer = new DispatcherTimer() {
                Interval = TimeSpan.FromSeconds(Math.Max(AutoRefreshInterval, 2))
            };
            _autoRefreshTimer.Tick += AutoRefreshTick;
            _autoRefreshTimer.Start();
            PanelStatus = $"Auto-refresh ON — every {AutoRefreshInterval}s";
        }

        private void StopAutoRefresh()
        {
            if (_autoRefreshTimer != null)
            {
                _autoRefreshTimer.Stop();
                _autoRefreshTimer.Tick -= AutoRefreshTick;
                _autoRefreshTimer = null;
            }
        }

        private async void AutoRefreshTick(object sender, EventArgs e)
        {
            if (_isRefreshing || PanelSchema == null) return;
            _isRefreshing = true;
            try
            {
                await PanelRefresh();
            }
            finally
            {
                _isRefreshing = false;
            }
        }

        // ── Field Classification ──

        private static readonly Regex IdentityPattern = new Regex(
            @"(Name|Descr|Model|Firmware|FwVer|HwVer|Serial|MAC|mac|Version|Vendor|Manufacturer|Contact|Location)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex MonitorPattern = new Regex(
            @"(Status|State|Oper|Admin|Temp|Temperature|Voltage|Current|Power|Fan|Speed|Load|Cpu|Memory|Uptime|Counter|Octets|Packets|Errors|Discards|Rate|Utilization|Health|Alarm|Sensor|Gauge)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex SystemPattern = new Regex(
            @"^sys(Descr|ObjectID|UpTime|Contact|Name|Location|Services|ORLastChange)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private void ClassifyFields(MibPanelSchema schema)
        {
            IdentityFields.Clear();
            MonitorFields.Clear();
            ConfigFields.Clear();
            SystemInfoItems.Clear();

            foreach (var module in schema.Modules)
            {
                foreach (var field in module.Scalars)
                {
                    if (SystemPattern.IsMatch(field.Name))
                    {
                        SystemInfoItems.Add(new SystemInfoItem
                        {
                            Label = FriendlyName(field.Name),
                            Value = field.CurrentValue ?? "—",
                            Category = field.Name.IndexOf("Contact", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       field.Name.IndexOf("Location", StringComparison.OrdinalIgnoreCase) >= 0
                                       ? "network" : "system"
                        });
                    }
                    else if (field.IsWritable)
                    {
                        ConfigFields.Add(field);
                    }
                    else if (IdentityPattern.IsMatch(field.Name) && !field.IsWritable)
                    {
                        IdentityFields.Add(field);
                    }
                    else
                    {
                        MonitorFields.Add(field);
                    }
                }
            }

            // Add connection info to system info
            SystemInfoItems.Add(new SystemInfoItem { Label = "IP Address", Value = schema.DeviceIp, Category = "network" });
            SystemInfoItems.Add(new SystemInfoItem { Label = "Port", Value = schema.DevicePort.ToString(), Category = "network" });
            SystemInfoItems.Add(new SystemInfoItem { Label = "Community", Value = schema.Community, Category = "network" });
            SystemInfoItems.Add(new SystemInfoItem { Label = "SNMP Version", Value = schema.SnmpVersion, Category = "identity" });

            // Build DataTables for each SNMP table
            PanelTables.Clear();
            foreach (var module in schema.Modules)
            {
                foreach (var table in module.Tables)
                {
                    var dt = new DataTable();

                    // First column: row label/index
                    dt.Columns.Add("Name", typeof(string));

                    // Add a column for each table column (use friendly names)
                    var colMap = new List<(string Oid, string Header)>();
                    foreach (var col in table.Columns)
                    {
                        var header = FriendlyName(col.Name);
                        // Avoid duplicate column names
                        var uniqueHeader = header;
                        int suffix = 2;
                        while (dt.Columns.Contains(uniqueHeader))
                            uniqueHeader = $"{header} {suffix++}";
                        dt.Columns.Add(uniqueHeader, typeof(string));
                        colMap.Add((col.Oid, uniqueHeader));
                    }

                    // Add data rows
                    foreach (var row in table.Rows)
                    {
                        var dr = dt.NewRow();
                        dr["Name"] = row.Label ?? row.Index;
                        foreach (var (oid, header) in colMap)
                        {
                            if (row.Values.TryGetValue(oid, out var cell))
                                dr[header] = cell.EnumLabel ?? cell.Value;
                        }
                        dt.Rows.Add(dr);
                    }

                    PanelTables.Add(new PanelTableItem
                    {
                        Name = FriendlyName(table.Name),
                        RowCount = table.RowCount,
                        ColumnCount = table.ColumnCount,
                        Data = dt.DefaultView,
                        SourceTable = table
                    });
                }
            }

            OnPropertyChanged(nameof(IdentityFields));
            OnPropertyChanged(nameof(MonitorFields));
            OnPropertyChanged(nameof(ConfigFields));
            OnPropertyChanged(nameof(SystemInfoItems));
            OnPropertyChanged(nameof(PanelTables));
        }

        public static string FriendlyName(string name)
        {
            // Strip common prefixes
            var clean = Regex.Replace(name, @"^(sd|sys|if|ent|cpm|cisco)", "", RegexOptions.IgnoreCase);
            if (string.IsNullOrEmpty(clean)) clean = name;
            // Add spaces between camelCase
            clean = Regex.Replace(clean, @"([a-z])([A-Z])", "$1 $2");
            clean = Regex.Replace(clean, @"([A-Z]+)([A-Z][a-z])", "$1 $2");
            // Capitalize first letter
            if (clean.Length > 0)
                clean = char.ToUpper(clean[0]) + clean.Substring(1);
            return clean;
        }

        public string GetEnumLabel(MibFieldSchema field)
        {
            if (field.Options != null && int.TryParse(field.CurrentValue, out var intVal))
            {
                var match = field.Options.FirstOrDefault(o => o.Value == intVal);
                if (match != null) return match.Label;
            }
            return field.CurrentValue ?? "—";
        }

        [RelayCommand]
        private async Task LoadMibFile()
        {
            var device = _deviceList.SelectedDevice;
            if (device == null) return;

            var dialog = new OpenFileDialog() {
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

                // Invalidate cached panel schema — MIBs changed
                Hubs.SnmpHub.InvalidateSchemaCache(device.Id);

                // Reload MIBs for this device and rebuild tree
                await _mibStore.LoadForDeviceAsync(device);
                MibCount = _mibStore.TotalDefinitions;

                // Regenerate MIB-based records if no walk data
                var walkRecords = await _store.LoadWalkDataAsync(device);
                if (walkRecords.Count > 0)
                    _allRecords = walkRecords;
                else if (_mibStore.TotalDefinitions > 0)
                    _allRecords = GenerateMibRecords();

                TotalOids = _allRecords.Count;
                BuildTree(_allRecords);
                ApplyFilter();

                // Rebuild panel if currently visible
                if (ShowPanel)
                    _ = BuildPanel();
            }
        }

        [RelayCommand]
        private async Task RemoveMib(string displayName)
        {
            if (string.IsNullOrEmpty(displayName)) return;

            var device = _deviceList.SelectedDevice;
            if (device == null) return;

            // displayName looks like "RFC1213-MIB (142)" — find matching file path by module name
            var moduleName = displayName.Contains('(')
                ? displayName[..displayName.LastIndexOf('(')].Trim()
                : displayName;

            // Find the file path that produced this module name
            string pathToRemove = null;
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

            // Invalidate cached panel schema — MIBs changed
            Hubs.SnmpHub.InvalidateSchemaCache(device.Id);

            // Save and reload
            await _deviceList.SaveAsync();
            await _mibStore.LoadForDeviceAsync(device);
            MibCount = _mibStore.TotalDefinitions;
            MibStatus = $"Removed {moduleName} — {_mibStore.TotalDefinitions} definitions remaining";

            // Regenerate records from remaining MIBs or walk data
            var walkRecords = await _store.LoadWalkDataAsync(device);
            if (walkRecords.Count > 0)
                _allRecords = walkRecords;
            else if (_mibStore.TotalDefinitions > 0)
                _allRecords = GenerateMibRecords();
            else
                _allRecords = new List<SnmpRecord>();

            TotalOids = _allRecords.Count;
            BuildTree(_allRecords);
            ApplyFilter();

            // Rebuild panel if currently visible, or clear stale data
            if (ShowPanel && _mibStore.TotalDefinitions > 0)
                _ = BuildPanel();
            else
                ClearPanelState();
        }

        [RelayCommand]
        private async Task UnloadAllMibs()
        {
            var device = _deviceList.SelectedDevice;
            if (device == null) return;

            var count = device.MibFilePaths.Count;
            if (count == 0) return;

            device.MibFilePaths.Clear();
            Hubs.SnmpHub.InvalidateSchemaCache(device.Id);

            await _deviceList.SaveAsync();
            await _mibStore.LoadForDeviceAsync(device);
            MibCount = 0;
            MibStatus = $"Unloaded {count} MIB files";

            _allRecords = new List<SnmpRecord>();
            TotalOids = 0;
            RootNodes.Clear();
            FlatRecords.Clear();
            ClearPanelState();
        }

        [RelayCommand]
        private void ExpandAllChildren(OidTreeNode node)
        {
            if (node == null) return;
            SetExpandedRecursive(node, true);
        }

        [RelayCommand]
        private void CollapseAllChildren(OidTreeNode node)
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

        // ── Context Menu: SNMP GET ──

        [RelayCommand]
        private async Task ContextGet(OidTreeNode node)
        {
            var device = _deviceList.SelectedDevice;
            if (device == null || node == null) return;
            var target = SnmpRecorderService.ResolveTarget(device);

            IsTreeQuerying = true;
            TreeQueryStatus = $"GET {node.Oid}...";
            try
            {
                var oid = node.IsLeaf && !node.Oid.EndsWith(".0") ? node.Oid + ".0" : node.Oid;
                var result = await _recorder.GetSingleAsync(target, oid);
                if (result != null)
                {
                    TreeQueryResults.Insert(0, new QueryResultItem
                    {
                        Time = DateTime.Now.ToString("HH:mm:ss"),
                        Operation = "GET",
                        Oid = result.Oid,
                        Name = ResolveName(result.Oid, ""),
                        ValueType = result.ValueType,
                        Value = result.Value,
                        IsSuccess = true
                    });
                    // Update the node's displayed value in the tree
                    if (node.IsLeaf)
                        node.Value = result.Value;
                    TreeQueryStatus = $"GET OK — {result.Value}";
                }
                else
                {
                    TreeQueryResults.Insert(0, new QueryResultItem
                    {
                        Time = DateTime.Now.ToString("HH:mm:ss"),
                        Operation = "GET",
                        Oid = oid,
                        Name = node.DisplayName,
                        Value = "No response",
                        IsSuccess = false
                    });
                    TreeQueryStatus = "GET — No response";
                }
            }
            catch (Exception ex)
            {
                TreeQueryResults.Insert(0, new QueryResultItem
                {
                    Time = DateTime.Now.ToString("HH:mm:ss"),
                    Operation = "GET",
                    Oid = node.Oid,
                    Name = node.DisplayName,
                    Value = $"Error: {ex.Message}",
                    IsSuccess = false
                });
                TreeQueryStatus = $"GET error: {ex.Message}";
            }
            finally
            {
                IsTreeQuerying = false;
            }
        }

        // ── Context Menu: SNMP SET ──

        // Known SNMP value types for the SET dialog
        private static readonly string[] SnmpValueTypes =
            { "Integer32", "OctetString", "ObjectIdentifier", "IpAddress", "Counter32", "Gauge32", "TimeTicks", "Counter64" };

        /// <summary>Resolve the best SNMP type name for a node by checking: live type, MIB Syntax/BaseType, then fallback.</summary>
        private string ResolveSnmpType(OidTreeNode node, string liveValueType = null)
        {
            // 1. Live value type from GET response (most accurate)
            if (!string.IsNullOrEmpty(liveValueType) && liveValueType != "Null" &&
                Array.IndexOf(SnmpValueTypes, liveValueType) >= 0)
                return liveValueType;

            // 2. Cached node ValueType (only if it's a valid SNMP type)
            if (!string.IsNullOrEmpty(node.ValueType) && node.ValueType != "Null" &&
                Array.IndexOf(SnmpValueTypes, node.ValueType) >= 0)
                return node.ValueType;

            // 3. MIB definition Syntax → map common MIB types to SNMP types
            var oid = node.Oid;
            if (_mibStore.LoadedOids.TryGetValue(oid, out var def))
            {
                var syntax = def.BaseType ?? def.Syntax;
                if (!string.IsNullOrEmpty(syntax))
                    return MapMibSyntaxToSnmpType(syntax);
            }

            return "OctetString";
        }

        private static string MapMibSyntaxToSnmpType(string syntax)
        {
            var s = syntax.Trim();
            if (s.StartsWith("INTEGER", StringComparison.OrdinalIgnoreCase) || s == "TruthValue" || s == "RowStatus")
                return "Integer32";
            if (s.Equals("OCTET STRING", StringComparison.OrdinalIgnoreCase) || s == "DisplayString" || s == "SnmpAdminString")
                return "OctetString";
            if (s.Equals("OBJECT IDENTIFIER", StringComparison.OrdinalIgnoreCase))
                return "ObjectIdentifier";
            if (s.Equals("IpAddress", StringComparison.OrdinalIgnoreCase) || s == "NetworkAddress")
                return "IpAddress";
            if (s.StartsWith("Counter32", StringComparison.OrdinalIgnoreCase) || s == "Counter")
                return "Counter32";
            if (s.StartsWith("Gauge32", StringComparison.OrdinalIgnoreCase) || s == "Gauge" || s == "Unsigned32")
                return "Gauge32";
            if (s.Equals("TimeTicks", StringComparison.OrdinalIgnoreCase))
                return "TimeTicks";
            if (s.StartsWith("Counter64", StringComparison.OrdinalIgnoreCase))
                return "Counter64";
            return "OctetString";
        }

        [RelayCommand]
        private async Task ContextSet(OidTreeNode node)
        {
            var device = _deviceList.SelectedDevice;
            if (device == null || node == null) return;
            var target = SnmpRecorderService.ResolveTarget(device);

            var oid = node.IsLeaf && !node.Oid.EndsWith(".0") ? node.Oid + ".0" : node.Oid;

            // Quick live GET to fetch actual current value and type (3s timeout — don't block the user)
            IsTreeQuerying = true;
            TreeQueryStatus = $"Reading {oid}...";
            SnmpRecord liveResult = null;
            try
            {
                var getTask = _recorder.GetSingleAsync(target, oid);
                if (await Task.WhenAny(getTask, Task.Delay(3000)) == getTask)
                    liveResult = await getTask;
            }
            catch { /* continue with empty value */ }
            finally { IsTreeQuerying = false; }

            var currentValue = liveResult?.Value ?? "";
            var valueType = ResolveSnmpType(node, liveResult?.ValueType);

            // Look up MIB enum values for hint display
            string enumHint = null;
            _mibStore.LoadedOids.TryGetValue(node.Oid, out var mibDef);
            if (mibDef?.EnumValues != null && mibDef.EnumValues.Count > 0)
            {
                var pairs = mibDef.EnumValues.Select(kv => $"{kv.Value} = {kv.Key}");
                enumHint = string.Join(",  ", pairs);
            }

            // Build SET dialog
            var dialog = new System.Windows.Window
            {
                Title = $"SET {node.DisplayName}",
                Width = 460, Height = enumHint != null ? 300 : 240,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                Owner = System.Windows.Application.Current.MainWindow,
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#1A1E2E")),
                ResizeMode = System.Windows.ResizeMode.NoResize
            };

            var stack = new System.Windows.Controls.StackPanel { Margin = new System.Windows.Thickness(16) };

            // OID label
            var lblOid = new System.Windows.Controls.TextBlock
            {
                Text = $"OID: {oid}",
                Foreground = System.Windows.Media.Brushes.LightGray,
                FontFamily = new System.Windows.Media.FontFamily("Cascadia Code,Consolas"),
                FontSize = 11, Margin = new System.Windows.Thickness(0, 0, 0, 8)
            };

            // Type (read-only — auto-resolved from MIB)
            var typePanel = new System.Windows.Controls.DockPanel { Margin = new System.Windows.Thickness(0, 0, 0, 8) };
            var lblType = new System.Windows.Controls.TextBlock
            {
                Text = "Type:",
                Foreground = System.Windows.Media.Brushes.Gray,
                FontSize = 12, VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new System.Windows.Thickness(0, 0, 8, 0)
            };
            System.Windows.Controls.DockPanel.SetDock(lblType, System.Windows.Controls.Dock.Left);
            var lblTypeValue = new System.Windows.Controls.TextBlock
            {
                Text = valueType,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4FC3F7")),
                FontSize = 12, FontWeight = System.Windows.FontWeights.SemiBold,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            typePanel.Children.Add(lblType);
            typePanel.Children.Add(lblTypeValue);

            // Current value (read-only)
            var currentPanel = new System.Windows.Controls.DockPanel { Margin = new System.Windows.Thickness(0, 0, 0, 8) };
            var lblCurrent = new System.Windows.Controls.TextBlock
            {
                Text = "Current:",
                Foreground = System.Windows.Media.Brushes.Gray,
                FontSize = 12, VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new System.Windows.Thickness(0, 0, 8, 0)
            };
            System.Windows.Controls.DockPanel.SetDock(lblCurrent, System.Windows.Controls.Dock.Left);
            var lblCurrentValue = new System.Windows.Controls.TextBlock
            {
                Text = string.IsNullOrEmpty(currentValue) ? "(no response)" : currentValue,
                Foreground = System.Windows.Media.Brushes.LightGreen,
                FontFamily = new System.Windows.Media.FontFamily("Cascadia Code,Consolas"),
                FontSize = 12, VerticalAlignment = System.Windows.VerticalAlignment.Center,
                TextTrimming = System.Windows.TextTrimming.CharacterEllipsis
            };
            currentPanel.Children.Add(lblCurrent);
            currentPanel.Children.Add(lblCurrentValue);

            // Enum hint (if MIB defines named values)
            System.Windows.Controls.TextBlock lblEnumHint = null;
            if (enumHint != null)
            {
                lblEnumHint = new System.Windows.Controls.TextBlock
                {
                    Text = $"Values: {enumHint}",
                    Foreground = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFB74D")),
                    FontSize = 10, TextWrapping = System.Windows.TextWrapping.Wrap,
                    Margin = new System.Windows.Thickness(0, 0, 0, 8)
                };
            }

            // New value input
            var lblVal = new System.Windows.Controls.TextBlock
            {
                Text = "New value:",
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 12, Margin = new System.Windows.Thickness(0, 0, 0, 4)
            };

            var txtValue = new System.Windows.Controls.TextBox
            {
                Text = currentValue,
                FontSize = 13, Padding = new System.Windows.Thickness(6, 4, 6, 4)
            };
            txtValue.SelectAll();

            var btnPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                Margin = new System.Windows.Thickness(0, 12, 0, 0)
            };

            var btnOk = new System.Windows.Controls.Button
            {
                Content = "SET", Width = 80, Padding = new System.Windows.Thickness(0, 4, 0, 4),
                IsDefault = true
            };
            btnOk.Click += (s, e) => { dialog.DialogResult = true; };

            var btnCancel = new System.Windows.Controls.Button
            {
                Content = "Cancel", Width = 80, Padding = new System.Windows.Thickness(0, 4, 0, 4),
                Margin = new System.Windows.Thickness(8, 0, 0, 0), IsCancel = true
            };

            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnCancel);
            stack.Children.Add(lblOid);
            stack.Children.Add(typePanel);
            stack.Children.Add(currentPanel);
            if (lblEnumHint != null) stack.Children.Add(lblEnumHint);
            stack.Children.Add(lblVal);
            stack.Children.Add(txtValue);
            stack.Children.Add(btnPanel);
            dialog.Content = stack;

            if (dialog.ShowDialog() != true)
                return;

            var newValue = txtValue.Text;
            IsTreeQuerying = true;
            TreeQueryStatus = $"SET {oid} = {newValue}...";
            try
            {
                var success = await _recorder.SetAsync(target, oid, newValue, valueType);
                TreeQueryResults.Insert(0, new QueryResultItem
                {
                    Time = DateTime.Now.ToString("HH:mm:ss"),
                    Operation = "SET",
                    Oid = oid,
                    Name = node.DisplayName,
                    ValueType = valueType,
                    Value = success ? newValue : "SET failed",
                    IsSuccess = success
                });

                if (success)
                {
                    if (node.IsLeaf) node.Value = newValue;
                    TreeQueryStatus = $"SET OK — {node.DisplayName} = {newValue}";
                }
                else
                {
                    TreeQueryStatus = $"SET failed for {node.DisplayName}";
                }
            }
            catch (Exception ex)
            {
                TreeQueryResults.Insert(0, new QueryResultItem
                {
                    Time = DateTime.Now.ToString("HH:mm:ss"),
                    Operation = "SET",
                    Oid = oid,
                    Name = node.DisplayName,
                    Value = $"Error: {ex.Message}",
                    IsSuccess = false
                });
                TreeQueryStatus = $"SET error: {ex.Message}";
            }
            finally
            {
                IsTreeQuerying = false;
            }
        }

        // ── Context Menu: SNMP WALK (subtree) ──

        [RelayCommand]
        private async Task ContextWalk(OidTreeNode node)
        {
            var device = _deviceList.SelectedDevice;
            if (device == null || node == null) return;
            var target = SnmpRecorderService.ResolveTarget(device);

            IsTreeQuerying = true;
            TreeQueryStatus = $"Walking {node.Oid}...";
            try
            {
                var results = await _recorder.WalkSubtreeAsync(target, node.Oid);

                foreach (var r in results)
                {
                    TreeQueryResults.Insert(0, new QueryResultItem
                    {
                        Time = DateTime.Now.ToString("HH:mm:ss"),
                        Operation = "WALK",
                        Oid = r.Oid,
                        Name = ResolveName(r.Oid, ""),
                        ValueType = r.ValueType,
                        Value = r.Value,
                        IsSuccess = true
                    });
                }

                // Update leaf values in the tree
                var resultLookup = results.ToDictionary(r => r.Oid, r => r);
                UpdateTreeValues(node, resultLookup);

                TreeQueryStatus = $"Walk complete — {results.Count} OIDs under {node.DisplayName}";
            }
            catch (Exception ex)
            {
                TreeQueryResults.Insert(0, new QueryResultItem
                {
                    Time = DateTime.Now.ToString("HH:mm:ss"),
                    Operation = "WALK",
                    Oid = node.Oid,
                    Name = node.DisplayName,
                    Value = $"Error: {ex.Message}",
                    IsSuccess = false
                });
                TreeQueryStatus = $"Walk error: {ex.Message}";
            }
            finally
            {
                IsTreeQuerying = false;
            }
        }

        [RelayCommand]
        private void ClearTreeQueryResults()
        {
            TreeQueryResults.Clear();
            TreeQueryStatus = string.Empty;
        }

        private static void UpdateTreeValues(OidTreeNode node, Dictionary<string, SnmpRecord> lookup)
        {
            if (node.IsLeaf)
            {
                // Try exact OID, then with .0 suffix
                if (lookup.TryGetValue(node.Oid, out var record))
                    node.Value = record.Value;
                else if (lookup.TryGetValue(node.Oid + ".0", out record))
                    node.Value = record.Value;
            }
            foreach (var child in node.Children)
                UpdateTreeValues(child, lookup);
        }
    }

    public partial class OidTreeNode : ObservableObject
    {
        public string Oid { get; set; } = string.Empty;
        public string Segment { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        [ObservableProperty] private string _value;
        public string ValueType { get; set; }
        public string Access { get; set; }
        public bool IsLeaf { get; set; }
        public ObservableCollection<OidTreeNode>  Children { get; } = new ObservableCollection<OidTreeNode>();

        [ObservableProperty] private bool _isExpanded;
        [ObservableProperty] private bool _isHighlighted;

        partial void OnValueChanged(string value)
        {
            OnPropertyChanged(nameof(ValueDisplay));
            OnPropertyChanged(nameof(Label));
            OnPropertyChanged(nameof(TreeLabel));
        }

        /// <summary>True when MIB ACCESS is read-write or read-create.</summary>
        public bool IsWritable => Access != null &&
            (Access.IndexOf("write", StringComparison.OrdinalIgnoreCase) >= 0 ||
             Access.IndexOf("create", StringComparison.OrdinalIgnoreCase) >= 0);

        /// <summary>True when node has children (branch node — suitable for WALK).</summary>
        public bool HasChildren => Children.Count > 0;

        /// <summary>For tree binding: " = value" for leaves, empty for branches. Truncated to 80 chars.</summary>
        public string ValueDisplay => IsLeaf && Value != null
            ? (Value.Length > 80 ? $"  =  {Value.Substring(0, 80)}..." : $"  =  {Value}")
            : string.Empty;

        public string Label => Value != null
            ? $"{DisplayName} ({Oid}) = {Value}"
            : $"{DisplayName} ({Oid})";

        public string TreeLabel => Value != null
            ? $"{DisplayName} = {TruncateValue(Value, 60)}"
            : DisplayName;

        private static string TruncateValue(string val, int max) =>
            val.Length > max ? val.Substring(0, max) + "..." : val;
    }

    public class SystemInfoItem
    {
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Category { get; set; } = "system"; // identity, system, network
    }

    public class PanelTableItem
    {
        public string Name { get; set; } = string.Empty;
        public int RowCount { get; set; }
        public int ColumnCount { get; set; }
        public DataView Data { get; set; }
        public MibTableSchema SourceTable { get; set; }
    }
}
