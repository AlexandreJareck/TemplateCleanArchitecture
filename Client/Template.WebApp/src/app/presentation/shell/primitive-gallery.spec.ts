import { TestBed } from '@angular/core/testing';
import { provideTransloco, type Translation, type TranslocoLoader } from '@jsverse/transloco';
import { of } from 'rxjs';
import { PrimitiveGallery } from './primitive-gallery';

class TestTranslationLoader implements TranslocoLoader {
  getTranslation() {
    return of<Translation>({
      gallery: { title: 'Frontend foundation' },
    });
  }
}

describe('PrimitiveGallery', () => {
  it('renders the internal gallery', async () => {
    await TestBed.configureTestingModule({
      imports: [PrimitiveGallery],
      providers: [
        provideTransloco({
          config: { availableLangs: ['en'], defaultLang: 'en' },
          loader: TestTranslationLoader,
        }),
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(PrimitiveGallery);
    await fixture.whenStable();
    expect((fixture.nativeElement as HTMLElement).querySelector('h1')?.textContent).toContain(
      'Frontend foundation',
    );
  });
});
