import { useEffect } from 'react'
import { create } from 'zustand'
import type { AppInfo } from './appBindingStore'

export interface OriginApp {
  appId: string
  displayName: string
  icon?: string | null
}

export interface OriginAppConnection {
  connectionKey: string
  connectionId: string | null
}

interface OriginAppStore {
  byScope: Record<string, Record<string, OriginApp>>
  ensure(connection: OriginAppConnection): void
  refresh(connectionKey: string | null): Promise<void>
  reset(): void
}

const connections = new Map<string, OriginAppConnection>()
const inFlight = new Map<string, number>()
let generation = 0
let stopListening: (() => void) | null = null

function scopeOf(connection: OriginAppConnection): string {
  return `${connection.connectionKey}\u0000${connection.connectionId ?? ''}`
}

export const useOriginAppStore = create<OriginAppStore>((set, get) => {
  const load = async (connection: OriginAppConnection): Promise<void> => {
    const scope = scopeOf(connection)
    const requested = ++generation
    inFlight.set(scope, requested)
    try {
      const result = await window.api.appServer.sendConnectionRequest(
        connection.connectionKey,
        'app/list',
        { includeCatalog: false, includeDisabled: false },
        undefined,
        connection.connectionId ? { connectionId: connection.connectionId } : undefined
      ) as { apps?: AppInfo[] } | null
      if (inFlight.get(scope) !== requested) return
      const byChannel: Record<string, OriginApp> = {}
      for (const app of result?.apps ?? []) {
        const channel = app.originChannel?.trim().toLowerCase()
        if (channel && !byChannel[channel]) {
          byChannel[channel] = { appId: app.appId, displayName: app.displayName, icon: app.icon }
        }
      }
      set((state) => {
        const byScope: Record<string, Record<string, OriginApp>> = {}
        for (const [key, catalog] of Object.entries(state.byScope)) {
          if (connections.get(key)?.connectionKey !== connection.connectionKey) byScope[key] = catalog
          else if (key !== scope) connections.delete(key)
        }
        byScope[scope] = byChannel
        return { byScope }
      })
      connections.set(scope, connection)
    } catch {
    } finally {
      if (inFlight.get(scope) === requested) inFlight.delete(scope)
    }
  }

  return {
    byScope: {},

    ensure(connection) {
      stopListening ??= window.api.appServer.onNotificationRaw((payload) => {
        if (!payload.foreground && payload.method === 'app/list/updated') void get().refresh(payload.connectionKey ?? null)
      })
      const scope = scopeOf(connection)
      if (!(scope in get().byScope) && !inFlight.has(scope)) void load(connection)
    },

    async refresh(connectionKey) {
      if (!connectionKey) return
      await Promise.all([...connections.entries()]
        .filter(([scope, connection]) => connection.connectionKey === connectionKey && scope in get().byScope)
        .map(([, connection]) => load(connection)))
    },

    reset() {
      stopListening?.()
      stopListening = null
      connections.clear()
      inFlight.clear()
      set({ byScope: {} })
    }
  }
})

export function useOriginApp(originChannel: string | null, connection: OriginAppConnection | null): OriginApp | null {
  const key = originChannel?.trim().toLowerCase() ?? ''
  const scope = connection ? scopeOf(connection) : ''
  const app = useOriginAppStore((state) => (key && scope ? state.byScope[scope]?.[key] ?? null : null))
  useEffect(() => {
    if (key && connection) useOriginAppStore.getState().ensure(connection)
  }, [key, scope])
  return app
}
