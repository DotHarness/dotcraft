import { useCallback, useSyncExternalStore } from 'react'
import type { DesktopPluginLocale } from '@dotcraft/plugin'
import { memoryStudioHost } from './runtime'
import { messages as en, type MessageKey } from './locales/en'
import { messages as zh } from './locales/zh-Hans'
import { messages as ja } from './locales/ja'
import { messages as ko } from './locales/ko'
import { messages as es } from './locales/es'
import { messages as fr } from './locales/fr'
import { messages as de } from './locales/de'

export type { MessageKey }
export type Translate = (key: MessageKey, params?: Record<string, string | number>) => string

const catalogs: Record<DesktopPluginLocale, Readonly<Record<MessageKey, string>>> = { en, 'zh-Hans': zh, ja, ko, es, fr, de }

export function translate(locale: DesktopPluginLocale, key: MessageKey, params?: Record<string, string | number>): string {
  const value = catalogs[locale]?.[key] ?? en[key] ?? key
  return value.replace(/\{\{(\w+)\}\}/g, (match, name: string) => params?.[name] == null ? match : String(params[name]))
}

export function localizedLabel(key: MessageKey): { default: string; translations: Record<string, string> } {
  return {
    default: en[key],
    translations: Object.fromEntries((Object.keys(catalogs) as DesktopPluginLocale[])
      .filter((locale) => locale !== 'en')
      .map((locale) => [locale, catalogs[locale][key]]))
  }
}

export function useLocale(): DesktopPluginLocale {
  const environment = memoryStudioHost().environment
  return useSyncExternalStore(environment.onChange, () => environment.locale, () => environment.locale)
}

export function useT(): Translate {
  const locale = useLocale()
  return useCallback((key, params) => translate(locale, key, params), [locale])
}
