import { create } from 'zustand'

export type HookSource = 'user' | 'workspace' | 'plugin' | 'unknown'
export type HookTrustStatus = 'managed' | 'trusted' | 'untrusted' | 'modified'

export interface HookMetadata {
  key: string
  eventName: string
  handlerType: string
  matcher?: string | null
  condition?: string | null
  command?: string | null
  timeoutSec?: number | null
  executionMode?: string | null
  asyncRewake?: boolean
  rewakeMessage?: string | null
  rewakeSummary?: string | null
  shell?: string | null
  once?: boolean
  statusMessage?: string | null
  sourcePath?: string | null
  source: HookSource
  pluginId?: string | null
  displayOrder: number
  enabled: boolean
  isManaged: boolean
  currentHash: string
  trustStatus: HookTrustStatus
}

export interface HookErrorInfo {
  source: string
  path?: string | null
  message: string
}

interface HooksListResult {
  hooks?: HookMetadata[]
  warnings?: HookErrorInfo[]
  errors?: HookErrorInfo[]
}

interface HooksSetStateResult extends HooksListResult {}

interface HooksState {
  hooks: HookMetadata[]
  warnings: HookErrorInfo[]
  errors: HookErrorInfo[]
  loading: boolean
  updatingKey: string | null
  error: string | null

  fetchHooks(): Promise<void>
  setHookState(key: string, state: { enabled?: boolean; trustedHash?: string }): Promise<void>
  reset(): void
}

export const useHooksStore = create<HooksState>((set) => ({
  hooks: [],
  warnings: [],
  errors: [],
  loading: false,
  updatingKey: null,
  error: null,

  async fetchHooks() {
    set({ loading: true, error: null })
    try {
      const result = (await window.api.appServer.sendRequest('hooks/list', {})) as HooksListResult
      set({
        hooks: normalizeHooks(result.hooks),
        warnings: result.warnings ?? [],
        errors: result.errors ?? [],
        loading: false
      })
    } catch (e: unknown) {
      set({ error: errorMessage(e), loading: false })
    }
  },

  async setHookState(key, state) {
    set({ updatingKey: key, error: null })
    try {
      const result = (await window.api.appServer.sendRequest('hooks/setState', {
        key,
        ...state
      })) as HooksSetStateResult
      set({
        hooks: normalizeHooks(result.hooks),
        warnings: result.warnings ?? [],
        errors: result.errors ?? [],
        updatingKey: null
      })
    } catch (e: unknown) {
      set({ error: errorMessage(e), updatingKey: null })
      throw e
    }
  },

  reset() {
    set({
      hooks: [],
      warnings: [],
      errors: [],
      loading: false,
      updatingKey: null,
      error: null
    })
  }
}))

function normalizeHooks(hooks: HookMetadata[] | undefined): HookMetadata[] {
  return [...(hooks ?? [])].sort((a, b) => a.displayOrder - b.displayOrder || a.key.localeCompare(b.key))
}

function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error)
}
