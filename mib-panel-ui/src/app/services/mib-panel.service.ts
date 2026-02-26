import { Injectable, signal } from '@angular/core';
import { MibPanelSchema, SetFeedback } from '../models/mib-schema';

@Injectable({ providedIn: 'root' })
export class MibPanelService {
  schema = signal<MibPanelSchema | null>(null);
  feedbacks = signal<SetFeedback[]>([]);
  isLoading = signal(false);

  private feedbackId = 0;

  async loadFromFile(file: File): Promise<void> {
    this.isLoading.set(true);
    try {
      const text = await file.text();
      const data = JSON.parse(text) as MibPanelSchema;
      this.schema.set(data);
    } finally {
      this.isLoading.set(false);
    }
  }

  loadFromJson(json: MibPanelSchema): void {
    this.schema.set(json);
  }

  /** Simulate an SNMP SET — in production this calls SignalR / HTTP API */
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

    // Simulate network delay then success
    setTimeout(() => {
      this.feedbacks.update(list =>
        list.map(f =>
          f.id === feedback.id
            ? { ...f, status: 'success' as const, message: 'SET acknowledged' }
            : f
        )
      );
    }, 400 + Math.random() * 600);

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
