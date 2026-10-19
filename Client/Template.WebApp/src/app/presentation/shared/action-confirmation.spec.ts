import { TestBed } from '@angular/core/testing';
import { provideTransloco, type Translation, type TranslocoLoader } from '@jsverse/transloco';
import { of } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { ActionConfirmation } from './action-confirmation';

class Loader implements TranslocoLoader {
  getTranslation() {
    return of<Translation>({ common: { reason: 'Reason', cancel: 'Cancel' } });
  }
}

describe('ActionConfirmation', () => {
  it('focuses the reason and emits only a non-empty confirmation', async () => {
    await TestBed.configureTestingModule({
      imports: [ActionConfirmation],
      providers: [
        provideTransloco({ config: { availableLangs: ['en'], defaultLang: 'en' }, loader: Loader }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(ActionConfirmation);
    const confirmed = vi.fn();
    fixture.componentInstance.confirmed.subscribe(confirmed);
    fixture.componentRef.setInput('request', { title: 'Confirm', actionLabel: 'Continue' });
    fixture.detectChanges();
    await fixture.whenStable();
    const input = (fixture.nativeElement as HTMLElement).querySelector<HTMLInputElement>('input');
    expect(document.activeElement).toBe(input);
    input!.value = 'Because it is obsolete';
    input!.dispatchEvent(new Event('input'));
    fixture.detectChanges();
    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLButtonElement>('button:last-child')!
      .click();
    expect(confirmed).toHaveBeenCalledWith('Because it is obsolete');
  });
});
