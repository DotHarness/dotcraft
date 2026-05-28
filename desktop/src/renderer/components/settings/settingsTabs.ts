import {
  Archive,
  BarChart3,
  Bot,
  Cable,
  Cpu,
  Globe2,
  Monitor,
  Server,
  Settings as SettingsIcon,
  UserRound,
  type LucideIcon
} from 'lucide-react'

import type { MessageKey } from '../../../shared/locales'
import type { SettingsTab } from '../../types/settings'

type Translate = (key: MessageKey) => string

export interface SettingsTabDefinition {
  id: SettingsTab
  label: string
  icon: LucideIcon
}

export interface SettingsTabOptions {
  personalizationAvailable: boolean
  mcpEnabled: boolean
  subAgentEnabled: boolean
}

export function buildSettingsTabs(t: Translate, options: SettingsTabOptions): SettingsTabDefinition[] {
  const tabs: SettingsTabDefinition[] = [
    { id: 'general', label: t('settings.tab.general'), icon: SettingsIcon },
    { id: 'connection', label: t('settings.tab.connection'), icon: Cable },
    { id: 'llmService', label: t('settings.tab.llmService'), icon: Cpu },
    { id: 'browserUse', label: t('settings.tab.browserUse'), icon: Globe2 },
    { id: 'computerControl', label: t('settings.tab.computerControl'), icon: Monitor },
    { id: 'usage', label: t('settings.tab.usage'), icon: BarChart3 }
  ]

  if (options.personalizationAvailable) {
    tabs.splice(1, 0, { id: 'personalization', label: t('settings.tab.personalization'), icon: UserRound })
  }
  if (options.mcpEnabled) {
    tabs.push({ id: 'mcp', label: 'MCP', icon: Server })
  }
  if (options.subAgentEnabled) {
    tabs.push({ id: 'subAgents', label: t('settings.tab.subAgents'), icon: Bot })
  }
  tabs.push({ id: 'archivedThreads', label: t('settings.tab.archivedThreads'), icon: Archive })

  return tabs
}
