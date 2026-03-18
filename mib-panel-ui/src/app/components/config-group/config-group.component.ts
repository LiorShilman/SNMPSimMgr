import { Component, Input, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MibFieldSchema } from '../../models/mib-schema';
import { MibPanelService } from '../../services/mib-panel.service';
import { BulkSetItem } from '../../services/signalr.service';

@Component({
  selector: 'app-config-group',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './config-group.component.html',
  styleUrl: './config-group.component.scss',
})
export class ConfigGroupComponent {
  @Input({ required: true }) fields: MibFieldSchema[] = [];

  panelService = inject(MibPanelService);

  // Track which field is being edited
  editingOid: string | null = null;
  editValue = '';

  // Dirty tracking: OID → pending value
  dirtyValues = new Map<string, string>();

  get dirtyCount(): number {
    return this.dirtyValues.size;
  }

  isDirty(field: MibFieldSchema): boolean {
    return this.dirtyValues.has(field.oid);
  }

  getDirtyValue(field: MibFieldSchema): string {
    return this.dirtyValues.get(field.oid) ?? '';
  }

  startEdit(field: MibFieldSchema): void {
    this.editingOid = field.oid;
    // If already dirty, resume editing the pending value
    this.editValue = this.dirtyValues.get(field.oid)
      ?? field.currentValue
      ?? field.defaultValue
      ?? '';
  }

  cancelEdit(): void {
    this.editingOid = null;
  }

  /** Stage change locally (no send). */
  stageEdit(field: MibFieldSchema): void {
    const original = field.currentValue ?? field.defaultValue ?? '';
    if (this.editValue !== original) {
      this.dirtyValues.set(field.oid, this.editValue);
    } else {
      this.dirtyValues.delete(field.oid); // reverted to original
    }
    this.editingOid = null;
  }

  /** Send single field immediately (existing behavior for quick edits). */
  sendSet(field: MibFieldSchema): void {
    const valueType = this.panelService.resolveValueType(field.inputType, field.baseType);
    const setOid = /^\d/.test(field.oid) ? field.oid + '.0' : field.oid;
    this.panelService.sendSet(setOid, field.name, this.editValue, valueType);
    field.currentValue = this.editValue;
    this.dirtyValues.delete(field.oid);
    this.editingOid = null;
  }

  /** Revert a single dirty field. */
  revertField(field: MibFieldSchema): void {
    this.dirtyValues.delete(field.oid);
  }

  /** Revert all dirty fields. */
  revertAll(): void {
    this.dirtyValues.clear();
  }

  /** Send all dirty fields as a single bulk SET via panelService. */
  sendAll(): void {
    if (this.dirtyCount === 0) return;

    const items: BulkSetItem[] = [];
    for (const [oid, value] of this.dirtyValues) {
      const field = this.fields.find(f => f.oid === oid);
      if (!field) continue;
      const valueType = this.panelService.resolveValueType(field.inputType, field.baseType);
      const setOid = /^\d/.test(oid) ? oid + '.0' : oid;
      items.push({ oid: setOid, value, valueType });
      field.currentValue = value;
    }

    this.panelService.sendBulkSet(items);
    this.dirtyValues.clear();
  }

  resolveEnum(field: MibFieldSchema): string | null {
    if (!field.options?.length || !field.currentValue) return null;
    const num = parseInt(field.currentValue, 10);
    return field.options.find(o => o.value === num)?.label ?? null;
  }

  friendlyName(name: string): string {
    return name
      .replace(/^sd|^sys/i, '')
      .replace(/([a-z])([A-Z])/g, '$1 $2')
      .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2');
  }

  // Toggle helpers
  getToggleOnValue(field: MibFieldSchema): string {
    if (field.options?.length === 2) {
      const sorted = [...field.options].sort((a, b) => a.value - b.value);
      return String(sorted[1].value);
    }
    return '1';
  }
  getToggleOffValue(field: MibFieldSchema): string {
    if (field.options?.length === 2) {
      const sorted = [...field.options].sort((a, b) => a.value - b.value);
      return String(sorted[0].value);
    }
    return '0';
  }
  getToggleLabel(field: MibFieldSchema, isOn: boolean): string {
    if (field.options?.length === 2) {
      const sorted = [...field.options].sort((a, b) => a.value - b.value);
      return isOn ? sorted[1].label : sorted[0].label;
    }
    return isOn ? 'ON' : 'OFF';
  }

  // Status LED color class
  getStatusClass(field: MibFieldSchema): string {
    if (!field.options?.length || !field.currentValue) return 'off';
    const num = parseInt(field.currentValue, 10);
    const label = (field.options.find(o => o.value === num)?.label || '').toLowerCase();
    if (['ok', 'normal', 'on', 'up', 'active', 'enabled'].includes(label)) return 'ok';
    if (['low', 'high', 'warning'].includes(label)) return 'warn';
    if (['fail', 'fault', 'alarm', 'error', 'critical'].includes(label) || label.includes('failed')) return 'error';
    return 'off';
  }

  displayValue(field: MibFieldSchema): string {
    if (!field.currentValue) return field.defaultValue || '—';

    const enumLabel = this.resolveEnum(field);
    if (enumLabel) return enumLabel;

    if (field.inputType === 'toggle') {
      return field.currentValue === this.getToggleOnValue(field)
        ? this.getToggleLabel(field, true) : this.getToggleLabel(field, false);
    }

    return field.currentValue;
  }
}
