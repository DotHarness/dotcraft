import * as Network from 'expo-network'
import { useRouter } from 'expo-router'
import { createContext, useContext, useEffect, useSyncExternalStore, type ReactNode } from 'react'
import { AppState } from 'react-native'
import type { ComputerLink } from '../core/computerLink'
import type { MobileSession } from '../core/session'
import type { ComputerState, MobileState } from '../core/state'
import type { AppRuntime, DemoHooks } from './runtime'

const RuntimeContext = createContext<AppRuntime | null>(null)
const ComputerContext = createContext<string | null>(null)

export function RuntimeProvider({ runtime, children }: { runtime: AppRuntime; children: ReactNode }) {
  const { session } = runtime
  useEffect(() => {
    void session.boot()
    const appState = AppState.addEventListener('change', (state) => session.setForeground(state === 'active'))
    const network = Network.addNetworkStateListener(({ type, isConnected, isInternetReachable }) =>
      session.networkChanged(`${type}/${isConnected}/${isInternetReachable}`),
    )
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

export function ComputerProvider({ computerId, children }: { computerId: string; children: ReactNode }) {
  const state = useMobileState()
  const router = useRouter()
  const gone = state.hydrated && !state.computers[computerId]
  useEffect(() => {
    if (!gone) return
    if (router.canDismiss()) router.dismissAll()
    else router.replace('/')
  }, [gone, router])
  if (!state.computers[computerId]) return null
  return <ComputerContext.Provider value={computerId}>{children}</ComputerContext.Provider>
}

function useComputerId(): string {
  const computerId = useContext(ComputerContext)
  if (!computerId) throw new Error('ComputerProvider is missing')
  return computerId
}

export function useComputer(): ComputerState {
  return useMobileState().computers[useComputerId()]
}

export function useComputerLink(): ComputerLink {
  return useSession().computer(useComputerId())!
}
