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
    expect(normalizeLocale('missing-locale')).toBe(DEFAULT_LOCALE)
  })

  it('keeps supported locale metadata usable by selectors and html lang', () => {
    expect(SUPPORTED_LOCALES.map((locale) => locale.value)).toEqual(['en', 'zh-Hans'])
    for (const locale of SUPPORTED_LOCALES) {
      expect(locale.nativeName.trim()).not.toBe('')
      expect(localeToHtmlLang(locale.value)).toBe(locale.htmlLang)
    }
  })

  it('translates through the shared catalog registry', () => {
    expect(translate('en', 'settings.title')).toBe('Settings')
    expect(translate('zh-Hans', 'settings.title')).toBe('设置')
  })
})
