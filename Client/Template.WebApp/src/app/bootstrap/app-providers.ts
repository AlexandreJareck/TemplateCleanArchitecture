import { provideHttpClient, withInterceptors, withXsrfConfiguration } from '@angular/common/http';
import { type ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { provideTransloco } from '@jsverse/transloco';
import { DepartmentsService } from '@infrastructure/api/generated/departments/departments.service';
import { DepartmentApiReader } from '@infrastructure/api/adapters/department-api-reader';
import { MasterDataApiAdapter } from '@infrastructure/api/adapters/master-data-api-adapter';
import { ProcurementApiAdapter } from '@infrastructure/api/adapters/procurement-api-adapter';
import { ReportApiAdapter } from '@infrastructure/api/adapters/report-api-adapter';
import { SourcingApiAdapter } from '@infrastructure/api/adapters/sourcing-api-adapter';
import { TranslocoHttpLoader } from '@infrastructure/localization/transloco-http-loader';
import type { RuntimeConfig } from '@infrastructure/runtime/runtime-config';
import { routes } from '@presentation/routes';
import {
  BUDGET_PORT,
  MASTER_DATA_PORT,
  REPORT_PORT,
  REQUISITION_PORT,
  SESSION_PORT,
  SOURCING_PORT,
} from '@presentation/state/ports';
import { apiBaseUrlInterceptor } from './api-base-url.interceptor';
import { authInterceptor } from '@infrastructure/session/auth.interceptor';
import { SessionAdapter } from '@infrastructure/session/session-adapter';
import { DEPARTMENT_READER, RUNTIME_CONFIG } from './tokens';

export function createAppConfig(runtimeConfig: RuntimeConfig): ApplicationConfig {
  return {
    providers: [
      provideBrowserGlobalErrorListeners(),
      { provide: RUNTIME_CONFIG, useValue: runtimeConfig },
      provideHttpClient(
        withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' }),
        withInterceptors([apiBaseUrlInterceptor, authInterceptor]),
      ),
      provideRouter(routes),
      provideTransloco({
        config: {
          availableLangs: ['pt-BR', 'en'],
          defaultLang: 'pt-BR',
          fallbackLang: 'en',
          reRenderOnLangChange: true,
          missingHandler: { useFallbackTranslation: false },
        },
        loader: TranslocoHttpLoader,
      }),
      {
        provide: DEPARTMENT_READER,
        deps: [DepartmentsService],
        useFactory: (service: DepartmentsService) => new DepartmentApiReader(service),
      },
      { provide: SESSION_PORT, useExisting: SessionAdapter },
      { provide: MASTER_DATA_PORT, useExisting: MasterDataApiAdapter },
      { provide: REQUISITION_PORT, useExisting: ProcurementApiAdapter },
      { provide: BUDGET_PORT, useExisting: ProcurementApiAdapter },
      { provide: SOURCING_PORT, useExisting: SourcingApiAdapter },
      { provide: REPORT_PORT, useExisting: ReportApiAdapter },
    ],
  };
}
