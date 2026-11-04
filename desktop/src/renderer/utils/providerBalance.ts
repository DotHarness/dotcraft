export function formatSpendAmount(amount: number, currency: string | undefined, locale: string): string {
  const code = currency?.trim() || 'USD'
  try {
    return amount.toLocaleString(locale, { style: 'currency', currency: code, maximumFractionDigits: 6 })
  } catch {
    return `${amount.toLocaleString(locale, { maximumFractionDigits: 6 })} ${code}`
  }
}

export function formatBalanceAmount(amount: string, currency: string, locale: string): string {
  const value = Number(amount)
  if (amount.trim() === '' || !Number.isFinite(value)) return `${amount} ${currency}`
  try {
    return value.toLocaleString(locale, { style: 'currency', currency, minimumFractionDigits: 2, maximumFractionDigits: 2 })
  } catch {
    return `${value.toLocaleString(locale, { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ${currency}`
  }
}
