import { Directive } from '@angular/core';
import { classes } from '@presentation/shared-ui/utils';

@Directive({
  selector: '[hlmDialogHeader],hlm-dialog-header',
  exportAs: 'hlmDialogHeader',
  host: { 'data-slot': 'dialog-header' },
})
export class HlmDialogHeader {
  constructor() {
    classes(() => 'gap-2 flex flex-col');
  }
}
