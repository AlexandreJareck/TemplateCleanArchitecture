import { Directive, effect, inject, input, TemplateRef, ViewContainerRef } from '@angular/core';
import { SessionFacade } from '../state/session.facade';

@Directive({ selector: '[appPermission]' })
export class PermissionDirective {
  private readonly template = inject(TemplateRef<unknown>);
  private readonly container = inject(ViewContainerRef);
  private readonly session = inject(SessionFacade);
  readonly permission = input.required<string>({ alias: 'appPermission' });
  constructor() {
    effect(() => {
      this.container.clear();
      if (this.session.can(this.permission())) this.container.createEmbeddedView(this.template);
    });
  }
}
