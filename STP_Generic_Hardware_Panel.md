# Software Test Procedure (STP)

## Generic Hardware Panel — SNMP & IDD Management System

| Field             | Value                                           |
|-------------------|------------------------------------------------|
| **Document ID**   | STP-GHP-001                                     |
| **Version**       | 1.0                                             |
| **Date**          | 2026-03-18                                      |
| **Author**        | Lior Shilman                                    |
| **Project**       | SNMPSimMgr — Generic Hardware & IDD Panel       |
| **Classification**| Internal                                        |
| **Status**        | Draft                                           |

---

## 1. Introduction

### 1.1 Purpose

This document defines the Software Test Procedure (STP) for the Generic Hardware Panel system. The system provides a unified web-based management interface for hardware devices that communicate via SNMP (v2c / v3) or IDD (Interface Design Document) protocols. It covers real-time monitoring, configuration (SET), bulk operations, automation, and connectivity resilience.

### 1.2 Scope

Testing covers the following subsystems:

| Subsystem                  | Technology            | Description                                      |
|----------------------------|-----------------------|-------------------------------------------------|
| Angular Client (UI)        | Angular 19, TypeScript | MIB panel, device cards, config editor, bulk SET |
| WPF Backend (Server)       | .NET Framework 4.7.2  | SignalR hub, SNMP engine, IDD dispatch, schema builder |
| SignalR Communication      | SignalR 2.x (jQuery)  | Bidirectional real-time messaging                |
| SNMP Engine                | SnmpSharpNet           | GET, SET, WALK (v2c + v3)                        |
| IDD Engine                 | Custom event dispatch  | Non-SNMP field management                        |

### 1.3 References

| Ref   | Document                                     |
|-------|----------------------------------------------|
| [R1]  | SNMPv2c / SNMPv3 RFCs (3411–3418)           |
| [R2]  | SignalR 2.x Documentation (ASP.NET)          |
| [R3]  | SnmpSharpNet Library API                     |
| [R4]  | Angular Signals & Standalone Components Docs |
| [R5]  | Project source: SNMPSimMgr-Integration       |

### 1.4 Abbreviations

| Abbr  | Meaning                                       |
|-------|-----------------------------------------------|
| STP   | Software Test Procedure                       |
| SNMP  | Simple Network Management Protocol            |
| MIB   | Management Information Base                   |
| OID   | Object Identifier                             |
| IDD   | Interface Design Document                     |
| SET   | SNMP write operation                          |
| GET   | SNMP read operation                           |
| WALK  | SNMP tree traversal (GETNEXT sequence)        |
| SUT   | System Under Test                             |
| DUT   | Device Under Test                             |
| UI    | User Interface (Angular client)               |

---

## 2. Test Environment

### 2.1 Hardware

| Item                          | Specification                          |
|-------------------------------|----------------------------------------|
| Test Workstation              | Windows 10 Pro, 8 GB RAM minimum       |
| SNMP Hardware Device (DUT)    | Any SNMPv2c/v3 capable device          |
| Network                       | LAN connection to DUT (port 161/UDP)   |

### 2.2 Software

| Component         | Version / Notes                             |
|-------------------|---------------------------------------------|
| WPF Application   | SNMPSimMgr (.NET Framework 4.7.2)           |
| Angular Client    | mib-panel-ui (Angular 19, Node.js >= 20.12) |
| Browser           | Chrome / Edge (latest)                      |
| SignalR Server    | Hosted by WPF on port 5050                  |

### 2.3 Prerequisites

1. WPF application running with at least one device profile configured.
2. Angular client served (default: `http://localhost:4200`).
3. Network connectivity between workstation and DUT.
4. At least one MIB file loaded for the SNMP device, **or** IDD field definitions configured.
5. Simulator available for offline testing (optional).

---

## 3. Test Cases

---

### 3.1 SignalR Connectivity

#### TC-3.1.1 — Initial Connection

| Field         | Value |
|---------------|-------|
| **Priority**  | Critical |
| **Precondition** | WPF application running with SignalR on port 5050. Angular client loaded in browser. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Open Angular client in browser | Connection status indicator shows "Connecting" |
| 2 | Wait for connection to establish | Status changes to "Connected" (green indicator) |
| 3 | Verify browser console | Log: `[SignalR] Connected to http://localhost:5050` |

---

#### TC-3.1.2 — Reconnection After Server Restart

| Field         | Value |
|---------------|-------|
| **Priority**  | Critical |
| **Precondition** | Angular client connected to WPF server. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Stop the WPF application | UI status changes to "Disconnected" |
| 2 | Observe reconnect attempts | Console shows exponential backoff: 3s, 6s, 12s, 24s, max 30s. Reconnect attempt counter increments. |
| 3 | Restart the WPF application | UI auto-reconnects. Status returns to "Connected". |
| 4 | If a device was loaded, verify values refresh automatically | Panel values update without manual action. Console: `[MibPanel] Connection restored — auto-refreshing values` |

---

#### TC-3.1.3 — Explicit Disconnect

| Field         | Value |
|---------------|-------|
| **Priority**  | Normal |
| **Precondition** | Angular client connected. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Trigger disconnect (if UI button exists, or programmatically) | Status changes to "Disconnected" |
| 2 | Wait 60 seconds | No reconnect attempts are made (stopped flag active) |

---

### 3.2 Device Discovery & Schema Loading

#### TC-3.2.1 — Load Device List

| Field         | Value |
|---------------|-------|
| **Priority**  | Critical |
| **Precondition** | Connected. At least one device profile exists in `Data/devices.json`. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Observe device card grid in sidebar | All configured devices appear as cards |
| 2 | Verify each card shows: name, IP, port | Correct values per device profile |
| 3 | If a simulator is running, verify indicator | Card shows "Simulating" badge with simulator port |

---

#### TC-3.2.2 — Load SNMP Device Schema

| Field         | Value |
|---------------|-------|
| **Priority**  | Critical |
| **Precondition** | Connected. SNMP device with MIB files configured. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Click on an SNMP device card | Loading spinner appears |
| 2 | Wait for schema to load | Panel displays modules, each with sections (Monitoring, Configuration) |
| 3 | Verify scalar fields have current values | Values populated from live device (via RequestRefresh) |
| 4 | Verify table data (if MIB defines tables) | Tables rendered with rows, columns, and cell values |
| 5 | Check module tabs at top | Tabs correspond to MIB module names |

---

#### TC-3.2.3 — Load IDD Device Schema

| Field         | Value |
|---------------|-------|
| **Priority**  | High |
| **Precondition** | Connected. Device profile has `IddFields` defined (IsIddDevice = true). |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Click on an IDD device card | Loading spinner appears |
| 2 | Wait for schema to load | Panel displays IDD groups as module sections |
| 3 | Verify fields show names, types, descriptions | Match IddFieldDef definitions |
| 4 | Verify writable fields have edit controls | Edit button/input visible for isWritable = true |

---

#### TC-3.2.4 — Schema Caching

| Field         | Value |
|---------------|-------|
| **Priority**  | Normal |
| **Precondition** | SNMP device loaded once. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Switch to a different device, then switch back | Schema loads significantly faster (cache hit) |
| 2 | Verify console: no MIB parsing log on second load | Hub uses cached schema |

---

#### TC-3.2.5 — Load Schema from Pre-Built JSON

| Field         | Value |
|---------------|-------|
| **Priority**  | Normal |
| **Precondition** | Device profile has `SchemaPath` set to a valid JSON file. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Click on device card | Schema loads from JSON file (no MIB parsing) |
| 2 | Verify all modules, fields, tables | Match the pre-built JSON content |

---

### 3.3 Real-Time Monitoring

#### TC-3.3.1 — Periodic WALK Updates

| Field         | Value |
|---------------|-------|
| **Priority**  | Critical |
| **Precondition** | SNMP device schema loaded. Periodic walk is running (auto-started). |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Observe panel values over 30 seconds | Values update automatically every ~10 seconds |
| 2 | Change a value on the real hardware (e.g., sysContact via CLI) | New value appears on panel within one walk cycle |
| 3 | Check console for MIB update logs | `[MibPanel] MIB batch update: N OIDs` |

---

#### TC-3.3.2 — Traffic Event Display

| Field         | Value |
|---------------|-------|
| **Priority**  | Normal |
| **Precondition** | Device loaded. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Perform a GET or SET on the device | Traffic event signal fires |
| 2 | Verify event contains: deviceName, operation, oid, value, sourceIp | All fields populated correctly |

---

#### TC-3.3.3 — OID Change Detection

| Field         | Value |
|---------------|-------|
| **Priority**  | High |
| **Precondition** | Device loaded. Periodic walk running. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Change a field value on the hardware | After next walk cycle, `onOidChanged` event fires |
| 2 | Verify event contains: oid, fieldName, newValue, previousValue | All values correct. fieldName resolved from schema name map. |

---

#### TC-3.3.4 — Selective Subtree WALK

| Field         | Value |
|---------------|-------|
| **Priority**  | Normal |
| **Precondition** | SNMP device loaded. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Invoke `RequestSelectiveWalk` with specific OID subtrees | Only the specified subtrees are walked |
| 2 | Verify returned values are within the subtree range | No OIDs outside the requested subtrees |
| 3 | Start selective periodic walk with subtree OIDs | Walk repeats at interval, covering only specified subtrees |

---

### 3.4 Field Display & Classification

#### TC-3.4.1 — Device Identity Header

| Field         | Value |
|---------------|-------|
| **Priority**  | Normal |
| **Precondition** | Device loaded with identity fields (sysName, model, firmware, etc.). |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Observe header card | Shows device name, model, firmware, serial, MAC, uptime |
| 2 | Verify uptime formatting | Displayed as "Xd Xh Xm Xs" (human-readable) |

---

#### TC-3.4.2 — Status Fields Color Coding

| Field         | Value |
|---------------|-------|
| **Priority**  | Normal |
| **Precondition** | Device loaded with status/enum fields. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Observe monitoring section fields | Enum values resolved to labels (e.g., 1 → "up") |
| 2 | Verify color coding | OK = green, Warning = amber, Alarm/Error = red, Off/Disabled = gray |

---

#### TC-3.4.3 — Counter & Gauge Display

| Field         | Value |
|---------------|-------|
| **Priority**  | Normal |
| **Precondition** | Device loaded with counter/gauge fields. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Observe counter fields | Values formatted with units (if defined) |
| 2 | After a walk cycle, verify counter increments | New value displayed correctly |

---

#### TC-3.4.4 — Table Display

| Field         | Value |
|---------------|-------|
| **Priority**  | High |
| **Precondition** | Device loaded with table data (e.g., ifTable). |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Observe table section | Columns match MIB column definitions |
| 2 | Verify row labels | Label column (e.g., ifDescr) used as row identifier |
| 3 | Verify cell values | Match live device values |

---

### 3.5 Monitors — Real-Time Status & Metrics

#### TC-3.5.1 — Monitor Card Grid Layout

| Field         | Value |
|---------------|-------|
| **Priority**  | High |
| **Precondition** | SNMP device loaded. Module has status/counter/gauge scalar fields. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Observe the MONITORING section within a module | Section header shows green accent, "MONITORING" title, and field count badge |
| 2 | Verify cards displayed in responsive grid (3–4 columns) | Cards adapt to panel width |
| 3 | Each card shows: field name, current value, unit (if defined) | All elements visible and correctly formatted |

---

#### TC-3.5.2 — Status Enum Color Coding

| Field         | Value |
|---------------|-------|
| **Priority**  | High |
| **Precondition** | Device with enum-based status fields (e.g., ifOperStatus: up/down). |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Observe card with status "up" / "active" / "ok" / "running" | Green left border + green pill badge |
| 2 | Observe card with status "down" / "error" / "critical" / "offline" | Red left border + red pill badge |
| 3 | Observe card with status "warning" / "degraded" / "standby" | Orange left border + orange pill badge |
| 4 | Observe card with status "disabled" / "off" | Gray left border + gray pill badge |

---

#### TC-3.5.3 — Status LED Indicator (Pulse Animation)

| Field         | Value |
|---------------|-------|
| **Priority**  | Normal |
| **Precondition** | Field with `inputType: status-led` present. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Observe LED dot on status-led field with "ok" status | Green LED with subtle glow shadow |
| 2 | Observe LED dot on field with "error" status | Red LED with pulsing animation |
| 3 | Observe LED dot on field with "warn" status | Orange LED with glow |

---

#### TC-3.5.4 — Counter & Gauge Formatting

| Field         | Value |
|---------------|-------|
| **Priority**  | Normal |
| **Precondition** | Device with counter/gauge fields with large values. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Observe counter field with value > 1000 | Formatted as "1.2K" |
| 2 | Observe counter field with value > 1,000,000 | Formatted as "5.6M" |
| 3 | Observe counter field with value > 1,000,000,000 | Formatted as "9.2G" |
| 4 | Verify counter icon (`⟳`) and blue card border | Visually distinct from status fields |
| 5 | Verify gauge icon (`◔`) | Gauge cards identifiable |

---

#### TC-3.5.5 — TimeTicks Uptime Display

| Field         | Value |
|---------------|-------|
| **Priority**  | Normal |
| **Precondition** | Device with sysUpTime or similar TimeTicks field. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Observe uptime field | Formatted as "Xd Xh Xm Xs" (e.g., "2d 5h 3m 12s") |
| 2 | Verify `⏱` icon and teal text styling | Uptime card visually distinct |
| 3 | After a walk cycle, verify uptime increments | Value increases between cycles |

---

#### TC-3.5.6 — Monitor Live Update (No User Interaction)

| Field         | Value |
|---------------|-------|
| **Priority**  | Critical |
| **Precondition** | Device loaded, periodic walk running. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Observe monitoring section for 30+ seconds without interaction | Values update automatically each walk cycle (~10s) |
| 2 | Change a counter on hardware (e.g., send traffic to increment ifInOctets) | Counter value increases on next walk cycle |
| 3 | Change a status field on hardware (e.g., disable an interface) | Status pill changes from "up" (green) → "down" (red) |
| 4 | Verify no flickering or layout shift during updates | Smooth value replacement |

---

#### TC-3.5.7 — Monitor Health Summary

| Field         | Value |
|---------------|-------|
| **Priority**  | Normal |
| **Precondition** | Module with multiple status fields in different states. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Observe section header summary | Shows "X OK / Y Warn / Z Alarm" counts |
| 2 | Change a field from "up" to "down" | OK count decreases, Alarm count increases |

---

### 3.6 Controls — Configuration & Editing

#### TC-3.6.1 — Configuration Section Display

| Field         | Value |
|---------------|-------|
| **Priority**  | High |
| **Precondition** | Device loaded. Module has writable scalar fields. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Observe CONFIGURATION section | Section header shows blue accent, "⚙ CONFIGURATION" title, field count |
| 2 | Each control row shows: field name (human-readable), current value, access badge (RW) | All elements visible |
| 3 | Verify non-writable fields in this section show RO badge | Access level correctly displayed |

---

#### TC-3.6.2 — Text Field Edit & Send

| Field         | Value |
|---------------|-------|
| **Priority**  | Critical |
| **Precondition** | Writable text field visible (e.g., sysContact). |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Click Edit (pencil icon) on the field | Text input appears with current value pre-filled |
| 2 | Type a new value | Input reflects new text |
| 3 | Click "Send" (blue button) | Feedback toast: pending → success. Field value updates. |
| 4 | Click Edit, then click "Cancel" (X) | Edit mode exits, no change sent |

---

#### TC-3.6.3 — Number Field with Min/Max

| Field         | Value |
|---------------|-------|
| **Priority**  | High |
| **Precondition** | Writable number field with min/max constraints defined. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Click Edit | Number input with spinner appears, range hint shown |
| 2 | Enter a value within range | Stage or Send succeeds |
| 3 | Enter a value outside range | Input shows validation hint (min/max displayed) |

---

#### TC-3.6.4 — Enum Dropdown Edit

| Field         | Value |
|---------------|-------|
| **Priority**  | High |
| **Precondition** | Writable enum field with options (e.g., adminStatus: up(1)/down(2)). |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Click Edit | Dropdown select appears with all options listed as "Label (value)" |
| 2 | Select a different option | Selected option highlighted |
| 3 | Click "Send" | Numeric value sent (e.g., "2" for "down"), not the label |
| 4 | Verify display after send | Shows resolved label (e.g., "down") not raw number |

---

#### TC-3.6.5 — Toggle Switch (Boolean Field)

| Field         | Value |
|---------------|-------|
| **Priority**  | High |
| **Precondition** | Toggle field visible (2-option enum, e.g., enabled/disabled). |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Observe toggle in ON state | Green/active toggle slider |
| 2 | Click toggle to switch OFF | Animated slide transition. SET sent with "off" value. |
| 3 | Click toggle to switch ON | Animated slide transition. SET sent with "on" value. |
| 4 | Verify feedback toast for each toggle | pending → success |

---

#### TC-3.6.6 — IP Address Field

| Field         | Value |
|---------------|-------|
| **Priority**  | Normal |
| **Precondition** | Writable IP address field visible. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Click Edit | Input appears with placeholder "0.0.0.0" |
| 2 | Enter valid IP (e.g., "192.168.1.100") | Value accepted |
| 3 | Click Send | SET sent with valueType "IpAddress" |

---

#### TC-3.6.7 — Friendly Name Display

| Field         | Value |
|---------------|-------|
| **Priority**  | Low |
| **Precondition** | Fields with camelCase names (e.g., "sysContact", "ifAdminStatus"). |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Observe field names in config section | camelCase converted to readable format (e.g., "Sys Contact", "If Admin Status") |

---

#### TC-3.6.8 — Control Field with Units

| Field         | Value |
|---------------|-------|
| **Priority**  | Low |
| **Precondition** | Field with units defined (e.g., "seconds", "dBm"). |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Observe field display | Unit shown alongside value (e.g., "300 seconds") |

---

### 3.7 Tables — MIB Table Display & Cell Editing

#### TC-3.7.1 — Table Rendering

| Field         | Value |
|---------------|-------|
| **Priority**  | Critical |
| **Precondition** | Device loaded with at least one MIB table (e.g., ifTable, hrStorageTable). |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Observe TABLES section | Section header shows purple accent, "▦ TABLES" title, table count |
| 2 | Verify table header: name, OID, row/column counts | All metadata displayed correctly |
| 3 | Verify column headers match MIB column definitions | Column names and order correct |
| 4 | Verify rows with correct index and label | Row label from labelColumn (e.g., ifDescr = "eth0") |
| 5 | Verify cell values populated | Match live device data |

---

#### TC-3.7.2 — Table Expand/Collapse

| Field         | Value |
|---------------|-------|
| **Priority**  | Normal |
| **Precondition** | Table section visible. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Click table header (▶ icon) | Table expands/collapses with smooth animation |
| 2 | Verify expand icon rotates | ▶ becomes ▼ when expanded |
| 3 | Collapsed state hides all rows | Only header visible |

---

#### TC-3.7.3 — Writable Column Identification

| Field         | Value |
|---------------|-------|
| **Priority**  | High |
| **Precondition** | Table with mix of read-only and writable columns. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Observe column headers | Writable columns have blue bottom border |
| 2 | Hover over writable cell | Light blue background highlight, edit button appears |
| 3 | Hover over read-only cell | No edit button, no highlight |

---

#### TC-3.7.4 — Table Cell Edit (Text/Number)

| Field         | Value |
|---------------|-------|
| **Priority**  | Critical |
| **Precondition** | Table with writable text/number column visible. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Double-click (or click edit button) on a writable cell | Inline input appears with current cell value |
| 2 | Enter a new value | Input reflects change |
| 3 | Click "Save" | SET sent with full OID = `columnOid.rowIndex` (e.g., `1.3.6.1.2.1.2.2.1.7.1`) |
| 4 | Verify cell updates | New value displayed in cell |
| 5 | Verify feedback toast | pending → success |

---

#### TC-3.7.5 — Table Cell Edit (Enum Dropdown)

| Field         | Value |
|---------------|-------|
| **Priority**  | High |
| **Precondition** | Table with writable enum column (e.g., ifAdminStatus). |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Double-click on enum cell | Dropdown select appears with options as "Label (value)" |
| 2 | Select a different option | Option selected |
| 3 | Click "Save" | SET sent with numeric value |
| 4 | Verify cell shows resolved label | e.g., "down" not "2" |

---

#### TC-3.7.6 — Table Cell Edit Cancel

| Field         | Value |
|---------------|-------|
| **Priority**  | Normal |
| **Precondition** | Cell in edit mode. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Enter edit mode on a cell | Input visible |
| 2 | Click Cancel (✕) | Edit mode exits, original value preserved, no SET sent |

---

#### TC-3.7.7 — Status LED in Table Cells

| Field         | Value |
|---------------|-------|
| **Priority**  | Normal |
| **Precondition** | Table with status-led column (e.g., operational status). |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Observe status-led cells | Colored LED pill with dot and label |
| 2 | Verify "up" rows show green LED | Green dot with glow |
| 3 | Verify "down" rows show red LED with pulse | Red dot with animation |
| 4 | Verify these cells are NOT editable | No edit button on hover, no double-click edit |

---

#### TC-3.7.8 — Table Live Updates via WALK

| Field         | Value |
|---------------|-------|
| **Priority**  | High |
| **Precondition** | Table loaded, periodic walk running. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Observe table cell values over 30 seconds | Cell values update each walk cycle |
| 2 | Change a table value on hardware | Updated on next walk. If status-led, color changes accordingly. |
| 3 | Verify no layout shift during update | Rows and columns remain stable |

---

#### TC-3.7.9 — Multiple Tables in Module

| Field         | Value |
|---------------|-------|
| **Priority**  | Normal |
| **Precondition** | Module with 2+ tables. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Observe TABLES section | All tables listed vertically |
| 2 | Expand/collapse each independently | Each table has its own expand state |
| 3 | Edit cells in different tables | Each operates independently |

---

### 3.8 Single Field SET (SNMP)

#### TC-3.8.1 — SET Writable Scalar (Immediate)

| Field         | Value |
|---------------|-------|
| **Priority**  | Critical |
| **Precondition** | SNMP device loaded. Writable field visible in Configuration section. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Click edit button on a writable field | Input field appears with current value |
| 2 | Enter a new value | Value shown in input |
| 3 | Click "Send" (blue button) | Feedback toast: "pending" → "success" |
| 4 | Verify field updates in panel | `currentValue` reflects new value |
| 5 | Verify on hardware (CLI or other tool) | Value changed on real device |
| 6 | Feedback toast auto-removes after 8 seconds | Toast disappears |

---

#### TC-3.8.2 — SET with Invalid Value

| Field         | Value |
|---------------|-------|
| **Priority**  | High |
| **Precondition** | Writable field visible. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Edit a field and enter an invalid value (e.g., string in integer field) | Send the value |
| 2 | Observe feedback | Feedback toast: "pending" → "error" with descriptive message |
| 3 | Verify field value unchanged | `currentValue` retains original value |

---

#### TC-3.8.3 — SET When Disconnected

| Field         | Value |
|---------------|-------|
| **Priority**  | High |
| **Precondition** | Disconnect from server. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Attempt to send a SET | Feedback toast: "error" — "Not connected to server" |
| 2 | SET controls may be disabled | `isConnected` computed = false |

---

### 3.9 Single Field SET (IDD)

#### TC-3.9.1 — IDD Field SET

| Field         | Value |
|---------------|-------|
| **Priority**  | High |
| **Precondition** | IDD device loaded. Writable IDD field visible. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Edit an IDD field and click "Send" | Feedback toast: "pending" → "success" ("IDD SET dispatched") |
| 2 | Verify WPF received the event | Debug output: `[SnmpHub] SendIddSet: device=..., field=..., value=...` |
| 3 | Verify `Simulator.RaiseIddSet` was called | WPF event handler processes the IDD write |

---

#### TC-3.9.2 — IDD vs SNMP Routing

| Field         | Value |
|---------------|-------|
| **Priority**  | High |
| **Precondition** | Device with mixed SNMP + IDD fields (if applicable). |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Send SET on a numeric OID field (e.g., `1.3.6.1.2.1.1.4.0`) | Routed via `sendSet` → SNMP SET on hardware |
| 2 | Send SET on a named field (e.g., `alarm-indicator`) | Routed via `sendIddSet` → `RaiseIddSet` event |

---

### 3.10 Dirty Tracking & Staged Edits

#### TC-3.10.1 — Stage Edit (No Auto-Send)

| Field         | Value |
|---------------|-------|
| **Priority**  | High |
| **Precondition** | Device loaded. Configuration section visible with writable fields. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Click edit on a writable field | Input appears |
| 2 | Enter a new value and click "Stage" (orange button) | Field row shows dirty state (orange highlight) |
| 3 | Verify pending value shown | "Pending: [new value]" + "was: [original value]" text visible |
| 4 | Verify no SET sent to server | No feedback toast, no traffic event |
| 5 | Verify dirty count badge on bulk bar | Count increments (e.g., "2 changes") |

---

#### TC-3.10.2 — Revert Single Field

| Field         | Value |
|---------------|-------|
| **Priority**  | Normal |
| **Precondition** | At least one field staged (dirty). |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Click "Revert" button on a dirty field | Field returns to original value, dirty indicator removed |
| 2 | Verify dirty count decreases | Bulk bar count updates |

---

#### TC-3.7.3 — Revert All

| Field         | Value |
|---------------|-------|
| **Priority**  | Normal |
| **Precondition** | Multiple fields staged. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Click "Revert All" in bulk action bar | All dirty indicators cleared |
| 2 | Verify dirty count = 0 | Bulk bar hidden |

---

### 3.8 Bulk SET Operations

#### TC-3.8.1 — Send All Staged Changes (SNMP)

| Field         | Value |
|---------------|-------|
| **Priority**  | Critical |
| **Precondition** | Multiple SNMP fields staged (dirty). |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Stage 3+ writable SNMP fields with new values | Dirty count = 3+ |
| 2 | Click "Send All" in bulk action bar | Feedback toast: "pending" for bulk(N) |
| 3 | Wait for completion | Feedback: "N/N succeeded" (success) or "X/N succeeded" (partial) |
| 4 | Verify all fields updated in panel | `currentValue` reflects new values for successful items |
| 5 | Verify on hardware | All values changed |
| 6 | Verify dirty map cleared | No more dirty indicators |

---

#### TC-3.8.2 — Send All Staged Changes (IDD)

| Field         | Value |
|---------------|-------|
| **Priority**  | High |
| **Precondition** | Multiple IDD fields staged (dirty). |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Stage 3+ writable IDD fields with new values | Dirty count = 3+ |
| 2 | Click "Send All" | Feedback toast: "pending" for bulk(N) |
| 3 | Wait for completion | Feedback: "N/N succeeded" |
| 4 | Verify WPF debug output | `[SnmpHub] SendIddBulkSet` logged for each item |

---

#### TC-3.8.3 — Send All Mixed (SNMP + IDD)

| Field         | Value |
|---------------|-------|
| **Priority**  | High |
| **Precondition** | Device with both SNMP and IDD writable fields. Multiple fields staged. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Stage SNMP + IDD fields | Both types in dirty map |
| 2 | Click "Send All" | Items split automatically: numeric OID → `SendBulkSet`, named → `SendIddBulkSet` |
| 3 | Wait for completion | Aggregated result: total succeeded/failed across both types |
| 4 | Verify both SNMP and IDD fields updated | Panel shows new values |

---

#### TC-3.8.4 — Bulk SET via CSV Import

| Field         | Value |
|---------------|-------|
| **Priority**  | High |
| **Precondition** | Bulk SET component visible. SNMP device loaded. |

**Prepare CSV file:**
```csv
# OID,Value,Type
1.3.6.1.2.1.1.4.0,admin@test.com,OctetString
1.3.6.1.2.1.1.6.0,Lab Room 5,OctetString
```

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Click file upload in Bulk SET component | File picker opens |
| 2 | Select CSV file | Preview table shows parsed items (2 rows) |
| 3 | Verify parsed OIDs, values, types | Match CSV content; comments/headers skipped |
| 4 | Click "Send" | Progress bar advances. Feedback toast: pending → result |
| 5 | Verify results table | Per-OID success/failure status |

---

#### TC-3.8.5 — Bulk SET via JSON Import

| Field         | Value |
|---------------|-------|
| **Priority**  | Normal |
| **Precondition** | Bulk SET component visible. |

**Prepare JSON file:**
```json
[
  { "oid": "1.3.6.1.2.1.1.4.0", "value": "admin@test.com", "valueType": "OctetString" },
  { "oid": "1.3.6.1.2.1.1.6.0", "value": "Lab Room 5", "valueType": "OctetString" }
]
```

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Upload JSON file | Preview table shows 2 items |
| 2 | Click "Send" | Bulk SET executed |
| 3 | Verify results | Per-OID success/failure |

---

### 3.9 SNMP Protocol Support

#### TC-3.9.1 — SNMPv2c GET/SET/WALK

| Field         | Value |
|---------------|-------|
| **Priority**  | Critical |
| **Precondition** | Device configured with SNMPv2c (community string). |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Load device schema | Schema loaded via WALK from hardware |
| 2 | Verify live values (GET) | All scalar/table values populated |
| 3 | SET a writable field | Value changed on device |
| 4 | Periodic WALK updates panel | Values refresh every cycle |

---

#### TC-3.9.2 — SNMPv3 Authentication & Privacy

| Field         | Value |
|---------------|-------|
| **Priority**  | High |
| **Precondition** | Device configured with SNMPv3 credentials (auth: MD5 or SHA, priv: DES or AES). |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Load device schema | V3 discovery + auth/priv negotiation succeeds |
| 2 | Verify live values | Encrypted SNMP communication works |
| 3 | SET a writable field | V3 SET succeeds |

---

#### TC-3.9.3 — WALK Skip-on-Timeout

| Field         | Value |
|---------------|-------|
| **Priority**  | High |
| **Precondition** | SNMP device with OID subtrees that may timeout (e.g., large MIBs). |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Start a full WALK on device | Walk begins at 1.3.6.1 |
| 2 | If a subtree times out | Log: `Timeout at [OID], skipping to [nextOid]` |
| 3 | Walk continues to next branch | `SkipToNextBranch` computes next sibling OID |
| 4 | Walk completes | All reachable OIDs recorded, unresponsive branches skipped |
| 5 | Verify max consecutive skips (10) | After 10 consecutive skips, walk ends gracefully |

---

#### TC-3.9.4 — Simulator Routing

| Field         | Value |
|---------------|-------|
| **Priority**  | High |
| **Precondition** | A simulator is running for the selected device. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Verify device card shows "Simulating" | Simulator endpoint registered |
| 2 | Perform GET on device | Request routed to simulator (localhost:simPort) |
| 3 | Perform SET on device | SET routed to simulator |
| 4 | Stop simulator | Requests route back to real hardware |

---

### 3.10 Name-Based Watches & Automation

#### TC-3.10.1 — WatchByName Callback

| Field         | Value |
|---------------|-------|
| **Priority**  | High |
| **Precondition** | Device loaded. Watch registered for a field name (e.g., "temperature"). |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Register watch: `panelService.watchByName('temperature', callback)` | Watch registered (case-insensitive) |
| 2 | Change the "temperature" field value (via SET or periodic walk) | Callback fires with OidChangedEvent |
| 3 | Verify event contains: newValue, previousValue, fieldName | All values correct |

---

#### TC-3.10.2 — Cross-Protocol Automation (SNMP → IDD)

| Field         | Value |
|---------------|-------|
| **Priority**  | High |
| **Precondition** | Device with SNMP "temperature" field and IDD "alarm-indicator" field. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Register automation: if temperature > 80°C, SET alarm-indicator = "ON" | Watch callback registered |
| 2 | Set temperature to 85 on hardware | Periodic walk detects change |
| 3 | Verify alarm-indicator SET triggered | `SendIddSet` called with "alarm-indicator", "ON" |

---

#### TC-3.10.3 — Unsubscribe Watch

| Field         | Value |
|---------------|-------|
| **Priority**  | Normal |
| **Precondition** | Watch registered. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Call unsubscribe function returned by `watchByName` | Watch removed |
| 2 | Change the watched field | Callback does NOT fire |

---

### 3.11 UI Interaction & Layout

#### TC-3.11.1 — Module Tab Navigation

| Field         | Value |
|---------------|-------|
| **Priority**  | Normal |
| **Precondition** | Device loaded with multiple modules. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Click a module tab | Panel scrolls to that module section |
| 2 | Verify all tabs correspond to loaded modules | Tab names match MIB module names |

---

#### TC-3.11.2 — Section Collapse/Expand

| Field         | Value |
|---------------|-------|
| **Priority**  | Low |
| **Precondition** | Module section with Monitoring + Configuration sub-sections. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Click section header to collapse | Section content hidden, count badge visible |
| 2 | Click again to expand | Content restored |

---

#### TC-3.11.3 — Health Summary

| Field         | Value |
|---------------|-------|
| **Priority**  | Normal |
| **Precondition** | Device loaded with enum/status fields. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Observe health summary | Shows counts: OK, Warning, Alarm, Info |
| 2 | Change a status field to alarm state | Alarm count increments |

---

#### TC-3.11.4 — Device Switch

| Field         | Value |
|---------------|-------|
| **Priority**  | High |
| **Precondition** | Two or more devices configured. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Load Device A | Schema A displayed |
| 2 | Click Device B card | Loading spinner → Schema B displayed |
| 3 | Verify Device A fields no longer shown | Full panel reset to Device B |
| 4 | Switch back to Device A | Schema loads from cache (fast) |

---

### 3.12 Error Handling & Edge Cases

#### TC-3.12.1 — Device Unreachable

| Field         | Value |
|---------------|-------|
| **Priority**  | High |
| **Precondition** | Device configured but physically unreachable (powered off or disconnected). |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Load device schema | Schema may load from cache/MIB, but values will be empty or stale |
| 2 | Attempt SET | Feedback: "error" — timeout or connection refused |
| 3 | Periodic walk logs timeouts | Walk handles timeouts via SkipToNextBranch, eventually ends |

---

#### TC-3.12.2 — Invalid MIB Files

| Field         | Value |
|---------------|-------|
| **Priority**  | Normal |
| **Precondition** | Device with corrupted or missing MIB files. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Load device | Error logged in console/debug output |
| 2 | Verify no crash | Application remains stable |

---

#### TC-3.12.3 — Concurrent Bulk SET

| Field         | Value |
|---------------|-------|
| **Priority**  | Normal |
| **Precondition** | Device loaded with many writable fields. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Stage many fields (10+) and click Send All | All items sent in single bulk call |
| 2 | Simultaneously trigger a periodic walk | Both operations complete without conflict |
| 3 | Verify final field values are consistent | Last write wins; values reflect either SET or walk result |

---

#### TC-3.12.4 — Send All with Zero Dirty Fields

| Field         | Value |
|---------------|-------|
| **Priority**  | Low |
| **Precondition** | No fields staged. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Verify "Send All" button not visible / disabled | Bulk bar hidden when dirtyCount = 0 |

---

### 3.13 Enum & Toggle Fields

#### TC-3.13.1 — Enum Field Edit

| Field         | Value |
|---------------|-------|
| **Priority**  | Normal |
| **Precondition** | Writable enum field visible (e.g., admin status with up(1)/down(2)). |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Click edit on enum field | Dropdown with enum options appears |
| 2 | Select a different option | Value staged/sent correctly (numeric value, not label) |
| 3 | Verify display shows label | e.g., "up" not "1" |

---

#### TC-3.13.2 — Toggle Field

| Field         | Value |
|---------------|-------|
| **Priority**  | Normal |
| **Precondition** | Toggle field visible (boolean/binary). |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Click toggle ON button | SET sent with "1" (or appropriate value) |
| 2 | Click toggle OFF button | SET sent with "0" |
| 3 | Verify visual state matches | Toggle reflects current value |

---

### 3.14 OID Watch Service (Server-Side)

#### TC-3.14.1 — Exact OID Watch

| Field         | Value |
|---------------|-------|
| **Priority**  | Normal |
| **Precondition** | OidWatchService initialized with schema. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Register watch for specific OID | Watch registered in server-side service |
| 2 | Trigger OID change (via walk or SET) | Callback fires with (oid, newValue, previousValue) |

---

#### TC-3.14.2 — Name-to-OID Resolution

| Field         | Value |
|---------------|-------|
| **Priority**  | Normal |
| **Precondition** | Schema loaded, name map built. |

**Steps:**

| # | Action | Expected Result |
|---|--------|-----------------|
| 1 | Call `ResolveNameToOid("sysName")` | Returns correct OID (e.g., `1.3.6.1.2.1.1.5`) |
| 2 | Call `ResolveOidToName("1.3.6.1.2.1.1.5")` | Returns "sysName" |
| 3 | Verify case-insensitive | `ResolveNameToOid("SYSNAME")` returns same result |

---

## 4. Test Summary Matrix

| Test ID    | Test Name                          | Priority  | Type        |
|------------|------------------------------------|-----------|-------------|
| TC-3.1.1   | Initial Connection                 | Critical  | Connectivity |
| TC-3.1.2   | Reconnection After Server Restart  | Critical  | Connectivity |
| TC-3.1.3   | Explicit Disconnect                | Normal    | Connectivity |
| TC-3.2.1   | Load Device List                   | Critical  | Schema      |
| TC-3.2.2   | Load SNMP Device Schema            | Critical  | Schema      |
| TC-3.2.3   | Load IDD Device Schema             | High      | Schema      |
| TC-3.2.4   | Schema Caching                     | Normal    | Schema      |
| TC-3.2.5   | Load Schema from Pre-Built JSON    | Normal    | Schema      |
| TC-3.3.1   | Periodic WALK Updates              | Critical  | Monitoring  |
| TC-3.3.2   | Traffic Event Display              | Normal    | Monitoring  |
| TC-3.3.3   | OID Change Detection               | High      | Monitoring  |
| TC-3.3.4   | Selective Subtree WALK             | Normal    | Monitoring  |
| TC-3.4.1   | Device Identity Header             | Normal    | Display     |
| TC-3.4.2   | Status Fields Color Coding         | Normal    | Display     |
| TC-3.4.3   | Counter & Gauge Display            | Normal    | Display     |
| TC-3.4.4   | Table Display                      | High      | Display     |
| TC-3.5.1   | SET Writable Scalar (Immediate)    | Critical  | SNMP SET    |
| TC-3.5.2   | SET with Invalid Value             | High      | SNMP SET    |
| TC-3.5.3   | SET When Disconnected              | High      | SNMP SET    |
| TC-3.6.1   | IDD Field SET                      | High      | IDD SET     |
| TC-3.6.2   | IDD vs SNMP Routing                | High      | IDD SET     |
| TC-3.7.1   | Stage Edit (No Auto-Send)          | High      | Dirty Track |
| TC-3.7.2   | Revert Single Field                | Normal    | Dirty Track |
| TC-3.7.3   | Revert All                         | Normal    | Dirty Track |
| TC-3.8.1   | Send All Staged (SNMP)             | Critical  | Bulk SET    |
| TC-3.8.2   | Send All Staged (IDD)              | High      | Bulk SET    |
| TC-3.8.3   | Send All Mixed (SNMP + IDD)        | High      | Bulk SET    |
| TC-3.8.4   | Bulk SET via CSV Import            | High      | Bulk SET    |
| TC-3.8.5   | Bulk SET via JSON Import           | Normal    | Bulk SET    |
| TC-3.9.1   | SNMPv2c GET/SET/WALK               | Critical  | Protocol    |
| TC-3.9.2   | SNMPv3 Auth & Privacy              | High      | Protocol    |
| TC-3.9.3   | WALK Skip-on-Timeout               | High      | Protocol    |
| TC-3.9.4   | Simulator Routing                  | High      | Protocol    |
| TC-3.10.1  | WatchByName Callback               | High      | Automation  |
| TC-3.10.2  | Cross-Protocol Automation          | High      | Automation  |
| TC-3.10.3  | Unsubscribe Watch                  | Normal    | Automation  |
| TC-3.11.1  | Module Tab Navigation              | Normal    | UI          |
| TC-3.11.2  | Section Collapse/Expand            | Low       | UI          |
| TC-3.11.3  | Health Summary                     | Normal    | UI          |
| TC-3.11.4  | Device Switch                      | High      | UI          |
| TC-3.12.1  | Device Unreachable                 | High      | Error       |
| TC-3.12.2  | Invalid MIB Files                  | Normal    | Error       |
| TC-3.12.3  | Concurrent Bulk SET                | Normal    | Error       |
| TC-3.12.4  | Send All with Zero Dirty           | Low       | Error       |
| TC-3.13.1  | Enum Field Edit                    | Normal    | Field Types |
| TC-3.13.2  | Toggle Field                       | Normal    | Field Types |
| TC-3.14.1  | Exact OID Watch                    | Normal    | OID Watch   |
| TC-3.14.2  | Name-to-OID Resolution             | Normal    | OID Watch   |

**Total: 42 test cases**
- Critical: 7
- High: 18
- Normal: 14
- Low: 3

---

## 5. Pass/Fail Criteria

- **Pass**: All Critical and High priority tests pass. No more than 2 Normal tests may fail with documented justification.
- **Conditional Pass**: All Critical tests pass. Up to 3 High tests may fail with documented workarounds.
- **Fail**: Any Critical test fails, or more than 3 High tests fail.

---

## 6. Test Execution Log

| Test ID | Date | Tester | Result | Notes |
|---------|------|--------|--------|-------|
|         |      |        |        |       |

---

*End of Document*
