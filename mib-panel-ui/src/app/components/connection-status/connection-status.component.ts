import { Component, inject, computed, signal, effect, untracked } from '@angular/core';
import { SignalRService, DeviceInfo } from '../../services/signalr.service';
import { MibPanelService } from '../../services/mib-panel.service';

@Component({
  selector: 'app-connection-status',
  standalone: true,
  template: `
    <div class="connection-bar">
      <div class="connection-indicator" [class]="signalR.connectionState()">
        <span class="dot"></span>
        <span class="label">{{ statusLabel() }}</span>
      </div>

      @if (signalR.connectionState() === 'connected') {
        <div class="device-picker">
          <button class="btn-devices" (click)="toggleDevices()">
            Devices &#9662;
          </button>
          @if (showDevices) {
            <div class="device-dropdown">
              @for (device of devices(); track device.id) {
                <button class="device-option"
                        [class.simulating]="device.isSimulating"
                        (mousedown)="$event.stopPropagation(); selectDevice(device)">
                  <span class="sim-dot" [class.active]="device.isSimulating"></span>
                  {{ device.name }}
                  @if (device.isSimulating) {
                    <span class="sim-badge">:{{ device.simulatorPort }}</span>
                  }
                </button>
              }
              @if (devices().length === 0) {
                <span class="no-devices">No devices found</span>
              }
            </div>
          }
        </div>

        @if (panelService.currentDeviceId()) {
          <button class="btn-refresh" (click)="refresh()">
            &#x21BB; Refresh
          </button>
        }
      }
    </div>
  `,
  styles: [`
    .connection-bar {
      display: flex; align-items: center; gap: 12px;
      padding: 6px 12px; background: #1a1e2e; border-radius: 8px;
      font-size: 12px;
    }
    .connection-indicator {
      display: flex; align-items: center; gap: 6px;
    }
    .dot {
      width: 8px; height: 8px; border-radius: 50%; display: inline-block;
    }
    .connected .dot { background: #22c55e; box-shadow: 0 0 6px #22c55e; }
    .connecting .dot { background: #f59e0b; animation: pulse 1s infinite; }
    .disconnected .dot { background: #666; }
    .error .dot { background: #ef4444; }
    .label { color: #a0a0b0; }
    .connected .label { color: #22c55e; }

    .device-picker { position: relative; }
    .btn-devices, .btn-refresh {
      background: #252a3a; border: 1px solid #3a4060; border-radius: 6px;
      color: #c0c8e0; padding: 4px 10px; cursor: pointer; font-size: 11px;
    }
    .btn-devices:hover, .btn-refresh:hover { background: #303650; }

    .device-dropdown {
      position: absolute; top: 100%; left: 0; margin-top: 4px;
      background: #1e2236; border: 1px solid #3a4060; border-radius: 8px;
      min-width: 200px; z-index: 100; overflow: hidden;
      box-shadow: 0 4px 16px rgba(0,0,0,0.4);
    }
    .device-option {
      display: flex; align-items: center; gap: 8px; width: 100%;
      padding: 8px 12px; background: none; border: none; color: #c0c8e0;
      cursor: pointer; font-size: 12px; text-align: left;
    }
    .device-option:hover { background: #252a40; }
    .sim-dot {
      width: 6px; height: 6px; border-radius: 50%; background: #555;
    }
    .sim-dot.active { background: #22c55e; }
    .sim-badge {
      margin-left: auto; font-size: 10px; color: #22c55e;
      background: #1a2a1a; padding: 1px 6px; border-radius: 4px;
    }
    .no-devices { padding: 8px 12px; color: #666; font-style: italic; }

    @keyframes pulse { 0%, 100% { opacity: 1; } 50% { opacity: 0.3; } }
  `]
})
export class ConnectionStatusComponent {
  signalR = inject(SignalRService);
  panelService = inject(MibPanelService);

  devices = signal<DeviceInfo[]>([]);
  showDevices = false;

  constructor() {
    // When connected, refresh device list (but don't reload schema — keep panel as-is)
    effect(() => {
      const state = this.signalR.connectionState();
      if (state === 'connected') {
        untracked(() => this.loadDevices());
      }
    });
  }

  statusLabel = computed(() => {
    switch (this.signalR.connectionState()) {
      case 'connected': return 'Connected';
      case 'connecting': return 'Connecting...';
      case 'error': return 'Error';
      default: return 'Disconnected';
    }
  });

  toggleDevices(): void {
    this.showDevices = !this.showDevices;
    if (this.showDevices) {
      this.loadDevices();
    }
  }

  async loadDevices(): Promise<void> {
    try {
      const list = await this.signalR.getDevices();
      this.devices.set(list);
    } catch {
      this.devices.set([]);
    }
  }

  selectDevice(device: DeviceInfo): void {
    console.log('[ConnectionStatus] selectDevice:', device.id, device.name);
    this.showDevices = false;
    this.panelService.loadFromDevice(device.id);
  }

  refresh(): void {
    this.panelService.refreshValues();
  }
}
