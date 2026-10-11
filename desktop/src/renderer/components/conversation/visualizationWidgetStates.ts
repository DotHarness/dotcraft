import type { KnownNotificationPayload } from '../../../shared/appServerBoundary'
import { visualizationKey } from './visualizationParser'
import type { VisualizationWidgetState } from './visualizationSecurity'

type WidgetStateListener = (state: VisualizationWidgetState | null) => void

const listeners = new Map<string, Map<string, WidgetStateListener>>()
let unsubscribeRemoteUpdates: (() => void) | null = null

function receiveRemoteUpdate(payload: KnownNotificationPayload): void {
  if (payload.method !== 'visualization/widgetState/updated') return
  const { threadId, path, widgetState } = payload.params
  publishVisualizationWidgetState(threadId, path, '', widgetState
    ? { modelContent: widgetState.modelContent ?? null, privateContent: widgetState.privateContent ?? null }
    : null)
}

export function subscribeVisualizationWidgetState(
  threadId: string,
  path: string,
  viewId: string,
  listener: WidgetStateListener
): () => void {
  unsubscribeRemoteUpdates ??= window.api.appServer.onNotification(receiveRemoteUpdate)
  const key = visualizationKey(threadId, path)
  const views = listeners.get(key) ?? new Map<string, WidgetStateListener>()
  views.set(viewId, listener)
  listeners.set(key, views)
  return () => {
    views.delete(viewId)
    if (views.size === 0 && listeners.get(key) === views) listeners.delete(key)
    if (listeners.size > 0) return
    unsubscribeRemoteUpdates?.()
    unsubscribeRemoteUpdates = null
  }
}

export function publishVisualizationWidgetState(
  threadId: string,
  path: string,
  sourceViewId: string,
  state: VisualizationWidgetState | null
): void {
  for (const [viewId, listener] of [...(listeners.get(visualizationKey(threadId, path)) ?? [])]) {
    if (viewId !== sourceViewId) listener(state)
  }
}
