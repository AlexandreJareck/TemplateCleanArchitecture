import { TestBed } from '@angular/core/testing';
import { success } from '@application/common/result';
import type { MasterDataPort } from '@application/master-data/master-data';
import { MASTER_DATA_PORT } from '../state/ports';
import {
  page,
  provideTestRoute,
  provideTestSession,
  provideTestTransloco,
  settle,
  text,
} from '../testing/presentation-test-kit';
import { MasterDataPage } from './master-data.page';

async function render() {
  const create = vi.fn(async () => success({ id: 'd1' }));
  const port = {
    list: vi.fn(async () => success(page([]))),
    create,
  } as unknown as MasterDataPort;
  TestBed.configureTestingModule({
    imports: [MasterDataPage],
    providers: [
      ...provideTestTransloco('pt-BR'),
      ...provideTestRoute({}, { kind: 'departments', titleKey: 'nav.departments' }),
      ...provideTestSession(['organization.read', 'organization.manage']),
      { provide: MASTER_DATA_PORT, useValue: port },
    ],
  });
  const fixture = TestBed.createComponent(MasterDataPage);
  await settle(fixture);
  const root = fixture.nativeElement as HTMLElement;
  root.querySelector<HTMLButtonElement>('.toolbar-primary')!.click();
  await settle(fixture);
  const type = async (id: string, value: string) => {
    const element = root.querySelector<HTMLInputElement>(`#${id}`)!;
    element.value = value;
    element.dispatchEvent(new Event('input'));
    element.dispatchEvent(new Event('blur'));
    await settle(fixture);
  };
  const save = () => root.querySelector<HTMLButtonElement>('.drawer-form button[type="submit"]')!;
  return { fixture, root, create, type, save };
}

describe('MasterDataPage code validation (F-08)', () => {
  it('rejects the code "x" inline and keeps save disabled without any request', async () => {
    const { fixture, root, create, type, save } = await render();
    await type('code', 'x');
    await type('name', 'A');
    expect(save().disabled).toBe(true);
    expect(text(root.querySelector('#code-error'))).toBe('O código deve ter de 2 a 20 caracteres.');
    expect(root.querySelector('#code')!.getAttribute('aria-invalid')).toBe('true');
    root.querySelector('.drawer-form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle(fixture);
    expect(create).not.toHaveBeenCalled();
  });

  it('rejects characters outside the domain pattern', async () => {
    const { root, type, save } = await render();
    await type('code', 'TEC INFRA');
    await type('name', 'Infra');
    expect(save().disabled).toBe(true);
    expect(text(root.querySelector('#code-error'))).toContain('Formato de código inválido');
  });

  it('accepts a valid code and sends it normalized; the API stays authoritative', async () => {
    const { fixture, root, create, type, save } = await render();
    await type('code', ' tec-infra ');
    await type('name', 'Infraestrutura');
    expect(root.querySelector('#code-error')).toBeNull();
    expect(save().disabled).toBe(false);
    root.querySelector('.drawer-form')!.dispatchEvent(new Event('submit', { cancelable: true }));
    await settle(fixture);
    expect(create).toHaveBeenCalledWith(
      'departments',
      expect.objectContaining({ code: 'TEC-INFRA', name: 'Infraestrutura' }),
    );
  });
});
