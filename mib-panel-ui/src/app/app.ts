import { Component, inject, computed, signal } from '@angular/core';
import { MibPanelService } from './services/mib-panel.service';
import { FieldClassifierService } from './services/field-classifier.service';
import { SignalRService } from './services/signalr.service';
import html2canvas from 'html2canvas';
import { SidePanelComponent } from './components/side-panel/side-panel.component';
import { ModuleSectionComponent } from './components/module-section/module-section.component';
import { SetFeedbackComponent } from './components/set-feedback/set-feedback.component';
import { DeviceCardComponent } from './components/device-card/device-card.component';
import { SystemInfoComponent } from './components/system-info/system-info.component';
import { ConnectionStatusComponent } from './components/connection-status/connection-status.component';
import { HelpGuideComponent } from './components/help-guide/help-guide.component';
import { TrapViewerComponent } from './components/trap-viewer/trap-viewer.component';
import { MibValidatorComponent } from './components/mib-validator/mib-validator.component';
import { TrapGeneratorComponent } from './components/trap-generator/trap-generator.component';
import { BulkSetComponent } from './components/bulk-set/bulk-set.component';

@Component({
  selector: 'app-root',
  imports: [SidePanelComponent, ModuleSectionComponent, SetFeedbackComponent, DeviceCardComponent, SystemInfoComponent, ConnectionStatusComponent, HelpGuideComponent, TrapViewerComponent, MibValidatorComponent, TrapGeneratorComponent, BulkSetComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private panelService = inject(MibPanelService);
  private classifier = inject(FieldClassifierService);
  private signalR = inject(SignalRService);

  schema = this.panelService.schema;
  identity = computed(() => {
    const s = this.schema();
    return s ? this.classifier.extractIdentity(s) : null;
  });

  systemInfo = computed(() => {
    const s = this.schema();
    return s ? this.classifier.extractSystemInfo(s) : [];
  });

  /** SignalR connection state for panel header dot */
  signalRState = computed(() => this.signalR.connectionState());

  /** Quick stats for panel header */
  panelStats = computed(() => {
    const s = this.schema();
    if (!s) return { fields: 0, tables: 0, modules: 0 };
    let tables = 0;
    for (const m of s.modules) tables += m.tables.length;
    return { fields: s.totalFields, tables, modules: s.modules.length };
  });

  /** Aggregate health summary across all modules */
  healthSummary = computed(() => {
    const s = this.schema();
    if (!s) return { ok: 0, warning: 0, alarm: 0, info: 0 };
    let ok = 0, warning = 0, alarm = 0, info = 0;
    for (const module of s.modules) {
      const classified = this.classifier.classifyScalars(module.scalars);
      for (const field of classified.status) {
        if (!field.options?.length || !field.currentValue) { info++; continue; }
        const num = parseInt(field.currentValue, 10);
        const label = (field.options.find((o: any) => o.value === num)?.label || '').toLowerCase();
        if (/up|active|ok|normal|enabled|true|running|online|ready/.test(label)) ok++;
        else if (/warning|degraded|standby|testing|suspended/.test(label)) warning++;
        else if (/down|error|fail|critical|disabled|false|offline|alarm|fault/.test(label)) alarm++;
        else info++;
      }
      info += classified.counters.length;
    }
    return { ok, warning, alarm, info };
  });

  /** Module tabs for navigation */
  moduleTabs = computed(() => {
    const s = this.schema();
    if (!s) return [];
    return s.modules.map((m, i) => ({
      name: m.moduleName,
      count: m.scalarCount + m.tableCount,
      index: i,
    }));
  });

  activeModuleIndex = 0;

  isPanelOpen = false;
  isGuideOpen = false;
  isTrapViewerOpen = false;
  isValidatorOpen = false;
  isTrapGenOpen = false;
  isBulkSetOpen = false;
  isDragging = false;
  isCapturing = signal(false);

  constructor() {
    // Auto-connect to WPF SignalR server
    this.signalR.connect();
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (file) {
      this.panelService.loadFromFile(file);
    }
    input.value = '';
  }

  scrollToModule(index: number): void {
    this.activeModuleIndex = index;
    const el = document.getElementById('panel-module-' + index);
    el?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  onDragOver(event: DragEvent): void {
    event.preventDefault();
    this.isDragging = true;
  }

  onDrop(event: DragEvent): void {
    event.preventDefault();
    this.isDragging = false;
    const file = event.dataTransfer?.files?.[0];
    if (file?.name.endsWith('.json')) {
      this.panelService.loadFromFile(file);
    }
  }

  async saveImage(): Promise<void> {
    // Capture the main content or the side panel body (whichever is visible)
    const target = this.isPanelOpen
      ? document.querySelector<HTMLElement>('.panel-body')
      : document.querySelector<HTMLElement>('.main-content');
    if (!target) return;

    this.isCapturing.set(true);
    try {
      const canvas = await html2canvas(target, {
        backgroundColor: '#141720',
        scale: 2,
        useCORS: true,
      });
      const blob = await new Promise<Blob | null>(resolve =>
        canvas.toBlob(resolve, 'image/png'));
      if (!blob) return;

      const name = this.schema()?.deviceName || 'panel';
      const ts = new Date().toISOString().replace(/[:.]/g, '-').slice(0, 19);
      const a = document.createElement('a');
      a.href = URL.createObjectURL(blob);
      a.download = `${name}_${ts}.png`;
      a.click();
      URL.revokeObjectURL(a.href);
    } finally {
      this.isCapturing.set(false);
    }
  }
}
