import { Directive } from '@angular/core';
import { classes } from '@presentation/shared-ui/utils';

@Directive({
  selector: 'option[hlmNativeSelectOption]',
  exportAs: 'hlmNativeSelectOption',
  host: { 'data-slot': 'native-select-option' },
})
export class HlmNativeSelectOption {
  constructor() {
    classes(() => 'bg-[Canvas] text-[CanvasText]');
  }
}
