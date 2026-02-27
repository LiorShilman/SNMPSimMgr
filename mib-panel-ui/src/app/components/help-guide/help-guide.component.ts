import { Component, output } from '@angular/core';

@Component({
  selector: 'app-help-guide',
  standalone: true,
  templateUrl: './help-guide.component.html',
  styleUrl: './help-guide.component.scss',
})
export class HelpGuideComponent {
  close = output<void>();
}
