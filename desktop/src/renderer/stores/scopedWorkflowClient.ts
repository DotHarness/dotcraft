import type { ServerNotificationMethods } from '@dotcraft/sdk/contracts'
import type { WorkflowRunClient } from './workflowRunCache'
import { isThreadObservationCurrent } from './threadConnectionStore'
import { useConnectionStore } from './connectionStore'
import { useWorkspaceProjectsStore } from './workspaceProjectsStore'

export function createScopedWorkflowClient(connectionKey: string, connectionId: string): WorkflowRunClient & { dispose(): void } {
  let disposed = false
  const cleanups = new Set<() => void>()
  const own = (cleanup: () => void): (() => void) => {
    cleanups.add(cleanup)
    return () => { cleanups.delete(cleanup); cleanup() }
  }
  const scope = (): string => !disposed && isThreadObservationCurrent({ connectionKey, connectionId })
    ? `${connectionKey}\u0000${connectionId}` : ''
  return {
    connectionKey: scope,
    request(method, params) {
      return window.api.appServer.sendConnectionRequest(connectionKey, method, params, undefined, { connectionId })
    },
    onNotification(method, listener) {
      return own(window.api.appServer.onNotificationRaw(payload => {
        if (scope() && payload.connectionKey === connectionKey && payload.connectionId === connectionId && payload.method === method) {
          listener(payload.params as ServerNotificationMethods[typeof method]['params'])
        }
      }))
    },
    onConnectionChange(listener) {
      let previous = scope()
      const changed = (): void => {
        const next = scope()
        if (next === previous) return
        previous = next
        listener(next !== '')
      }
      const stopConnection = useConnectionStore.subscribe(changed)
      const stopProjects = useWorkspaceProjectsStore.subscribe(changed)
      return own(() => { stopConnection(); stopProjects() })
    },
    dispose() {
      disposed = true
      for (const cleanup of cleanups) cleanup()
      cleanups.clear()
    }
  }
}
