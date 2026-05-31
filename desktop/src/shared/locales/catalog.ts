import type { AppLocale } from './types'
import { DEFAULT_LOCALE } from './types'
import { MESSAGES_EN, type MessageId } from './messages/en'
import { MESSAGES_ZH_HANS } from './messages/zh-Hans'

/**
 * Flat message keys shared by main and renderer. Values may contain `{{name}}` placeholders.
 */
const CATALOGS: Record<AppLocale, Record<string, string>> = {
  en: MESSAGES_EN as unknown as Record<string, string>,
  'zh-Hans': MESSAGES_ZH_HANS as Record<string, string>
}

export function translate(
  locale: AppLocale,
  key: MessageId | string,
  vars?: Record<string, string | number>
): string {
  const table = CATALOGS[locale] ?? CATALOGS[DEFAULT_LOCALE]
  const fallback = CATALOGS[DEFAULT_LOCALE]
  let s = table[key] ?? fallback[key] ?? String(key)
  if (vars) {
    for (const [k, v] of Object.entries(vars)) {
      s = s.replace(new RegExp(`\\{\\{${k}\\}\\}`, 'g'), () => String(v))
    }
  }
  return s
}

export type MessageKey = MessageId
