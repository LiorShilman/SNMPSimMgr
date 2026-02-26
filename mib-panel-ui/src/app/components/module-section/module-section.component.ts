import { Component, Input, inject, OnInit } from '@angular/core';
import { MibModuleSchema, MibFieldSchema } from '../../models/mib-schema';
import { FieldClassifierService, ClassifiedScalars } from '../../services/field-classifier.service';
import { StatusGridComponent } from '../status-grid/status-grid.component';
import { ConfigGroupComponent } from '../config-group/config-group.component';
import { MibTableComponent } from '../mib-table/mib-table.component';

@Component({
  selector: 'app-module-section',
  standalone: true,
  imports: [StatusGridComponent, ConfigGroupComponent, MibTableComponent],
  template: `
    <section class="module-section">
      <div class="module-header" (click)="isExpanded = !isExpanded">
        <span class="expand-icon">{{ isExpanded ? '▾' : '▸' }}</span>
        <span class="module-name">{{ module.moduleName }}</span>
      </div>

      @if (isExpanded) {
        <div class="module-body">
          <!-- Status / Monitoring Section -->
          @if (classified.status.length + classified.counters.length > 0) {
            <div class="section">
              <div class="section-header">
                <span class="section-icon">&#9673;</span>
                <span class="section-title">Monitoring</span>
              </div>
              <app-status-grid [fields]="monitorFields" />
            </div>
          }

          <!-- Configuration Section -->
          @if (classified.config.length > 0) {
            <div class="section">
              <div class="section-header">
                <span class="section-icon">&#9881;</span>
                <span class="section-title">Configuration</span>
              </div>
              <app-config-group [fields]="classified.config" />
            </div>
          }

          <!-- Tables Section -->
          @if (module.tables.length > 0) {
            <div class="section">
              <div class="section-header">
                <span class="section-icon">&#9638;</span>
                <span class="section-title">Tables</span>
              </div>
              <div class="tables-list">
                @for (table of module.tables; track table.oid) {
                  <app-mib-table [table]="table" />
                }
              </div>
            </div>
          }
        </div>
      }
    </section>
  `,
  styles: [`
    .module-section {
      border: 1px solid #252d42;
      border-radius: 14px;
      background: #171b28;
      overflow: hidden;
    }

    .module-header {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 16px 20px;
      cursor: pointer;
      background: linear-gradient(135deg, #1c2133 0%, #1e2740 100%);
      transition: all 0.2s;

      &:hover {
        background: linear-gradient(135deg, #1f2538 0%, #222c48 100%);
      }
    }

    .expand-icon {
      color: #6C9FFF;
      font-size: 14px;
      width: 16px;
      transition: color 0.15s;
    }

    .module-name {
      font-weight: 700;
      font-size: 15px;
      color: #E8EAED;
      flex: 1;
      letter-spacing: 0.2px;
    }

    .module-body {
      padding: 4px 18px 18px;
    }

    .section {
      margin-top: 16px;
    }

    .section-header {
      display: flex;
      align-items: center;
      gap: 8px;
      padding-bottom: 10px;
      margin-bottom: 10px;
      border-bottom: 1px solid #252d42;
    }

    .section-icon {
      font-size: 14px;
      color: #5A6888;
    }

    .section-title {
      font-size: 11px;
      font-weight: 700;
      text-transform: uppercase;
      letter-spacing: 1.5px;
      color: #5A6888;
    }

    .tables-list {
      display: flex;
      flex-direction: column;
      gap: 12px;
    }
  `]
})
export class ModuleSectionComponent implements OnInit {
  @Input({ required: true }) module!: MibModuleSchema;

  private classifier = inject(FieldClassifierService);

  isExpanded = true;
  classified: ClassifiedScalars = { identity: [], status: [], config: [], counters: [] };
  monitorFields: MibFieldSchema[] = [];

  ngOnInit(): void {
    this.classified = this.classifier.classifyScalars(this.module.scalars);
    this.monitorFields = [...this.classified.status, ...this.classified.counters];
  }
}
