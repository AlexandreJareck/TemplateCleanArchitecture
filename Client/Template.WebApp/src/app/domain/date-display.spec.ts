import {
  formatFileSize,
  formatInstant,
  formatLocalDate,
  isValidTimeZone,
  nextLocalDate,
  startOfLocalDayUtc,
} from './date-display';

describe('date display', () => {
  it('formats a business date as the same calendar day in pt-BR', () => {
    expect(formatLocalDate('2026-12-17', 'pt-BR')).toBe('17/12/2026');
    expect(formatLocalDate('2026-01-01', 'pt-BR')).toBe('01/01/2026');
  });

  it('keeps unparseable and empty values visible', () => {
    expect(formatLocalDate(undefined, 'pt-BR')).toBe('—');
    expect(formatLocalDate('soon', 'pt-BR')).toBe('soon');
    expect(formatInstant('not-a-date', 'pt-BR', 'America/Sao_Paulo')).toBe('not-a-date');
  });

  it('renders a UTC instant in the organization time zone, not in UTC', () => {
    const value = formatInstant('2026-11-02T16:55:03.417612Z', 'pt-BR', 'America/Sao_Paulo');
    expect(value).toContain('02/11/2026');
    expect(value).toContain('13:55');
    expect(value).not.toContain('T');
    expect(formatInstant('2026-11-02T16:55:03Z', 'pt-BR', 'America/Sao_Paulo', true)).toContain(
      '13:55:03',
    );
  });

  it('falls back to the default business zone for an unknown zone', () => {
    expect(isValidTimeZone('Mars/Olympus')).toBe(false);
    expect(formatInstant('2026-11-02T16:55:03Z', 'pt-BR', 'Mars/Olympus')).toContain('13:55');
  });

  it('converts local day bounds to UTC instants', () => {
    expect(startOfLocalDayUtc('2026-11-05', 'America/Sao_Paulo')).toBe('2026-11-05T03:00:00.000Z');
    expect(startOfLocalDayUtc('2026-11-05', 'UTC')).toBe('2026-11-05T00:00:00.000Z');
    expect(startOfLocalDayUtc('bad', 'UTC')).toBeUndefined();
    expect(nextLocalDate('2026-12-31')).toBe('2027-01-01');
  });

  it('formats file sizes for people', () => {
    expect(formatFileSize(512, 'pt-BR')).toBe('512 B');
    expect(formatFileSize(2048, 'pt-BR')).toBe('2 KB');
    expect(formatFileSize(1_572_864, 'pt-BR')).toBe('1,5 MB');
    expect(formatFileSize(undefined, 'pt-BR')).toBe('—');
  });
});
