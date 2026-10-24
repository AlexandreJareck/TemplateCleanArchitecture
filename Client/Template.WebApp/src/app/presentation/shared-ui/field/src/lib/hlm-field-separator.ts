import { ChangeDetectionStrategy, Component } from '@angular/core';
import { HlmSeparator } from '@presentation/shared-ui/separator';
import { classes } from '@presentation/shared-ui/utils';

@Component({
  selector: 'hlm-field-separator',
  exportAs: 'hlmFieldSeparator',
  imports: [HlmSeparator],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { 'data-slot': 'field-separator' },
  templateUrl: './hlm-field-separator.html',
})
export class HlmFieldSeparator {
  constructor() {
    classes(() => '-my-2 h-5 text-sm group-data-[variant=outline]/field-group:-mb-2 relative');
  }
}
