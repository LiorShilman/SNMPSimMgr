using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SNMPSimMgr.Models;
using SNMPSimMgr.Services;

namespace SNMPSimMgr.ViewModels
{
    public partial class DeviceListViewModel : ObservableObject
    {
        private readonly DeviceProfileStore _store;
        private readonly PackageService  _packageService = new PackageService();

        public ObservableCollection<DeviceProfile>  Devices { get; } = new ObservableCollection<DeviceProfile>();

        [ObservableProperty]
        private DeviceProfile _selectedDevice;

        /// <summary>True when editing an existing device (form populated from selection).</summary>
        [ObservableProperty] private bool _isEditing;

        // New/Edit device form fields
        [ObservableProperty] private string _newName = string.Empty;
        [ObservableProperty] private string _newIp = string.Empty;
        [ObservableProperty] private int _newPort = 161;
        [ObservableProperty] private SnmpVersionOption _newVersion = SnmpVersionOption.V2c;
        [ObservableProperty] private string _newCommunity = "public";
        [ObservableProperty] private string _newV3User = string.Empty;
        [ObservableProperty] private string _newV3AuthPass = string.Empty;
        [ObservableProperty] private string _newV3PrivPass = string.Empty;
        [ObservableProperty] private AuthProtocol _newV3AuthProto = AuthProtocol.MD5;
        [ObservableProperty] private PrivProtocol _newV3PrivProto = PrivProtocol.DES;

        public DeviceListViewModel(DeviceProfileStore store)
        {
            _store = store;
        }

        partial void OnSelectedDeviceChanged(DeviceProfile value)
        {
            if (value != null)
            {
                // Populate form with selected device's data for editing
                NewName = value.Name;
                NewIp = value.IpAddress;
                NewPort = value.Port;
                NewVersion = value.Version;
                NewCommunity = value.Community;
                NewV3User = value.V3Credentials?.Username ?? "";
                NewV3AuthPass = value.V3Credentials?.AuthPassword ?? "";
                NewV3PrivPass = value.V3Credentials?.PrivPassword ?? "";
                NewV3AuthProto = value.V3Credentials?.AuthProtocol ?? AuthProtocol.MD5;
                NewV3PrivProto = value.V3Credentials?.PrivProtocol ?? PrivProtocol.DES;
                IsEditing = true;
            }
            else
            {
                IsEditing = false;
            }
        }

        public async Task LoadAsync()
        {
            var profiles = await _store.LoadProfilesAsync();
            Devices.Clear();
            foreach (var p in profiles)
                Devices.Add(p);
        }

        [RelayCommand]
        private async Task AddDevice()
        {
            if (string.IsNullOrWhiteSpace(NewName) || string.IsNullOrWhiteSpace(NewIp))
                return;

            var device = new DeviceProfile() {
                Name = NewName,
                IpAddress = NewIp,
                Port = NewPort,
                Version = NewVersion,
                Community = NewCommunity,
            };

            if (NewVersion == SnmpVersionOption.V3)
            {
                device.V3Credentials = new SnmpV3Credentials() {
                    Username = NewV3User,
                    AuthProtocol = NewV3AuthProto,
                    AuthPassword = NewV3AuthPass,
                    PrivProtocol = NewV3PrivProto,
                    PrivPassword = NewV3PrivPass,
                };
            }

            Devices.Add(device);
            await SaveAsync();

            // Clear form
            IsEditing = false;
            ClearForm();
        }

        [RelayCommand]
        private async Task SaveEdit()
        {
            if (SelectedDevice == null || !IsEditing) return;
            if (string.IsNullOrWhiteSpace(NewName) || string.IsNullOrWhiteSpace(NewIp)) return;

            SelectedDevice.Name = NewName;
            SelectedDevice.IpAddress = NewIp;
            SelectedDevice.Port = NewPort;
            SelectedDevice.Version = NewVersion;
            SelectedDevice.Community = NewCommunity;

            if (NewVersion == SnmpVersionOption.V3)
            {
                SelectedDevice.V3Credentials = new SnmpV3Credentials()
                {
                    Username = NewV3User,
                    AuthProtocol = NewV3AuthProto,
                    AuthPassword = NewV3AuthPass,
                    PrivProtocol = NewV3PrivProto,
                    PrivPassword = NewV3PrivPass,
                };
            }
            else
            {
                SelectedDevice.V3Credentials = null;
            }

            await SaveAsync();

            // Force DataGrid refresh by replacing item in collection
            var idx = Devices.IndexOf(SelectedDevice);
            if (idx >= 0)
            {
                var device = SelectedDevice;
                Devices[idx] = device;
                SelectedDevice = device;
            }
        }

        [RelayCommand]
        private void CancelEdit()
        {
            SelectedDevice = null;
            IsEditing = false;
            ClearForm();
        }

        private void ClearForm()
        {
            NewName = string.Empty;
            NewIp = string.Empty;
            NewPort = 161;
            NewVersion = SnmpVersionOption.V2c;
            NewCommunity = "public";
            NewV3User = string.Empty;
            NewV3AuthPass = string.Empty;
            NewV3PrivPass = string.Empty;
            NewV3AuthProto = AuthProtocol.MD5;
            NewV3PrivProto = PrivProtocol.DES;
        }

        [RelayCommand]
        private async Task RemoveDevice()
        {
            if (SelectedDevice == null) return;
            Devices.Remove(SelectedDevice);
            SelectedDevice = null;
            IsEditing = false;
            ClearForm();
            await SaveAsync();
        }

        [RelayCommand]
        private async Task AddTestDevices()
        {
            var testDevices = new[]
            {
                new DeviceProfile
                {
                    Name = "demo.pysnmp.com",
                    IpAddress = "demo.pysnmp.com",
                    Port = 161,
                    Version = SnmpVersionOption.V2c,
                    Community = "public"
                },
                new DeviceProfile
                {
                    Name = "Local Simulator",
                    IpAddress = "127.0.0.1",
                    Port = 10161,
                    Version = SnmpVersionOption.V2c,
                    Community = "public"
                }
            };

            foreach (var device in testDevices)
            {
                if (!Devices.Any(d => d.Name == device.Name))
                    Devices.Add(device);
            }

            await SaveAsync();
        }

        [ObservableProperty] private string _packageStatus = string.Empty;

        // Discovery
        private readonly DiscoveryService  _discovery = new DiscoveryService();
        private CancellationTokenSource _discoveryCts;
        public ObservableCollection<DiscoveredDevice>  DiscoveredDevices { get; } = new ObservableCollection<DiscoveredDevice>();

        [ObservableProperty] private string _scanSubnet = "192.168.1.0";
        [ObservableProperty] private int _scanStart = 1;
        [ObservableProperty] private int _scanEnd = 254;
        [ObservableProperty] private string _scanCommunity = "public";
        [ObservableProperty] private bool _isScanning;
        [ObservableProperty] private int _scanProgress;
        [ObservableProperty] private int _scanTotal = 254;
        [ObservableProperty] private string _scanStatus = string.Empty;

        [RelayCommand]
        private async Task StartDiscovery()
        {
            if (IsScanning) return;

            IsScanning = true;
            ScanProgress = 0;
            ScanTotal = ScanEnd - ScanStart + 1;
            DiscoveredDevices.Clear();
            ScanStatus = "Scanning...";

            _discoveryCts = new CancellationTokenSource();

            _discovery.ProgressChanged += (done, total) =>
                App.Current.Dispatcher.Invoke(() =>
                {
                    ScanProgress = done;
                    ScanStatus = $"Scanning... {done}/{total}";
                });

            try
            {
                var results = await _discovery.ScanRangeAsync(
                    ScanSubnet, ScanStart, ScanEnd,
                    ScanCommunity, 161, _discoveryCts.Token);

                foreach (var d in results)
                    DiscoveredDevices.Add(d);

                ScanStatus = $"Found {results.Count} devices";
            }
            catch (OperationCanceledException)
            {
                ScanStatus = "Scan cancelled";
            }
            catch (Exception ex)
            {
                ScanStatus = $"Scan error: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
            }
        }

        [RelayCommand]
        private void StopDiscovery()
        {
            _discoveryCts?.Cancel();
        }

        [RelayCommand]
        private async Task AddDiscoveredDevices()
        {
            int added = 0;
            foreach (var d in DiscoveredDevices.Where(d => d.IsSelected))
            {
                var name = !string.IsNullOrEmpty(d.SysName) ? d.SysName : d.IpAddress;
                if (Devices.Any(dev => dev.IpAddress == d.IpAddress)) continue;

                Devices.Add(new DeviceProfile
                {
                    Name = name,
                    IpAddress = d.IpAddress,
                    Port = d.Port,
                    Version = SnmpVersionOption.V2c,
                    Community = d.Community
                });
                added++;
            }

            if (added > 0) await SaveAsync();
            ScanStatus = $"Added {added} devices";
        }

        [RelayCommand]
        private async Task ExportPackage()
        {
            if (Devices.Count == 0)
            {
                PackageStatus = "No devices to export!";
                return;
            }

            var dialog = new SaveFileDialog() {
                Filter = "SNMP Sim Package (*.snmpsim)|*.snmpsim",
                DefaultExt = ".snmpsim",
                FileName = $"snmpsim_export_{DateTime.Now:yyyyMMdd_HHmm}"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                PackageStatus = "Exporting...";
                await _packageService.ExportAsync(dialog.FileName, Devices.ToList());
                PackageStatus = $"Exported {Devices.Count} devices to {System.IO.Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                PackageStatus = $"Export failed: {ex.Message}";
            }
        }

        [RelayCommand]
        private async Task ImportPackage()
        {
            var dialog = new OpenFileDialog() {
                Filter = "SNMP Sim Package (*.snmpsim)|*.snmpsim|All files (*.*)|*.*",
                DefaultExt = ".snmpsim"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                PackageStatus = "Importing...";
                var imported = await _packageService.ImportAsync(dialog.FileName);

                foreach (var device in imported)
                {
                    if (!Devices.Any(d => d.Name == device.Name))
                        Devices.Add(device);
                }

                await SaveAsync();
                PackageStatus = $"Imported {imported.Count} devices from {System.IO.Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                PackageStatus = $"Import failed: {ex.Message}";
            }
        }

        public async Task SaveAsync()
        {
            await _store.SaveProfilesAsync(Devices.ToList());
        }
    }
}
