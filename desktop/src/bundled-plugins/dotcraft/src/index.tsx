import type { DesktopPluginActivate } from '@dotcraft/plugin'
import { useEffect } from 'react'

export interface DotCraftDesktopDriver {
  whenWorkbenchRestored(): Promise<void>
}

interface DriverState {
  disposed: boolean
  restored: boolean
  waiters: Set<{
    resolve: () => void
    reject: (error: Error) => void
  }>
}

declare global {
  var driver: DotCraftDesktopDriver | undefined
}

export const activate: DesktopPluginActivate = (host) => {
  if (globalThis.driver !== undefined) {
    throw new Error('A Desktop automation driver is already installed.')
  }

  const state: DriverState = {
    disposed: false,
    restored: false,
    waiters: new Set()
  }
  const driver: DotCraftDesktopDriver = {
    whenWorkbenchRestored: () => waitForWorkbench(state)
  }

  host.ui.add('app.background', () => <WorkbenchReadySignal state={state} />)
  globalThis.driver = driver

  return {
    dispose() {
      state.disposed = true
      rejectWaiters(state, new Error('The Desktop automation driver was disposed before the workbench restored.'))
      if (globalThis.driver === driver) {
        Reflect.deleteProperty(globalThis, 'driver')
      }
    }
  }
}

function WorkbenchReadySignal({ state }: { state: DriverState }): null {
  useEffect(() => {
    if (state.disposed) return
    state.restored = true
    for (const waiter of state.waiters) waiter.resolve()
    state.waiters.clear()
  }, [state])
  return null
}

function waitForWorkbench(state: DriverState): Promise<void> {
  if (state.restored) return Promise.resolve()
  if (state.disposed) {
    return Promise.reject(new Error('The Desktop automation driver is not active.'))
  }
  return new Promise<void>((resolve, reject) => {
    state.waiters.add({ resolve, reject })
  })
}

function rejectWaiters(state: DriverState, error: Error): void {
  for (const waiter of state.waiters) waiter.reject(error)
  state.waiters.clear()
}
