import { create } from 'zustand'

import { normalizeWorkspaceConfigChangedPayload } from '../utils/workspaceConfigChanged'

/**
 * Holds the foreground workspace's source control binding so non-settings surfaces
 * (e.g. the commit entry in the thread header) can gate behavior on the effective
 * provider. `sourceControl/get` always targets the connected (foreground) workspace,
 * so results are cached by workspace path and refreshed on workspace switch and on
 * `workspace/configChanged` notifications carrying the `sourceControl` region.
 */
interface SourceControlState {
  workspacePath: string | null
  effectiveProvider: string | null
  status: string | null
  ensure: (workspacePath: string | null | undefined, capable: boolean) => void
  refresh: (workspacePath: string | null | undefined, capable: boolean) => Promise<void>
}

let notificationUnsubscribe: (() => void) | null = null

function ensureNotificationSubscription(): void {
  if (notificationUnsubscribe) return
  if (typeof window === 'undefined' || !window.api?.appServer?.onNotification) return
  notificationUnsubscribe = window.api.appServer.onNotification((payload) => {
    const event = normalizeWorkspaceConfigChangedPayload(payload as { method: string; params: unknown })
    if (!event?.regions.includes('sourceControl')) return
    const { workspacePath, refresh } = useSourceControlStore.getState()
    if (workspacePath) void refresh(workspacePath, true)
  })
}

export const useSourceControlStore = create<SourceControlState>((set, get) => ({
  workspacePath: null,
  effectiveProvider: null,
  status: null,
  ensure: (workspacePath, capable) => {
    if (!capable || !workspacePath) {
      if (get().workspacePath !== null) {
        set({ workspacePath: null, effectiveProvider: null, status: null })
      }
      return
    }
    ensureNotificationSubscription()
    if (get().workspacePath === workspacePath) return
    // Set the target path optimistically so concurrent ensures dedupe to one fetch.
    set({ workspacePath, effectiveProvider: null, status: null })
    void get().refresh(workspacePath, capable)
  },
  refresh: async (workspacePath, capable) => {
    if (!capable || !workspacePath) return
    try {
      const snap = (await window.api.appServer.sendRequest('sourceControl/get', {}, 20_000)) as {
        effectiveProvider?: string
        status?: string
      }
      // Ignore stale responses if the foreground workspace changed during the request.
      if (get().workspacePath !== workspacePath) return
      set({ effectiveProvider: snap?.effectiveProvider ?? null, status: snap?.status ?? null })
    } catch {
      if (get().workspacePath !== workspacePath) return
      set({ effectiveProvider: null, status: null })
    }
  }
}))
