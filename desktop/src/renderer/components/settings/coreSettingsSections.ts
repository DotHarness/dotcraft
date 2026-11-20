import type { DesktopPluginSettingsSection } from '@dotcraft/plugin'
import type { MessageKey } from '../../../shared/locales'
import type { SettingsTab } from '../../types/settings'

type CoreSettingsSectionEntry =
  | { readonly tab: SettingsTab; readonly labelKey: MessageKey }
  | { readonly tab: SettingsTab; readonly literalLabel: string }

const SECTION_ENTRIES: Record<DesktopPluginSettingsSection, CoreSettingsSectionEntry> = {
  general: { tab: 'general', labelKey: 'settings.tab.general' },
  import: { tab: 'import', labelKey: 'settings.tab.import' },
  profile: { tab: 'profile', labelKey: 'settings.tab.profile' },
  appearance: { tab: 'appearance', labelKey: 'settings.tab.appearance' },
  'archived-threads': { tab: 'archivedThreads', labelKey: 'settings.tab.archivedThreads' },
  voice: { tab: 'voice', labelKey: 'settings.tab.voice' },
  personalization: { tab: 'personalization', labelKey: 'settings.tab.personalization' },
  pet: { tab: 'pet', labelKey: 'settings.tab.pet' },
  usage: { tab: 'usage', labelKey: 'settings.tab.usage' },
  'model-providers': { tab: 'llmService', labelKey: 'settings.tab.llmService' },
  browser: { tab: 'browserUse', labelKey: 'settings.tab.browserUse' },
  'computer-use': { tab: 'computerControl', labelKey: 'settings.tab.computerControl' },
  connections: { tab: 'connections', labelKey: 'settings.tab.connections' },
  mcp: { tab: 'mcp', literalLabel: 'MCP' },
  hooks: { tab: 'hooks', labelKey: 'settings.tab.hooks' },
  'source-control': { tab: 'sourceControl', labelKey: 'settings.tab.sourceControl' },
  worktrees: { tab: 'worktrees', labelKey: 'settings.tab.worktrees' }
}

const TAB_TO_SECTION = new Map<SettingsTab, DesktopPluginSettingsSection>(
  (Object.entries(SECTION_ENTRIES) as [DesktopPluginSettingsSection, CoreSettingsSectionEntry][])
    .map(([section, entry]) => [entry.tab, section])
)

export const CORE_SETTINGS_SURFACES = (
  Object.keys(SECTION_ENTRIES) as DesktopPluginSettingsSection[]
).map((section) => `settings.${section}` as const)

export function settingsTabForSection(section: DesktopPluginSettingsSection): SettingsTab | null {
  return SECTION_ENTRIES[section]?.tab ?? null
}

export function coreSettingsSectionForTab(tab: SettingsTab): DesktopPluginSettingsSection | null {
  return TAB_TO_SECTION.get(tab) ?? null
}

export function coreSettingsSectionTitle(
  section: DesktopPluginSettingsSection,
  t: (key: MessageKey) => string
): string {
  const entry = SECTION_ENTRIES[section]
  return 'labelKey' in entry ? t(entry.labelKey) : entry.literalLabel
}
