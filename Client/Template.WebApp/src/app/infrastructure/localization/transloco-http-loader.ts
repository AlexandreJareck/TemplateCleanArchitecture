import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import type { Translation, TranslocoLoader } from '@jsverse/transloco';

@Injectable({ providedIn: 'root' })
export class TranslocoHttpLoader implements TranslocoLoader {
  private readonly http = inject(HttpClient);

  getTranslation(language: string) {
    return this.http.get<Translation>(`/i18n/${language}.json`, {
      headers: {
        'Cache-Control': 'no-cache',
        Pragma: 'no-cache',
      },
    });
  }
}
