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
    en: '/features/agent-system/hooks'
  },
  servers: {
    en: '/features/self-hosted/server-deployment#connect-from-desktop',
    zhHans: '/features/self-hosted/server-deployment#从-desktop-连接'
  },
  modelProviders: {
    en: '/features/entry-points/desktop#model-providers'
  },
  memory: {
    en: '/features/agent-system/memory'
  },
  subAgents: {
    en: '/features/agent-system/subagents'
  },
  mcp: {
    en: '/features/agent-system/plugins-tools#mcp-servers'
  },
  security: {
    en: '/features/self-hosted/security'
  }
}

export function resolveSettingsDocsUrl(topic: SettingsDocsTopic, locale: AppLocale): string {
  const route = SETTINGS_DOCS_ROUTES[topic]
  const localizedRoute = locale === 'zh-Hans' ? route.zhHans ?? route.en : route.en
  const localePrefix = locale === 'zh-Hans' ? '/zh' : ''
  return `${SETTINGS_DOCS_BASE_URL}${localePrefix}${localizedRoute}`
}
