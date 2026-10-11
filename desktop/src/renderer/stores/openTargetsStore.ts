import { create } from 'zustand'
import {
  mergeOpenTargetPreferences,
  rememberOpenTarget,
  type OpenTargetInfo,
  type OpenTargetPreferences
} from '../../shared/openTargets'

export type OpenTargetsStatus = 'idle' | 'loading' | 'ready' | 'error'

interface OpenTargetsState {
  status: OpenTargetsStatus
  targets: OpenTargetInfo[]
  preferences: OpenTargetPreferences | undefined
  refresh(): Promise<void>
  remember(targetId: string, projectRoot: string | null | undefined): void
  setGlobal(targetId: string): void
}

let inFlight: Promise<void> | null = null

function savePreferences(update: OpenTargetPreferences): void {
  void window.api.settings.set({ openTargetPreferences: update }).catch(() => {})
}

export const useOpenTargetsStore = create<OpenTargetsState>((set, get) => ({
  status: 'idle',
  targets: [],
  preferences: undefined,

  refresh() {
    if (inFlight) return inFlight
    if (get().status !== 'ready') set({ status: 'loading' })
    inFlight = (async () => {
      const [targets, settings] = await Promise.all([window.api.shell.listOpenTargets(), window.api.settings.get()])
      set({ status: 'ready', targets, preferences: settings.openTargetPreferences })
    })()
      .catch(() => {
        if (get().status !== 'ready') set({ status: 'error' })
      })
      .finally(() => {
        inFlight = null
      })
    return inFlight
  },

  remember(targetId, projectRoot) {
    const update = rememberOpenTarget(targetId, projectRoot)
    set((state) => ({ preferences: mergeOpenTargetPreferences(state.preferences, update) }))
    savePreferences(update)
  },

  setGlobal(targetId) {
    const update = { global: targetId }
    set((state) => ({ preferences: mergeOpenTargetPreferences(state.preferences, update) }))
    savePreferences(update)
  }
}))
