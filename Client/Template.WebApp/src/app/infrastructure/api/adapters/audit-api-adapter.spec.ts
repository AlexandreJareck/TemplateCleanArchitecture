import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { describe, expect, it, vi } from 'vitest';
import { AuditRecordsService } from '../generated/audit-records/audit-records.service';
import { OrganizationSettingsService } from '../generated/organization-settings/organization-settings.service';
import { UsersService } from '../generated/users/users.service';
import { AuditApiAdapter } from './audit-api-adapter';
import { OrganizationApiAdapter } from './organization-api-adapter';

describe('AuditApiAdapter', () => {
  it('sends the filters and maps records with sorted allow-listed details', async () => {
    const getApiV2AuditRecords = vi.fn(() =>
      of({
        items: [
          {
            id: 'a1',
            occurredAt: '2026-11-02T16:55:03Z',
            actorId: 'u1',
            actorName: 'Synthetic Auditee',
            action: 'requisition.submitted',
            resourceType: 'PurchaseRequisition',
            resourceId: 'r1',
            toState: 'Submitted',
            ruleIds: ['REQ-004'],
            details: { revision: '1', number: 'REQ-2026-000001' },
          },
        ],
        page: 1,
        pageSize: 20,
        totalItems: 1,
        totalPages: 1,
      }),
    );
    TestBed.configureTestingModule({
      providers: [{ provide: AuditRecordsService, useValue: { getApiV2AuditRecords } }],
    });
    const result = await TestBed.inject(AuditApiAdapter).list({
      page: 2,
      pageSize: 20,
      action: 'requisition.submitted',
      from: '2026-11-01T03:00:00.000Z',
    });
    expect(getApiV2AuditRecords).toHaveBeenCalledWith(
      expect.objectContaining({
        page: 2,
        action: 'requisition.submitted',
        from: '2026-11-01T03:00:00.000Z',
      }),
    );
    expect(result.ok && result.value.items[0]).toMatchObject({
      actorId: 'u1',
      actorName: 'Synthetic Auditee',
      action: 'requisition.submitted',
      ruleIds: ['REQ-004'],
      details: [
        ['number', 'REQ-2026-000001'],
        ['revision', '1'],
      ],
    });
  });

  it('maps a 403 to a forbidden application error', async () => {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: AuditRecordsService,
          useValue: {
            getApiV2AuditRecords: () =>
              throwError(
                () => new HttpErrorResponse({ status: 403, error: { code: 'forbidden' } }),
              ),
          },
        },
      ],
    });
    const result = await TestBed.inject(AuditApiAdapter).list({ page: 1, pageSize: 20 });
    expect(!result.ok && result.error.kind).toBe('forbidden');
  });
});

describe('OrganizationApiAdapter', () => {
  it('maps settings and directory users', async () => {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: OrganizationSettingsService,
          useValue: {
            getApiV2OrganizationSettings: () =>
              of({
                baseCurrency: 'BRL',
                timeZone: 'America/Sao_Paulo',
                supportedCurrencies: ['BRL', 'USD'],
              }),
          },
        },
        {
          provide: UsersService,
          useValue: {
            getApiV2Users: () =>
              of({
                items: [
                  { userId: 'u1', userName: 'demo.admin', displayName: null, isActive: true },
                ],
              }),
          },
        },
      ],
    });
    const adapter = TestBed.inject(OrganizationApiAdapter);
    const settings = await adapter.get();
    expect(settings.ok && settings.value).toEqual({
      displayName: '',
      baseCurrency: 'BRL',
      timeZone: 'America/Sao_Paulo',
      supportedCurrencies: ['BRL', 'USD'],
    });
    const users = await adapter.list({ page: 1, pageSize: 100 });
    expect(users.ok && users.value[0]).toEqual({
      id: 'u1',
      userName: 'demo.admin',
      displayName: 'demo.admin',
      active: true,
    });
  });
});
