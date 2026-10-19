import { Component, signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { describe, expect, it } from 'vitest';
import { SessionFacade } from '../state/session.facade';
import { PermissionDirective } from './permission.directive';

@Component({
  imports: [PermissionDirective],
  template: `<span *appPermission="'catalog.read'">Catalog</span>`,
})
class HostComponent {}

describe('PermissionDirective', () => {
  it('renders content only for an effective permission', async () => {
    const allowed = signal(false);
    await TestBed.configureTestingModule({
      imports: [HostComponent],
      providers: [{ provide: SessionFacade, useValue: { can: () => allowed() } }],
    }).compileComponents();
    const fixture = TestBed.createComponent(HostComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('Catalog');
    allowed.set(true);
    fixture.detectChanges();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain('Catalog');
  });
});
