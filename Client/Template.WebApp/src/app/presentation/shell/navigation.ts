import {
  lucideBuilding2,
  lucideCalendarRange,
  lucideChartColumn,
  lucideCircleCheck,
  lucideFileText,
  lucideLayoutDashboard,
  lucidePackage,
  lucidePackageCheck,
  lucidePiggyBank,
  lucideRuler,
  lucideScale,
  lucideShoppingCart,
  lucideTags,
  lucideTruck,
  lucideUsers,
  lucideWallet,
} from '@ng-icons/lucide';

export interface NavItem {
  readonly labelKey: string;
  readonly link: string;
  readonly icon: string;
  /** Visible when the user holds any of these permissions; empty means always visible. */
  readonly anyOf: readonly string[];
  readonly exact?: boolean;
}

export interface NavGroup {
  readonly labelKey: string;
  readonly items: readonly NavItem[];
}

export const NAV_ICONS = {
  lucideBuilding2,
  lucideCalendarRange,
  lucideChartColumn,
  lucideCircleCheck,
  lucideFileText,
  lucideLayoutDashboard,
  lucidePackage,
  lucidePackageCheck,
  lucidePiggyBank,
  lucideRuler,
  lucideScale,
  lucideShoppingCart,
  lucideTags,
  lucideTruck,
  lucideUsers,
  lucideWallet,
};

export const NAVIGATION: readonly NavGroup[] = [
  {
    labelKey: 'navGroups.overview',
    items: [
      {
        labelKey: 'nav.dashboard',
        link: '/',
        icon: 'lucideLayoutDashboard',
        anyOf: [],
        exact: true,
      },
    ],
  },
  {
    labelKey: 'navGroups.procurement',
    items: [
      {
        labelKey: 'nav.myRequisitions',
        link: '/requisitions/mine',
        icon: 'lucideFileText',
        anyOf: ['requisitions.create'],
      },
      {
        labelKey: 'nav.teamRequisitions',
        link: '/requisitions/team',
        icon: 'lucideUsers',
        anyOf: ['requisitions.read-team', 'requisitions.read-all'],
      },
      {
        labelKey: 'nav.sourcing',
        link: '/sourcing',
        icon: 'lucideScale',
        anyOf: ['sourcing.rfqs.manage', 'sourcing.quotes.read'],
      },
      {
        labelKey: 'nav.approvals',
        link: '/approvals/inbox',
        icon: 'lucideCircleCheck',
        anyOf: ['approvals.decide'],
      },
      {
        labelKey: 'nav.purchaseOrders',
        link: '/purchasing/orders',
        icon: 'lucideShoppingCart',
        anyOf: ['purchasing.orders.read'],
      },
      {
        labelKey: 'nav.receiving',
        link: '/receiving/queue',
        icon: 'lucidePackageCheck',
        anyOf: ['receiving.receipts.create'],
      },
    ],
  },
  {
    labelKey: 'navGroups.budget',
    items: [
      {
        labelKey: 'nav.budgetPeriods',
        link: '/budget/periods',
        icon: 'lucideCalendarRange',
        anyOf: ['budget.read'],
      },
      {
        labelKey: 'nav.budgetAllocations',
        link: '/budget/allocations',
        icon: 'lucidePiggyBank',
        anyOf: ['budget.read'],
      },
    ],
  },
  {
    labelKey: 'navGroups.masterData',
    items: [
      {
        labelKey: 'nav.departments',
        link: '/organization/departments',
        icon: 'lucideBuilding2',
        anyOf: ['organization.read'],
      },
      {
        labelKey: 'nav.costCenters',
        link: '/organization/cost-centers',
        icon: 'lucideWallet',
        anyOf: ['organization.read'],
      },
      {
        labelKey: 'nav.suppliers',
        link: '/suppliers',
        icon: 'lucideTruck',
        anyOf: ['suppliers.read'],
      },
      {
        labelKey: 'nav.categories',
        link: '/catalog/categories',
        icon: 'lucideTags',
        anyOf: ['catalog.read'],
      },
      {
        labelKey: 'nav.units',
        link: '/catalog/units',
        icon: 'lucideRuler',
        anyOf: ['catalog.read'],
      },
      {
        labelKey: 'nav.items',
        link: '/catalog/items',
        icon: 'lucidePackage',
        anyOf: ['catalog.read'],
      },
    ],
  },
  {
    labelKey: 'navGroups.insights',
    items: [
      {
        labelKey: 'nav.reports',
        link: '/reports',
        icon: 'lucideChartColumn',
        anyOf: ['reports.request'],
      },
    ],
  },
];
