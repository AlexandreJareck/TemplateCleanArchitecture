import { Injectable } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class AccessTokenStore {
  #token: string | undefined;
  get token() {
    return this.#token;
  }
  set(token: string | undefined) {
    this.#token = token;
  }
  clear() {
    this.#token = undefined;
  }
}
