import { useEffect } from 'react'
import { create } from 'zustand'
import type { AppInfo } from './appBindingStore'

export interface OriginApp {
  appId: string
  displayName: string
  icon?: string | null
}

interface OriginAppStore {
  byChannel: Record<string, OriginApp>
  loaded: boolean
  ensure(): void
  refresh(): Promise<void>
  reset(): void
}

let inFlight: Promise<void> | null = null
let generation = 0

export const useOriginAppStore = create<OriginAppStore>((set, get) => {
  const fetchOriginApps = async (requested: number): Promise<void> => {
    try {
      const result = await window.api.appServer.sendRequest('app/list', {
        includeCatalog: false,
        includeDisabled: false
      }) as { apps?: AppInfo[] }
      if (requested !== generation) return
      const byChannel: Record<string, OriginApp> = {}
      for (const app of result.apps ?? []) {
        const channel = app.originChannel?.trim().toLowerCase()
        if (channel && !byChannel[channel]) {
          byChannel[channel] = { appId: app.appId, displayName: app.displayName, icon: app.icon }
        }
      }
      set({ byChannel, loaded: true })
    } catch {
      // Rows keep the generic channel badge until the next load.
    }
  }

  const load = (): Promise<void> => {
    const request: Promise<void> = fetchOriginApps(++generation).finally(() => {
      if (inFlight === request) inFlight = null
    })
    inFlight = request
    return request
  }

  return {
    byChannel: {},
    loaded: false,

    ensure() {
      if (!get().loaded && !inFlight) void load()
    },

    refresh: load,

    reset() {
      generation += 1
      inFlight = null
      set({ byChannel: {}, loaded: false })
    }
  }
})

export function useOriginApp(originChannel: string | null): OriginApp | null {
  const key = originChannel?.trim().toLowerCase() ?? ''
  const app = useOriginAppStore((state) => (key ? state.byChannel[key] ?? null : null))
  useEffect(() => {
    if (key) useOriginAppStore.getState().ensure()
  }, [key])
  return app
}
