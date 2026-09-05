export type DesktopPluginSettingsTab = `desktop-plugin-settings:${string}:${string}`

export type SettingsTab =
  | 'profile'
  | 'general'
  | 'appearance'
  | 'voice'
  | 'personalization'
  | 'dreams'
  | 'connections'
  | 'llmService'
  | 'browserUse'
  | 'computerControl'
  | 'usage'
  | 'archivedThreads'
  | 'sourceControl'
  | 'hooks'
  | 'mcp'
  | 'subAgents'
  | DesktopPluginSettingsTab

/** Ids a persisted or deep-linked location may still name after a page merge. */
const REPLACED_SETTINGS_TABS: Record<string, SettingsTab> = {
  connection: 'connections',
  servers: 'connections'
}

export function normalizeSettingsTab(tab: string): SettingsTab {
  return REPLACED_SETTINGS_TABS[tab] ?? (tab as SettingsTab)
}
