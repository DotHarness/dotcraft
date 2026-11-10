import type { RawNotificationPayload } from '../../shared/appServerBoundary'
import { translate, type AppLocale } from '../../shared/locales'
import { currentMcpManagementScope } from './mcpManagement'
import { useMcpStore } from './mcpStore'
import { addToast } from './toastStore'

export function applyMcpNotification(payload: RawNotificationPayload, locale: AppLocale): boolean {
  if (payload.method !== 'mcpServer/startupStatus/updated' && payload.method !== 'mcpServer/oauthLogin/completed') return false
  const params = (payload.params ?? {}) as Record<string, unknown>
  if (payload.method === 'mcpServer/startupStatus/updated' && typeof params.threadId === 'string') return true
  const scope = currentMcpManagementScope()
  if (!scope
    || (payload.connectionKey != null && payload.connectionKey !== scope.connectionKey)
    || (payload.connectionId != null && payload.connectionId !== scope.connectionId)) return true
  if (payload.method === 'mcpServer/startupStatus/updated') {
    if (params.status !== 'starting') useMcpStore.getState().invalidateCatalog()
  } else {
    const success = params.success === true
    addToast(translate(locale, success ? 'mcp.oauth.completed' : 'mcp.oauth.failed'), success ? 'success' : 'error')
  }
  return true
}
