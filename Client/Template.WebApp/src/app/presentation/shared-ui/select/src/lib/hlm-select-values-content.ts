import { Directive } from '@angular/core';
import { classes } from '@presentation/shared-ui/utils';

@Directive({
  selector: '[hlmSelectValuesContent],hlm-select-values-content',
  exportAs: 'hlmSelectValuesContent',
})
export class HlmSelectValuesContent {
  constructor() {
    classes(() => 'gap-2 flex');
  }
}
