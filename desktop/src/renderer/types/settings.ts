export type ExtensionSettingsTab = `extension-settings:${string}:${string}:${string}`

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
  | ExtensionSettingsTab
