import { ipcRenderer } from 'electron'
import {
  isKnownServerRequest,
  type KnownServerRequestPayload,
  type RawServerRequestPayload,
  type ServerRequestSettledPayload
} from '../shared/appServerBoundary'
import { TokenMulticastDispatcher } from './notificationDispatcher'

const known = new TokenMulticastDispatcher<KnownServerRequestPayload>()
const raw = new TokenMulticastDispatcher<RawServerRequestPayload>()
const settled = new TokenMulticastDispatcher<ServerRequestSettledPayload>()

ipcRenderer.on('appserver:server-request', (_event, payload: RawServerRequestPayload) => {
  if (isKnownServerRequest(payload)) known.dispatch(payload)
  else raw.dispatch(payload)
})
ipcRenderer.on('appserver:server-request-settled', (_event, payload: ServerRequestSettledPayload) => {
  settled.dispatch(payload)
})

export const serverRequestEvents = {
  onServerRequest: (callback: (payload: KnownServerRequestPayload) => void) => known.subscribe(callback),
  onServerRequestRaw: (callback: (payload: RawServerRequestPayload) => void) => raw.subscribe(callback),
  onServerRequestSettled: (callback: (payload: ServerRequestSettledPayload) => void) => settled.subscribe(callback)
}
