import { Component, Input, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MibFieldSchema } from '../../models/mib-schema';
import { MibPanelService } from '../../services/mib-panel.service';

@Component({
  selector: 'app-scalar-field',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './scalar-field.component.html',
  styleUrl: './scalar-field.component.scss',
})
export class ScalarFieldComponent {
  @Input({ required: true }) field!: MibFieldSchema;

  panelService = inject(MibPanelService);
  editValue = '';
  isEditing = false;

  get displayValue(): string {
    if (!this.field.currentValue) return '—';

    // Resolve enum label
    if (this.field.options?.length) {
      const num = parseInt(this.field.currentValue, 10);
      const opt = this.field.options.find(o => o.value === num);
      if (opt) return `${opt.label} (${num})`;
    }

    // TimeTicks → human readable
    if (this.field.inputType === 'timeticks') {
      const ticks = parseInt(this.field.currentValue, 10);
      if (!isNaN(ticks)) {
        const secs = Math.floor(ticks / 100);
        const d = Math.floor(secs / 86400);
        const h = Math.floor((secs % 86400) / 3600);
        const m = Math.floor((secs % 3600) / 60);
        const s = secs % 60;
        return d > 0 ? `${d}d ${h}h ${m}m ${s}s` : `${h}h ${m}m ${s}s`;
      }
    }

    const val = this.field.currentValue;
    if (this.field.units) return `${val} ${this.field.units}`;
    return val;
  }

  get typeIcon(): string {
    switch (this.field.inputType) {
      case 'counter': return '⟳';
      case 'gauge':   return '◔';
      case 'timeticks': return '⏱';
      case 'ip':      return '⌘';
      case 'enum':    return '☰';
      case 'toggle':  return '⊘';
      case 'number':  return '#';
      case 'text':    return 'T';
      case 'oid':     return '⎆';
      case 'bits':    return '⊞';
      default:        return '·';
    }
  }

  get accessBadge(): string {
    if (this.field.access.includes('create')) return 'RC';
    if (this.field.isWritable) return 'RW';
    return 'RO';
  }

  startEdit(): void {
    if (!this.field.isWritable) return;
    this.editValue = this.field.currentValue || this.field.defaultValue || '';
    this.isEditing = true;
  }

  cancelEdit(): void {
    this.isEditing = false;
  }

  sendSet(): void {
    const valueType = this.panelService.resolveValueType(this.field.inputType, this.field.baseType);
    this.panelService.sendSet(
      this.field.oid + '.0',
      this.field.name,
      this.editValue,
      valueType
    );
    this.field.currentValue = this.editValue;
    this.isEditing = false;
  }
}
