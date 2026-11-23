import { ipcRenderer } from 'electron'
import {
  PROJECT_CREATION_CONFIRM_DISMISSED_CHANNEL,
  PROJECT_CREATION_CONFIRM_REQUEST_CHANNEL,
  PROJECT_CREATION_CONFIRM_RESPOND_CHANNEL,
  type ProjectCreationConfirmRequest
} from '../shared/projectCreationConfirm'

type RequestCallback = (request: ProjectCreationConfirmRequest) => void
type DismissedCallback = (payload: { requestId: string }) => void

const requestCallbacks = new Set<RequestCallback>()
const dismissedCallbacks = new Set<DismissedCallback>()

ipcRenderer.on(
  PROJECT_CREATION_CONFIRM_REQUEST_CHANNEL,
  (_event: Electron.IpcRendererEvent, request: ProjectCreationConfirmRequest) => {
    for (const callback of requestCallbacks) callback(request)
  }
)

ipcRenderer.on(
  PROJECT_CREATION_CONFIRM_DISMISSED_CHANNEL,
  (_event: Electron.IpcRendererEvent, payload: { requestId: string }) => {
    for (const callback of dismissedCallbacks) callback(payload)
  }
)

export const projectCreationConfirmPreload = {
  onProjectCreationConfirmRequest(callback: RequestCallback): () => void {
    requestCallbacks.add(callback)
    return () => {
      requestCallbacks.delete(callback)
    }
  },
  onProjectCreationConfirmDismissed(callback: DismissedCallback): () => void {
    dismissedCallbacks.add(callback)
    return () => {
      dismissedCallbacks.delete(callback)
    }
  },
  respondProjectCreationConfirm(params: { requestId: string; confirmed: boolean }): Promise<boolean> {
    return ipcRenderer.invoke(PROJECT_CREATION_CONFIRM_RESPOND_CHANNEL, params)
  }
}
