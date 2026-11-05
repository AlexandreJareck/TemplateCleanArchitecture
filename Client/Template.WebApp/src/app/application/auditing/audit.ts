import type { Result } from '../common/result';
import type { Page } from '../master-data/master-data';

/** One append-only audit record (AUD-001/002); `details` holds the server's allow-listed fields only (AUD-003). */
export interface AuditRecord {
  readonly id: string;
  readonly occurredAt: string;
  readonly actorId?: string;
  readonly action: string;
  readonly resourceType: string;
  readonly resourceId?: string;
  readonly fromState?: string;
  readonly toState?: string;
  readonly ruleIds: readonly string[];
  readonly reason?: string;
  readonly correlationId?: string;
  readonly details: readonly (readonly [string, string])[];
}

/** Filters of the global audit query; instants are UTC, `to` is exclusive. */
export interface AuditQuery {
  readonly page: number;
  readonly pageSize: number;
  readonly sort?: string;
  readonly action?: string;
  readonly actorId?: string;
  readonly resourceType?: string;
  readonly resourceId?: string;
  readonly from?: string;
  readonly to?: string;
}

/** Read-only access to the global audit trail (`audit.read`, AUD-004); there is no write operation (SOD-8). */
export interface AuditPort {
  list(query: AuditQuery, signal?: AbortSignal): Promise<Result<Page<AuditRecord>>>;
}

export type AuditFilterProblem = 'actorFormat' | 'range';

const GUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/** Client-side filter checks mirroring the API's syntactic validation; the API stays authoritative. */
export class AuditUseCases {
  static validate(filters: {
    readonly actorId?: string;
    readonly fromDate?: string;
    readonly toDate?: string;
  }): Partial<Record<'actorId' | 'toDate', AuditFilterProblem>> {
    const problems: Partial<Record<'actorId' | 'toDate', AuditFilterProblem>> = {};
    const actor = filters.actorId?.trim();
    if (actor && !GUID.test(actor)) problems.actorId = 'actorFormat';
    if (filters.fromDate && filters.toDate && filters.toDate < filters.fromDate)
      problems.toDate = 'range';
    return problems;
  }

  /** Trims text filters and drops empty ones so the query string carries only real filters. */
  static compact(query: AuditQuery): AuditQuery {
    const entries = Object.entries(query).map(([key, value]) => [
      key,
      typeof value === 'string' ? value.trim() : value,
    ]);
    return Object.fromEntries(
      entries.filter(([, value]) => value !== undefined && value !== ''),
    ) as unknown as AuditQuery;
  }
}
