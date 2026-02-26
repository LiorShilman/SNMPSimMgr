import { Component, inject, computed } from '@angular/core';
import { MibPanelService } from './services/mib-panel.service';
import { FieldClassifierService } from './services/field-classifier.service';
import { SidePanelComponent } from './components/side-panel/side-panel.component';
import { ModuleSectionComponent } from './components/module-section/module-section.component';
import { SetFeedbackComponent } from './components/set-feedback/set-feedback.component';
import { DeviceCardComponent } from './components/device-card/device-card.component';
import { SystemInfoComponent } from './components/system-info/system-info.component';

@Component({
  selector: 'app-root',
  imports: [SidePanelComponent, ModuleSectionComponent, SetFeedbackComponent, DeviceCardComponent, SystemInfoComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private panelService = inject(MibPanelService);
  private classifier = inject(FieldClassifierService);

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
  isDragging = false;

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
