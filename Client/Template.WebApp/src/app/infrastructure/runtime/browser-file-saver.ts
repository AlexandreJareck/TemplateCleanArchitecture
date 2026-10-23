import { DOCUMENT } from '@angular/common';
import { inject, Injectable } from '@angular/core';

/**
 * Browser download mechanism: an in-memory object URL for the fetched Blob, clicked through a temporary anchor with
 * the server's sanitized file name and revoked immediately after. No storage URL, token or file content is persisted.
 */
@Injectable({ providedIn: 'root' })
export class BrowserFileSaver {
  private readonly document = inject(DOCUMENT);

  save(content: Blob, fileName: string) {
    const view = this.document.defaultView;
    if (!view) return;
    const url = view.URL.createObjectURL(content);
    try {
      const anchor = this.document.createElement('a');
      anchor.href = url;
      anchor.download = fileName;
      anchor.rel = 'noopener';
      anchor.style.display = 'none';
      this.document.body.appendChild(anchor);
      anchor.click();
      anchor.remove();
    } finally {
      view.setTimeout(() => view.URL.revokeObjectURL(url), 0);
    }
  }
}
