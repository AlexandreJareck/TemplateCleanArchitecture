import { InjectionToken } from '@angular/core';
import type { DepartmentReader } from '@application/ports/department-reader';
import type { RuntimeConfig } from '@infrastructure/runtime/runtime-config';

export const RUNTIME_CONFIG = new InjectionToken<RuntimeConfig>('RUNTIME_CONFIG');
export const DEPARTMENT_READER = new InjectionToken<DepartmentReader>('DEPARTMENT_READER');
