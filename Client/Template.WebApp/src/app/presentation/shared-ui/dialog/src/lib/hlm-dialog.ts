import { ChangeDetectionStrategy, Component, forwardRef } from '@angular/core';
import { BrnDialog, provideBrnDialogDefaultOptions } from '@spartan-ng/brain/dialog';
import { HlmDialogOverlay } from './hlm-dialog-overlay';

@Component({
  selector: 'hlm-dialog',
  exportAs: 'hlmDialog',
  imports: [HlmDialogOverlay],
  providers: [
    {
      provide: BrnDialog,
      useExisting: forwardRef(() => HlmDialog),
    },
    provideBrnDialogDefaultOptions({
      // add custom options here
    }),
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './hlm-dialog.html',
})
export class HlmDialog extends BrnDialog {}
