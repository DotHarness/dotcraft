import { create } from 'zustand'

import { useConnectionStore } from './connectionStore'
import { normalizeWorkspaceConfigChangedPayload } from '../utils/workspaceConfigChanged'

/**
 * Holds the foreground workspace's source control binding so non-settings surfaces
 * (e.g. the commit entry in the thread header) can gate behavior on the effective
 * provider. `sourceControl/get` always targets the connected (foreground) workspace,
 * so the result describes whichever workspace the active AppServer connection is bound
 * to. The store is refreshed when the consumer's workspace path changes, on every
 * connection epoch change (a workspace switch promotes a different connection and bumps
 * the epoch), and on `workspace/configChanged` notifications carrying the `sourceControl`
 * region.
 */
interface SourceControlState {
  workspacePath: string | null
  effectiveProvider: string | null
  status: string | null
  perforceChangelist: boolean | null
  ensure: (workspacePath: string | null | undefined, capable: boolean) => void
  refresh: (workspacePath: string | null | undefined, capable: boolean) => Promise<void>
}

let notificationUnsubscribe: (() => void) | null = null
let connectionUnsubscribe: (() => void) | null = null
// Monotonic token so only the most recent in-flight refresh applies its result. `sourceControl/get`
// describes the foreground connection, so the latest request — not the request whose path still
// matches the cache — is the authoritative one when several consumers refresh during a switch.
let refreshToken = 0

function ensureSubscriptions(): void {
  if (typeof window === 'undefined') return
  if (!notificationUnsubscribe && window.api?.appServer?.onNotification) {
    notificationUnsubscribe = window.api.appServer.onNotification((payload) => {
      const event = normalizeWorkspaceConfigChangedPayload(payload as { method: string; params: unknown })
      if (!event?.regions.includes('sourceControl')) return
      const { workspacePath, refresh } = useSourceControlStore.getState()
      if (workspacePath) void refresh(workspacePath, true)
    })
  }
  if (!connectionUnsubscribe) {
    // A workspace switch promotes a different AppServer connection and re-emits a `connected`
    // status, bumping the epoch. Re-fetch then so the cached binding follows the new foreground
    // workspace even when the consumer's workspace path string did not change (e.g. reconnect).
    connectionUnsubscribe = useConnectionStore.subscribe((state, prev) => {
      if (state.connectionEpoch === prev.connectionEpoch) return
      if (state.status !== 'connected') return
      const { workspacePath, refresh } = useSourceControlStore.getState()
      if (workspacePath) void refresh(workspacePath, true)
    })
  }
}

export const useSourceControlStore = create<SourceControlState>((set, get) => ({
  workspacePath: null,
  effectiveProvider: null,
  status: null,
  perforceChangelist: null,
  ensure: (workspacePath, capable) => {
    if (!capable || !workspacePath) {
      if (get().workspacePath !== null) {
        set({ workspacePath: null, effectiveProvider: null, status: null, perforceChangelist: null })
      }
      return
    }
    ensureSubscriptions()
    if (get().workspacePath === workspacePath) return
    // Set the target path optimistically so consumers do not flash the previous workspace's
    // provider while the new binding loads.
    set({ workspacePath, effectiveProvider: null, status: null, perforceChangelist: null })
    void get().refresh(workspacePath, capable)
  },
  refresh: async (workspacePath, capable) => {
    if (!capable || !workspacePath) return
    const token = ++refreshToken
    try {
      const snap = (await window.api.appServer.sendRequest('sourceControl/get', {}, 20_000)) as {
        effectiveProvider?: string
        status?: string
        capabilities?: {
          perforceChangelist?: boolean
        }
      }
      // Apply only if this is still the latest refresh; the response describes the current
      // foreground workspace, which is the one the latest request was issued for.
      if (token !== refreshToken) return
      set({
        workspacePath,
        effectiveProvider: snap?.effectiveProvider ?? null,
        status: snap?.status ?? null,
        perforceChangelist: snap?.capabilities?.perforceChangelist ?? null
      })
    } catch {
      if (token !== refreshToken) return
      set({ effectiveProvider: null, status: null, perforceChangelist: null })
    }
  }
}))
