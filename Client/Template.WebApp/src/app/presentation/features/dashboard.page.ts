import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideArrowRight,
  lucideBuilding2,
  lucideFilePlus,
  lucideFileText,
  lucidePackage,
  lucidePiggyBank,
  lucideTruck,
} from '@ng-icons/lucide';
import { BrandLogo } from '../shared/brand-logo';
import { SessionFacade } from '../state/session.facade';

interface DashboardCard {
  readonly labelKey: string;
  readonly descriptionKey: string;
  readonly link: string;
  readonly icon: string;
  readonly tone: 'indigo' | 'cyan' | 'emerald' | 'amber';
  readonly permission: string;
}

const CARDS: readonly DashboardCard[] = [
  {
    labelKey: 'requisition.new',
    descriptionKey: 'dashboard.cards.newRequisition',
    link: '/requisitions/new',
    icon: 'lucideFilePlus',
    tone: 'indigo',
    permission: 'requisitions.create',
  },
  {
    labelKey: 'nav.myRequisitions',
    descriptionKey: 'dashboard.cards.myRequisitions',
    link: '/requisitions/mine',
    icon: 'lucideFileText',
    tone: 'cyan',
    permission: 'requisitions.create',
  },
  {
    labelKey: 'nav.budgetAllocations',
    descriptionKey: 'dashboard.cards.budgetAllocations',
    link: '/budget/allocations',
    icon: 'lucidePiggyBank',
    tone: 'emerald',
    permission: 'budget.read',
  },
  {
    labelKey: 'nav.suppliers',
    descriptionKey: 'dashboard.cards.suppliers',
    link: '/suppliers',
    icon: 'lucideTruck',
    tone: 'amber',
    permission: 'suppliers.read',
  },
  {
    labelKey: 'nav.items',
    descriptionKey: 'dashboard.cards.items',
    link: '/catalog/items',
    icon: 'lucidePackage',
    tone: 'cyan',
    permission: 'catalog.read',
  },
  {
    labelKey: 'nav.departments',
    descriptionKey: 'dashboard.cards.departments',
    link: '/organization/departments',
    icon: 'lucideBuilding2',
    tone: 'indigo',
    permission: 'organization.read',
  },
];

@Component({
  selector: 'app-dashboard-page',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, TranslocoPipe, NgIcon, BrandLogo],
  providers: [
    provideIcons({
      lucideArrowRight,
      lucideBuilding2,
      lucideFilePlus,
      lucideFileText,
      lucidePackage,
      lucidePiggyBank,
      lucideTruck,
    }),
  ],
  templateUrl: './dashboard.page.html',
  styles: [
    `
      .hero {
        position: relative;
        overflow: hidden;
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: var(--space-6);
        margin-bottom: var(--space-8);
        padding: var(--space-8);
        border-radius: var(--radius-xl);
        color: var(--on-brand);
        background:
          radial-gradient(circle at 85% 20%, var(--brand-glow), transparent 45%),
          linear-gradient(135deg, var(--brand-hero-from), var(--brand-gradient-to));
        box-shadow: var(--shadow-lg);
      }
      .hero-copy {
        display: grid;
        gap: var(--space-2);
        max-width: 40rem;
      }
      .hero h1 {
        margin: 0;
        font-size: 2.25rem;
        font-weight: 800;
        letter-spacing: -0.02em;
      }
      .hero p {
        margin: 0;
        opacity: 0.92;
      }
      .eyebrow {
        font-size: 0.75rem;
        font-weight: 700;
        letter-spacing: 0.08em;
        text-transform: uppercase;
      }
      .hero-mark {
        opacity: 0.9;
      }
      .section-title {
        margin: 0 0 var(--space-4);
        font-size: 1rem;
        font-weight: 700;
        color: var(--muted-foreground);
      }
      .cards {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(15rem, 1fr));
        gap: var(--space-4);
      }
      .card {
        position: relative;
        display: grid;
        gap: var(--space-2);
        padding: var(--space-6);
        border: 1px solid var(--border);
        border-radius: var(--radius-lg);
        background: var(--card);
        color: var(--foreground);
        text-decoration: none;
        box-shadow: var(--shadow-sm);
        transition:
          transform var(--transition-fast),
          box-shadow var(--transition-fast),
          border-color var(--transition-fast);
      }
      .card:hover {
        transform: translateY(-3px);
        border-color: var(--ring);
        box-shadow: var(--shadow-md);
      }
      .card-icon {
        display: inline-grid;
        place-items: center;
        width: 2.75rem;
        height: 2.75rem;
        margin-bottom: var(--space-2);
        border-radius: var(--radius-md);
        font-size: 1.375rem;
        background: var(--info-soft);
        color: var(--info);
      }
      [data-tone='indigo'] .card-icon {
        background: var(--accent);
        color: var(--accent-foreground);
      }
      [data-tone='emerald'] .card-icon {
        background: var(--success-soft);
        color: var(--success);
      }
      [data-tone='amber'] .card-icon {
        background: var(--warning-soft);
        color: var(--warning);
      }
      .card-title {
        font-weight: 700;
      }
      .card-description {
        font-size: 0.875rem;
        color: var(--muted-foreground);
      }
      .card-arrow {
        position: absolute;
        top: var(--space-6);
        right: var(--space-6);
        color: var(--muted-foreground);
        transition: transform var(--transition-fast);
      }
      .card:hover .card-arrow {
        transform: translateX(3px);
        color: var(--primary);
      }
      @media (max-width: 48rem) {
        .hero {
          padding: var(--space-6);
        }
        .hero-mark {
          display: none;
        }
      }
    `,
  ],
})
export class DashboardPage {
  protected readonly session = inject(SessionFacade);
  protected readonly cards = computed(() => {
    this.session.user();
    return CARDS.filter((card) => this.session.can(card.permission));
  });
}
