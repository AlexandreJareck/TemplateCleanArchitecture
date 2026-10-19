import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { describe, expect, it } from 'vitest';
import { CategoriesService } from '../generated/categories/categories.service';
import { CatalogItemsService } from '../generated/catalog-items/catalog-items.service';
import { CostCentersService } from '../generated/cost-centers/cost-centers.service';
import { DepartmentsService } from '../generated/departments/departments.service';
import { EnrichmentService } from '../generated/enrichment/enrichment.service';
import { SuppliersService } from '../generated/suppliers/suppliers.service';
import { UnitsOfMeasureService } from '../generated/units-of-measure/units-of-measure.service';
import { MasterDataApiAdapter } from './master-data-api-adapter';

function adapter(departments: object) {
  TestBed.configureTestingModule({
    providers: [
      MasterDataApiAdapter,
      { provide: DepartmentsService, useValue: departments },
      { provide: CostCentersService, useValue: {} },
      { provide: CategoriesService, useValue: {} },
      { provide: UnitsOfMeasureService, useValue: {} },
      { provide: CatalogItemsService, useValue: {} },
      { provide: SuppliersService, useValue: {} },
      { provide: EnrichmentService, useValue: {} },
    ],
  });
  return TestBed.inject(MasterDataApiAdapter);
}

describe('MasterDataApiAdapter', () => {
  it('maps generated paging DTOs into Application models', async () => {
    const result = await adapter({
      getApiV2Departments: () =>
        of({
          items: [
            {
              id: 'd1',
              code: 'FIN',
              name: 'Finance',
              isActive: true,
              version: '5',
              headUserId: 'u1',
            },
          ],
          page: 2,
          pageSize: 20,
          totalItems: 21,
          totalPages: 2,
        }),
    }).list('departments', { page: 2, pageSize: 20 });
    expect(result).toMatchObject({
      ok: true,
      value: { page: 2, items: [{ id: 'd1', ownerUserId: 'u1', version: '5' }] },
    });
  });

  it('sends If-Match and maps a stale version response', async () => {
    let header = '';
    const result = await adapter({
      putApiV2DepartmentsId: (
        _id: string,
        _body: unknown,
        options: { headers: Record<string, string> },
      ) => {
        header = options.headers['If-Match'];
        return throwError(
          () =>
            new HttpErrorResponse({
              status: 412,
              error: { code: 'concurrency.version_mismatch', version: '8' },
            }),
        );
      },
    }).update(
      'departments',
      { id: 'd1', code: 'FIN', name: 'Old', active: true, version: '7' },
      { code: 'FIN', name: 'New' },
    );
    expect(header).toBe('"7"');
    expect(result).toMatchObject({
      ok: false,
      error: { kind: 'version-mismatch', currentVersion: '8' },
    });
  });
});
