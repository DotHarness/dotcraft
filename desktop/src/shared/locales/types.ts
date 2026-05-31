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
