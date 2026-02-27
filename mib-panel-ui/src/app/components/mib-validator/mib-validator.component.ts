import { Component, inject, signal, output } from '@angular/core';
import { SignalRService, MibValidationResult, MibFileValidation, MibValidationIssue } from '../../services/signalr.service';
import { MibPanelService } from '../../services/mib-panel.service';

@Component({
  selector: 'app-mib-validator',
  standalone: true,
  template: `
    <div class="validator-backdrop" (click)="close.emit()"></div>
    <div class="validator-dialog">
      <div class="dialog-header">
        <span class="dialog-icon">&#9745;</span>
        <span class="dialog-title">MIB Validation</span>
        <button class="btn-close" (click)="close.emit()">&times;</button>
      </div>

      @if (loading()) {
        <div class="dialog-body loading-state">
          <div class="spinner"></div>
          <span>Validating MIB files...</span>
        </div>
      } @else if (error()) {
        <div class="dialog-body error-state">
          <span class="error-icon">&#9888;</span>
          <span>{{ error() }}</span>
          <button class="btn-retry" (click)="validate()">Retry</button>
        </div>
      } @else if (result()) {
        <div class="dialog-body">
          <!-- Summary Bar -->
          <div class="summary-bar">
            <span class="summary-device">{{ result()!.deviceName }}</span>
            <span class="summary-stat">{{ result()!.files.length }} files</span>
            <span class="summary-stat">{{ totalDefinitions() }} definitions</span>
            @if (totalIssues() > 0) {
              <span class="summary-stat issues">{{ totalIssues() }} issues</span>
            } @else {
              <span class="summary-stat clean">0 issues</span>
            }
          </div>

          <!-- Dependency Map -->
          @if (result()!.dependencies.length) {
            <div class="dep-section" [class.has-missing]="depMissing() > 0">
              <div class="dep-header" (click)="depsExpanded = !depsExpanded">
                <span class="file-expand" [class.expanded]="depsExpanded">&#9654;</span>
                <span class="dep-title">Dependencies</span>
                @if (depLoaded() > 0) {
                  <span class="dep-stat loaded">{{ depLoaded() }} loaded</span>
                }
                @if (depStandard() > 0) {
                  <span class="dep-stat standard">{{ depStandard() }} standard</span>
                }
                @if (depMissing() > 0) {
                  <span class="dep-stat missing">{{ depMissing() }} missing</span>
                } @else {
                  <span class="dep-stat all-ok">All satisfied</span>
                }
              </div>

              @if (depsExpanded) {
                <div class="dep-body">
                  @for (file of result()!.dependencies; track file.fileName) {
                    @if (file.imports.length > 0) {
                      <div class="dep-file">
                        <div class="dep-file-name">{{ file.moduleName }}
                          <span class="dep-file-hint">({{ file.fileName }})</span>
                        </div>
                        <div class="dep-imports">
                          @for (imp of file.imports; track imp.moduleName) {
                            <span class="dep-chip" [class]="'dep-' + imp.status"
                                  [title]="imp.status === 'loaded' ? 'Provided by ' + imp.providedBy : imp.status">
                              {{ imp.moduleName }}
                            </span>
                          }
                        </div>
                      </div>
                    }
                  }
                </div>
              }
            </div>
          }

          <!-- Per-file results -->
          <div class="file-list">
            @for (file of result()!.files; track file.fileName) {
              <div class="file-card" [class.has-issues]="file.issueCount > 0">
                <div class="file-header" (click)="toggleFile(file)">
                  <span class="file-expand" [class.expanded]="expandedFiles[file.fileName]">&#9654;</span>
                  <span class="file-icon" [class]="fileStatus(file)">&#9679;</span>
                  <span class="file-name">{{ file.fileName }}</span>
                  <span class="file-defs">{{ file.definitionCount }} defs</span>
                  @if (file.issueCount > 0) {
                    <span class="file-issues">{{ file.issueCount }} issues</span>
                  } @else {
                    <span class="file-clean">OK</span>
                  }
                </div>

                @if (expandedFiles[file.fileName] && file.issues.length > 0) {
                  <div class="issue-list">
                    @for (issue of file.issues; track $index) {
                      <div class="issue-row" [class]="'severity-' + issue.severity">
                        <span class="issue-badge">{{ issue.severity }}</span>
                        <span class="issue-msg">{{ issue.message }}</span>
                        @if (issue.context) {
                          <span class="issue-ctx">{{ issue.context }}</span>
                        }
                      </div>
                    }
                  </div>
                }
              </div>
            }
          </div>
        </div>
      } @else {
        <div class="dialog-body empty-state">
          <span>Click Validate to check MIB files for issues.</span>
          <button class="btn-validate" (click)="validate()">Validate</button>
        </div>
      }
    </div>
  `,
  styles: [`
    .validator-backdrop {
      position: fixed;
      inset: 0;
      background: rgba(0, 0, 0, 0.5);
      z-index: 900;
    }

    .validator-dialog {
      position: fixed;
      top: 50%;
      left: 50%;
      transform: translate(-50%, -50%);
      width: min(640px, 92vw);
      max-height: 80vh;
      background: #1a1e2e;
      border: 1px solid #2a3050;
      border-radius: 14px;
      z-index: 950;
      display: flex;
      flex-direction: column;
      box-shadow: 0 16px 48px rgba(0, 0, 0, 0.5);
      animation: validator-in 0.2s ease-out;
    }

    @keyframes validator-in {
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

    .dialog-icon {
      font-size: 18px;
      color: #4C9AFF;
    }

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
    }

    // Loading
    .loading-state {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: 12px;
      padding: 40px 20px;
      color: #8C95A6;
      font-size: 14px;
    }

    .spinner {
      width: 20px;
      height: 20px;
      border: 2px solid #2a3050;
      border-top-color: #4C9AFF;
      border-radius: 50%;
      animation: spin 0.8s linear infinite;
    }

    @keyframes spin {
      to { transform: rotate(360deg); }
    }

    // Error
    .error-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 12px;
      padding: 40px 20px;
      color: #FF5252;
      font-size: 14px;
      text-align: center;
    }

    .error-icon { font-size: 28px; }

    .btn-retry, .btn-validate {
      background: #4C9AFF;
      border: none;
      color: #fff;
      padding: 8px 24px;
      border-radius: 6px;
      font-size: 13px;
      font-weight: 600;
      cursor: pointer;
      transition: background 0.15s;
    }
    .btn-retry:hover, .btn-validate:hover { background: #79B8FF; }

    // Empty
    .empty-state {
      display: flex;
      flex-direction: column;
      align-items: center;
      gap: 16px;
      padding: 40px 20px;
      color: #8C95A6;
      font-size: 14px;
    }

    // Summary
    .summary-bar {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 10px 14px;
      background: rgba(255, 255, 255, 0.03);
      border: 1px solid #252d42;
      border-radius: 10px;
      margin-bottom: 14px;
    }

    .summary-device {
      font-weight: 700;
      font-size: 13px;
      color: #FFAB00;
      margin-right: auto;
    }

    .summary-stat {
      font-size: 12px;
      font-weight: 600;
      color: #8C95A6;
      background: rgba(255, 255, 255, 0.05);
      padding: 3px 10px;
      border-radius: 12px;
    }

    .summary-stat.issues {
      color: #FF5252;
      background: rgba(255, 82, 82, 0.1);
    }

    .summary-stat.clean {
      color: #57D9A3;
      background: rgba(87, 217, 163, 0.1);
    }

    // File list
    .file-list {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .file-card {
      border: 1px solid #252d42;
      border-radius: 10px;
      overflow: hidden;
      background: #171b28;
    }

    .file-card.has-issues {
      border-color: rgba(255, 171, 0, 0.3);
    }

    .file-header {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px 14px;
      cursor: pointer;
      transition: background 0.15s;
    }
    .file-header:hover { background: rgba(255, 255, 255, 0.03); }

    .file-expand {
      font-size: 10px;
      color: #5A6888;
      transition: transform 0.2s;
      width: 14px;
    }
    .file-expand.expanded { transform: rotate(90deg); }

    .file-icon {
      font-size: 10px;
    }
    .file-icon.ok { color: #57D9A3; }
    .file-icon.warning { color: #FFAB00; }
    .file-icon.error { color: #FF5252; }

    .file-name {
      font-weight: 600;
      font-size: 13px;
      color: #E8EAED;
      flex: 1;
      font-family: 'Consolas', monospace;
    }

    .file-defs {
      font-size: 11px;
      color: #5A6888;
    }

    .file-issues {
      font-size: 11px;
      font-weight: 600;
      color: #FFAB00;
      background: rgba(255, 171, 0, 0.1);
      padding: 2px 8px;
      border-radius: 10px;
    }

    .file-clean {
      font-size: 11px;
      font-weight: 600;
      color: #57D9A3;
    }

    // Issue rows
    .issue-list {
      border-top: 1px solid #252d42;
      padding: 6px 0;
    }

    .issue-row {
      display: flex;
      align-items: flex-start;
      gap: 8px;
      padding: 6px 14px 6px 36px;
      font-size: 12px;
    }

    .issue-badge {
      font-size: 10px;
      font-weight: 700;
      text-transform: uppercase;
      padding: 1px 6px;
      border-radius: 4px;
      flex-shrink: 0;
    }

    .severity-error .issue-badge {
      background: rgba(255, 82, 82, 0.15);
      color: #FF5252;
    }

    .severity-warning .issue-badge {
      background: rgba(255, 171, 0, 0.15);
      color: #FFAB00;
    }

    .severity-info .issue-badge {
      background: rgba(140, 149, 166, 0.15);
      color: #8C95A6;
    }

    .issue-msg {
      color: #CDD1D8;
      flex: 1;
    }

    .issue-ctx {
      font-family: 'Consolas', monospace;
      font-size: 11px;
      color: #5A6888;
      flex-shrink: 0;
    }

    // Dependency section
    .dep-section {
      border: 1px solid #252d42;
      border-radius: 10px;
      margin-bottom: 14px;
      background: #171b28;
      overflow: hidden;
    }

    .dep-section.has-missing {
      border-color: rgba(255, 82, 82, 0.3);
    }

    .dep-header {
      display: flex;
      align-items: center;
      gap: 8px;
      padding: 10px 14px;
      cursor: pointer;
      transition: background 0.15s;
    }
    .dep-header:hover { background: rgba(255, 255, 255, 0.03); }

    .dep-title {
      font-weight: 700;
      font-size: 13px;
      color: #E8EAED;
      flex: 1;
    }

    .dep-stat {
      font-size: 11px;
      font-weight: 600;
      padding: 2px 8px;
      border-radius: 10px;
    }
    .dep-stat.loaded {
      color: #57D9A3;
      background: rgba(87, 217, 163, 0.1);
    }
    .dep-stat.standard {
      color: #8C95A6;
      background: rgba(140, 149, 166, 0.1);
    }
    .dep-stat.missing {
      color: #FF5252;
      background: rgba(255, 82, 82, 0.1);
    }
    .dep-stat.all-ok {
      color: #57D9A3;
      background: rgba(87, 217, 163, 0.1);
    }

    .dep-body {
      border-top: 1px solid #252d42;
      padding: 10px 14px;
      display: flex;
      flex-direction: column;
      gap: 10px;
    }

    .dep-file-name {
      font-size: 12px;
      font-weight: 600;
      color: #CDD1D8;
      margin-bottom: 4px;
    }

    .dep-file-hint {
      font-weight: 400;
      color: #5A6888;
      font-size: 11px;
    }

    .dep-imports {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
    }

    .dep-chip {
      font-size: 11px;
      font-weight: 600;
      font-family: 'Consolas', monospace;
      padding: 3px 10px;
      border-radius: 12px;
      cursor: default;
    }

    .dep-loaded {
      color: #57D9A3;
      background: rgba(87, 217, 163, 0.12);
      border: 1px solid rgba(87, 217, 163, 0.25);
    }

    .dep-standard {
      color: #8C95A6;
      background: rgba(140, 149, 166, 0.08);
      border: 1px solid rgba(140, 149, 166, 0.15);
    }

    .dep-missing {
      color: #FF5252;
      background: rgba(255, 82, 82, 0.12);
      border: 1px solid rgba(255, 82, 82, 0.3);
      animation: pulse-missing 2s ease-in-out infinite;
    }

    @keyframes pulse-missing {
      0%, 100% { border-color: rgba(255, 82, 82, 0.3); }
      50% { border-color: rgba(255, 82, 82, 0.7); }
    }
  `]
})
export class MibValidatorComponent {
  private signalR = inject(SignalRService);
  private panelService = inject(MibPanelService);

  close = output();

  result = signal<MibValidationResult | null>(null);
  loading = signal(false);
  error = signal<string | null>(null);
  expandedFiles: Record<string, boolean> = {};
  depsExpanded = true;

  constructor() {
    // Auto-validate on open
    this.validate();
  }

  totalDefinitions(): number {
    return this.result()?.files.reduce((sum, f) => sum + f.definitionCount, 0) ?? 0;
  }

  totalIssues(): number {
    return this.result()?.files.reduce((sum, f) => sum + f.issueCount, 0) ?? 0;
  }

  depLoaded(): number {
    return this.countDepsByStatus('loaded');
  }

  depStandard(): number {
    return this.countDepsByStatus('standard');
  }

  depMissing(): number {
    return this.countDepsByStatus('missing');
  }

  private countDepsByStatus(status: string): number {
    const deps = this.result()?.dependencies;
    if (!deps) return 0;
    let count = 0;
    for (const file of deps) {
      for (const imp of file.imports) {
        if (imp.status === status) count++;
      }
    }
    return count;
  }

  fileStatus(file: MibFileValidation): string {
    if (file.issues.some(i => i.severity === 'error')) return 'error';
    if (file.issues.length > 0) return 'warning';
    return 'ok';
  }

  toggleFile(file: MibFileValidation): void {
    this.expandedFiles[file.fileName] = !this.expandedFiles[file.fileName];
  }

  async validate(): Promise<void> {
    const deviceId = this.panelService.currentDeviceId();
    if (!deviceId) {
      this.error.set('No device loaded. Select a device first.');
      return;
    }

    this.loading.set(true);
    this.error.set(null);

    try {
      const res = await this.signalR.validateMib(deviceId);
      this.result.set(res);
      // Auto-expand files with issues
      this.expandedFiles = {};
      for (const f of res.files) {
        if (f.issueCount > 0) this.expandedFiles[f.fileName] = true;
      }
    } catch (err: any) {
      this.error.set(err?.message || 'Validation failed');
    } finally {
      this.loading.set(false);
    }
  }
}
