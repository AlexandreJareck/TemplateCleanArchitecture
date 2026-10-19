import { describe, expect, it } from 'vitest';
import { formatMoney } from './money-display';

describe('formatMoney', () => {
  it('uses the requested locale and currency', () => {
    expect(formatMoney({ amount: 12.5, currency: 'BRL' }, 'pt-BR')).toContain('12,50');
  });
});
