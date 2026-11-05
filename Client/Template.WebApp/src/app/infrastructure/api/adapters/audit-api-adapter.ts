import { inject, Injectable } from '@angular/core';
import type { AuditPort, AuditQuery, AuditRecord } from '@application/auditing/audit';
import { failure, success, type Result } from '@application/common/result';
import type { Page } from '@application/master-data/master-data';
import { firstValueFrom, fromEvent, takeUntil } from 'rxjs';
import { AuditRecordsService } from '../generated/audit-records/audit-records.service';
import type { AuditRecordView } from '../generated/models/auditRecordView';
import { mapProblem } from '../../http/problem-mapper';
import { mapPage } from './procurement-api-adapter';

/** Generated-client adapter of the read-only global audit query (`GET /api/v2/audit-records`, AUD-004). */
@Injectable({ providedIn: 'root' })
export class AuditApiAdapter implements AuditPort {
  private readonly records = inject(AuditRecordsService);

  async list(query: AuditQuery, signal?: AbortSignal): Promise<Result<Page<AuditRecord>>> {
    try {
      const source = this.records.getApiV2AuditRecords({
        page: query.page,
        pageSize: query.pageSize,
        sort: query.sort,
        action: query.action,
        actorId: query.actorId,
        resourceType: query.resourceType,
        resourceId: query.resourceId,
        from: query.from,
        to: query.to,
      });
      const bounded = signal ? source.pipe(takeUntil(fromEvent(signal, 'abort'))) : source;
      return success(mapPage(await firstValueFrom(bounded), mapAuditRecord));
    } catch (error) {
      return failure(mapProblem(error, signal));
    }
  }
}

export function mapAuditRecord(value: AuditRecordView): AuditRecord {
  return {
    id: value.id ?? '',
    occurredAt: value.occurredAt ?? '',
    actorId: value.actorId ?? undefined,
    action: value.action ?? '',
    resourceType: value.resourceType ?? '',
    resourceId: value.resourceId ?? undefined,
    fromState: value.fromState ?? undefined,
    toState: value.toState ?? undefined,
    ruleIds: value.ruleIds ?? [],
    reason: value.reason ?? undefined,
    correlationId: value.correlationId ?? undefined,
    details: Object.entries(value.details ?? {}).sort(([a], [b]) => a.localeCompare(b)),
  };
}
