/**
 * Display rules for the two kinds of time the API returns (api-guidelines: business dates are `yyyy-MM-dd`, instants
 * are UTC ISO 8601). A business date is shown as the same calendar day everywhere; an instant is rendered in the
 * organization time zone (NFR-AUD-2), never in the browser zone. Values that do not parse are returned unchanged so
 * nothing is hidden.
 */
const LOCAL_DATE = /^(\d{4})-(\d{2})-(\d{2})$/;
const formatters = new Map<string, Intl.DateTimeFormat>();

function formatter(locale: string, options: Intl.DateTimeFormatOptions) {
  const key = `${locale}|${JSON.stringify(options)}`;
  let cached = formatters.get(key);
  if (!cached) {
    cached = new Intl.DateTimeFormat(locale, options);
    formatters.set(key, cached);
  }
  return cached;
}

export const DEFAULT_TIME_ZONE = 'America/Sao_Paulo';

export function isValidTimeZone(timeZone: string | undefined): timeZone is string {
  if (!timeZone) return false;
  try {
    new Intl.DateTimeFormat('en', { timeZone });
    return true;
  } catch {
    return false;
  }
}

/** `2026-12-17` -> `17/12/2026` (pt-BR); the calendar day never shifts with the viewer's zone. */
export function formatLocalDate(value: string | undefined | null, locale: string): string {
  if (!value) return '—';
  const match = LOCAL_DATE.exec(value.slice(0, 10));
  if (!match) return value;
  const [, year, month, day] = match;
  const date = new Date(Date.UTC(Number(year), Number(month) - 1, Number(day)));
  return formatter(locale, { dateStyle: 'short', timeZone: 'UTC' }).format(date);
}

/** UTC instant -> date and time in the given zone, e.g. `02/11/2026, 13:55`. */
export function formatInstant(
  value: string | undefined | null,
  locale: string,
  timeZone: string,
  withSeconds = false,
): string {
  if (!value) return '—';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return value;
  return formatter(locale, {
    dateStyle: 'short',
    timeStyle: withSeconds ? 'medium' : 'short',
    timeZone: isValidTimeZone(timeZone) ? timeZone : DEFAULT_TIME_ZONE,
  }).format(date);
}

/** Offset in milliseconds of `timeZone` at `instant` (positive east of UTC). */
function zoneOffset(instant: number, timeZone: string): number {
  const parts = formatter('en-US', {
    timeZone,
    hourCycle: 'h23',
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  }).formatToParts(new Date(instant));
  const part = (type: string) => Number(parts.find((item) => item.type === type)?.value ?? 0);
  const asUtc = Date.UTC(
    part('year'),
    part('month') - 1,
    part('day'),
    part('hour'),
    part('minute'),
    part('second'),
  );
  return asUtc - Math.floor(instant / 1000) * 1000;
}

/**
 * UTC instant of 00:00 of a business date in `timeZone`, for inclusive/exclusive range filters
 * (`2026-11-05` in America/Sao_Paulo -> `2026-11-05T03:00:00.000Z`). Returns `undefined` for an invalid date.
 */
export function startOfLocalDayUtc(value: string, timeZone: string): string | undefined {
  const match = LOCAL_DATE.exec(value);
  if (!match) return undefined;
  const zone = isValidTimeZone(timeZone) ? timeZone : DEFAULT_TIME_ZONE;
  const [, year, month, day] = match;
  const wall = Date.UTC(Number(year), Number(month) - 1, Number(day));
  let instant = wall - zoneOffset(wall, zone);
  instant = wall - zoneOffset(instant, zone);
  return new Date(instant).toISOString();
}

/** The next calendar day of a business date (`2026-12-31` -> `2027-01-01`). */
export function nextLocalDate(value: string): string | undefined {
  const match = LOCAL_DATE.exec(value);
  if (!match) return undefined;
  const [, year, month, day] = match;
  return new Date(Date.UTC(Number(year), Number(month) - 1, Number(day) + 1))
    .toISOString()
    .slice(0, 10);
}

/** Byte count as a short localized size (`2048` -> `2 KB`, `1536000` -> `1,5 MB` in pt-BR). */
export function formatFileSize(bytes: number | undefined | null, locale: string): string {
  if (bytes === undefined || bytes === null || !Number.isFinite(bytes) || bytes < 0) return '—';
  const units = ['B', 'KB', 'MB', 'GB'];
  let value = bytes;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit++;
  }
  const number = new Intl.NumberFormat(locale, { maximumFractionDigits: unit === 0 ? 0 : 1 });
  return `${number.format(value)} ${units[unit]}`;
}
