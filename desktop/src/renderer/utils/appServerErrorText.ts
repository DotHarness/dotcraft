import { readAppServerErrorFields } from '../../shared/appServerError'
import { translate, type AppLocale } from '../../shared/locales'

function serverTextVars(value: unknown): Record<string, string | number> | undefined {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return undefined
  const vars: Record<string, string | number> = {}
  for (const [key, raw] of Object.entries(value as Record<string, unknown>)) {
    if (typeof raw === 'string' || typeof raw === 'number') {
      vars[key] = raw
    } else if (typeof raw === 'boolean') {
      vars[key] = raw ? 'true' : 'false'
    }
  }
  return Object.keys(vars).length > 0 ? vars : undefined
}

export function serverFallbackText(
  locale: AppLocale,
  messageKey: unknown,
  params: unknown,
  fallbackText: unknown,
  message?: unknown
): string | null {
  const fallback =
    typeof fallbackText === 'string'
      ? fallbackText
      : typeof message === 'string'
        ? message
        : null
  if (typeof messageKey !== 'string' || messageKey.trim() === '') {
    return fallback
  }
  const key = messageKey.trim()
  const localized = translate(locale, key, serverTextVars(params))
  return localized === key ? fallback : localized
}

export function describeAppServerError(locale: AppLocale, error: unknown): string {
  const data = readAppServerErrorFields(error).data
  return serverFallbackText(locale, data?.messageKey, data?.params, data?.fallbackText)
    ?? (error instanceof Error ? error.message : String(error ?? ''))
}
