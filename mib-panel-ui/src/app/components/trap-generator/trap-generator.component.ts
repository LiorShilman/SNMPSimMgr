import { Component, inject, signal, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SignalRService, TrapBinding } from '../../services/signalr.service';

interface TrapPreset {
  label: string;
  oid: string;
}

const PRESETS: TrapPreset[] = [
  { label: 'coldStart', oid: '1.3.6.1.6.3.1.1.5.1' },
  { label: 'warmStart', oid: '1.3.6.1.6.3.1.1.5.2' },
  { label: 'linkDown', oid: '1.3.6.1.6.3.1.1.5.3' },
  { label: 'linkUp', oid: '1.3.6.1.6.3.1.1.5.4' },
  { label: 'authenticationFailure', oid: '1.3.6.1.6.3.1.1.5.5' },
];

@Component({
  selector: 'app-trap-generator',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="trapgen-backdrop" (click)="close.emit()"></div>
    <div class="trapgen-dialog">
      <div class="dialog-header">
        <span class="dialog-icon">&#9889;</span>
        <span class="dialog-title">Send Trap</span>
        <button class="btn-close" (click)="close.emit()">&times;</button>
      </div>

      <div class="dialog-body">
        <!-- Presets -->
        <div class="preset-row">
          <span class="preset-label">Presets:</span>
          @for (p of presets; track p.oid) {
            <button class="btn-preset" [class.active]="trapOid === p.oid" (click)="trapOid = p.oid">
              {{ p.label }}
            </button>
          }
        </div>

        <!-- Main fields -->
        <div class="form-grid">
          <label class="form-label">Trap OID</label>
          <input class="form-input" [(ngModel)]="trapOid" placeholder="1.3.6.1.6.3.1.1.5.3" />

          <label class="form-label">Target IP</label>
          <input class="form-input" [(ngModel)]="targetIp" placeholder="127.0.0.1" />

          <label class="form-label">Target Port</label>
          <input class="form-input" type="number" [(ngModel)]="targetPort" />
        </div>

        <!-- Variable Bindings -->
        <div class="bindings-section">
          <div class="bindings-header">
            <span class="bindings-title">Variable Bindings</span>
            <button class="btn-add-binding" (click)="addBinding()">+ Add</button>
          </div>

          @if (bindings.length > 0) {
            <div class="bindings-table">
              <div class="binding-header-row">
                <span>OID</span>
                <span>Value</span>
                <span>Type</span>
                <span></span>
              </div>
              @for (b of bindings; track $index; let i = $index) {
                <div class="binding-row">
                  <input class="binding-input" [(ngModel)]="b.oid" placeholder="1.3.6.1.2.1.1.3.0" />
                  <input class="binding-input" [(ngModel)]="b.value" placeholder="value" />
                  <select class="binding-select" [(ngModel)]="b.valueType">
                    <option value="OctetString">OctetString</option>
                    <option value="Integer32">Integer32</option>
                    <option value="Counter32">Counter32</option>
                    <option value="Gauge32">Gauge32</option>
                    <option value="TimeTicks">TimeTicks</option>
                    <option value="ObjectIdentifier">OID</option>
                    <option value="IpAddress">IpAddress</option>
                  </select>
                  <button class="btn-remove" (click)="removeBinding(i)">&times;</button>
                </div>
              }
            </div>
          } @else {
            <div class="no-bindings">No variable bindings — click Add to include data</div>
          }
        </div>

        <!-- Feedback -->
        @if (feedback()) {
          <div class="feedback" [class]="feedbackType()">{{ feedback() }}</div>
        }
      </div>

      <div class="dialog-footer">
        <button class="btn-send" [disabled]="sending()" (click)="send()">
          @if (sending()) {
            Sending...
          } @else {
            &#9889; Send Trap
          }
        </button>
      </div>
    </div>
  `,
  styles: [`
    .trapgen-backdrop {
      position: fixed;
      inset: 0;
      background: rgba(0, 0, 0, 0.5);
      z-index: 900;
    }

    .trapgen-dialog {
      position: fixed;
      top: 50%;
      left: 50%;
      transform: translate(-50%, -50%);
      width: min(560px, 92vw);
      max-height: 85vh;
      background: #1a1e2e;
      border: 1px solid #2a3050;
      border-radius: 14px;
      z-index: 950;
      display: flex;
      flex-direction: column;
      box-shadow: 0 16px 48px rgba(0, 0, 0, 0.5);
      animation: trapgen-in 0.2s ease-out;
    }

    @keyframes trapgen-in {
      from { opacity: 0; transform: translate(-50%, -50%) scale(0.95); }
      to { opacity: 1; transform: translate(-50%, -50%) scale(1); }
    }

    .dialog-header {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 16px 20px;
      border-bottom: 1px solid #252d42;
      background: linear-gradient(135deg, #1c2133 0%, #1e2740 100%);
      border-radius: 14px 14px 0 0;
    }

    .dialog-icon { font-size: 18px; color: #FFAB00; }

    .dialog-title {
      font-weight: 700;
      font-size: 15px;
      color: #E8EAED;
      flex: 1;
    }

    .btn-close {
      background: none;
      border: none;
      color: #8C95A6;
      font-size: 22px;
      cursor: pointer;
      padding: 0 4px;
      line-height: 1;
      transition: color 0.15s;
    }
    .btn-close:hover { color: #FF5252; }

    .dialog-body {
      flex: 1;
      overflow-y: auto;
      padding: 16px 20px;
      display: flex;
      flex-direction: column;
      gap: 14px;
    }

    // Presets
    .preset-row {
      display: flex;
      align-items: center;
      gap: 6px;
      flex-wrap: wrap;
    }

    .preset-label {
      font-size: 11px;
      font-weight: 600;
      color: #5A6888;
      text-transform: uppercase;
      letter-spacing: 0.5px;
    }

    .btn-preset {
      background: rgba(255, 255, 255, 0.04);
      border: 1px solid #252d42;
      color: #8C95A6;
      padding: 4px 10px;
      border-radius: 14px;
      font-size: 11px;
      cursor: pointer;
      transition: all 0.15s;
    }
    .btn-preset:hover {
      background: rgba(255, 171, 0, 0.08);
      border-color: rgba(255, 171, 0, 0.3);
      color: #FFAB00;
    }
    .btn-preset.active {
      background: rgba(255, 171, 0, 0.12);
      border-color: #FFAB00;
      color: #FFAB00;
    }

    // Form
    .form-grid {
      display: grid;
      grid-template-columns: 100px 1fr;
      gap: 8px 12px;
      align-items: center;
    }

    .form-label {
      font-size: 12px;
      font-weight: 600;
      color: #8C95A6;
    }

    .form-input {
      background: #141824;
      border: 1px solid #252d42;
      color: #E8EAED;
      padding: 7px 10px;
      border-radius: 6px;
      font-size: 13px;
      font-family: 'Consolas', monospace;
      transition: border-color 0.15s;
    }
    .form-input:focus {
      outline: none;
      border-color: #4C9AFF;
    }

    // Bindings
    .bindings-section {
      border: 1px solid #252d42;
      border-radius: 10px;
      overflow: hidden;
    }

    .bindings-header {
      display: flex;
      align-items: center;
      padding: 8px 12px;
      background: rgba(255, 255, 255, 0.03);
      border-bottom: 1px solid #252d42;
    }

    .bindings-title {
      font-size: 11px;
      font-weight: 700;
      color: #5A6888;
      text-transform: uppercase;
      letter-spacing: 0.5px;
      flex: 1;
    }

    .btn-add-binding {
      background: rgba(76, 154, 255, 0.1);
      border: 1px solid rgba(76, 154, 255, 0.3);
      color: #4C9AFF;
      padding: 3px 10px;
      border-radius: 4px;
      font-size: 11px;
      font-weight: 600;
      cursor: pointer;
      transition: background 0.15s;
    }
    .btn-add-binding:hover { background: rgba(76, 154, 255, 0.2); }

    .binding-header-row {
      display: grid;
      grid-template-columns: 1fr 1fr 110px 28px;
      gap: 6px;
      padding: 6px 12px;
      font-size: 10px;
      font-weight: 700;
      color: #5A6888;
      text-transform: uppercase;
      border-bottom: 1px solid #1e2236;
    }

    .binding-row {
      display: grid;
      grid-template-columns: 1fr 1fr 110px 28px;
      gap: 6px;
      padding: 4px 12px;
      align-items: center;
    }
    .binding-row:hover { background: rgba(255, 255, 255, 0.02); }

    .binding-input {
      background: #141824;
      border: 1px solid #252d42;
      color: #E8EAED;
      padding: 5px 8px;
      border-radius: 4px;
      font-size: 12px;
      font-family: 'Consolas', monospace;
    }
    .binding-input:focus { outline: none; border-color: #4C9AFF; }

    .binding-select {
      background: #141824;
      border: 1px solid #252d42;
      color: #E8EAED;
      padding: 5px 4px;
      border-radius: 4px;
      font-size: 11px;
    }

    .btn-remove {
      background: none;
      border: none;
      color: #5A6888;
      font-size: 16px;
      cursor: pointer;
      padding: 0;
      line-height: 1;
    }
    .btn-remove:hover { color: #FF5252; }

    .no-bindings {
      padding: 14px 12px;
      color: #5A6888;
      font-size: 12px;
      text-align: center;
      font-style: italic;
    }

    // Feedback
    .feedback {
      padding: 8px 12px;
      border-radius: 6px;
      font-size: 12px;
      font-weight: 600;
    }
    .feedback.success {
      background: rgba(87, 217, 163, 0.1);
      color: #57D9A3;
      border: 1px solid rgba(87, 217, 163, 0.2);
    }
    .feedback.error {
      background: rgba(255, 82, 82, 0.1);
      color: #FF5252;
      border: 1px solid rgba(255, 82, 82, 0.2);
    }

    // Footer
    .dialog-footer {
      padding: 12px 20px;
      border-top: 1px solid #252d42;
      display: flex;
      justify-content: flex-end;
    }

    .btn-send {
      background: #FFAB00;
      border: none;
      color: #1a1e2e;
      padding: 8px 24px;
      border-radius: 6px;
      font-size: 13px;
      font-weight: 700;
      cursor: pointer;
      transition: background 0.15s;
    }
    .btn-send:hover { background: #FFC233; }
    .btn-send:disabled { opacity: 0.5; cursor: not-allowed; }
  `]
})
export class TrapGeneratorComponent {
  private signalR = inject(SignalRService);

  close = output();

  presets = PRESETS;
  trapOid = '1.3.6.1.6.3.1.1.5.3'; // linkDown default
  targetIp = '127.0.0.1';
  targetPort = 162;
  bindings: TrapBinding[] = [];

  sending = signal(false);
  feedback = signal<string | null>(null);
  feedbackType = signal<'success' | 'error'>('success');

  addBinding(): void {
    this.bindings = [...this.bindings, { oid: '', value: '', valueType: 'OctetString' }];
  }

  removeBinding(index: number): void {
    this.bindings = this.bindings.filter((_, i) => i !== index);
  }

  async send(): Promise<void> {
    if (!this.trapOid.trim()) {
      this.feedback.set('Trap OID is required');
      this.feedbackType.set('error');
      return;
    }

    this.sending.set(true);
    this.feedback.set(null);

    try {
      const result = await this.signalR.sendTrap(
        this.trapOid.trim(),
        this.targetIp.trim() || '127.0.0.1',
        this.targetPort || 162,
        this.bindings.filter(b => b.oid.trim())
      );

      if (result.success) {
        this.feedback.set(result.message);
        this.feedbackType.set('success');
      } else {
        this.feedback.set(result.message);
        this.feedbackType.set('error');
      }
    } catch (err: any) {
      this.feedback.set(err?.message || 'Failed to send trap');
      this.feedbackType.set('error');
    } finally {
      this.sending.set(false);
    }
  }
}
