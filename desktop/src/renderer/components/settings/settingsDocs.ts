import type { AppLocale } from '../../../shared/locales'

export const SETTINGS_DOCS_BASE_URL = 'https://www.dotcraft.net'

export type SettingsDocsTopic =
  | 'hooks'
  | 'mcp'
  | 'memory'
  | 'modelProviders'
  | 'security'
  | 'servers'
  | 'subAgents'

interface SettingsDocsRoute {
  en: string
  zhHans?: string
}

const SETTINGS_DOCS_ROUTES: Record<SettingsDocsTopic, SettingsDocsRoute> = {
  hooks: {
    en: '/guide/hooks'
  },
  servers: {
    en: '/self-hosting/server-deployment#connect-from-desktop',
    zhHans: '/self-hosting/server-deployment#从-desktop-连接'
  },
  modelProviders: {
    en: '/start/desktop#set-up-a-model',
    zhHans: '/start/desktop#配好模型'
  },
  memory: {
    en: '/guide/memory'
  },
  subAgents: {
    en: '/guide/subagents'
  },
  mcp: {
    en: '/guide/plugins-tools#connect-an-mcp-server',
    zhHans: '/guide/plugins-tools#连接-mcp-server'
  },
  security: {
    en: '/self-hosting/security'
  }
}

export function resolveSettingsDocsUrl(topic: SettingsDocsTopic, locale: AppLocale): string {
  const route = SETTINGS_DOCS_ROUTES[topic]
  const localizedRoute = locale === 'zh-Hans' ? route.zhHans ?? route.en : route.en
  const localePrefix = locale === 'zh-Hans' ? '/zh' : ''
  return `${SETTINGS_DOCS_BASE_URL}${localePrefix}${localizedRoute}`
}
