import type { MessageKey } from '../../../shared/locales'
import type { SettingsTab } from '../../types/settings'
import type { SettingsTabDefinition } from './settingsTabs'

export interface SettingsSearchEntry {
  id: string
  tab: SettingsTab
  label: string
  panelLabel: string
  group: SettingsTabDefinition['group']
  icon: SettingsTabDefinition['icon']
  revealLabel?: string
}

type IndexedSettingsEntry = SettingsSearchEntry & { keywords: string }

interface SectionEntry {
  panel: SettingsTab
  key: MessageKey
  keywords?: string
}

const SECTION_ENTRIES: SectionEntry[] = [
  { panel: 'general', key: 'settings.language', keywords: 'locale' },
  { panel: 'general', key: 'settings.notifications.title' },
  { panel: 'general', key: 'settings.group.composer', keywords: 'follow up interrupt' },
  { panel: 'general', key: 'settings.general.showInMenuBar', keywords: 'tray' },
  { panel: 'general', key: 'settings.personalization.showThinkingContent', keywords: 'reasoning' },
  { panel: 'appearance', key: 'settings.appearance.mode.label', keywords: 'theme dark light' },
  { panel: 'appearance', key: 'settings.appearance.accent.label', keywords: 'color' },
  { panel: 'appearance', key: 'settings.appearance.contrast.label' },
  { panel: 'voice', key: 'settings.voice.microphone.title', keywords: 'microphone' },
  { panel: 'voice', key: 'settings.voice.models.title', keywords: 'whisper' },
  { panel: 'personalization', key: 'settings.group.permissions', keywords: 'approval sandbox' },
  { panel: 'personalization', key: 'settings.permissions.workspaceDefault.label', keywords: 'approval' },
  { panel: 'personalization', key: 'settings.group.tools', keywords: 'code mode' },
  { panel: 'personalization', key: 'settings.personalization.promptSuggestions' },
  { panel: 'personalization', key: 'settings.personalization.group.skills' },
  { panel: 'personalization', key: 'settings.personalization.group.memory' },
  { panel: 'sourceControl', key: 'settings.sourceControl.provider.label', keywords: 'git perforce' },
  { panel: 'hooks', key: 'settings.hooks.pluginTrust.title' }
]

export function buildSettingsSearchEntries(
  tabs: SettingsTabDefinition[],
  t: (key: MessageKey) => string,
  options: { subAgentEnabled: boolean }
): IndexedSettingsEntry[] {
  const entries: IndexedSettingsEntry[] = tabs.map((tab) => ({
    id: tab.id,
    tab: tab.id,
    label: tab.label,
    panelLabel: tab.label,
    group: tab.group,
    icon: tab.icon,
    keywords: ''
  }))
  for (const section of SECTION_ENTRIES) {
    const panel = tabs.find((tab) => tab.id === section.panel)
    if (!panel) continue
    const label = t(section.key)
    entries.push({
      id: `${panel.id}:${section.key}`,
      tab: panel.id,
      label,
      panelLabel: panel.label,
      group: panel.group,
      icon: panel.icon,
      keywords: section.keywords ?? '',
      revealLabel: label
    })
  }
  const personalization = tabs.find((tab) => tab.id === 'personalization')
  if (options.subAgentEnabled && personalization) {
    entries.push({
      id: 'subAgents',
      tab: 'subAgents',
      label: t('settings.tab.subAgents'),
      panelLabel: personalization.label,
      group: personalization.group,
      icon: personalization.icon,
      keywords: 'agent delegate'
    })
  }
  return entries
}

export function filterSettingsSearchEntries<T extends { label: string; keywords: string }>(
  entries: T[],
  query: string
): T[] {
  const needle = query.trim().toLowerCase()
  if (!needle) return []
  return entries.filter((entry) => `${entry.label} ${entry.keywords}`.toLowerCase().includes(needle))
}

const REVEAL_ATTEMPTS = 60

export function revealSettingsLabel(label: string, attempt = 0): void {
  const target = [...document.querySelectorAll<HTMLElement>('[data-settings-label]')]
    .find((element) => element.textContent === label)
  if (target) {
    target.scrollIntoView({ block: 'center' })
    return
  }
  if (attempt < REVEAL_ATTEMPTS) {
    window.requestAnimationFrame(() => revealSettingsLabel(label, attempt + 1))
  }
}
