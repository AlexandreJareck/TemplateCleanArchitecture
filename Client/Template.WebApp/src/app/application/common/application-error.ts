export type ApplicationError =
  | ErrorDetail<'cancelled'>
  | ErrorDetail<'not-authenticated' | 'forbidden' | 'not-found' | 'validation'>
  | ErrorDetail<'conflict' | 'version-mismatch' | 'business-rule'>
  | ErrorDetail<'rate-limited' | 'unavailable' | 'unexpected'>;

interface ErrorDetail<TKind extends string> {
  readonly kind: TKind;
  readonly code: string;
  readonly detail?: string;
  readonly traceId?: string;
  readonly ruleId?: string;
  readonly currentVersion?: string;
  readonly currentState?: string;
  readonly retryAfterSeconds?: number;
  readonly fieldErrors?: Readonly<Record<string, readonly string[]>>;
}
