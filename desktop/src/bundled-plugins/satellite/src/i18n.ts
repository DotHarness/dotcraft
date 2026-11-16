import { useCallback, useSyncExternalStore } from 'react'
import type { DesktopPluginLocale } from '@dotcraft/plugin'
import { satelliteHost } from './runtime'
import { messages as en } from './locales/en'
import { messages as zh } from './locales/zh-Hans'
import { messages as ja } from './locales/ja'
import { messages as ko } from './locales/ko'
import { messages as es } from './locales/es'
import { messages as fr } from './locales/fr'
import { messages as de } from './locales/de'

const catalogs: Record<DesktopPluginLocale, Readonly<Record<string, string>>> = { en, 'zh-Hans': zh, ja, ko, es, fr, de }

export function translate(locale: DesktopPluginLocale, key: string, params?: Record<string, string | number>): string {
  const value = catalogs[locale][key] ?? (en as Record<string, string>)[key] ?? key
  return value.replace(/\{\{(\w+)\}\}/g, (match, name: string) => params?.[name] == null ? match : String(params[name]))
}

export function useLocale(): DesktopPluginLocale {
  const environment = satelliteHost().environment
  return useSyncExternalStore(environment.onChange, () => environment.locale, () => environment.locale)
}

export function useT(): (key: string, params?: Record<string, string | number>) => string {
  const locale = useLocale()
  return useCallback((key, params) => translate(locale, key, params), [locale])
}
