import {
  Archive,
  BarChart3,
  Bot,
  Cable,
  Cpu,
  Globe2,
  Monitor,
  Palette,
  Server,
  Settings as SettingsIcon,
  SlidersHorizontal,
  UserRound,
  type LucideIcon
} from 'lucide-react'

import type { MessageKey } from '../../../shared/locales'
import type { SettingsTab } from '../../types/settings'
import { McpIcon } from './McpIcon'

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
    { id: 'appearance', label: t('settings.tab.appearance'), icon: Palette },
    { id: 'profile', label: t('settings.tab.profile'), icon: UserRound },
    { id: 'connection', label: t('settings.tab.connection'), icon: Cable },
    { id: 'servers', label: t('settings.tab.servers'), icon: Server },
    { id: 'llmService', label: t('settings.tab.llmService'), icon: Cpu },
    { id: 'browserUse', label: t('settings.tab.browserUse'), icon: Globe2 },
    { id: 'computerControl', label: t('settings.tab.computerControl'), icon: Monitor },
    { id: 'usage', label: t('settings.tab.usage'), icon: BarChart3 }
  ]

  if (options.personalizationAvailable) {
    // After General and Profile.
    tabs.splice(2, 0, { id: 'personalization', label: t('settings.tab.personalization'), icon: SlidersHorizontal })
  }
  if (options.mcpEnabled) {
    tabs.push({ id: 'mcp', label: 'MCP', icon: McpIcon })
  }
  if (options.subAgentEnabled) {
    tabs.push({ id: 'subAgents', label: t('settings.tab.subAgents'), icon: Bot })
  }
  tabs.push({ id: 'archivedThreads', label: t('settings.tab.archivedThreads'), icon: Archive })

  return tabs
}
