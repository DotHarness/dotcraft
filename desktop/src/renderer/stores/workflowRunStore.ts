import { useConnectionStore } from './connectionStore'
import { useWorkspaceProjectsStore } from './workspaceProjectsStore'
import { workflowRunsFor, type WorkflowRunClient } from './workflowRunCache'
import { getThreadConnection } from './threadConnectionStore'
import { createScopedWorkflowClient } from './scopedWorkflowClient'

export { selectWorkflowRunEntry } from './workflowRunCache'

let lastConnection: { project: string; key: string; id: string | null } | undefined

export const desktopWorkflowRunClient: WorkflowRunClient = {
  forThread(threadId) {
    const source = getThreadConnection(threadId) ?? useConnectionStore.getState()
    return createScopedWorkflowClient(source.connectionKey ?? '', source.connectionId ?? '')
  },
  connectionKey() {
    const project = useWorkspaceProjectsStore.getState().foregroundProjectId
    const { connectionKey, connectionId } = useConnectionStore.getState()
    if (connectionKey) lastConnection = { project, key: connectionKey, id: connectionId }
    const source = lastConnection?.project === project ? lastConnection : null
    return source ? `${project}\u0000${source.key}\u0000${source.id ?? ''}` : project
  },
  onConnectionChange(listener) {
    const stopConnection = useConnectionStore.subscribe((next, previous) => {
      if (next.connectionEpoch !== previous.connectionEpoch) listener(next.status === 'connected')
    })
    const stopProject = useWorkspaceProjectsStore.subscribe((next, previous) => {
      if (next.foregroundProjectId !== previous.foregroundProjectId) listener(false)
    })
    return () => { stopConnection(); stopProject() }
  },
  request(method, params) {
    return window.api.appServer.sendRequestRaw(method, params) as never
  },
  onNotification(method, listener) {
    return window.api.appServer.onNotificationRaw((notification) => {
      const source = useConnectionStore.getState()
      if (source.status === 'connected' && notification.method === method && notification.connectionKey === source.connectionKey
        && notification.connectionId === source.connectionId) listener(notification.params as never)
    })
  }
}

export const useWorkflowRunStore = workflowRunsFor(desktopWorkflowRunClient)
