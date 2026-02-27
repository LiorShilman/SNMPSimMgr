import { Injectable, signal, inject, effect, untracked } from '@angular/core';
import { MibPanelSchema, SetFeedback } from '../models/mib-schema';
import { SignalRService } from './signalr.service';

@Injectable({ providedIn: 'root' })
export class MibPanelService {
  private signalR = inject(SignalRService);

  schema = signal<MibPanelSchema | null>(null);
  feedbacks = signal<SetFeedback[]>([]);
  isLoading = signal(false);
  currentDeviceId = signal<string | null>(null);

  private feedbackId = 0;

  constructor() {
    // Auto-update panel values when traffic events arrive with values
    // Only track latestTraffic — read schema with untracked to avoid loop
    // (setting schema would retrigger this effect otherwise)
    effect(() => {
      const traffic = this.signalR.latestTraffic();
      if (!traffic || !traffic.value) return;

      console.log('[MibPanel] Traffic update:', traffic.operation, traffic.oid, '=', traffic.value);

      untracked(() => {
        const current = this.schema();
        if (!current) return;

        const oid = traffic.oid;
        let updated = false;
        for (const module of current.modules) {
          for (const field of module.scalars) {
            const fieldOid = field.oid.endsWith('.0') ? field.oid : field.oid + '.0';
            if (fieldOid === oid || field.oid === oid) {
              console.log('[MibPanel] Matched field:', field.name, fieldOid, '← new value:', traffic.value);
              field.currentValue = traffic.value;
              updated = true;
            }
          }
        }
        if (updated) {
          this.schema.set({ ...current });
          console.log('[MibPanel] Schema updated with new value');
        } else {
          console.log('[MibPanel] No matching field found for OID:', oid);
        }
      });
    });
  }

  async loadFromFile(file: File): Promise<void> {
    this.isLoading.set(true);
    this.currentDeviceId.set(null);
    try {
      const text = await file.text();
      const data = JSON.parse(text) as MibPanelSchema;
      this.schema.set(data);
    } finally {
      this.isLoading.set(false);
    }
  }

  loadFromJson(json: MibPanelSchema): void {
    this.currentDeviceId.set(null);
    this.schema.set(json);
  }

  /** Load schema from SignalR for a specific device */
  async loadFromDevice(deviceId: string): Promise<void> {
    console.log('[MibPanel] loadFromDevice called with:', deviceId);
    this.isLoading.set(true);
    this.currentDeviceId.set(deviceId);
    try {
      const schema = await this.signalR.requestSchema(deviceId);
      console.log('[MibPanel] requestSchema returned:', schema);
      if (schema) {
        this.schema.set(schema as MibPanelSchema);
      } else {
        console.warn('[MibPanel] Schema was null/undefined');
      }
    } catch (err) {
      console.error('[MibPanel] loadFromDevice error:', err);
    } finally {
      this.isLoading.set(false);
    }
  }

  /** Refresh scalar values from a live simulator via SignalR */
  async refreshValues(): Promise<void> {
    const deviceId = this.currentDeviceId();
    if (!deviceId || this.signalR.connectionState() !== 'connected') return;

    this.isLoading.set(true);
    try {
      const values = await this.signalR.requestRefresh(deviceId);
      const current = this.schema();
      if (current && values) {
        // Update scalar values in-place
        for (const module of current.modules) {
          for (const field of module.scalars) {
            const oid = field.oid.endsWith('.0') ? field.oid : field.oid + '.0';
            if (values[oid] !== undefined) {
              field.currentValue = values[oid];
            }
          }
        }
        // Trigger signal update (new reference)
        this.schema.set({ ...current });
      }
    } finally {
      this.isLoading.set(false);
    }
  }

  /** Send an SNMP SET — real via SignalR if connected, mock fallback otherwise */
  sendSet(oid: string, name: string, value: string, valueType: string): void {
    const feedback: SetFeedback = {
      id: ++this.feedbackId,
      oid,
      name,
      value,
      valueType,
      timestamp: new Date(),
      status: 'pending',
    };

    this.feedbacks.update(list => [feedback, ...list]);

    const deviceId = this.currentDeviceId();
    if (deviceId && this.signalR.connectionState() === 'connected') {
      // Real SignalR call
      this.signalR.sendSet(deviceId, oid, value, valueType)
        .then(result => {
          this.feedbacks.update(list =>
            list.map(f =>
              f.id === feedback.id
                ? { ...f, status: (result.success ? 'success' : 'error') as any, message: result.message }
                : f
            )
          );
        })
        .catch(err => {
          this.feedbacks.update(list =>
            list.map(f =>
              f.id === feedback.id
                ? { ...f, status: 'error' as const, message: err?.message || 'SET failed' }
                : f
            )
          );
        });
    } else {
      // Fallback: simulate (for file-loaded schemas or disconnected state)
      setTimeout(() => {
        this.feedbacks.update(list =>
          list.map(f =>
            f.id === feedback.id
              ? { ...f, status: 'success' as const, message: 'SET acknowledged (simulated)' }
              : f
          )
        );
      }, 400 + Math.random() * 600);
    }

    // Auto-remove after 8 seconds
    setTimeout(() => {
      this.feedbacks.update(list => list.filter(f => f.id !== feedback.id));
    }, 8000);
  }

  /** Map inputType to SNMP value type for SET */
  resolveValueType(inputType: string, baseType: string): string {
    switch (inputType) {
      case 'number':
      case 'enum':
      case 'toggle':
        return 'Integer32';
      case 'counter':
        return baseType.includes('64') ? 'Counter64' : 'Counter32';
      case 'gauge':
        return 'Gauge32';
      case 'timeticks':
        return 'TimeTicks';
      case 'ip':
        return 'IpAddress';
      case 'oid':
        return 'ObjectIdentifier';
      default:
        return 'OctetString';
    }
  }
}
