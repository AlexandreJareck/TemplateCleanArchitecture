import type { Routes } from '@angular/router';
import { authenticatedGuard, permissionGuard } from './security/guards';

export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () => import('./features/login.page').then((module) => module.LoginPage),
    title: 'Entrar — Procurement Hub',
  },
  {
    path: 'forbidden',
    loadComponent: () => import('./features/status.page').then((module) => module.StatusPage),
    data: { key: 'status.forbidden' },
    title: 'Acesso negado',
  },
  {
    path: 'unavailable',
    loadComponent: () => import('./features/status.page').then((module) => module.StatusPage),
    data: { key: 'status.unavailable' },
    title: 'Serviço indisponível',
  },
  {
    path: '',
    canActivate: [authenticatedGuard],
    loadComponent: () => import('./shell/app-shell').then((module) => module.AppShell),
    children: [
      {
        path: '',
        loadComponent: () =>
          import('./features/dashboard.page').then((module) => module.DashboardPage),
        title: 'Procurement Hub',
      },
      {
        path: 'organization/departments',
        canActivate: [permissionGuard],
        data: { permission: 'organization.read', kind: 'departments', titleKey: 'nav.departments' },
        loadComponent: () =>
          import('./features/master-data.page').then((module) => module.MasterDataPage),
        title: 'Departamentos',
      },
      {
        path: 'organization/cost-centers',
        canActivate: [permissionGuard],
        data: {
          permission: 'organization.read',
          kind: 'cost-centers',
          titleKey: 'nav.costCenters',
        },
        loadComponent: () =>
          import('./features/master-data.page').then((module) => module.MasterDataPage),
        title: 'Centros de custo',
      },
      {
        path: 'suppliers',
        canActivate: [permissionGuard],
        data: { permission: 'suppliers.read' },
        loadComponent: () =>
          import('./features/suppliers.page').then((module) => module.SuppliersPage),
        title: 'Fornecedores',
      },
      {
        path: 'catalog/categories',
        canActivate: [permissionGuard],
        data: { permission: 'catalog.read', kind: 'categories', titleKey: 'nav.categories' },
        loadComponent: () =>
          import('./features/master-data.page').then((module) => module.MasterDataPage),
        title: 'Categorias',
      },
      {
        path: 'catalog/units',
        canActivate: [permissionGuard],
        data: { permission: 'catalog.read', kind: 'units', titleKey: 'nav.units' },
        loadComponent: () =>
          import('./features/master-data.page').then((module) => module.MasterDataPage),
        title: 'Unidades de medida',
      },
      {
        path: 'catalog/items',
        canActivate: [permissionGuard],
        data: { permission: 'catalog.read', kind: 'catalog-items', titleKey: 'nav.items' },
        loadComponent: () =>
          import('./features/master-data.page').then((module) => module.MasterDataPage),
        title: 'Itens de catálogo',
      },
      {
        path: 'budget/periods',
        canActivate: [permissionGuard],
        data: { permission: 'budget.read' },
        loadComponent: () =>
          import('./features/budget-periods.page').then((module) => module.BudgetPeriodsPage),
        title: 'Períodos fiscais',
      },
      {
        path: 'budget/allocations',
        canActivate: [permissionGuard],
        data: { permission: 'budget.read' },
        loadComponent: () =>
          import('./features/budget-allocations.page').then(
            (module) => module.BudgetAllocationsPage,
          ),
        title: 'Alocações orçamentárias',
      },
      {
        path: 'budget/allocations/:id',
        canActivate: [permissionGuard],
        data: { permission: 'budget.read' },
        loadComponent: () =>
          import('./features/budget-allocation-detail.page').then(
            (module) => module.BudgetAllocationDetailPage,
          ),
        title: 'Alocação orçamentária',
      },
      {
        path: 'requisitions/mine',
        canActivate: [permissionGuard],
        data: { permission: 'requisitions.create', scope: 'mine' },
        loadComponent: () =>
          import('./features/requisitions.page').then((module) => module.RequisitionsPage),
        title: 'Minhas requisições',
      },
      {
        path: 'requisitions/team',
        canActivate: [permissionGuard],
        data: {
          anyPermission: ['requisitions.read-team', 'requisitions.read-all'],
          scope: 'visible',
        },
        loadComponent: () =>
          import('./features/requisitions.page').then((module) => module.RequisitionsPage),
        title: 'Requisições da equipe',
      },
      {
        path: 'requisitions/new',
        canActivate: [permissionGuard],
        data: { permission: 'requisitions.create' },
        loadComponent: () =>
          import('./features/requisition-editor.page').then(
            (module) => module.RequisitionEditorPage,
          ),
        title: 'Nova requisição',
      },
      {
        path: 'requisitions/:id/edit',
        canActivate: [permissionGuard],
        data: { permission: 'requisitions.create' },
        loadComponent: () =>
          import('./features/requisition-editor.page').then(
            (module) => module.RequisitionEditorPage,
          ),
        title: 'Editar requisição',
      },
      {
        path: 'requisitions/:id',
        canActivate: [permissionGuard],
        data: {
          anyPermission: ['requisitions.create', 'requisitions.read-team', 'requisitions.read-all'],
        },
        loadComponent: () =>
          import('./features/requisition-detail.page').then(
            (module) => module.RequisitionDetailPage,
          ),
        title: 'Requisição',
      },
      {
        path: 'reports',
        canActivate: [permissionGuard],
        data: { permission: 'reports.request' },
        loadComponent: () => import('./features/reports.page').then((module) => module.ReportsPage),
        title: 'Relatórios',
      },
      {
        path: 'sourcing',
        canActivate: [permissionGuard],
        data: { anyPermission: ['sourcing.rfqs.manage', 'sourcing.quotes.read'] },
        loadComponent: () =>
          import('./features/sourcing.page').then((module) => module.SourcingPage),
        title: 'Cotações',
      },
      {
        path: 'sourcing/:id',
        canActivate: [permissionGuard],
        data: { anyPermission: ['sourcing.rfqs.manage', 'sourcing.quotes.read'] },
        loadComponent: () =>
          import('./features/rfq-detail.page').then((module) => module.RfqDetailPage),
        title: 'Solicitação de cotação',
      },
      {
        path: 'approvals/inbox',
        canActivate: [permissionGuard],
        data: { permission: 'approvals.decide' },
        loadComponent: () =>
          import('./features/approvals-inbox.page').then((module) => module.ApprovalsInboxPage),
        title: 'Aprovações',
      },
      {
        path: 'approvals/:id',
        loadComponent: () =>
          import('./features/approval-detail.page').then((module) => module.ApprovalDetailPage),
        title: 'Aprovação',
      },
      {
        path: 'dev/primitives',
        loadComponent: () =>
          import('./shell/primitive-gallery').then((module) => module.PrimitiveGallery),
        title: 'Primitive gallery',
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
