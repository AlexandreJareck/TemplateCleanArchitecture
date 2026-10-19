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
        path: 'dev/primitives',
        loadComponent: () =>
          import('./shell/primitive-gallery').then((module) => module.PrimitiveGallery),
        title: 'Primitive gallery',
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
