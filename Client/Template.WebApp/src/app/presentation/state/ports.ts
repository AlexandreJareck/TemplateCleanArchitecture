import { InjectionToken } from '@angular/core';
import type { ApprovalPort } from '@application/approvals/approvals';
import type { MasterDataPort } from '@application/master-data/master-data';
import type { BudgetPort, RequisitionPort } from '@application/procurement/procurement';
import type { PurchasingPort } from '@application/purchasing/purchasing';
import type { ReportPort } from '@application/reports/reports';
import type { SessionPort } from '@application/session/session';
import type { SourcingPort } from '@application/sourcing/sourcing';

export const SESSION_PORT = new InjectionToken<SessionPort>('SESSION_PORT');
export const MASTER_DATA_PORT = new InjectionToken<MasterDataPort>('MASTER_DATA_PORT');
export const REQUISITION_PORT = new InjectionToken<RequisitionPort>('REQUISITION_PORT');
export const BUDGET_PORT = new InjectionToken<BudgetPort>('BUDGET_PORT');
export const SOURCING_PORT = new InjectionToken<SourcingPort>('SOURCING_PORT');
export const REPORT_PORT = new InjectionToken<ReportPort>('REPORT_PORT');
export const APPROVAL_PORT = new InjectionToken<ApprovalPort>('APPROVAL_PORT');
export const PURCHASING_PORT = new InjectionToken<PurchasingPort>('PURCHASING_PORT');
