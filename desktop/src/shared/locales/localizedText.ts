import type { AppLocale, LocalizedTextMap } from './types'

/**
 * Resolve a data-driven localized text map (for example a plugin- or
 * extension-provided label) for the active locale, falling back to a base
 * string. Unlike the host message catalog, these strings ship with the data
 * rather than the app, so unknown locales simply fall back.
 */
export function resolveLocalizedText(
  localized: LocalizedTextMap | null | undefined,
  fallback: string | null | undefined,
  locale: AppLocale
): string | undefined {
  const localizedValue = localized?.[locale]?.trim()
  if (localizedValue) return localizedValue
  const fallbackValue = fallback?.trim()
  return fallbackValue || undefined
}
