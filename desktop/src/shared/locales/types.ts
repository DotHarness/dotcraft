/** BCP 47; see specs/clients/desktop-client.md §22.3 */
export type AppLocale = 'en' | 'zh-Hans'

export const DEFAULT_LOCALE: AppLocale = 'en'

/** Top-level application menu ids (stable IPC / routing). */
export const TOP_LEVEL_MENU_IDS = ['file', 'edit', 'view', 'window', 'help'] as const
export type TopLevelMenuId = (typeof TOP_LEVEL_MENU_IDS)[number]
