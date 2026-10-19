import type { ApplicationError } from './application-error';

export type Result<T> =
  | { readonly ok: true; readonly value: T }
  | { readonly ok: false; readonly error: ApplicationError };

export const success = <T>(value: T): Result<T> => ({ ok: true, value });
export const failure = <T = never>(error: ApplicationError): Result<T> => ({ ok: false, error });
