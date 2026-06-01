import { readdirSync, readFileSync, statSync } from 'node:fs'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import {
  DEFAULT_LOCALE,
  SUPPORTED_LOCALES,
  localeToHtmlLang,
  normalizeLocale,
  translate
} from '.'
import { MESSAGES_DE } from './messages/de'
import { MESSAGES_EN } from './messages/en'
import { MESSAGES_ES } from './messages/es'
import { MESSAGES_FR } from './messages/fr'
import { MESSAGES_JA } from './messages/ja'
import { MESSAGES_KO } from './messages/ko'
import { MESSAGES_ZH_HANS } from './messages/zh-Hans'

const NON_ENGLISH_CATALOGS = {
  'zh-Hans': MESSAGES_ZH_HANS,
  ja: MESSAGES_JA,
  ko: MESSAGES_KO,
  es: MESSAGES_ES,
  fr: MESSAGES_FR,
  de: MESSAGES_DE
} satisfies Record<string, Record<string, string>>

describe('desktop locales', () => {
  it('normalizes supported locale aliases', () => {
    expect(normalizeLocale('en-US')).toBe('en')
    expect(normalizeLocale('en-AU')).toBe('en')
    expect(normalizeLocale('zh-CN')).toBe('zh-Hans')
    expect(normalizeLocale('zh_SG')).toBe('zh-Hans')
    expect(normalizeLocale('zh-Hant')).toBe(DEFAULT_LOCALE)
    expect(normalizeLocale('ja-JP')).toBe('ja')
    expect(normalizeLocale('ko-KR')).toBe('ko')
    expect(normalizeLocale('es-MX')).toBe('es')
    expect(normalizeLocale('fr-CA')).toBe('fr')
    expect(normalizeLocale('de-AT')).toBe('de')
    expect(normalizeLocale('missing-locale')).toBe(DEFAULT_LOCALE)
  })

  it('keeps supported locale metadata usable by selectors and html lang', () => {
    expect(SUPPORTED_LOCALES.map((locale) => locale.value)).toEqual([
      'en',
      'zh-Hans',
      'ja',
      'ko',
      'es',
      'fr',
      'de'
    ])
    for (const locale of SUPPORTED_LOCALES) {
      expect(locale.nativeName.trim()).not.toBe('')
      expect(localeToHtmlLang(locale.value)).toBe(locale.htmlLang)
    }
  })

  it('translates through the shared catalog registry', () => {
    expect(translate('en', 'settings.title')).toBe('Settings')
    expect(translate('zh-Hans', 'settings.title')).toBe('设置')
    expect(translate('ja', 'settings.title')).toBe('設定')
    expect(translate('ko', 'settings.title')).toBe('설정')
    expect(translate('es', 'settings.title')).toBe('Configuración')
    expect(translate('fr', 'settings.title')).toBe('Paramètres')
    expect(translate('de', 'settings.title')).toBe('Einstellungen')
  })

  it('keeps every English catalog key covered in every supported locale', () => {
    const missing = Object.keys(MESSAGES_EN).flatMap((key) =>
      Object.entries(NON_ENGLISH_CATALOGS)
        .filter(([, catalog]) => catalog[key] == null)
        .map(([locale]) => `${locale}:${key}`)
    )

    expect(missing).toEqual([])
  })

  it('keeps settings screen keys covered in every supported locale', () => {
    const missing = Array.from(collectMessageKeys(['settings'])).flatMap((key) =>
      Object.entries(NON_ENGLISH_CATALOGS)
        .filter(([, catalog]) => catalog[key] == null)
        .map(([locale]) => `${locale}:${key}`)
    )

    expect(missing).toEqual([])
  })

  it('keeps main shell navigation keys covered in every supported locale', () => {
    const missing = Array.from(collectMessageKeys([
      'layout',
      'sidebar',
      'plugins/PluginInstallDialog.tsx'
    ])).flatMap((key) =>
      Object.entries(NON_ENGLISH_CATALOGS)
        .filter(([, catalog]) => catalog[key] == null)
        .map(([locale]) => `${locale}:${key}`)
    )

    expect(missing).toEqual([])
  })

  it('keeps welcome composer keys covered in every supported locale', () => {
    const missing = Array.from(collectMessageKeys([
      'conversation/ConversationWelcome.tsx'
    ])).flatMap((key) =>
      Object.entries(NON_ENGLISH_CATALOGS)
        .filter(([, catalog]) => catalog[key] == null)
        .map(([locale]) => `${locale}:${key}`)
    )

    expect(missing).toEqual([])
  })

  it('keeps composer mascot keys covered in every supported locale', () => {
    const missing = Array.from(collectMessageKeys([
      'conversation/useComposerMascot.ts'
    ])).flatMap((key) =>
      Object.entries(NON_ENGLISH_CATALOGS)
        .filter(([, catalog]) => catalog[key] == null)
        .map(([locale]) => `${locale}:${key}`)
    )

    expect(missing).toEqual([])
  })

  it('keeps channel, plugin, automation, and team keys covered in every supported locale', () => {
    const missing = Array.from(collectMessageKeys([
      'channels',
      'plugins',
      'automations',
      'teams'
    ])).flatMap((key) =>
      Object.entries(NON_ENGLISH_CATALOGS)
        .filter(([, catalog]) => catalog[key] == null)
        .map(([locale]) => `${locale}:${key}`)
    )

    expect(missing).toEqual([])
  })
})

function collectMessageKeys(componentTargets: string[]): Set<string> {
  const localeDir = dirname(fileURLToPath(import.meta.url))
  const keys = new Set<string>()

  for (const target of componentTargets) {
    const targetPath = resolve(localeDir, '../../renderer/components', target)
    for (const file of walkSourceFiles(targetPath)) {
      const source = readFileSync(file, 'utf8')
      const keyPattern = /['"]([a-z][a-zA-Z0-9]*(?:\.[a-zA-Z0-9]+)+)['"]/g
      let match: RegExpExecArray | null

      while ((match = keyPattern.exec(source)) != null) {
        if (Object.hasOwn(MESSAGES_EN, match[1])) {
          keys.add(match[1])
        }
      }
    }
  }

  return keys
}

function walkSourceFiles(dir: string): string[] {
  if (!statSync(dir).isDirectory()) return [dir]

  return readdirSync(dir).flatMap((name) => {
    const fullPath = join(dir, name)
    const stats = statSync(fullPath)
    if (stats.isDirectory()) return walkSourceFiles(fullPath)
    return /\.(ts|tsx)$/.test(name) ? [fullPath] : []
  })
}
