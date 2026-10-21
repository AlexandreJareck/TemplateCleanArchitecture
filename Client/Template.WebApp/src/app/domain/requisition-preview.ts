/**
 * Client-side preview of requisition totals (CALC-002): line total = round(quantity × unit price) to two decimals,
 * midpoint away from zero; requisition total = Σ line totals. Values are decimal strings and the arithmetic uses
 * scaled integers, never binary floating point. The preview is advisory: the server recalculates and is authoritative.
 */
export const QUANTITY_DECIMALS = 3;
export const UNIT_PRICE_DECIMALS = 4;
export const MONEY_DECIMALS = 2;

const DECIMAL = /^\d+(?:\.(\d+))?$/;

/** Parses a non-negative decimal string into a scaled integer, or `undefined` when invalid or too precise. */
export function parseScaled(value: string, scale: number): bigint | undefined {
  const trimmed = value.trim();
  const match = DECIMAL.exec(trimmed);
  if (!match) return undefined;
  const fraction = match[1] ?? '';
  if (fraction.length > scale) return undefined;
  const [whole] = trimmed.split('.');
  return BigInt(`${whole}${fraction.padEnd(scale, '0')}`);
}

/** Formats a scaled integer with exactly `scale` decimals. */
export function formatScaled(value: bigint, scale: number): string {
  const negative = value < 0n;
  const digits = (negative ? -value : value).toString().padStart(scale + 1, '0');
  const whole = digits.slice(0, digits.length - scale);
  const fraction = scale > 0 ? `.${digits.slice(digits.length - scale)}` : '';
  return `${negative ? '-' : ''}${whole}${fraction}`;
}

/** Previewed line total (two decimals), or `undefined` while quantity or price is not a valid decimal. */
export function previewLineTotal(quantity: string, unitPrice: string): string | undefined {
  const scaledQuantity = parseScaled(quantity, QUANTITY_DECIMALS);
  const scaledPrice = parseScaled(unitPrice, UNIT_PRICE_DECIMALS);
  if (scaledQuantity === undefined || scaledPrice === undefined) return undefined;
  const product = scaledQuantity * scaledPrice; // scale 7
  const divisor = 10n ** BigInt(QUANTITY_DECIMALS + UNIT_PRICE_DECIMALS - MONEY_DECIMALS);
  const rounded = (product + divisor / 2n) / divisor; // non-negative: half away from zero
  return formatScaled(rounded, MONEY_DECIMALS);
}

/** Σ of two-decimal amounts; invalid entries are ignored by the preview. */
export function previewTotal(amounts: readonly (string | undefined)[]): string {
  const sum = amounts.reduce<bigint>((total, amount) => {
    const scaled = amount === undefined ? undefined : parseScaled(amount, MONEY_DECIMALS);
    return scaled === undefined ? total : total + scaled;
  }, 0n);
  return formatScaled(sum, MONEY_DECIMALS);
}

/** Whether `requested` fits `available` (both two-decimal strings); `undefined` when unknown. */
export function fitsAvailability(
  requested: string,
  available: string | undefined,
): boolean | undefined {
  if (available === undefined) return undefined;
  const scaledRequested = parseScaled(requested, MONEY_DECIMALS);
  const scaledAvailable = parseScaled(available, MONEY_DECIMALS);
  if (scaledRequested === undefined || scaledAvailable === undefined) return undefined;
  return scaledRequested <= scaledAvailable;
}
