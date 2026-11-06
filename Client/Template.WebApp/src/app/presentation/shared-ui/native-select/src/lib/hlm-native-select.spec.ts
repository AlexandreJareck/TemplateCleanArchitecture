import { ChangeDetectionStrategy, Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { form, FormField } from '@angular/forms/signals';
import { HlmNativeSelect } from './hlm-native-select';
import { HlmNativeSelectOption } from './hlm-native-select-option';

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormField, HlmNativeSelect, HlmNativeSelectOption],
  template: `
    <hlm-native-select [formField]="pickForm.id">
      <option hlmNativeSelectOption value="">—</option>
      @for (option of options(); track option) {
        <option hlmNativeSelectOption [value]="option">{{ option }}</option>
      }
    </hlm-native-select>
  `,
})
class SelectHost {
  readonly options = signal<readonly string[]>([]);
  readonly model = signal({ id: '' });
  readonly pickForm = form(this.model);
}

describe('HlmNativeSelect (local change: value re-sync)', () => {
  async function render() {
    const fixture = TestBed.createComponent(SelectHost);
    await fixture.whenStable();
    const select = () => (fixture.nativeElement as HTMLElement).querySelector('select')!;
    return { fixture, host: fixture.componentInstance, select };
  }

  it('shows a value written before its options exist once the options arrive', async () => {
    const { fixture, host, select } = await render();
    host.model.set({ id: 'b' });
    await fixture.whenStable();
    host.options.set(['a', 'b']);
    await fixture.whenStable();
    expect(select().value).toBe('b');
    expect(host.model().id).toBe('b');
  });

  it('keeps the selection when the option list is replaced', async () => {
    const { fixture, host, select } = await render();
    host.options.set(['a', 'b']);
    host.model.set({ id: 'b' });
    await fixture.whenStable();
    host.options.set(['b', 'c']);
    await fixture.whenStable();
    expect(select().value).toBe('b');
  });
});
