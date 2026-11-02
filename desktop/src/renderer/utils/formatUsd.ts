const MIN_CENTS = 0.01

function usdFormat(locale: string | undefined, options: Intl.NumberFormatOptions): Intl.NumberFormat {
  return new Intl.NumberFormat(locale, { style: 'currency', currency: 'USD', ...options })
}

/** Formats a USD amount to the cent; amounts too small to show a cent read as "<$0.01" rather than "$0.00". */
export function formatUsd(value: number, locale?: string): string {
  const format = usdFormat(locale, { minimumFractionDigits: 2, maximumFractionDigits: 2 })
  if (value > 0 && value < MIN_CENTS) return `<${format.format(MIN_CENTS)}`
  return format.format(value)
}

/** Formats a USD axis value compactly, keeping cents only below one dollar. */
export function formatCompactUsd(value: number, locale?: string): string {
  return usdFormat(locale, { notation: 'compact', maximumFractionDigits: Math.abs(value) < 1 ? 2 : 1 }).format(value)
}
