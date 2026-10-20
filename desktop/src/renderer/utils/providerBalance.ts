import type { ProviderSummary } from '../stores/providersStore'

const TOP_UP_URLS: Record<string, string> = {
  deepseek: 'https://platform.deepseek.com/top_up'
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

export function providerTopUpUrl(provider: ProviderSummary | null): string | null {
  return provider?.webSearchBackend ? TOP_UP_URLS[provider.webSearchBackend] ?? null : null
}
