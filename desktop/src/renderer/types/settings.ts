export type DesktopPluginSettingsTab = `desktop-plugin-settings:${string}:${string}`

export type SettingsTab =
  | 'profile'
  | 'general'
  | 'appearance'
  | 'voice'
  | 'personalization'
  | 'dreams'
  | 'connection'
  | 'servers'
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
