import {
  Anchor,
  Archive,
  BarChart3,
  Bot,
  Cable,
  Cpu,
  GitBranch,
  Globe2,
  Monitor,
  Mic,
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
  group: SettingsTabGroup
}

export type SettingsTabGroup = 'personal' | 'integrations' | 'coding' | 'archived'

export interface SettingsTabOptions {
  personalizationAvailable: boolean
  sourceControlEnabled: boolean
  mcpEnabled: boolean
  hooksEnabled: boolean
  subAgentEnabled: boolean
}

export function buildSettingsTabs(t: Translate, options: SettingsTabOptions): SettingsTabDefinition[] {
  const tabs: SettingsTabDefinition[] = [
    { id: 'general', label: t('settings.tab.general'), icon: SettingsIcon, group: 'personal' },
    { id: 'profile', label: t('settings.tab.profile'), icon: UserRound, group: 'personal' },
    { id: 'appearance', label: t('settings.tab.appearance'), icon: Palette, group: 'personal' }
  ]

  if (options.personalizationAvailable) {
    tabs.push({
      id: 'personalization',
      label: t('settings.tab.personalization'),
      icon: SlidersHorizontal,
      group: 'personal'
    })
  }
  tabs.push(
    { id: 'voice', label: t('settings.tab.voice'), icon: Mic, group: 'personal' },
    { id: 'usage', label: t('settings.tab.usage'), icon: BarChart3, group: 'personal' }
  )

  if (options.mcpEnabled) {
    tabs.push({ id: 'mcp', label: 'MCP', icon: McpIcon, group: 'integrations' })
  }
  tabs.push(
    { id: 'browserUse', label: t('settings.tab.browserUse'), icon: Globe2, group: 'integrations' },
    { id: 'computerControl', label: t('settings.tab.computerControl'), icon: Monitor, group: 'integrations' }
  )

  if (options.hooksEnabled) {
    tabs.push({
      id: 'hooks',
      label: t('settings.tab.hooks'),
      icon: Anchor,
      group: 'coding'
    })
  }
  tabs.push(
    { id: 'connection', label: t('settings.tab.connection'), icon: Cable, group: 'coding' },
    { id: 'servers', label: t('settings.tab.servers'), icon: Server, group: 'coding' },
    { id: 'llmService', label: t('settings.tab.llmService'), icon: Cpu, group: 'coding' }
  )
  if (options.sourceControlEnabled) {
    tabs.push({ id: 'sourceControl', label: t('settings.tab.sourceControl'), icon: GitBranch, group: 'coding' })
  }
  if (options.subAgentEnabled) {
    tabs.push({ id: 'subAgents', label: t('settings.tab.subAgents'), icon: Bot, group: 'coding' })
  }
  tabs.push({ id: 'archivedThreads', label: t('settings.tab.archivedThreads'), icon: Archive, group: 'archived' })

  return tabs
}
