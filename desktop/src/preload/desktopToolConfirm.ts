import { ipcRenderer } from 'electron'
import {
  DESKTOP_TOOL_CONFIRM_DISMISSED_CHANNEL,
  DESKTOP_TOOL_CONFIRM_REQUEST_CHANNEL,
  DESKTOP_TOOL_CONFIRM_RESPOND_CHANNEL,
  type DesktopToolConfirmRequest
} from '../shared/desktopToolConfirm'

type RequestCallback = (request: DesktopToolConfirmRequest) => void
type DismissedCallback = (payload: { requestId: string }) => void

const requestCallbacks = new Set<RequestCallback>()
const dismissedCallbacks = new Set<DismissedCallback>()

ipcRenderer.on(
  DESKTOP_TOOL_CONFIRM_REQUEST_CHANNEL,
  (_event: Electron.IpcRendererEvent, request: DesktopToolConfirmRequest) => {
    for (const callback of requestCallbacks) callback(request)
  }
)

ipcRenderer.on(
  DESKTOP_TOOL_CONFIRM_DISMISSED_CHANNEL,
  (_event: Electron.IpcRendererEvent, payload: { requestId: string }) => {
    for (const callback of dismissedCallbacks) callback(payload)
  }
)

export const desktopToolConfirmPreload = {
  onDesktopToolConfirmRequest(callback: RequestCallback): () => void {
    requestCallbacks.add(callback)
    return () => {
      requestCallbacks.delete(callback)
    }
  },
  onDesktopToolConfirmDismissed(callback: DismissedCallback): () => void {
    dismissedCallbacks.add(callback)
    return () => {
      dismissedCallbacks.delete(callback)
    }
  },
  respondDesktopToolConfirm(params: { requestId: string; confirmed: boolean }): Promise<boolean> {
    return ipcRenderer.invoke(DESKTOP_TOOL_CONFIRM_RESPOND_CHANNEL, params)
  }
}
