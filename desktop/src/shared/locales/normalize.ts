import type { AppLocale } from './types'
import { DEFAULT_LOCALE, SUPPORTED_LOCALES } from './types'

const LOCALE_ALIASES = new Map<string, AppLocale>()
const LANGUAGE_ALIASES = new Map<string, AppLocale>()

for (const locale of SUPPORTED_LOCALES) {
  LOCALE_ALIASES.set(locale.value.toLowerCase(), locale.value)
  if (locale.matchLanguage) {
    LANGUAGE_ALIASES.set(locale.value.split('-')[0].toLowerCase(), locale.value)
  }
  for (const alias of locale.aliases) {
    LOCALE_ALIASES.set(alias.toLowerCase(), locale.value)
  }
}

export function normalizeLocale(raw: unknown): AppLocale {
  if (typeof raw !== 'string') return DEFAULT_LOCALE
  const normalized = raw.trim().replace(/_/g, '-').toLowerCase()
  const direct = LOCALE_ALIASES.get(normalized)
  if (direct) return direct
  const language = normalized.split('-')[0]
  const languageMatch = LANGUAGE_ALIASES.get(language)
  if (languageMatch) return languageMatch
  return DEFAULT_LOCALE
}

/** `document.documentElement.lang` */
export function localeToHtmlLang(locale: AppLocale): string {
  return SUPPORTED_LOCALES.find((item) => item.value === locale)?.htmlLang ?? DEFAULT_LOCALE
}
