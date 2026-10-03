import * as Network from 'expo-network'
import { createContext, useContext, useEffect, useSyncExternalStore, type ReactNode } from 'react'
import { AppState } from 'react-native'
import type { MobileSession } from '../core/session'
import type { MobileState } from '../core/state'
import type { AppRuntime, DemoHooks } from './runtime'

const RuntimeContext = createContext<AppRuntime | null>(null)

export function RuntimeProvider({ runtime, children }: { runtime: AppRuntime; children: ReactNode }) {
  const { session } = runtime
  useEffect(() => {
    void session.boot()
    const appState = AppState.addEventListener('change', (state) => session.setForeground(state === 'active'))
    const network = Network.addNetworkStateListener(() => session.networkChanged())
    return () => {
      appState.remove()
      network.remove()
    }
  }, [session])
  return <RuntimeContext.Provider value={runtime}>{children}</RuntimeContext.Provider>
}

function useRuntime(): AppRuntime {
  const runtime = useContext(RuntimeContext)
  if (!runtime) throw new Error('RuntimeProvider is missing')
  return runtime
}

export function useSession(): MobileSession {
  return useRuntime().session
}

export function useDemo(): DemoHooks | null {
  return useRuntime().demo
}

export function useMobileState(): MobileState {
  const { store } = useRuntime().session
  return useSyncExternalStore(store.subscribe, store.getState, store.getState)
}
