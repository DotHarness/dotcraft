import { getLocales, useLocales, type Locale } from 'expo-localization'
import { createContext, useContext, useMemo } from 'react'
import { MESSAGES_DE } from './messages/de'
import { MESSAGES_EN, type Catalog, type MessageId } from './messages/en'
import { MESSAGES_ES } from './messages/es'
import { MESSAGES_FR } from './messages/fr'
import { MESSAGES_JA } from './messages/ja'
import { MESSAGES_KO } from './messages/ko'
import { MESSAGES_ZH_HANS } from './messages/zh-Hans'

const CATALOGS = {
  en: MESSAGES_EN,
  'zh-Hans': MESSAGES_ZH_HANS,
  ja: MESSAGES_JA,
  ko: MESSAGES_KO,
  es: MESSAGES_ES,
  fr: MESSAGES_FR,
  de: MESSAGES_DE,
} satisfies Record<string, Catalog>

type AppLocale = keyof typeof CATALOGS

function localeFor(tag: string | null): AppLocale {
  const normalized = (tag ?? '').toLowerCase()
  if (normalized.startsWith('zh')) {
    return /hant|-tw|-hk|-mo/.test(normalized) ? 'en' : 'zh-Hans'
  }
  const language = normalized.split('-')[0]
  return Object.keys(CATALOGS).includes(language) ? (language as AppLocale) : 'en'
}

type Vars = Record<string, string | number>

export interface I18n {
  t: (key: MessageId, vars?: Vars) => string
  around: (key: MessageId, slot: string) => [string, string]
  ago: (iso: string | null) => string
  date: (iso: string) => string
  dateTime: (iso: string) => string
  duration: (ms: number) => string
}

function createI18n(locale: AppLocale): I18n {
  const t = (key: MessageId, vars: Vars = {}) => {
    let text: string = CATALOGS[locale][key]
    for (const [name, value] of Object.entries(vars)) text = text.split(`{{${name}}}`).join(String(value))
    return text
  }
  const shortDate = (at: number) => new Intl.DateTimeFormat(locale, { month: 'short', day: 'numeric' }).format(at)
  return {
    t,
    around(key, slot) {
      const text = t(key)
      const marker = `{{${slot}}}`
      const index = text.indexOf(marker)
      return index < 0 ? [text, ''] : [text.slice(0, index), text.slice(index + marker.length)]
    },
    ago(iso) {
      const at = Date.parse(iso ?? '')
      const minutes = Math.floor((Date.now() - at) / 60_000)
      if (!Number.isFinite(minutes) || minutes < 1) return t('time.justNow')
      if (minutes < 60) return t('time.minutes', { count: minutes })
      const hours = Math.floor(minutes / 60)
      if (hours < 24) return t('time.hours', { count: hours })
      const days = Math.floor(hours / 24)
      if (days < 2) return t('time.yesterday')
      if (days < 7) return t('time.days', { count: days })
      return shortDate(at)
    },
    date: (iso) => shortDate(Date.parse(iso)),
    dateTime: (iso) => new Intl.DateTimeFormat(locale, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' }).format(Date.parse(iso)),
    duration(ms) {
      const total = Math.max(0, Math.round(ms / 1000))
      const parts: [number, 'hour' | 'minute' | 'second', string][] = [
        [Math.floor(total / 3600), 'hour', 'h'],
        [Math.floor((total % 3600) / 60), 'minute', 'm'],
        [total % 60, 'second', 's'],
      ]
      const shown = parts.filter(([value], index) => value > 0 || (index === 2 && total === 0))
      return shown
        .map(([value, unit, short]) => {
          try {
            return new Intl.NumberFormat(locale, { style: 'unit', unit, unitDisplay: 'narrow' }).format(value)
          } catch {
            return `${value}${short}`
          }
        })
        .join(' ')
    },
  }
}

export const I18nContext = createContext<I18n>(createI18n('en'))

function deviceTag(locale: Locale | undefined): string | null {
  const tag = locale?.languageTag ?? null
  const script = locale?.languageScriptCode ?? null
  return script && tag?.startsWith('zh') ? `zh-${script}` : tag
}

export function useDeviceI18n(override?: string): I18n {
  const locales = useLocales()
  const locale = localeFor(override ?? deviceTag(locales[0]))
  return useMemo(() => createI18n(locale), [locale])
}

export function deviceI18n(): I18n {
  return createI18n(localeFor(deviceTag(getLocales()[0])))
}

export function useI18n(): I18n {
  return useContext(I18nContext)
}
