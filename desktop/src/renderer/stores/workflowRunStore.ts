import { useConnectionStore } from './connectionStore'
import { useWorkspaceProjectsStore } from './workspaceProjectsStore'
import { workflowRunsFor, type WorkflowRunClient } from './workflowRunCache'

export { selectWorkflowRunEntry } from './workflowRunCache'

let lastConnection: { project: string; key: string } | undefined

export const desktopWorkflowRunClient: WorkflowRunClient = {
  connectionKey() {
    const project = useWorkspaceProjectsStore.getState().foregroundProjectId
    const connection = useConnectionStore.getState().connectionKey
    if (connection) lastConnection = { project, key: connection }
    const key = connection ?? (lastConnection?.project === project ? lastConnection.key : null)
    return key ? `${project}\u0000${key}` : project
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
      if (notification.method === method) listener(notification.params as never)
    })
  }
}

export const useWorkflowRunStore = workflowRunsFor(desktopWorkflowRunClient)
