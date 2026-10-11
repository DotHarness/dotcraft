export type GenerativeUiValueFormat = 'number' | 'compact' | 'percent' | 'currency'

export function formatGenerativeUiValue(value: unknown, format: GenerativeUiValueFormat | undefined, locale: string, currency = 'USD'): string {
  if (value === null || value === undefined) return ''
  if (typeof value !== 'number') return String(value)
  if (format === 'compact') return new Intl.NumberFormat(locale, { notation: 'compact', maximumFractionDigits: 1 }).format(value)
  if (format === 'percent') return new Intl.NumberFormat(locale, { style: 'percent', maximumFractionDigits: 1 }).format(value)
  if (format === 'currency') return new Intl.NumberFormat(locale, { style: 'currency', currency }).format(value)
  return new Intl.NumberFormat(locale, { maximumFractionDigits: 2 }).format(value)
}
