import { Component, inject, computed } from '@angular/core';
import { MibPanelService } from './services/mib-panel.service';
import { FieldClassifierService } from './services/field-classifier.service';
import { SignalRService } from './services/signalr.service';
import { SidePanelComponent } from './components/side-panel/side-panel.component';
import { ModuleSectionComponent } from './components/module-section/module-section.component';
import { SetFeedbackComponent } from './components/set-feedback/set-feedback.component';
import { DeviceCardComponent } from './components/device-card/device-card.component';
import { SystemInfoComponent } from './components/system-info/system-info.component';
import { ConnectionStatusComponent } from './components/connection-status/connection-status.component';
import { HelpGuideComponent } from './components/help-guide/help-guide.component';

@Component({
  selector: 'app-root',
  imports: [SidePanelComponent, ModuleSectionComponent, SetFeedbackComponent, DeviceCardComponent, SystemInfoComponent, ConnectionStatusComponent, HelpGuideComponent],
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

  isPanelOpen = false;
  isGuideOpen = false;
  isDragging = false;

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
}
