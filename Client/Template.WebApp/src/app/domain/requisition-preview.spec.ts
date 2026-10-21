import { describe, expect, it } from 'vitest';
import {
  fitsAvailability,
  formatScaled,
  parseScaled,
  previewLineTotal,
  previewTotal,
} from './requisition-preview';

describe('requisition preview (CALC-002 mirror, server authoritative)', () => {
  it('parses decimals into scaled integers and rejects excess precision or signs', () => {
    expect(parseScaled('12.5', 2)).toBe(1250n);
    expect(parseScaled('0', 3)).toBe(0n);
    expect(parseScaled('1.2345', 3)).toBeUndefined();
    expect(parseScaled('-1', 2)).toBeUndefined();
    expect(parseScaled('1,5', 2)).toBeUndefined();
    expect(parseScaled('', 2)).toBeUndefined();
  });

  it('formats scaled integers with fixed decimals', () => {
    expect(formatScaled(5n, 2)).toBe('0.05');
    expect(formatScaled(123456n, 2)).toBe('1234.56');
    expect(formatScaled(-7n, 2)).toBe('-0.07');
  });

  it('rounds line totals half away from zero exactly like the server', () => {
    expect(previewLineTotal('3', '33.3333')).toBe('100.00');
    expect(previewLineTotal('0.5', '0.25')).toBe('0.13');
    expect(previewLineTotal('1', '0.125')).toBe('0.13');
    expect(previewLineTotal('2', '150')).toBe('300.00');
    expect(previewLineTotal('abc', '1')).toBeUndefined();
    expect(previewLineTotal('1', '1.23456')).toBeUndefined();
  });

  it('sums previews and checks availability without floating point', () => {
    expect(previewTotal(['0.10', '0.20', undefined, 'x'])).toBe('0.30');
    expect(fitsAvailability('100.00', '100.00')).toBe(true);
    expect(fitsAvailability('100.01', '100.00')).toBe(false);
    expect(fitsAvailability('1', undefined)).toBeUndefined();
  });
});
