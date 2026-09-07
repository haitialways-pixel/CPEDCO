/** Narrow no-break space — French/Haitian thousands separator. */
export const THIN_SPACE = "\u202F";

export function moneySymbol(currency: string): string {
  return currency.toUpperCase() === "USD" ? "$US" : "G";
}

function roundAwayFromZero(value: number, decimals: number): number {
  const factor = 10 ** decimals;
  return Math.sign(value) * Math.round(Math.abs(value) * factor) / factor;
}

/** Display only. Values stay numeric(19,4) on the server. */
export function formatMoney(value: number, currency: string): string {
  return `${formatMoneyNumber(value)} ${moneySymbol(currency)}`;
}

export function formatMoneyNumber(value: number): string {
  const rounded = roundAwayFromZero(value, 2);
  const negative = rounded < 0;
  const absolute = Math.abs(rounded);
  const whole = Math.trunc(absolute);
  const fraction = Math.round((absolute - whole) * 100);
  const digits = String(whole);
  let grouped = "";
  for (let i = 0; i < digits.length; i++) {
    grouped += digits[i];
    const remaining = digits.length - i - 1;
    if (remaining > 0 && remaining % 3 === 0) grouped += THIN_SPACE;
  }
  return `${negative ? "-" : ""}${grouped},${String(fraction).padStart(2, "0")}`;
}
