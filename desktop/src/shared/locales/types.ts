/** BCP 47; see specs/clients/desktop-client.md §22.3 */
export const SUPPORTED_LOCALES = [
  {
    value: 'en',
    nativeName: 'English',
    htmlLang: 'en',
    matchLanguage: true,
    aliases: ['en-US', 'en-GB']
  },
  {
    value: 'zh-Hans',
    nativeName: '简体中文',
    htmlLang: 'zh-Hans',
    matchLanguage: false,
    aliases: ['zh', 'zh-CN', 'zh-SG']
  },
  {
    value: 'ja',
    nativeName: '日本語',
    htmlLang: 'ja',
    matchLanguage: true,
    aliases: ['ja-JP']
  },
  {
    value: 'ko',
    nativeName: '한국어',
    htmlLang: 'ko',
    matchLanguage: true,
    aliases: ['ko-KR']
  },
  {
    value: 'es',
    nativeName: 'Español',
    htmlLang: 'es',
    matchLanguage: true,
    aliases: ['es-ES', 'es-MX', 'es-AR', 'es-CL', 'es-CO']
  },
  {
    value: 'fr',
    nativeName: 'Français',
    htmlLang: 'fr',
    matchLanguage: true,
    aliases: ['fr-FR', 'fr-CA', 'fr-BE', 'fr-CH']
  },
  {
    value: 'de',
    nativeName: 'Deutsch',
    htmlLang: 'de',
    matchLanguage: true,
    aliases: ['de-DE', 'de-AT', 'de-CH']
  }
] as const

export type AppLocale = (typeof SUPPORTED_LOCALES)[number]['value']

export const DEFAULT_LOCALE: AppLocale = 'en'

export type LocalizedTextMap = Partial<Record<AppLocale, string>>
export type EnglishRequiredLocalizedText = { en: string } & LocalizedTextMap

export const SUPPORTED_LOCALE_VALUES = SUPPORTED_LOCALES.map((locale) => locale.value) as AppLocale[]

/** Top-level application menu ids (stable IPC / routing). */
export const TOP_LEVEL_MENU_IDS = ['file', 'edit', 'view', 'window', 'help'] as const
export type TopLevelMenuId = (typeof TOP_LEVEL_MENU_IDS)[number]
