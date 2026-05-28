import type { AppLocale } from './types'
import { DEFAULT_LOCALE } from './types'

export function normalizeLocale(raw: unknown): AppLocale {
  if (raw === 'zh-Hans' || raw === 'zh-CN' || raw === 'zh') {
    return 'zh-Hans'
  }
  return DEFAULT_LOCALE
}

/** `document.documentElement.lang` */
export function localeToHtmlLang(locale: AppLocale): string {
  return locale === 'zh-Hans' ? 'zh-Hans' : 'en'
}
