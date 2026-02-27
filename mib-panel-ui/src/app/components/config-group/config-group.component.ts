import { Component, Input, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MibFieldSchema } from '../../models/mib-schema';
import { MibPanelService } from '../../services/mib-panel.service';

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

  startEdit(field: MibFieldSchema): void {
    this.editingOid = field.oid;
    this.editValue = field.currentValue || field.defaultValue || '';
  }

  cancelEdit(): void {
    this.editingOid = null;
  }

  sendSet(field: MibFieldSchema): void {
    const valueType = this.panelService.resolveValueType(field.inputType, field.baseType);
    // IDD fields (non-numeric OID) don't need .0 suffix; SNMP scalars do
    const setOid = /^\d/.test(field.oid) ? field.oid + '.0' : field.oid;
    this.panelService.sendSet(setOid, field.name, this.editValue, valueType);
    field.currentValue = this.editValue;
    this.editingOid = null;
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

  displayValue(field: MibFieldSchema): string {
    if (!field.currentValue) return field.defaultValue || '—';

    const enumLabel = this.resolveEnum(field);
    if (enumLabel) return enumLabel;

    if (field.inputType === 'toggle') {
      return field.currentValue === '1' ? 'ON' : 'OFF';
    }

    return field.currentValue;
  }
}
