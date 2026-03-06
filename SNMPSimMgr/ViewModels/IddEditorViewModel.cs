using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SNMPSimMgr.Models;
using SNMPSimMgr.Services;

namespace SNMPSimMgr.ViewModels
{
    public partial class IddEditorViewModel : ObservableObject
    {
        private readonly DeviceProfileStore _store;
        private readonly DeviceListViewModel _deviceListVm;

        private static readonly string SchemasRoot = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Data", "schemas");

        // ── Device Identity ──────────────────────────────────────────────
        [ObservableProperty] private string _deviceName = "My-IDD-Device";
        [ObservableProperty] private string _deviceIp = "10.0.0.100";

        // ── Fields Collection ────────────────────────────────────────────
        public ObservableCollection<IddFieldDef>  Fields { get; } = new ObservableCollection<IddFieldDef>();

        [ObservableProperty] private IddFieldDef _selectedField;

        [ObservableProperty] private string _statusText = string.Empty;

        // ── Edit Form Fields ─────────────────────────────────────────────
        [ObservableProperty] private string _editId = string.Empty;
        [ObservableProperty] private string _editName = string.Empty;
        [ObservableProperty] private string _editDescription = string.Empty;
        [ObservableProperty] private string _editGroup = string.Empty;
        [ObservableProperty] private string _editType = "text";
        [ObservableProperty] private bool _editIsWritable;
        [ObservableProperty] private string _editDefaultValue = string.Empty;
        [ObservableProperty] private string _editUnits = string.Empty;
        [ObservableProperty] private string _editMin = string.Empty;
        [ObservableProperty] private string _editMax = string.Empty;
        [ObservableProperty] private string _editMaxLength = string.Empty;
        [ObservableProperty] private string _editOptionsText = string.Empty;
        [ObservableProperty] private bool _isEditing;

        // ── Available Types ──────────────────────────────────────────────
        public string[] FieldTypes { get; } =
        {
            "text", "number", "enum", "toggle", "status-led",
            "gauge", "counter", "timeticks", "ip", "oid", "bits"
        };

        public IddEditorViewModel(DeviceProfileStore store, DeviceListViewModel deviceListVm)
        {
            _store = store;
            _deviceListVm = deviceListVm;
            Directory.CreateDirectory(SchemasRoot);
        }

        // ── When selection changes, populate edit form ────────────────────
        partial void OnSelectedFieldChanged(IddFieldDef value)
        {
            if (value == null)
            {
                ClearForm();
                return;
            }

            IsEditing = true;
            EditId = value.Id;
            EditName = value.Name;
            EditDescription = value.Description ?? string.Empty;
            EditGroup = value.Group ?? string.Empty;
            EditType = value.Type ?? "text";
            EditIsWritable = value.IsWritable;
            EditDefaultValue = value.DefaultValue ?? string.Empty;
            EditUnits = value.Units ?? string.Empty;
            EditMin = value.Min.HasValue ? value.Min.Value.ToString() : string.Empty;
            EditMax = value.Max.HasValue ? value.Max.Value.ToString() : string.Empty;
            EditMaxLength = value.MaxLength.HasValue ? value.MaxLength.Value.ToString() : string.Empty;
            EditOptionsText = OptionsToText(value.Options);
        }

        // ── Add Field ────────────────────────────────────────────────────
        [RelayCommand]
        private void AddField()
        {
            if (string.IsNullOrWhiteSpace(EditId) || string.IsNullOrWhiteSpace(EditName))
            {
                StatusText = "Id and Name are required";
                return;
            }

            if (Fields.Any(f => f.Id == EditId))
            {
                StatusText = $"Field with Id '{EditId}' already exists — use Update";
                return;
            }

            var field = BuildFieldFromForm();
            Fields.Add(field);
            StatusText = $"Added: {field.Name} ({field.Type})";
            ClearForm();
        }

        // ── Update Field ─────────────────────────────────────────────────
        [RelayCommand]
        private void UpdateField()
        {
            if (SelectedField == null)
            {
                StatusText = "Select a field to update";
                return;
            }

            if (string.IsNullOrWhiteSpace(EditId) || string.IsNullOrWhiteSpace(EditName))
            {
                StatusText = "Id and Name are required";
                return;
            }

            var index = Fields.IndexOf(SelectedField);
            if (index < 0) return;

            var updated = BuildFieldFromForm();
            Fields[index] = updated;
            SelectedField = updated;
            StatusText = $"Updated: {updated.Name}";
        }

        // ── Delete Field ─────────────────────────────────────────────────
        [RelayCommand]
        private void DeleteField()
        {
            if (SelectedField == null)
            {
                StatusText = "Select a field to delete";
                return;
            }

            var name = SelectedField.Name;
            Fields.Remove(SelectedField);
            SelectedField = null;
            ClearForm();
            StatusText = $"Deleted: {name}";
        }

        // ── Duplicate Field ──────────────────────────────────────────────
        [RelayCommand]
        private void DuplicateField()
        {
            if (SelectedField == null)
            {
                StatusText = "Select a field to duplicate";
                return;
            }

            var clone = BuildFieldFromForm();
            clone.Id = clone.Id + ".copy";
            clone.Name = clone.Name + " (copy)";
            var index = Fields.IndexOf(SelectedField);
            Fields.Insert(index + 1, clone);
            SelectedField = clone;
            StatusText = $"Duplicated: {clone.Name}";
        }

        // ── Move Up/Down ─────────────────────────────────────────────────
        [RelayCommand]
        private void MoveUp()
        {
            if (SelectedField == null) return;
            var index = Fields.IndexOf(SelectedField);
            if (index <= 0) return;
            Fields.Move(index, index - 1);
        }

        [RelayCommand]
        private void MoveDown()
        {
            if (SelectedField == null) return;
            var index = Fields.IndexOf(SelectedField);
            if (index < 0 || index >= Fields.Count - 1) return;
            Fields.Move(index, index + 1);
        }

        // ── Clear Form ───────────────────────────────────────────────────
        [RelayCommand]
        private void ClearForm()
        {
            IsEditing = false;
            SelectedField = null;
            EditId = string.Empty;
            EditName = string.Empty;
            EditDescription = string.Empty;
            EditGroup = string.Empty;
            EditType = "text";
            EditIsWritable = false;
            EditDefaultValue = string.Empty;
            EditUnits = string.Empty;
            EditMin = string.Empty;
            EditMax = string.Empty;
            EditMaxLength = string.Empty;
            EditOptionsText = string.Empty;
        }

        // ── Clear All Fields ─────────────────────────────────────────────
        [RelayCommand]
        private void ClearAll()
        {
            Fields.Clear();
            ClearForm();
            StatusText = "All fields cleared";
        }

        // ── Save to JSON ─────────────────────────────────────────────────
        [RelayCommand]
        private void SaveToJson()
        {
            if (Fields.Count == 0)
            {
                StatusText = "No fields to save";
                return;
            }

            if (string.IsNullOrWhiteSpace(DeviceName))
            {
                StatusText = "Device Name is required";
                return;
            }

            var dialog = new SaveFileDialog() {
                Filter = "JSON Files (*.json)|*.json",
                DefaultExt = ".json",
                FileName = DeviceName + ".json",
                InitialDirectory = SchemasRoot
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                IddPanelBuilderService.ExportSchemaToFile(
                    DeviceName, DeviceIp, Fields.ToList(), dialog.FileName);

                // Also save a copy to Data/schemas/ for auto-loading
                var schemasCopy = Path.Combine(SchemasRoot, DeviceName + ".json");
                if (!string.Equals(dialog.FileName, schemasCopy, StringComparison.OrdinalIgnoreCase))
                {
                    IddPanelBuilderService.ExportSchemaToFile(
                        DeviceName, DeviceIp, Fields.ToList(), schemasCopy);
                }

                StatusText = $"Saved {Fields.Count} fields to {Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                StatusText = $"Save failed: {ex.Message}";
            }
        }

        // ── Quick Save (to Data/schemas/) ────────────────────────────────
        [RelayCommand]
        private void QuickSave()
        {
            if (Fields.Count == 0)
            {
                StatusText = "No fields to save";
                return;
            }

            if (string.IsNullOrWhiteSpace(DeviceName))
            {
                StatusText = "Device Name is required";
                return;
            }

            try
            {
                var path = Path.Combine(SchemasRoot, DeviceName + ".json");
                IddPanelBuilderService.ExportSchemaToFile(
                    DeviceName, DeviceIp, Fields.ToList(), path);
                StatusText = $"Quick-saved {Fields.Count} fields → {DeviceName}.json";
            }
            catch (Exception ex)
            {
                StatusText = $"Quick save failed: {ex.Message}";
            }
        }

        // ── Load from JSON ───────────────────────────────────────────────
        [RelayCommand]
        private void LoadFromJson()
        {
            var dialog = new OpenFileDialog() {
                Filter = "JSON Files (*.json)|*.json|All files (*.*)|*.*",
                DefaultExt = ".json",
                InitialDirectory = SchemasRoot
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                var json = File.ReadAllText(dialog.FileName);
                var schema = JsonSerializer.Deserialize<MibPanelSchema>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (schema == null)
                {
                    StatusText = "Failed to parse JSON schema";
                    return;
                }

                // Populate device identity
                DeviceName = schema.DeviceName ?? Path.GetFileNameWithoutExtension(dialog.FileName);
                DeviceIp = schema.DeviceIp ?? "10.0.0.100";

                // Convert MibFieldSchema back to IddFieldDef
                Fields.Clear();
                foreach (var module in schema.Modules ?? new List<MibModuleSchema>())
                {
                    foreach (var scalar in module.Scalars ?? new List<MibFieldSchema>())
                    {
                        Fields.Add(FieldSchemaToIddDef(scalar, module.ModuleName));
                    }
                }

                ClearForm();
                StatusText = $"Loaded {Fields.Count} fields from {Path.GetFileName(dialog.FileName)}";
            }
            catch (Exception ex)
            {
                StatusText = $"Load failed: {ex.Message}";
            }
        }

        // ── Register as Device ───────────────────────────────────────────
        [RelayCommand]
        private async Task RegisterAsDevice()
        {
            if (Fields.Count == 0)
            {
                StatusText = "No fields — add fields first";
                return;
            }

            if (string.IsNullOrWhiteSpace(DeviceName))
            {
                StatusText = "Device Name is required";
                return;
            }

            // Save schema JSON
            var schemaPath = Path.Combine(SchemasRoot, DeviceName + ".json");
            IddPanelBuilderService.ExportSchemaToFile(
                DeviceName, DeviceIp, Fields.ToList(), schemaPath);

            // Check if device already exists
            var existing = _deviceListVm.Devices.FirstOrDefault(d => d.Name == DeviceName);
            if (existing != null)
            {
                existing.IddFields = Fields.ToList();
                existing.SchemaPath = schemaPath;
                StatusText = $"Updated device '{DeviceName}' with {Fields.Count} IDD fields";
            }
            else
            {
                var profile = new DeviceProfile() {
                    Name = DeviceName,
                    IpAddress = DeviceIp,
                    Port = 0,
                    IddFields = Fields.ToList(),
                    SchemaPath = schemaPath
                };
                _deviceListVm.Devices.Add(profile);
                StatusText = $"Registered new device '{DeviceName}' with {Fields.Count} IDD fields";
            }

            await _deviceListVm.SaveAsync();
        }

        // ── Load from Device ─────────────────────────────────────────────
        [RelayCommand]
        private void LoadFromDevice()
        {
            var device = _deviceListVm.SelectedDevice;
            if (device == null)
            {
                StatusText = "Select a device in the DEVICES tab first";
                return;
            }

            DeviceName = device.Name;
            DeviceIp = device.IpAddress;
            Fields.Clear();

            if (device.IddFields != null && device.IddFields.Count > 0)
            {
                foreach (var f in device.IddFields)
                    Fields.Add(CloneField(f));
                ClearForm();
                StatusText = $"Loaded {Fields.Count} IDD fields from '{device.Name}'";
            }
            else if (!string.IsNullOrEmpty(device.SchemaPath) && File.Exists(device.SchemaPath))
            {
                // Try loading from schema JSON
                try
                {
                    var json = File.ReadAllText(device.SchemaPath);
                    var schema = JsonSerializer.Deserialize<MibPanelSchema>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (schema?.Modules != null)
                    {
                        foreach (var module in schema.Modules)
                            foreach (var scalar in module.Scalars ?? new List<MibFieldSchema>())
                                Fields.Add(FieldSchemaToIddDef(scalar, module.ModuleName));
                    }

                    ClearForm();
                    StatusText = $"Loaded {Fields.Count} fields from schema '{Path.GetFileName(device.SchemaPath)}'";
                }
                catch (Exception ex)
                {
                    StatusText = $"Failed to load schema: {ex.Message}";
                }
            }
            else
            {
                StatusText = $"Device '{device.Name}' has no IDD fields or schema file";
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private IddFieldDef BuildFieldFromForm()
        {
            var field = new IddFieldDef() {
                Id = EditId.Trim(),
                Name = EditName.Trim(),
                Description = string.IsNullOrWhiteSpace(EditDescription) ? null : EditDescription.Trim(),
                Group = string.IsNullOrWhiteSpace(EditGroup) ? null : EditGroup.Trim(),
                Type = EditType,
                IsWritable = EditIsWritable,
                DefaultValue = string.IsNullOrWhiteSpace(EditDefaultValue) ? null : EditDefaultValue.Trim(),
                Units = string.IsNullOrWhiteSpace(EditUnits) ? null : EditUnits.Trim()
            };

            if (int.TryParse(EditMin, out var min)) field.Min = min;
            if (int.TryParse(EditMax, out var max)) field.Max = max;
            if (int.TryParse(EditMaxLength, out var ml)) field.MaxLength = ml;

            var options = TextToOptions(EditOptionsText);
            if (options.Count > 0) field.Options = options;

            return field;
        }

        private static IddFieldDef FieldSchemaToIddDef(MibFieldSchema scalar, string moduleName)
        {
            var def = new IddFieldDef() {
                Id = scalar.Oid ?? string.Empty,
                Name = scalar.Name ?? string.Empty,
                Description = scalar.Description,
                Group = moduleName,
                Type = scalar.InputType ?? "text",
                IsWritable = scalar.IsWritable,
                DefaultValue = scalar.CurrentValue,
                Units = scalar.Units
            };

            if (scalar.MinValue.HasValue) def.Min = (int)scalar.MinValue.Value;
            if (scalar.MaxValue.HasValue) def.Max = (int)scalar.MaxValue.Value;
            if (scalar.MaxLength.HasValue) def.MaxLength = (int)scalar.MaxLength.Value;

            if (scalar.Options != null && scalar.Options.Count > 0)
            {
                def.Options = new Dictionary<string, int>();
                foreach (var opt in scalar.Options)
                    def.Options[opt.Label] = opt.Value;
            }

            return def;
        }

        private static IddFieldDef CloneField(IddFieldDef src) => new IddFieldDef()
        {
            Id = src.Id,
            Name = src.Name,
            Description = src.Description,
            Group = src.Group,
            Type = src.Type,
            IsWritable = src.IsWritable,
            DefaultValue = src.DefaultValue,
            Units = src.Units,
            Min = src.Min,
            Max = src.Max,
            MaxLength = src.MaxLength,
            Options = src.Options != null ? new Dictionary<string, int>(src.Options) : null
        };

        private static string OptionsToText(Dictionary<string, int> options)
        {
            if (options == null || options.Count == 0) return string.Empty;
            return string.Join("\n", options.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        }

        private static Dictionary<string, int> TextToOptions(string text)
        {
            var result = new Dictionary<string, int>();
            if (string.IsNullOrWhiteSpace(text)) return result;

            foreach (var line in text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var eqIndex = line.IndexOf('=');
                if (eqIndex <= 0) continue;
                var label = line.Substring(0, eqIndex).Trim();
                var valStr = line.Substring(eqIndex + 1).Trim();
                if (int.TryParse(valStr, out var val))
                    result[label] = val;
            }

            return result;
        }
    }
}
