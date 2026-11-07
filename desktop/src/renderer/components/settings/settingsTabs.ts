import {
  Anchor,
  Archive,
  BarChart3,
  Cable,
  Cpu,
  Download,
  FolderGit2,
  GitBranch,
  Globe2,
  Monitor,
  Mic,
  Palette,
  Settings as SettingsIcon,
  SlidersHorizontal,
  UserRound
} from 'lucide-react'
import type { DesktopPluginIconComponent } from '@dotcraft/plugin'

import type { MessageKey } from '../../../shared/locales'
import type { SettingsTab } from '../../types/settings'
import { McpIcon } from './McpIcon'
import { PetIcon } from './panels/pet/PetIcon'

type Translate = (key: MessageKey) => string

export interface SettingsTabDefinition {
  id: SettingsTab
  label: string
  icon: DesktopPluginIconComponent
  group: SettingsTabGroup
}

export type SettingsTabGroup = 'personal' | 'integrations' | 'workspace'

export interface SettingsTabOptions {
  sourceControlEnabled: boolean
  mcpEnabled: boolean
  hooksEnabled: boolean
  sessionImportEnabled: boolean
}

export function buildSettingsTabs(t: Translate, options: SettingsTabOptions): SettingsTabDefinition[] {
  const tabs: SettingsTabDefinition[] = [
    { id: 'general', label: t('settings.tab.general'), icon: SettingsIcon, group: 'personal' }
  ]
  if (options.sessionImportEnabled) {
    tabs.push({ id: 'import', label: t('settings.tab.import'), icon: Download, group: 'personal' })
  }
  tabs.push(
    { id: 'profile', label: t('settings.tab.profile'), icon: UserRound, group: 'personal' },
    { id: 'appearance', label: t('settings.tab.appearance'), icon: Palette, group: 'personal' },
    { id: 'archivedThreads', label: t('settings.tab.archivedThreads'), icon: Archive, group: 'personal' },
    { id: 'pet', label: t('settings.tab.pet'), icon: PetIcon, group: 'personal' },
    { id: 'voice', label: t('settings.tab.voice'), icon: Mic, group: 'personal' },
    { id: 'usage', label: t('settings.tab.usage'), icon: BarChart3, group: 'personal' },
    { id: 'browserUse', label: t('settings.tab.browserUse'), icon: Globe2, group: 'integrations' },
    { id: 'computerControl', label: t('settings.tab.computerControl'), icon: Monitor, group: 'integrations' },
    { id: 'connections', label: t('settings.tab.connections'), icon: Cable, group: 'integrations' },
    { id: 'personalization', label: t('settings.tab.personalization'), icon: SlidersHorizontal, group: 'workspace' }
  )

  if (options.mcpEnabled) {
    tabs.push({ id: 'mcp', label: 'MCP', icon: McpIcon, group: 'workspace' })
  }
  if (options.hooksEnabled) {
    tabs.push({ id: 'hooks', label: t('settings.tab.hooks'), icon: Anchor, group: 'workspace' })
  }
  tabs.push({ id: 'llmService', label: t('settings.tab.llmService'), icon: Cpu, group: 'workspace' })
  if (options.sourceControlEnabled) {
    tabs.push({ id: 'sourceControl', label: t('settings.tab.sourceControl'), icon: GitBranch, group: 'workspace' })
  }
  tabs.push({ id: 'worktrees', label: t('settings.tab.worktrees'), icon: FolderGit2, group: 'workspace' })

  return tabs
}
