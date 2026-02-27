import { Component, inject, signal, effect, untracked } from '@angular/core';
import { SignalRService, TrapEvent } from '../../services/signalr.service';

interface TrapEntry extends TrapEvent {
  id: number;
  expanded: boolean;
  friendlyOid: string;
  severity: 'critical' | 'warning' | 'info';
}

const KNOWN_TRAPS: Record<string, { name: string; severity: 'critical' | 'warning' | 'info' }> = {
  '1.3.6.1.6.3.1.1.5.1': { name: 'coldStart', severity: 'info' },
  '1.3.6.1.6.3.1.1.5.2': { name: 'warmStart', severity: 'info' },
  '1.3.6.1.6.3.1.1.5.3': { name: 'linkDown', severity: 'critical' },
  '1.3.6.1.6.3.1.1.5.4': { name: 'linkUp', severity: 'info' },
  '1.3.6.1.6.3.1.1.5.5': { name: 'authenticationFailure', severity: 'warning' },
};

@Component({
  selector: 'app-trap-viewer',
  standalone: true,
  template: `
    <div class="trap-viewer">
      <div class="trap-header">
        <div class="trap-title">
          <span class="trap-icon">&#9888;</span>
          <span>Trap Viewer</span>
          @if (trapCount() > 0) {
            <span class="trap-badge">{{ trapCount() }}</span>
          }
        </div>
        <div class="trap-actions">
          @if (isPaused()) {
            <button type="button" class="btn-action resume" (click)="isPaused.set(false)">
              &#9654; Resume
            </button>
          } @else {
            <button type="button" class="btn-action pause" (click)="isPaused.set(true)">
              &#10074;&#10074; Pause
            </button>
          }
          <button type="button" class="btn-action clear" (click)="clear()">Clear</button>
        </div>
      </div>

      <div class="trap-list">
        @if (traps().length === 0) {
          <div class="trap-empty">
            <span class="empty-icon">&#128752;</span>
            <span>No traps received yet</span>
            <span class="empty-hint">Traps will appear here in real-time when the WPF trap listener is active</span>
          </div>
        }

        @for (trap of traps(); track trap.id) {
          <div class="trap-row" [class]="trap.severity" [class.expanded]="trap.expanded">
            <div class="trap-summary" (click)="toggleExpand(trap)">
              <span class="severity-dot"></span>
              <span class="trap-time">{{ formatTime(trap.timestamp) }}</span>
              <span class="trap-source">{{ trap.sourceIp }}</span>
              <span class="trap-oid-name">{{ trap.friendlyOid }}</span>
              <span class="trap-bindings-count">{{ trap.variableBindings.length }} binding{{ trap.variableBindings.length !== 1 ? 's' : '' }}</span>
              <span class="expand-chevron" [class.open]="trap.expanded">&#9654;</span>
            </div>

            @if (trap.expanded && trap.variableBindings.length > 0) {
              <div class="trap-detail">
                <table class="bindings-table">
                  <thead>
                    <tr>
                      <th>OID</th>
                      <th>Value</th>
                      <th>Type</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (vb of trap.variableBindings; track vb.oid) {
                      <tr>
                        <td class="vb-oid">{{ vb.oid }}</td>
                        <td class="vb-value">{{ vb.value }}</td>
                        <td class="vb-type">{{ vb.valueType }}</td>
                      </tr>
                    }
                  </tbody>
                </table>
              </div>
            }
          </div>
        }
      </div>
    </div>
  `,
  styles: [`
    .trap-viewer {
      display: flex;
      flex-direction: column;
      background: #1B1F2A;
      border: 1px solid #2A3040;
      border-radius: 14px;
      overflow: hidden;
      max-height: 70vh;
    }

    .trap-header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 14px 20px;
      background: linear-gradient(135deg, #1a2035 0%, #1e2740 50%, #1a2035 100%);
      border-bottom: 1px solid #2A3040;
    }

    .trap-title {
      display: flex;
      align-items: center;
      gap: 10px;
      font-weight: 700;
      font-size: 15px;
      color: #E8EAED;
    }

    .trap-icon {
      font-size: 18px;
      color: #FFAB00;
    }

    .trap-badge {
      font-size: 11px;
      font-weight: 700;
      background: #FF5252;
      color: #fff;
      padding: 2px 8px;
      border-radius: 10px;
      min-width: 20px;
      text-align: center;
    }

    .trap-actions {
      display: flex;
      gap: 6px;
    }

    .btn-action {
      background: rgba(255, 255, 255, 0.04);
      border: 1px solid #2A3040;
      color: #8C95A6;
      padding: 5px 12px;
      border-radius: 6px;
      font-size: 11px;
      font-weight: 600;
      cursor: pointer;
      transition: all 0.15s;

      &:hover { background: rgba(255, 255, 255, 0.08); color: #CDD1D8; }
      &.clear:hover { color: #FF6B6B; border-color: rgba(255, 107, 107, 0.3); }
      &.pause:hover { color: #FFAB00; border-color: rgba(255, 171, 0, 0.3); }
      &.resume:hover { color: #57D9A3; border-color: rgba(87, 217, 163, 0.3); }
    }

    .trap-list {
      flex: 1;
      overflow-y: auto;
      padding: 8px;

      &::-webkit-scrollbar { width: 6px; }
      &::-webkit-scrollbar-track { background: transparent; }
      &::-webkit-scrollbar-thumb { background: #3D4663; border-radius: 3px; }
    }

    .trap-empty {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 8px;
      padding: 40px 20px;
      color: #5A6888;
      font-size: 13px;
    }

    .empty-icon { font-size: 28px; opacity: 0.5; }
    .empty-hint { font-size: 11px; color: #3D4663; text-align: center; }

    .trap-row {
      border-radius: 10px;
      margin-bottom: 4px;
      border: 1px solid transparent;
      transition: all 0.15s;
      overflow: hidden;

      &:hover { background: rgba(255, 255, 255, 0.02); }

      &.critical {
        border-left: 3px solid #FF5252;
        .severity-dot { background: #FF5252; box-shadow: 0 0 6px rgba(255, 82, 82, 0.5); }
      }
      &.warning {
        border-left: 3px solid #FFAB00;
        .severity-dot { background: #FFAB00; box-shadow: 0 0 6px rgba(255, 171, 0, 0.5); }
      }
      &.info {
        border-left: 3px solid #4C9AFF;
        .severity-dot { background: #4C9AFF; box-shadow: 0 0 6px rgba(76, 154, 255, 0.3); }
      }

      &.expanded {
        background: rgba(255, 255, 255, 0.03);
        border-color: #252d42;
      }
    }

    .trap-summary {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 10px 14px;
      cursor: pointer;
      transition: background 0.1s;

      &:hover { background: rgba(255, 255, 255, 0.02); }
    }

    .severity-dot {
      width: 7px;
      height: 7px;
      border-radius: 50%;
      flex-shrink: 0;
    }

    .trap-time {
      font-family: 'Consolas', monospace;
      font-size: 11px;
      color: #5A6888;
      flex-shrink: 0;
    }

    .trap-source {
      font-family: 'Consolas', monospace;
      font-size: 12px;
      color: #79B8FF;
      min-width: 100px;
      flex-shrink: 0;
    }

    .trap-oid-name {
      font-size: 13px;
      font-weight: 600;
      color: #E8EAED;
      flex: 1;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .trap-bindings-count {
      font-size: 11px;
      color: #5A6888;
      flex-shrink: 0;
    }

    .expand-chevron {
      display: inline-block;
      font-size: 10px;
      color: #5A6888;
      transition: transform 0.2s;

      &.open { transform: rotate(90deg); }
    }

    .trap-detail {
      padding: 0 14px 12px;
    }

    .bindings-table {
      width: 100%;
      border-collapse: collapse;
      font-size: 11px;

      th {
        background: #141824;
        padding: 6px 10px;
        text-align: left;
        color: #7A849A;
        font-weight: 600;
        text-transform: uppercase;
        letter-spacing: 0.5px;
        font-size: 10px;
        border-bottom: 1px solid #252d42;
      }

      td {
        padding: 5px 10px;
        border-bottom: 1px solid #1a1f2e;
      }

      .vb-oid {
        font-family: 'Consolas', monospace;
        color: #9AA5B8;
        font-size: 11px;
      }

      .vb-value {
        font-family: 'Consolas', monospace;
        color: #E8EAED;
      }

      .vb-type {
        color: #A78BFA;
        font-size: 10px;
        text-transform: uppercase;
      }
    }
  `]
})
export class TrapViewerComponent {
  private signalR = inject(SignalRService);
  private nextId = 0;

  traps = signal<TrapEntry[]>([]);
  trapCount = signal(0);
  isPaused = signal(false);

  constructor() {
    effect(() => {
      const trap = this.signalR.latestTrap();
      if (!trap) return;

      untracked(() => {
        if (this.isPaused()) return;

        const known = KNOWN_TRAPS[trap.oid];
        const entry: TrapEntry = {
          ...trap,
          id: ++this.nextId,
          expanded: false,
          friendlyOid: known?.name || trap.oid,
          severity: known?.severity || 'info',
        };

        const current = this.traps();
        const updated = [entry, ...current];
        if (updated.length > 200) updated.length = 200;
        this.traps.set(updated);
        this.trapCount.set(updated.length);
      });
    });
  }

  toggleExpand(trap: TrapEntry): void {
    this.traps.update(list =>
      list.map(t => t.id === trap.id ? { ...t, expanded: !t.expanded } : t)
    );
  }

  clear(): void {
    this.traps.set([]);
    this.trapCount.set(0);
  }

  formatTime(ts: string): string {
    try {
      const d = new Date(ts);
      return d.toLocaleTimeString('en-GB', { hour12: false });
    } catch {
      return ts;
    }
  }
}
