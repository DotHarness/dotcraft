import { describe, expect, it } from 'vitest'
import {
  DEFAULT_LOCALE,
  SUPPORTED_LOCALES,
  localeToHtmlLang,
  normalizeLocale,
  translate
} from '.'

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

  it('falls back to English for untranslated keys in partial catalogs', () => {
    expect(translate('ja', 'settings.llm.title')).toBe(translate('en', 'settings.llm.title'))
  })
})
