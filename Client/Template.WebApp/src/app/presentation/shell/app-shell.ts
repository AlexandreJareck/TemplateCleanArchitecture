import { DOCUMENT } from '@angular/common';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  DestroyRef,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { TranslocoPipe } from '@jsverse/transloco';
import { NgIcon, provideIcons } from '@ng-icons/core';
import {
  lucideLogOut,
  lucideMenu,
  lucideMonitor,
  lucideMoon,
  lucideSun,
  lucideX,
} from '@ng-icons/lucide';
import { HlmButton } from '@presentation/shared-ui/button';
import { filter } from 'rxjs';
import { BrandLogo } from '../shared/brand-logo';
import { SessionFacade } from '../state/session.facade';
import { ThemeFacade } from '../state/theme.facade';
import { NAV_ICONS, NAVIGATION } from './navigation';

@Component({
  selector: 'app-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    TranslocoPipe,
    HlmButton,
    NgIcon,
    BrandLogo,
  ],
  providers: [
    provideIcons({
      ...NAV_ICONS,
      lucideLogOut,
      lucideMenu,
      lucideMonitor,
      lucideMoon,
      lucideSun,
      lucideX,
    }),
  ],
  templateUrl: './app-shell.html',
  styleUrl: './app-shell.css',
})
export class AppShell {
  protected readonly session = inject(SessionFacade);
  protected readonly theme = inject(ThemeFacade);
  private readonly router = inject(Router);
  private readonly document = inject(DOCUMENT);
  private readonly destroyRef = inject(DestroyRef);
  readonly open = signal(false);
  protected readonly groups = computed(() => {
    this.session.user();
    return NAVIGATION.map((group) => ({
      ...group,
      items: group.items.filter(
        (item) => item.anyOf.length === 0 || item.anyOf.some((p) => this.session.can(p)),
      ),
    })).filter((group) => group.items.length > 0);
  });
  protected readonly initials = computed(() =>
    (this.session.user()?.displayName ?? '')
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0]!.toUpperCase())
      .join(''),
  );
  protected readonly themeIcon = computed(
    () =>
      ({ light: 'lucideSun', dark: 'lucideMoon', system: 'lucideMonitor' })[
        this.theme.preference()
      ],
  );
  constructor() {
    this.router.events
      .pipe(
        filter((event): event is NavigationEnd => event instanceof NavigationEnd),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(() => {
        this.open.set(false);
        setTimeout(() => this.document.getElementById('main-content')?.focus());
      });
  }
  async logout(all = false) {
    await this.session.logout(all);
    await this.router.navigateByUrl('/login');
  }
}
