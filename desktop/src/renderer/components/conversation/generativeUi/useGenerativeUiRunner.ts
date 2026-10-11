import { useCallback, useEffect, useRef, useState } from 'react'
import { GenerativeUiConnection } from './generativeUiConnection'
import type {
  GenerativeUiAction,
  GenerativeUiBreakpoint,
  GenerativeUiFailure,
  GenerativeUiJson,
  GenerativeUiNode,
  GenerativeUiSavedState,
  GenerativeUiTheme
} from './generativeUiProtocol'
import { acquireGenerativeUiRunner } from './generativeUiRunnerHost'

export type GenerativeUiRunnerState =
  | { status: 'loading'; tree: null; failure: null }
  | { status: 'ready'; tree: GenerativeUiNode[]; failure: null }
  | { status: 'failed'; tree: GenerativeUiNode[] | null; failure: GenerativeUiFailure }

interface RunnerOptions {
  code: string
  enabled: boolean
  attempt: number
  theme: GenerativeUiTheme
  breakpoint: GenerativeUiBreakpoint
  initialState?: GenerativeUiSavedState
  restoredState?: GenerativeUiSavedState
  onAction?: (action: GenerativeUiAction) => void
  onStateChange?: (state: GenerativeUiSavedState) => void
}

const LOADING: GenerativeUiRunnerState = { status: 'loading', tree: null, failure: null }

export function useGenerativeUiRunner(options: RunnerOptions): {
  state: GenerativeUiRunnerState
  invoke: (id: string, args: GenerativeUiJson[]) => void
  reportHostError: (message: string) => void
} {
  const { code, enabled, attempt, theme, breakpoint, restoredState } = options
  const initialStateJson = JSON.stringify(options.initialState ?? null)
  const [state, setState] = useState<GenerativeUiRunnerState>(LOADING)
  const connectionRef = useRef<GenerativeUiConnection | null>(null)
  const latest = useRef(options)
  latest.current = options

  const fail = useCallback((failure: GenerativeUiFailure): void => {
    connectionRef.current?.dispose()
    connectionRef.current = null
    setState(current => current.status === 'failed' ? current : { status: 'failed', tree: failure.stage === 'host_error' ? null : current.tree, failure })
  }, [])

  useEffect(() => {
    setState(LOADING)
    if (!enabled) return
    let active = true
    const release = acquireGenerativeUiRunner({
      onPort: port => {
        const connection = new GenerativeUiConnection(port, {
          onSnapshot: tree => { if (active) setState({ status: 'ready', tree, failure: null }) },
          onAction: action => { if (active) latest.current.onAction?.(action) },
          onState: state => { if (active) latest.current.onStateChange?.(state) },
          onFailure: failure => { if (active) fail(failure) }
        })
        connectionRef.current = connection
        const { theme, breakpoint, restoredState, initialState } = latest.current
        connection.start({ code, theme, breakpoint, initialState: restoredState ?? initialState })
      },
      onFailure: failure => { if (active) fail(failure) }
    })
    return () => {
      active = false
      release()
      connectionRef.current?.dispose()
      connectionRef.current = null
    }
  }, [attempt, code, enabled, fail, initialStateJson])

  useEffect(() => {
    connectionRef.current?.setGlobals(theme, breakpoint)
  }, [breakpoint, theme])

  useEffect(() => {
    if (restoredState) connectionRef.current?.restore(restoredState)
  }, [restoredState])

  const invoke = useCallback((id: string, args: GenerativeUiJson[]): void => {
    connectionRef.current?.invoke(id, args)
  }, [])

  const reportHostError = useCallback((message: string): void => {
    fail({ stage: 'host_error', message })
  }, [fail])

  return { state, invoke, reportHostError }
}
