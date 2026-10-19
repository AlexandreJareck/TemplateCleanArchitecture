export interface MoneyDisplay {
  readonly amount: number;
  readonly currency: string;
}

export function formatMoney(money: MoneyDisplay, locale: string): string {
  return new Intl.NumberFormat(locale, {
    style: 'currency',
    currency: money.currency,
  }).format(money.amount);
}
