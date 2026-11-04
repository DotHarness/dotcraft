export type ConnectionMode = 'local' | 'remote'
export type BinarySource = 'bundled' | 'path' | 'custom'
export type BrowserUseApprovalMode = 'alwaysAsk' | 'askUnknown' | 'neverAsk'
export type TaskCompletionNotificationMode = 'whenUnfocused' | 'always' | 'never'
export type FollowUpQueueMode = 'queue' | 'steer'
export type LinkOpenTarget = 'inAppBrowser' | 'systemBrowser'

export interface LinkOpenPreferences {
  web: LinkOpenTarget
  local: LinkOpenTarget
}

export const DEFAULT_LINK_OPEN_PREFERENCES: LinkOpenPreferences = { web: 'inAppBrowser', local: 'inAppBrowser' }

function normalizeLinkOpenTarget(value: unknown): LinkOpenTarget {
  return value === 'systemBrowser' ? 'systemBrowser' : 'inAppBrowser'
}

export function normalizeLinkOpenPreferences(value: unknown): LinkOpenPreferences {
  const source = value != null && typeof value === 'object' ? value as Partial<Record<keyof LinkOpenPreferences, unknown>> : {}
  return { web: normalizeLinkOpenTarget(source.web), local: normalizeLinkOpenTarget(source.local) }
}
