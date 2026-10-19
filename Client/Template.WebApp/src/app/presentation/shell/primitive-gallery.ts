import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { form, FormField, required } from '@angular/forms/signals';
import { TranslocoPipe } from '@jsverse/transloco';
import { HlmAlertImports } from '@presentation/shared-ui/alert';
import { HlmButtonImports } from '@presentation/shared-ui/button';
import { HlmCheckboxImports } from '@presentation/shared-ui/checkbox';
import { HlmDialogImports } from '@presentation/shared-ui/dialog';
import { HlmEmptyImports } from '@presentation/shared-ui/empty';
import { HlmInputImports } from '@presentation/shared-ui/input';
import { HlmNativeSelectImports } from '@presentation/shared-ui/native-select';
import { HlmSkeletonImports } from '@presentation/shared-ui/skeleton';
import { HlmTableImports } from '@presentation/shared-ui/table';

@Component({
  selector: 'app-primitive-gallery',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormField,
    TranslocoPipe,
    ...HlmAlertImports,
    ...HlmButtonImports,
    ...HlmCheckboxImports,
    ...HlmDialogImports,
    ...HlmEmptyImports,
    ...HlmInputImports,
    ...HlmNativeSelectImports,
    ...HlmSkeletonImports,
    ...HlmTableImports,
  ],
  templateUrl: './primitive-gallery.html',
  styleUrl: './primitive-gallery.css',
})
export class PrimitiveGallery {
  readonly formModel = signal({ name: '' });
  readonly exampleForm = form(this.formModel, (path) => {
    required(path.name, { message: 'validation.required' });
  });
  readonly toastVisible = signal(false);
}
