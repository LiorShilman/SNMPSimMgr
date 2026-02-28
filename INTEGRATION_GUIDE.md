# MIB Panel UI - Integration Guide

> How to integrate the SNMP MIB Panel into an existing Angular project.

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [File Inventory](#2-file-inventory)
3. [Dependencies](#3-dependencies)
4. [JSON Schema Contract](#4-json-schema-contract)
5. [Step-by-Step Integration](#5-step-by-step-integration)
6. [WPF SignalR Hub — Full C# Implementation](#6-wpf-signalr-hub--full-c-implementation)
7. [Customization](#7-customization)
8. [Minimal Working Example](#8-minimal-working-example)

---

## 1. Architecture Overview

```
┌─────────────────────────────────────────────────────┐
│                  Your Angular App                    │
│                                                     │
│  ┌──────────────┐    ┌──────────────────────────┐   │
│  │ SignalR Hub   │───▶│ MibPanelService          │   │
│  │ (real-time)   │    │ - schema signal           │   │
│  └──────────────┘    │ - sendSet()               │   │
│                      │ - refreshValues()          │   │
│                      └──────────┬───────────────┘   │
│                                 │                    │
│                      ┌──────────▼───────────────┐   │
│                      │ FieldClassifierService    │   │
│                      │ - classifyScalars()       │   │
│                      │ - extractIdentity()       │   │
│                      └──────────┬───────────────┘   │
│                                 │                    │
│         ┌───────────────────────┼──────────────┐    │
│         ▼                       ▼              ▼    │
│  ┌────────────┐  ┌────────────────┐  ┌──────────┐  │
│  │StatusGrid  │  │ConfigGroup     │  │MibTable  │  │
│  │(monitoring)│  │(SET editable)  │  │(tables)  │  │
│  └────────────┘  └────────────────┘  └──────────┘  │
│                                                     │
│  Wrapped in: ModuleSectionComponent                 │
│  Wrapped in: SidePanelComponent (slide-out)         │
└─────────────────────────────────────────────────────┘
```

### Data Flow

1. **JSON schema** arrives (from file, API, or SignalR `RequestSchema`)
2. **MibPanelService** stores it in a `signal<MibPanelSchema>`
3. **FieldClassifierService** categorizes scalars: identity / status / config / counters
4. **ModuleSectionComponent** renders each module with 3 sections:
   - Monitoring (StatusGrid) - read-only status cards
   - Configuration (ConfigGroup) - editable fields with SET
   - Tables (MibTable) - tabular data with cell editing
5. **Real-time updates**: SignalR traffic events auto-update field values via an Angular `effect()`
6. **SET operations**: User edits a value → MibPanelService sends via SignalR → toast feedback

### Key Design Patterns

- **Angular Signals** for state (not RxJS Subjects)
- **Standalone Components** (no NgModule needed)
- **`effect()` + `untracked()`** for auto-updating values without infinite loops
- **Immutable schema cloning** in `emitSchema()` to trigger change detection

---

## 2. File Inventory

Copy these files from `mib-panel-ui/src/app/` into your project:

### Models (1 file)

| File | Purpose |
|------|---------|
| `models/mib-schema.ts` | All TypeScript interfaces: MibPanelSchema, MibFieldSchema, MibTableSchema, EnumOption, SetFeedback, etc. |

### Services (3 files)

| File | Purpose |
|------|---------|
| `services/signalr.service.ts` | SignalR hub client with auto-reconnect. **Adapt to your hub.** |
| `services/mib-panel.service.ts` | Panel state management, SET commands, auto-refresh, live value updates |
| `services/field-classifier.service.ts` | Field categorization (identity/status/config/counter), formatting helpers |

### Core Components (8 components)

| Component | Files | Purpose |
|-----------|-------|---------|
| **SidePanelComponent** | `side-panel/side-panel.component.ts` | Slide-out panel container (inline template) |
| **ModuleSectionComponent** | `module-section/module-section.component.ts` | Module wrapper: monitoring + config + tables (inline template) |
| **StatusGridComponent** | `status-grid/status-grid.component.ts` | Status/monitoring cards grid (inline template) |
| **ConfigGroupComponent** | `config-group/config-group.component.ts + .html + .scss` | Editable config fields with SNMP SET |
| **ScalarFieldComponent** | `scalar-field/scalar-field.component.ts + .html + .scss` | Individual field renderer (all input types) |
| **MibTableComponent** | `mib-table/mib-table.component.ts + .html + .scss` | Table data with cell editing |
| **DeviceCardComponent** | `device-card/device-card.component.ts + .html + .scss` | Device identity header card |
| **SystemInfoComponent** | `system-info/system-info.component.ts + .html + .scss` | System info footer |

### Utility Components (2 components)

| Component | Files | Purpose |
|-----------|-------|---------|
| **SetFeedbackComponent** | `set-feedback/set-feedback.component.ts` | Toast notifications for SET results (inline template) |
| **ConnectionStatusComponent** | `connection-status/connection-status.component.ts` | SignalR connection dot + device picker (inline template) |

### Styles

| File | Purpose |
|------|---------|
| `src/styles.scss` | Global dark theme: fonts, colors, scrollbar styling |

---

## 3. Dependencies

### Required Angular Packages

```bash
npm install @angular/forms
```

`FormsModule` is needed for `[(ngModel)]` bindings in config-group, scalar-field, and mib-table.

### SignalR Client

The project uses **jQuery SignalR 2.x** (for .NET Framework 4.x hubs):

```html
<!-- In your index.html -->
<script src="assets/vendor/jquery-3.7.1.min.js"></script>
<script src="assets/vendor/jquery.signalR-2.4.3.min.js"></script>
```

> **If your backend uses ASP.NET Core SignalR**, replace `signalr.service.ts` with `@microsoft/signalr`:
> ```bash
> npm install @microsoft/signalr
> ```

### No Other Dependencies

The panel has zero external library dependencies beyond Angular core + forms.

---

## 4. JSON Schema Contract

### TypeScript Interfaces

```typescript
// models/mib-schema.ts

export interface MibPanelSchema {
  deviceName: string;
  deviceIp: string;
  devicePort: number;
  community: string;
  snmpVersion: string;
  exportedAt: string;
  totalFields: number;
  modules: MibModuleSchema[];
}

export interface MibModuleSchema {
  moduleName: string;
  scalarCount: number;
  tableCount: number;
  scalars: MibFieldSchema[];
  tables: MibTableSchema[];
}

export interface MibFieldSchema {
  oid: string;                // Unique identifier (OID or custom ID)
  name: string;               // Display name
  description?: string;
  access: string;             // "read-only" | "read-write" | "read-create"
  isWritable: boolean;        // true if user can SET this field
  inputType: string;          // UI type (see table below)
  baseType: string;           // SNMP base type or custom type
  units?: string;             // Display units (e.g., "dBm", "MHz")
  displayHint?: string;
  minValue?: number;          // Validation constraint
  maxValue?: number;
  minLength?: number;
  maxLength?: number;
  defaultValue?: string;
  options?: EnumOption[];     // For enum/toggle fields
  currentValue?: string;      // Current live value
  currentValueType?: string;
  status?: string;
  tableIndex?: string;
}

export interface MibTableSchema {
  name: string;
  oid: string;
  description?: string;
  labelColumn?: string;       // Column name used for row labels
  rowCount: number;
  columnCount: number;
  columns: MibFieldSchema[];
  rows: MibTableRow[];
}

export interface MibTableRow {
  index: string;              // Row index (e.g., "1", "2", "1.3")
  label?: string;             // Friendly row name
  values: Record<string, MibCellValue>;  // Keyed by column OID
}

export interface MibCellValue {
  value: string;
  type?: string;
  enumLabel?: string;
}

export interface EnumOption {
  label: string;              // Human-readable label (e.g., "Enabled")
  value: number;              // Numeric value to send (e.g., 1)
}

export interface SetFeedback {
  id: number;
  oid: string;
  name: string;
  value: string;
  valueType: string;
  timestamp: Date;
  status: 'pending' | 'success' | 'error';
  message?: string;
}
```

### Supported inputType Values

| inputType | UI Rendering | Example |
|-----------|-------------|---------|
| `text` | Text input | Hostname, Description |
| `number` | Number input with min/max | Port, Threshold |
| `enum` | Dropdown select | Protocol (1=v1, 2=v2c, 3=v3) |
| `toggle` | Toggle switch (2-option enum) | Enabled/Disabled |
| `ip` | Text input (IP pattern) | 192.168.1.1 |
| `counter` | Read-only counter card | Packet count |
| `gauge` | Read-only gauge card | Temperature |
| `timeticks` | Formatted uptime | "3d 4h 20m" |
| `status-led` | LED dot indicator | Online/Offline |
| `oid` | OID display | sysObjectID |
| `bits` | Text display | Bit flags |

### Minimal JSON Example

```json
{
  "deviceName": "My Device",
  "deviceIp": "192.168.1.100",
  "devicePort": 161,
  "community": "public",
  "snmpVersion": "V2c",
  "exportedAt": "2026-02-28T12:00:00Z",
  "totalFields": 8,
  "modules": [
    {
      "moduleName": "System Info",
      "scalarCount": 5,
      "tableCount": 0,
      "scalars": [
        {
          "oid": "1.3.6.1.2.1.1.5.0",
          "name": "sysName",
          "description": "Device hostname",
          "access": "read-write",
          "isWritable": true,
          "inputType": "text",
          "baseType": "OCTET STRING",
          "currentValue": "Router-Lab-1",
          "maxLength": 64
        },
        {
          "oid": "1.3.6.1.2.1.1.3.0",
          "name": "sysUpTime",
          "description": "Time since last restart",
          "access": "read-only",
          "isWritable": false,
          "inputType": "timeticks",
          "baseType": "TimeTicks",
          "currentValue": "8654321"
        },
        {
          "oid": "1.3.6.1.4.1.12345.1.1.0",
          "name": "deviceStatus",
          "description": "Overall device status",
          "access": "read-only",
          "isWritable": false,
          "inputType": "status-led",
          "baseType": "Integer32",
          "currentValue": "1",
          "options": [
            { "label": "Offline", "value": 0 },
            { "label": "Online", "value": 1 },
            { "label": "Error", "value": 2 }
          ]
        },
        {
          "oid": "1.3.6.1.4.1.12345.1.2.0",
          "name": "outputEnabled",
          "description": "Enable or disable output",
          "access": "read-write",
          "isWritable": true,
          "inputType": "toggle",
          "baseType": "Integer32",
          "currentValue": "1",
          "options": [
            { "label": "Disabled", "value": 0 },
            { "label": "Enabled", "value": 1 }
          ]
        },
        {
          "oid": "1.3.6.1.4.1.12345.1.3.0",
          "name": "outputPower",
          "description": "Output power level in dBm",
          "access": "read-write",
          "isWritable": true,
          "inputType": "number",
          "baseType": "Integer32",
          "currentValue": "25",
          "units": "dBm",
          "minValue": 0,
          "maxValue": 50
        }
      ],
      "tables": [
        {
          "name": "portTable",
          "oid": "1.3.6.1.4.1.12345.2",
          "labelColumn": "portName",
          "rowCount": 2,
          "columnCount": 3,
          "columns": [
            {
              "oid": "1.3.6.1.4.1.12345.2.1.1",
              "name": "portName",
              "access": "read-only",
              "isWritable": false,
              "inputType": "text",
              "baseType": "OCTET STRING"
            },
            {
              "oid": "1.3.6.1.4.1.12345.2.1.2",
              "name": "portStatus",
              "access": "read-only",
              "isWritable": false,
              "inputType": "status-led",
              "baseType": "Integer32",
              "options": [
                { "label": "Down", "value": 0 },
                { "label": "Up", "value": 1 }
              ]
            },
            {
              "oid": "1.3.6.1.4.1.12345.2.1.3",
              "name": "portSpeed",
              "access": "read-write",
              "isWritable": true,
              "inputType": "enum",
              "baseType": "Integer32",
              "options": [
                { "label": "100 Mbps", "value": 100 },
                { "label": "1 Gbps", "value": 1000 },
                { "label": "10 Gbps", "value": 10000 }
              ]
            }
          ],
          "rows": [
            {
              "index": "1",
              "label": "Port 1",
              "values": {
                "1.3.6.1.4.1.12345.2.1.1": { "value": "Port 1" },
                "1.3.6.1.4.1.12345.2.1.2": { "value": "1", "enumLabel": "Up" },
                "1.3.6.1.4.1.12345.2.1.3": { "value": "1000", "enumLabel": "1 Gbps" }
              }
            },
            {
              "index": "2",
              "label": "Port 2",
              "values": {
                "1.3.6.1.4.1.12345.2.1.1": { "value": "Port 2" },
                "1.3.6.1.4.1.12345.2.1.2": { "value": "0", "enumLabel": "Down" },
                "1.3.6.1.4.1.12345.2.1.3": { "value": "100", "enumLabel": "100 Mbps" }
              }
            }
          ]
        }
      ]
    }
  ]
}
```

---

## 5. Step-by-Step Integration

### Step 1: Copy Models

Copy `mib-panel-ui/src/app/models/mib-schema.ts` into your project:

```
your-project/src/app/models/mib-schema.ts
```

If your JSON schema has different field names, update the interfaces to match. The key is that `MibPanelSchema.modules[].scalars[]` and `.tables[]` drive the UI.

### Step 2: Copy Services

Copy all 3 service files:

```
your-project/src/app/services/
  ├── signalr.service.ts        ← Adapt to your hub (see below)
  ├── mib-panel.service.ts      ← Copy as-is
  └── field-classifier.service.ts ← Copy as-is
```

**Adapting SignalR Service:**

If you already have a SignalR service, you have two options:

**Option A: Use your existing service** — add these hub method wrappers:

```typescript
// Add to your existing SignalR service:
async requestSchema(deviceId: string): Promise<MibPanelSchema> {
  return this.hubProxy.invoke('RequestSchema', deviceId);
}

async requestRefresh(deviceId: string): Promise<Record<string, string>> {
  return this.hubProxy.invoke('RequestRefresh', deviceId);
}

async sendSet(deviceId: string, oid: string, value: string, valueType: string): Promise<SetResult> {
  return this.hubProxy.invoke('SendSet', deviceId, oid, value, valueType);
}

// Add these signals:
connectionState = signal<'disconnected' | 'connecting' | 'connected' | 'error'>('disconnected');
latestTraffic = signal<TrafficEvent | null>(null, { equal: () => false });
```

Then update `mib-panel.service.ts` imports to point to your service.

**Option B: Use our SignalR service** — just change the server URL:

```typescript
// In signalr.service.ts, line ~123:
private serverUrl = 'http://your-server:5050';
```

### Step 3: Copy Components

Copy the entire `components/` folder:

```
your-project/src/app/components/
  ├── side-panel/
  ├── module-section/
  ├── status-grid/
  ├── config-group/
  ├── scalar-field/
  ├── mib-table/
  ├── device-card/
  ├── system-info/
  ├── set-feedback/
  └── connection-status/
```

All components are **standalone** — no NgModule registration needed. Just import them where you use them.

### Step 4: Add FormsModule

Ensure `FormsModule` is available. If your app uses standalone bootstrap:

```typescript
// app.config.ts
import { provideFormsModule } from '@angular/forms';
// Or in each component that uses [(ngModel)]:
import { FormsModule } from '@angular/forms';
```

The components that need `FormsModule` already import it internally:
- `ConfigGroupComponent`
- `ScalarFieldComponent`
- `MibTableComponent`

### Step 5: Wire Into Your Side Panel

Replace your current side panel content with our components. Here's how to integrate into an existing side panel:

```html
<!-- your-component.html -->

<!-- Your existing trigger button -->
<button (click)="isPanelOpen = true">Open Panel</button>

<!-- The MIB Panel side panel -->
<app-side-panel [isOpen]="isPanelOpen" (close)="isPanelOpen = false">
  <!-- Optional: device identity header -->
  <div panel-header>
    <app-device-card
      [identity]="identity()"
      [community]="schema()?.community || ''"
      [snmpVersion]="schema()?.snmpVersion || ''"
      [port]="schema()?.devicePort || 0" />
  </div>

  <!-- Module sections — this is the main content -->
  @if (schema(); as s) {
    @for (mod of s.modules; track mod.moduleName; let i = $index) {
      <app-module-section [module]="mod" />
    }

    <!-- Optional: system info footer -->
    <app-system-info [items]="systemInfo()" />
  }
</app-side-panel>

<!-- SET feedback toasts (place once, anywhere in the app) -->
<app-set-feedback />
```

```typescript
// your-component.ts
import { Component, inject, computed } from '@angular/core';
import { MibPanelService } from './services/mib-panel.service';
import { FieldClassifierService } from './services/field-classifier.service';
import { SidePanelComponent } from './components/side-panel/side-panel.component';
import { ModuleSectionComponent } from './components/module-section/module-section.component';
import { DeviceCardComponent } from './components/device-card/device-card.component';
import { SystemInfoComponent } from './components/system-info/system-info.component';
import { SetFeedbackComponent } from './components/set-feedback/set-feedback.component';

@Component({
  selector: 'app-my-page',
  standalone: true,
  imports: [
    SidePanelComponent,
    ModuleSectionComponent,
    DeviceCardComponent,
    SystemInfoComponent,
    SetFeedbackComponent,
  ],
  templateUrl: './my-page.component.html',
})
export class MyPageComponent {
  private panelService = inject(MibPanelService);
  private classifier = inject(FieldClassifierService);

  schema = this.panelService.schema;
  isPanelOpen = false;

  identity = computed(() => {
    const s = this.schema();
    return s ? this.classifier.extractIdentity(s) : null;
  });

  systemInfo = computed(() => {
    const s = this.schema();
    return s ? this.classifier.extractSystemInfo(s) : [];
  });

  // Load schema when component initializes or when device changes
  loadDevice(deviceId: string) {
    this.panelService.loadFromDevice(deviceId);
    this.isPanelOpen = true;
  }

  // Or load from a JSON object
  loadFromJson(data: MibPanelSchema) {
    this.panelService.loadFromJson(data);
    this.isPanelOpen = true;
  }
}
```

### Step 6: Add Global Styles

Add the dark theme colors to your `styles.scss` (or scope them to the panel container):

```scss
/* Scoped to panel — add to your global styles or component styles */
.mib-panel-theme {
  --bg-main: #141720;
  --bg-panel: #1B1F2A;
  --bg-card: #171b28;
  --border: #2A3040;
  --border-subtle: #252d42;

  --text-primary: #E8EAED;
  --text-secondary: #CDD1D8;
  --text-tertiary: #8C95A6;

  --accent-blue: #4C9AFF;
  --accent-green: #57D9A3;
  --accent-yellow: #FFAB00;
  --accent-red: #FF5252;
  --accent-purple: #A78BFA;

  color: var(--text-secondary);
  font-family: 'Inter', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
}

/* Custom scrollbar */
::-webkit-scrollbar { width: 8px; height: 8px; }
::-webkit-scrollbar-track { background: #1B1F2A; }
::-webkit-scrollbar-thumb {
  background: #3D4663;
  border-radius: 4px;
  &:hover { background: #4C9AFF; }
}
```

### Step 7: SignalR Scripts (if using jQuery SignalR 2.x)

Copy vendor files to your assets:

```
your-project/src/assets/vendor/
  ├── jquery-3.7.1.min.js
  └── jquery.signalR-2.4.3.min.js
```

Add to `index.html`:

```html
<script src="assets/vendor/jquery-3.7.1.min.js"></script>
<script src="assets/vendor/jquery.signalR-2.4.3.min.js"></script>
```

Or configure in `angular.json`:

```json
"scripts": [
  "src/assets/vendor/jquery-3.7.1.min.js",
  "src/assets/vendor/jquery.signalR-2.4.3.min.js"
]
```

---

## 6. WPF SignalR Hub — Full C# Implementation

This is the complete Hub contract your WPF backend needs to implement for the Angular panel to work.

### 6.1 Complete Hub Class (copy-paste ready)

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR;
using Microsoft.AspNet.SignalR.Hubs;
using Newtonsoft.Json;

[HubName("snmpHub")]
public class SnmpHub : Hub
{
    // ── Server methods (called by Angular client) ─────────────────

    /// <summary>
    /// Build and return a full MIB panel schema for a device.
    /// The Angular client calls this when user selects a device.
    /// Must return a MibPanelSchema JSON object.
    /// </summary>
    public async Task<MibPanelSchema> RequestSchema(string deviceId)
    {
        // YOUR IMPLEMENTATION:
        // 1. Find device by ID
        // 2. Build the MibPanelSchema with modules, scalars, tables
        // 3. Return the schema
        throw new NotImplementedException("Implement: build panel schema for device");
    }

    /// <summary>
    /// Batch GET all OID values and return as flat dictionary.
    /// Angular calls this for "Refresh" button.
    /// Returns { "1.3.6.1.2.1.1.5.0": "Router-1", ... }
    /// </summary>
    public async Task<Dictionary<string, string>> RequestRefresh(string deviceId)
    {
        // YOUR IMPLEMENTATION:
        // 1. Find device, get its current schema OIDs
        // 2. Read all current values (from hardware, simulator, or cache)
        // 3. Return flat OID→value map
        throw new NotImplementedException("Implement: refresh all values");
    }

    /// <summary>
    /// Send a SET command to change a single value.
    /// Angular calls this when user edits a field and clicks Save.
    /// </summary>
    public async Task<SetResult> SendSet(string deviceId, string oid, string value, string valueType)
    {
        try
        {
            // YOUR IMPLEMENTATION:
            // 1. Find device by ID
            // 2. Send the SET command to hardware/simulator
            // 3. Return success/failure

            // Example:
            // var device = FindDevice(deviceId);
            // var success = await YourSnmpService.SetAsync(device, oid, value, valueType);
            // return new SetResult { Success = success, Message = success ? "SET OK" : "SET failed" };

            throw new NotImplementedException("Implement: SNMP SET");
        }
        catch (Exception ex)
        {
            return new SetResult { Success = false, Message = ex.Message };
        }
    }

    /// <summary>
    /// Send multiple SETs in batch (for CSV/JSON bulk import).
    /// </summary>
    public async Task<BulkSetResult> SendBulkSet(string deviceId, List<BulkSetItem> items)
    {
        var result = new BulkSetResult { Total = items?.Count ?? 0 };
        foreach (var item in items ?? new List<BulkSetItem>())
        {
            try
            {
                var setResult = await SendSet(deviceId, item.Oid, item.Value, item.ValueType);
                result.Results.Add(new BulkSetItemResult
                {
                    Oid = item.Oid,
                    Success = setResult.Success,
                    Message = setResult.Message
                });
                if (setResult.Success) result.Succeeded++; else result.Failed++;
            }
            catch (Exception ex)
            {
                result.Results.Add(new BulkSetItemResult
                {
                    Oid = item.Oid, Success = false, Message = ex.Message
                });
                result.Failed++;
            }
        }
        return result;
    }

    /// <summary>
    /// List all known devices with their status.
    /// Angular calls this to populate the device picker dropdown.
    /// </summary>
    public async Task<List<DeviceInfo>> GetDevices()
    {
        // YOUR IMPLEMENTATION:
        // Return list of all devices the system knows about.
        // Set IsSimulating=true if device has an active simulator.
        throw new NotImplementedException("Implement: list devices");
    }

    /// <summary>
    /// Send an IDD SET (non-SNMP field, identified by text ID).
    /// Only needed if you have IDD (non-SNMP) devices.
    /// </summary>
    public Task<SetResult> SendIddSet(string deviceId, string fieldId, string value)
    {
        // YOUR IMPLEMENTATION (optional):
        // Handle non-OID field updates (e.g., custom protocol fields)
        return Task.FromResult(new SetResult { Success = true, Message = "IDD SET dispatched" });
    }

    // ── Static broadcast helpers ──────────────────────────────────
    // Call these from your WPF code to push real-time updates to Angular.

    /// <summary>
    /// Broadcast a value change to all connected Angular clients.
    /// Call this after any GET, SET, or poll that changes a value.
    /// The Angular panel auto-finds the matching field and updates the UI.
    /// </summary>
    public static void BroadcastTraffic(string deviceName, string operation,
                                         string oid, string value, string sourceIp)
    {
        var context = GlobalHost.ConnectionManager.GetHubContext<SnmpHub>();
        context.Clients.All.onTrafficReceived(new
        {
            deviceName,
            operation,   // "GET", "SET", "WALK"
            oid,         // e.g., "1.3.6.1.2.1.1.5.0"
            value,       // e.g., "Router-Lab-1"
            sourceIp,    // e.g., "192.168.1.100"
            timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Broadcast a device status change (e.g., started simulating, went offline).
    /// </summary>
    public static void BroadcastDeviceStatus(string deviceId, string deviceName, string status)
    {
        var context = GlobalHost.ConnectionManager.GetHubContext<SnmpHub>();
        context.Clients.All.onDeviceStatusChanged(new
        {
            deviceId,
            deviceName,
            status,      // "Idle", "Simulating", "Recording", "Offline"
            timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Broadcast multiple updated values at once (efficient batch update).
    /// </summary>
    public static void BroadcastMibUpdate(string deviceId, Dictionary<string, string> updatedValues)
    {
        var context = GlobalHost.ConnectionManager.GetHubContext<SnmpHub>();
        context.Clients.All.onMibUpdated(new
        {
            deviceId,
            values = updatedValues,  // { "1.3.6.1.2.1.1.5.0": "NewName", ... }
            timestamp = DateTime.UtcNow
        });
    }
}
```

### 6.2 DTO Classes (Data Transfer Objects)

Add these classes to your project — they define the JSON contract between WPF and Angular:

```csharp
// ── Core DTOs ──────────────────────────────────────────────────

public class SetResult
{
    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; } = string.Empty;
}

public class DeviceInfo
{
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("ipAddress")]
    public string IpAddress { get; set; } = string.Empty;

    [JsonProperty("port")]
    public int Port { get; set; }

    [JsonProperty("isSimulating")]
    public bool IsSimulating { get; set; }

    [JsonProperty("simulatorPort")]
    public int SimulatorPort { get; set; }
}

// ── Bulk SET DTOs ──────────────────────────────────────────────

public class BulkSetItem
{
    [JsonProperty("oid")]
    public string Oid { get; set; } = string.Empty;

    [JsonProperty("value")]
    public string Value { get; set; } = string.Empty;

    [JsonProperty("valueType")]
    public string ValueType { get; set; } = "OctetString";
}

public class BulkSetResult
{
    [JsonProperty("total")]
    public int Total { get; set; }

    [JsonProperty("succeeded")]
    public int Succeeded { get; set; }

    [JsonProperty("failed")]
    public int Failed { get; set; }

    [JsonProperty("results")]
    public List<BulkSetItemResult> Results { get; set; } = new();
}

public class BulkSetItemResult
{
    [JsonProperty("oid")]
    public string Oid { get; set; } = string.Empty;

    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; } = string.Empty;
}
```

### 6.3 MibPanelSchema C# Classes

These match the TypeScript interfaces 1:1 — the JSON serializes directly to what Angular expects:

```csharp
public class MibPanelSchema
{
    [JsonProperty("deviceName")]
    public string DeviceName { get; set; } = string.Empty;

    [JsonProperty("deviceIp")]
    public string DeviceIp { get; set; } = string.Empty;

    [JsonProperty("devicePort")]
    public int DevicePort { get; set; }

    [JsonProperty("community")]
    public string Community { get; set; } = string.Empty;

    [JsonProperty("snmpVersion")]
    public string SnmpVersion { get; set; } = string.Empty;

    [JsonProperty("exportedAt")]
    public string ExportedAt { get; set; } = string.Empty;

    [JsonProperty("totalFields")]
    public int TotalFields { get; set; }

    [JsonProperty("modules")]
    public List<MibModuleSchema> Modules { get; set; } = new();
}

public class MibModuleSchema
{
    [JsonProperty("moduleName")]
    public string ModuleName { get; set; } = string.Empty;

    [JsonProperty("scalarCount")]
    public int ScalarCount { get; set; }

    [JsonProperty("tableCount")]
    public int TableCount { get; set; }

    [JsonProperty("scalars")]
    public List<MibFieldSchema> Scalars { get; set; } = new();

    [JsonProperty("tables")]
    public List<MibTableSchema> Tables { get; set; } = new();
}

public class MibFieldSchema
{
    [JsonProperty("oid")]
    public string Oid { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("description")]
    public string? Description { get; set; }

    [JsonProperty("access")]
    public string Access { get; set; } = "read-only";

    [JsonProperty("isWritable")]
    public bool IsWritable { get; set; }

    /// <summary>
    /// UI input type: text | number | enum | toggle | ip | counter | gauge | timeticks | status-led | oid | bits
    /// </summary>
    [JsonProperty("inputType")]
    public string InputType { get; set; } = "text";

    [JsonProperty("baseType")]
    public string BaseType { get; set; } = "OCTET STRING";

    [JsonProperty("units")]
    public string? Units { get; set; }

    [JsonProperty("displayHint")]
    public string? DisplayHint { get; set; }

    [JsonProperty("minValue")]
    public int? MinValue { get; set; }

    [JsonProperty("maxValue")]
    public int? MaxValue { get; set; }

    [JsonProperty("minLength")]
    public int? MinLength { get; set; }

    [JsonProperty("maxLength")]
    public int? MaxLength { get; set; }

    [JsonProperty("defaultValue")]
    public string? DefaultValue { get; set; }

    [JsonProperty("options")]
    public List<EnumOption>? Options { get; set; }

    [JsonProperty("currentValue")]
    public string? CurrentValue { get; set; }

    [JsonProperty("currentValueType")]
    public string? CurrentValueType { get; set; }
}

public class MibTableSchema
{
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonProperty("oid")]
    public string Oid { get; set; } = string.Empty;

    [JsonProperty("description")]
    public string? Description { get; set; }

    [JsonProperty("labelColumn")]
    public string? LabelColumn { get; set; }

    [JsonProperty("rowCount")]
    public int RowCount { get; set; }

    [JsonProperty("columnCount")]
    public int ColumnCount { get; set; }

    [JsonProperty("columns")]
    public List<MibFieldSchema> Columns { get; set; } = new();

    [JsonProperty("rows")]
    public List<MibTableRow> Rows { get; set; } = new();
}

public class MibTableRow
{
    [JsonProperty("index")]
    public string Index { get; set; } = string.Empty;

    [JsonProperty("label")]
    public string? Label { get; set; }

    [JsonProperty("values")]
    public Dictionary<string, MibCellValue> Values { get; set; } = new();
}

public class MibCellValue
{
    [JsonProperty("value")]
    public string Value { get; set; } = string.Empty;

    [JsonProperty("type")]
    public string? Type { get; set; }

    [JsonProperty("enumLabel")]
    public string? EnumLabel { get; set; }
}

public class EnumOption
{
    [JsonProperty("label")]
    public string Label { get; set; } = string.Empty;

    [JsonProperty("value")]
    public int Value { get; set; }
}
```

### 6.4 OWIN Startup (SignalR Server Registration)

Add this to your WPF App.xaml.cs to start the SignalR server:

```csharp
using Microsoft.Owin.Hosting;
using Owin;
using Microsoft.AspNet.SignalR;

// In App.OnStartup or MainWindow constructor:
var url = "http://localhost:5050";
WebApp.Start(url, app =>
{
    // Enable CORS for Angular dev server
    app.UseCors(Microsoft.Owin.Cors.CorsOptions.AllowAll);

    // Map SignalR hub
    var hubConfig = new HubConfiguration
    {
        EnableDetailedErrors = true,
        EnableJSONP = true
    };
    app.MapSignalR(hubConfig);
});
```

**NuGet packages needed:**

```
Microsoft.AspNet.SignalR.SelfHost
Microsoft.Owin.Cors
```

### 6.5 Pushing Real-Time Updates from WPF

Call these static methods from anywhere in your WPF code to push updates to the Angular panel:

```csharp
// After polling a device value:
SnmpHub.BroadcastTraffic(
    deviceName: "MyDevice",
    operation: "GET",
    oid: "1.3.6.1.2.1.1.5.0",
    value: "Router-Lab-1",
    sourceIp: "192.168.1.100"
);

// After user changes device state:
SnmpHub.BroadcastDeviceStatus(
    deviceId: "device-123",
    deviceName: "MyDevice",
    status: "Simulating"
);

// After batch refresh (more efficient than individual broadcasts):
var values = new Dictionary<string, string>
{
    ["1.3.6.1.2.1.1.5.0"] = "Router-Lab-1",
    ["1.3.6.1.2.1.1.3.0"] = "8654321",
    ["1.3.6.1.4.1.12345.1.2.0"] = "1"
};
SnmpHub.BroadcastMibUpdate("device-123", values);
```

### 6.6 Angular ↔ WPF Communication Flow

```
┌──────────────────────────────────────────────────────────────────┐
│                         WPF Backend                              │
│                                                                  │
│  ┌─────────────┐     ┌────────────────────┐                     │
│  │ Your Device  │────▶│ SnmpHub            │                     │
│  │ Service      │     │                    │                     │
│  └─────────────┘     │ Methods:           │                     │
│                       │  RequestSchema()   │◀── Angular calls    │
│  ┌─────────────┐     │  RequestRefresh()   │                     │
│  │ Polling /    │────▶│  SendSet()          │──▶ Angular calls    │
│  │ Events      │     │  GetDevices()       │                     │
│  └─────────────┘     │                    │                     │
│                       │ Broadcasts:        │                     │
│                       │  BroadcastTraffic()│──▶ Push to Angular  │
│                       │  BroadcastStatus() │──▶ Push to Angular  │
│                       │  BroadcastUpdate() │──▶ Push to Angular  │
│                       └────────────────────┘                     │
└──────────────────────────────────────────────────────────────────┘
         ▲                        │
         │    SignalR WebSocket    │
         │    (localhost:5050)     ▼
┌──────────────────────────────────────────────────────────────────┐
│                      Angular Client                              │
│                                                                  │
│  SignalRService ──▶ MibPanelService ──▶ Components               │
│  (signals)          (schema signal)     (auto-update UI)         │
│                                                                  │
│  User clicks SET ──▶ MibPanelService.sendSet()                   │
│                     ──▶ SignalRService.sendSet()                  │
│                     ──▶ Hub.SendSet()                             │
│                     ◀── SetResult { success, message }           │
│                     ──▶ Toast notification                       │
└──────────────────────────────────────────────────────────────────┘
```

### 6.7 WPF-Side Wiring — Complete App Startup Pattern

This is the most important piece: how your WPF `App.xaml.cs` (or `MainWindow`) ties everything
together — initializing the Hub, starting the server, and wiring device events to broadcasts.

#### Full App.xaml.cs Example

```csharp
using System;
using System.Collections.Specialized;
using System.Windows;
using Microsoft.Owin.Hosting;

public partial class App : Application
{
    private IDisposable? _signalRHost;
    private bool _signalRRunning;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // ── 1. Create your services ──────────────────────────────
        var deviceStore    = new DeviceProfileStore();    // loads/saves device configs
        var snmpService    = new SnmpPollingService();    // polls devices for values
        var schemaBuilder  = new SchemaBuilderService();  // builds MibPanelSchema from device data
        var simulatorVm    = new SimulatorViewModel();    // manages running simulators

        // ── 2. Set Hub static references ─────────────────────────
        // SignalR 2.0 creates a new Hub instance per call — statics
        // are the standard pattern without a DI container.
        SnmpHub.Store         = deviceStore;
        SnmpHub.ExportService = schemaBuilder;
        SnmpHub.SimulatorVm   = simulatorVm;
        // Add any other services your Hub methods need

        // ── 3. Start SignalR server ──────────────────────────────
        StartSignalR(port: 5050);

        // ── 4. Wire device events → SignalR broadcasts ───────────
        WireEventBroadcasts(snmpService, simulatorVm);

        // ── 5. Create and show main window ───────────────────────
        var mainWindow = new MainWindow { DataContext = simulatorVm };
        mainWindow.Show();
    }

    // ─────────────────────────────────────────────────────────────
    // SignalR Server Startup
    // ─────────────────────────────────────────────────────────────
    private void StartSignalR(int port)
    {
        try
        {
            // Try binding to all interfaces (requires admin or urlacl)
            var url = $"http://+:{port}";
            _signalRHost = WebApp.Start<SignalRStartup>(url);
            _signalRRunning = true;
        }
        catch
        {
            try
            {
                // Fallback: localhost only (no admin needed)
                var url = $"http://localhost:{port}";
                _signalRHost = WebApp.Start<SignalRStartup>(url);
                _signalRRunning = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"SignalR failed to start:\n{ex.Message}\n\n" +
                    "The app will work without real-time web panel.\n\n" +
                    "Tip: run as Admin, or execute:\n" +
                    $"netsh http add urlacl url=http://+:{port}/ user=Everyone",
                    "SignalR", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Event → Broadcast Wiring
    // ─────────────────────────────────────────────────────────────
    private void WireEventBroadcasts(
        SnmpPollingService poller,
        SimulatorViewModel simulatorVm)
    {
        // ┌──────────────────────────────────────────────────┐
        // │  PATTERN A: Individual value changes             │
        // │  Fire BroadcastTraffic for every GET/SET result  │
        // └──────────────────────────────────────────────────┘
        poller.ValueReceived += (deviceName, operation, oid, value, sourceIp) =>
        {
            if (!_signalRRunning) return;
            SnmpHub.BroadcastTraffic(deviceName, operation, oid, value, sourceIp);
            // Angular receives → onTrafficReceived → auto-updates matching field
        };

        // ┌──────────────────────────────────────────────────┐
        // │  PATTERN B: Device status changes                │
        // │  Fire BroadcastDeviceStatus on start/stop        │
        // └──────────────────────────────────────────────────┘

        // Option 1: ObservableCollection — fires on add/remove
        simulatorVm.ActiveSimulators.CollectionChanged += (_, args) =>
        {
            if (!_signalRRunning) return;

            if (args.Action == NotifyCollectionChangedAction.Add && args.NewItems != null)
            {
                foreach (SimulatorDeviceStatus s in args.NewItems)
                    SnmpHub.BroadcastDeviceStatus(s.DeviceId, s.DeviceName, "Running");
            }
            if (args.Action == NotifyCollectionChangedAction.Remove && args.OldItems != null)
            {
                foreach (SimulatorDeviceStatus s in args.OldItems)
                    SnmpHub.BroadcastDeviceStatus(s.DeviceId, s.DeviceName, "Stopped");
            }
            // Angular receives → onDeviceStatusChanged → updates device picker
        };

        // Option 2: Simple event (if you don't use ObservableCollection)
        // simulatorVm.DeviceStarted += (id, name) =>
        // {
        //     if (_signalRRunning)
        //         SnmpHub.BroadcastDeviceStatus(id, name, "Running");
        // };
        // simulatorVm.DeviceStopped += (id, name) =>
        // {
        //     if (_signalRRunning)
        //         SnmpHub.BroadcastDeviceStatus(id, name, "Stopped");
        // };

        // ┌──────────────────────────────────────────────────┐
        // │  PATTERN C: Batch value updates                  │
        // │  Fire BroadcastMibUpdate after a full poll cycle  │
        // └──────────────────────────────────────────────────┘
        poller.PollCycleCompleted += (deviceId, allValues) =>
        {
            if (!_signalRRunning) return;

            // allValues = Dictionary<string, string> { oid → value }
            SnmpHub.BroadcastMibUpdate(deviceId, allValues);
            // Angular receives → onMibUpdated → updates all matching fields at once
            // More efficient than sending 100 individual BroadcastTraffic calls
        };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _signalRHost?.Dispose();   // stop SignalR server
        base.OnExit(e);
    }
}
```

#### Your Polling Service — Raising Events

Here's an example of a polling service that raises the events consumed above:

```csharp
public class SnmpPollingService
{
    // ── Events that App.xaml.cs wires to SignalR broadcasts ──

    /// <summary>Fires for each individual value received.</summary>
    public event Action<string, string, string, string, string>? ValueReceived;
    //                   deviceName, operation, oid,    value,  sourceIp

    /// <summary>Fires after a complete poll cycle with all values.</summary>
    public event Action<string, Dictionary<string, string>>? PollCycleCompleted;
    //                   deviceId, { oid → value }

    private readonly System.Timers.Timer _timer;

    public SnmpPollingService(int intervalMs = 5000)
    {
        _timer = new System.Timers.Timer(intervalMs);
        _timer.Elapsed += async (_, _) => await PollAllDevicesAsync();
    }

    public void StartPolling() => _timer.Start();
    public void StopPolling()  => _timer.Stop();

    private async Task PollAllDevicesAsync()
    {
        // For each active device, poll all its OIDs
        foreach (var device in GetActiveDevices())
        {
            var allValues = new Dictionary<string, string>();

            foreach (var oid in device.MonitoredOids)
            {
                try
                {
                    var value = await SnmpGetAsync(device.IpAddress, device.Port,
                                                   device.Community, oid);

                    allValues[oid] = value;

                    // Fire individual value event → BroadcastTraffic
                    ValueReceived?.Invoke(
                        device.Name,       // deviceName
                        "GET",             // operation
                        oid,               // e.g., "1.3.6.1.2.1.1.5.0"
                        value,             // e.g., "Router-Lab-1"
                        device.IpAddress   // sourceIp
                    );
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Poll failed: {oid} — {ex.Message}");
                }
            }

            // Fire batch event → BroadcastMibUpdate (more efficient)
            if (allValues.Count > 0)
                PollCycleCompleted?.Invoke(device.Id, allValues);
        }
    }

    private Task<string> SnmpGetAsync(string ip, int port, string community, string oid)
    {
        // YOUR SNMP library call here (e.g., SharpSNMP, SnmpSharpNet)
        throw new NotImplementedException();
    }

    private IEnumerable<DeviceProfile> GetActiveDevices()
    {
        throw new NotImplementedException();
    }
}
```

#### When to Use Each Broadcast Pattern

| Pattern | Method | When to Use | Angular Handler |
| ------- | ------ | ----------- | --------------- |
| **Individual value** | `BroadcastTraffic()` | After each GET/SET — real-time field updates | `onTrafficReceived` |
| **Device status** | `BroadcastDeviceStatus()` | Device started/stopped/connected/offline | `onDeviceStatusChanged` |
| **Batch update** | `BroadcastMibUpdate()` | After full poll cycle — update all fields at once | `onMibUpdated` |
| **Trap** | `BroadcastTrap()` | When an SNMP trap is received | `onTrapReceived` |

#### How Angular Processes Each Broadcast

```
BroadcastTraffic("MyDevice", "GET", "1.3.6.1.2.1.1.5.0", "Router-1", "10.0.0.1")
    │
    ▼  SignalR WebSocket
    │
    ▼  Angular SignalRService.latestTraffic signal updates
    │
    ▼  MibPanelService effect() detects change
    │
    ▼  Finds field with matching OID in current schema
    │
    ▼  Updates field.currentValue = "Router-1"
    │
    ▼  Emits new schema clone → Angular change detection
    │
    ▼  Component re-renders with new value (no page reload)
```

#### Thread Safety Note

SignalR broadcasts can be called from **any thread** — SignalR handles the serialization
and WebSocket delivery internally. However, if you need to read WPF UI-bound data
(like `ObservableCollection`), use the Dispatcher:

```csharp
// Safe: broadcasting doesn't touch UI
SnmpHub.BroadcastTraffic(name, "GET", oid, value, ip);  // ✓ Any thread

// Needs Dispatcher: reading from ObservableCollection bound to UI
Application.Current.Dispatcher.Invoke(() =>
{
    var items = viewModel.SomeObservableCollection.ToList();
    // Now safe to iterate and broadcast
    foreach (var item in items)
        SnmpHub.BroadcastTraffic(item.Name, "GET", item.Oid, item.Value, item.Ip);
});
```

---

## 7. Customization

### Adding Custom Input Types

In `ConfigGroupComponent` and `ScalarFieldComponent`, find the `@switch (field.inputType)` block and add your case:

```html
@case ('my-custom-type') {
  <my-custom-input [(ngModel)]="editValue" ... />
}
```

### Changing the Color Theme

All colors are hardcoded in component styles. To change them globally, search and replace:

| Current Color | Role | Replace With |
|--------------|------|-------------|
| `#141720` | Main background | Your bg color |
| `#1B1F2A` | Panel/card background | Your card bg |
| `#171b28` | Inner card background | Your inner bg |
| `#252d42` | Subtle borders | Your border |
| `#2A3040` | Main borders | Your border |
| `#E8EAED` | Primary text | Your text |
| `#CDD1D8` | Secondary text | Your secondary |
| `#4C9AFF` | Blue accent | Your primary |
| `#57D9A3` | Green/success | Your success |
| `#FFAB00` | Yellow/warning | Your warning |
| `#FF5252` | Red/error | Your error |
| `#A78BFA` | Purple (tables) | Your table accent |

### Modifying Field Classification

In `field-classifier.service.ts`, edit the pattern arrays:

```typescript
// Add your field name patterns for identity fields:
const IDENTITY_PATTERNS = [
  /devicename|sysname/i,
  /model/i,
  /firmware|fwver|swver/i,
  /my-custom-identity-field/i,  // ← add here
];

// Add your status patterns:
const STATUS_PATTERNS = [
  /status/i,
  /alarm/i,
  /my-custom-status/i,  // ← add here
];
```

### Simulated Mode (No SignalR)

When SignalR is not connected, SET operations automatically fall back to **simulated mode** — the value updates locally with a 400-600ms fake delay and shows "SET acknowledged (simulated)" in the toast. No code changes needed.

To load data without SignalR at all:

```typescript
// Load from a JSON file
this.panelService.loadFromFile(file);

// Load from an object
this.panelService.loadFromJson(mySchemaObject);
```

---

## 8. Minimal Working Example

A complete standalone component that renders the MIB panel from a JSON schema:

```typescript
// minimal-panel.component.ts
import { Component, inject, computed, OnInit } from '@angular/core';
import { MibPanelService } from './services/mib-panel.service';
import { FieldClassifierService } from './services/field-classifier.service';
import { SidePanelComponent } from './components/side-panel/side-panel.component';
import { ModuleSectionComponent } from './components/module-section/module-section.component';
import { SetFeedbackComponent } from './components/set-feedback/set-feedback.component';
import { MibPanelSchema } from './models/mib-schema';

@Component({
  selector: 'app-minimal-panel',
  standalone: true,
  imports: [SidePanelComponent, ModuleSectionComponent, SetFeedbackComponent],
  template: `
    <button (click)="isPanelOpen = true"
            style="padding: 12px 24px; background: #4C9AFF; color: white;
                   border: none; border-radius: 8px; cursor: pointer; font-size: 16px;">
      Open MIB Panel
    </button>

    <app-side-panel [isOpen]="isPanelOpen" (close)="isPanelOpen = false">
      <h2 panel-header style="color: #E8EAED; font-size: 18px; margin: 0;">
        {{ schema()?.deviceName || 'MIB Panel' }}
      </h2>

      @if (schema(); as s) {
        @for (mod of s.modules; track mod.moduleName) {
          <app-module-section [module]="mod" />
        }
      }
    </app-side-panel>

    <app-set-feedback />
  `,
})
export class MinimalPanelComponent implements OnInit {
  private panelService = inject(MibPanelService);
  schema = this.panelService.schema;
  isPanelOpen = false;

  ngOnInit() {
    // Option 1: Load from a JSON object
    const demoSchema: MibPanelSchema = {
      deviceName: 'Demo Device',
      deviceIp: '192.168.1.1',
      devicePort: 161,
      community: 'public',
      snmpVersion: 'V2c',
      exportedAt: new Date().toISOString(),
      totalFields: 3,
      modules: [{
        moduleName: 'Configuration',
        scalarCount: 3,
        tableCount: 0,
        scalars: [
          {
            oid: '1.3.6.1.2.1.1.5.0', name: 'sysName',
            access: 'read-write', isWritable: true,
            inputType: 'text', baseType: 'OCTET STRING',
            currentValue: 'MyRouter', maxLength: 64
          },
          {
            oid: '1.3.6.1.4.1.99.1.0', name: 'outputEnabled',
            access: 'read-write', isWritable: true,
            inputType: 'toggle', baseType: 'Integer32',
            currentValue: '1',
            options: [
              { label: 'Disabled', value: 0 },
              { label: 'Enabled', value: 1 }
            ]
          },
          {
            oid: '1.3.6.1.4.1.99.2.0', name: 'outputPower',
            access: 'read-write', isWritable: true,
            inputType: 'number', baseType: 'Integer32',
            currentValue: '25', units: 'dBm',
            minValue: 0, maxValue: 50
          }
        ],
        tables: []
      }]
    };

    this.panelService.loadFromJson(demoSchema);

    // Option 2: Load from SignalR device
    // this.panelService.loadFromDevice('device-123');

    // Option 3: Load from JSON file
    // this.panelService.loadFromFile(jsonFile);
  }
}
```

### Testing the Example

1. Copy all files from Section 2 into your project
2. Add the `MinimalPanelComponent` to your routing or parent component
3. Run `ng serve`
4. Click "Open MIB Panel" — the side panel slides in showing:
   - **Monitoring**: sysName displayed as info card
   - **Configuration**: Toggle switch and number input with SET support
5. Edit a value and click Save — a toast notification appears (simulated mode)

---

## Component Dependency Graph

```
MinimalPanelComponent (or your host)
  ├── SidePanelComponent
  │     └── (projected content via ng-content)
  │
  ├── ModuleSectionComponent
  │     ├── StatusGridComponent         (monitoring cards)
  │     ├── ConfigGroupComponent        (editable fields)
  │     │     └── uses MibPanelService.sendSet()
  │     └── MibTableComponent           (data tables)
  │           └── uses MibPanelService.sendSet()
  │
  └── SetFeedbackComponent
        └── reads MibPanelService.feedbacks signal

Services (providedIn: 'root' — auto-injected):
  ├── MibPanelService        ← manages schema + SET commands
  ├── SignalRService          ← hub communication
  └── FieldClassifierService  ← field categorization
```

---

## Troubleshooting

| Issue | Solution |
|-------|---------|
| Module sections empty | Check that `scalars` array has `inputType` and `access` fields |
| SET button not showing | Ensure `isWritable: true` and `access: "read-write"` on the field |
| Toggle not rendering | Needs exactly 2 `options` with `value: 0` and `value: 1` |
| Enum dropdown empty | Check `options` array has `{ label: string, value: number }` items |
| SignalR not connecting | Verify server URL, check CORS settings, ensure hub name matches |
| Values not updating | Ensure `onTrafficReceived` broadcasts include the OID with `.0` suffix for scalars |
| Dark theme conflicts | Scope styles using a parent class: `.mib-panel-theme { ... }` |
