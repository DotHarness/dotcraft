export type ConnectionMode = 'local' | 'remote'
export type BinarySource = 'bundled' | 'path' | 'custom'
export type BrowserUseApprovalMode = 'alwaysAsk' | 'askUnknown' | 'neverAsk'
export type TaskCompletionNotificationMode = 'whenUnfocused' | 'always' | 'never'
export type FollowUpQueueMode = 'queue' | 'steer'
export type LinkOpenTarget = 'inAppBrowser' | 'systemBrowser'

export interface LinkPreferences {
  web: LinkOpenTarget
  local: LinkOpenTarget
  favicons: boolean
}

export const DEFAULT_LINK_PREFERENCES: LinkPreferences = { web: 'inAppBrowser', local: 'inAppBrowser', favicons: true }

function normalizeLinkOpenTarget(value: unknown): LinkOpenTarget {
  return value === 'systemBrowser' ? 'systemBrowser' : 'inAppBrowser'
}

export function normalizeLinkPreferences(value: unknown): LinkPreferences {
  const source = value != null && typeof value === 'object' ? value as Partial<Record<keyof LinkPreferences, unknown>> : {}
  return { web: normalizeLinkOpenTarget(source.web), local: normalizeLinkOpenTarget(source.local), favicons: source.favicons !== false }
}
