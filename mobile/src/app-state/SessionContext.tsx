import * as Network from 'expo-network'
import { useRouter } from 'expo-router'
import { createContext, useContext, useEffect, useMemo, useSyncExternalStore, type ReactNode } from 'react'
import { AppState } from 'react-native'
import type { ComputerLink } from '../core/computerLink'
import type { MobileSession } from '../core/session'
import type { ComputerState, MobileState } from '../core/state'
import { selectStore } from '../core/selection'
import { selectComputerStore } from '../core/computerSelection'
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

export function useMobileSelector<T>(select: (state: MobileState) => T, equal: (left: T, right: T) => boolean = Object.is): T {
  const { store } = useRuntime().session
  const selected = useMemo(() => selectStore(store, select, equal), [store, select, equal])
  return useSyncExternalStore(selected.subscribe, selected.getState, selected.getState)
}

const wholeState = (state: MobileState) => state

export function useMobileState(): MobileState {
  return useMobileSelector(wholeState)
}

export function ComputerProvider({ computerId, children }: { computerId: string; children: ReactNode }) {
  const present = useMobileSelector((state) => Boolean(state.computers[computerId]))
  const gone = useMobileSelector((state) => state.hydrated && !state.computers[computerId])
  const router = useRouter()
  useEffect(() => {
    if (!gone) return
    if (router.canDismiss()) router.dismissAll()
    else router.replace('/')
  }, [gone, router])
  if (!present) return null
  return <ComputerContext.Provider value={computerId}>{children}</ComputerContext.Provider>
}

function useComputerId(): string {
  const computerId = useContext(ComputerContext)
  if (!computerId) throw new Error('ComputerProvider is missing')
  return computerId
}

export function useComputerSelector<T>(select: (state: ComputerState) => T, equal: (left: T, right: T) => boolean = Object.is): T {
  const computerId = useComputerId()
  const { store } = useSession()
  const selected = useMemo(() => selectComputerStore(store, computerId, select, equal), [computerId, store, select, equal])
  return useSyncExternalStore(selected.subscribe, selected.getState, selected.getState)
}

export function useComputer(): ComputerState {
  return useComputerSelector((state) => state)
}

export function useComputerLink(): ComputerLink {
  return useSession().computer(useComputerId())!
}
