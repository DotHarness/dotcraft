import { Component, useEffect, useMemo, useRef, useState, type ReactNode, type RefObject } from 'react'
import { CircleAlert } from 'lucide-react'
import { useT } from '../../../contexts/LocaleContext'
import { useDocumentThemeMode } from '../../../utils/theme'
import { Button } from '../../ui/Button'
import { Skeleton } from '../../ui/Skeleton'
import { GenerativeUiRenderContext, type GenerativeUiRenderContextValue } from './GenerativeUiContext'
import { generativeUiBreakpointForWidth, type GenerativeUiAction, type GenerativeUiBreakpoint, type GenerativeUiFailure, type GenerativeUiJson, type GenerativeUiSavedState } from './generativeUiProtocol'
import { GenerativeUiNodes } from './GenerativeUiRenderer'
import { useGenerativeUiRunner, type GenerativeUiRunnerState } from './useGenerativeUiRunner'
import styles from './GenerativeUi.module.css'

export interface GenerativeUiViewProps {
  code: string
  streaming?: boolean
  deferred?: boolean
  /** Disables interaction without dimming, for example while the block's turn runs. */
  locked?: boolean
  initialState?: GenerativeUiSavedState
  /** A state saved elsewhere, restored into the running program when it changes. */
  restoredState?: GenerativeUiSavedState
  onAction?: (action: GenerativeUiAction) => void
  onStateChange?: (state: GenerativeUiSavedState) => void
}

export function GenerativeUiView({ code, streaming = false, deferred = false, locked = false, initialState, restoredState, onAction, onStateChange }: GenerativeUiViewProps): JSX.Element {
  const rootRef = useRef<HTMLDivElement>(null)
  const [attempt, setAttempt] = useState(0)
  const theme = useDocumentThemeMode() === 'light' ? 'light' : 'dark'
  const breakpoint = useContainerBreakpoint(rootRef)
  const { state, invoke, reportHostError } = useGenerativeUiRunner({
    code,
    enabled: !streaming && !deferred,
    attempt,
    theme,
    breakpoint,
    initialState,
    restoredState,
    onAction,
    onStateChange
  })
  return (
    <div ref={rootRef} className={styles.root} aria-busy={state.status === 'loading'}>
      <GenerativeUiSurface
        key={attempt}
        state={state}
        streaming={streaming}
        locked={locked}
        invoke={invoke}
        onHostError={reportHostError}
        onRetry={() => setAttempt(value => value + 1)}
      />
    </div>
  )
}

export function GenerativeUiSurface({ state, streaming = false, locked = false, invoke, onHostError, onRetry }: {
  state: GenerativeUiRunnerState
  streaming?: boolean
  locked?: boolean
  invoke: (id: string, args: GenerativeUiJson[]) => void
  onHostError: (message: string) => void
  onRetry: () => void
}): JSX.Element {
  const t = useT()
  const failed = state.status === 'failed'
  const inert = failed || locked
  const context = useMemo<GenerativeUiRenderContextValue>(() => ({
    inert,
    invoke: (callback, args) => { if (callback && !inert) invoke(callback.$callback, args) }
  }), [inert, invoke])

  if (state.status === 'loading') {
    return (
      <div role="status" className={styles.skeleton}>
        <span className={styles.visuallyHidden}>{t(streaming ? 'generativeUi.working' : 'generativeUi.loading')}</span>
        <Skeleton width="38%" height={14} />
        <Skeleton width="86%" height={12} />
        <Skeleton width="64%" height={12} />
        <div className={styles.skeletonControls}>
          <Skeleton width={88} height={32} radius={999} />
          <Skeleton width={72} height={32} radius={999} />
        </div>
      </div>
    )
  }
  return (
    <>
      {state.tree && (
        <div className={styles.frame} inert={inert || undefined} aria-disabled={inert || undefined} data-inert={failed ? '' : undefined}>
          <GenerativeUiHostBoundary onError={onHostError}>
            <GenerativeUiRenderContext.Provider value={context}>
              <GenerativeUiNodes nodes={state.tree} />
            </GenerativeUiRenderContext.Provider>
          </GenerativeUiHostBoundary>
        </div>
      )}
      {state.status === 'failed' && <GenerativeUiFailureNotice failure={state.failure} onRetry={onRetry} />}
    </>
  )
}

function GenerativeUiFailureNotice({ failure, onRetry }: { failure: GenerativeUiFailure; onRetry: () => void }): JSX.Element {
  const t = useT()
  const location = failure.line === undefined
    ? null
    : failure.column === undefined
      ? t('generativeUi.line', { line: failure.line })
      : t('generativeUi.lineColumn', { line: failure.line, column: failure.column })
  const detail = [t(`generativeUi.stage.${failure.stage}`), location, failure.message].filter(Boolean).join(' · ')
  return (
    <div role="status" className={styles.failure} data-stage={failure.stage}>
      <div className={styles.failureBody}>
        <CircleAlert size={14} aria-hidden className={styles.failureIcon} />
        <div className={styles.failureText}>
          <span>{t('generativeUi.failed')}</span>
          <span className={styles.failureDetail} title={detail}>{detail}</span>
        </div>
      </div>
      <Button size="sm" variant="secondary" onClick={onRetry}>{t('common.retry')}</Button>
    </div>
  )
}

function useContainerBreakpoint(ref: RefObject<HTMLElement | null>): GenerativeUiBreakpoint {
  const [breakpoint, setBreakpoint] = useState<GenerativeUiBreakpoint>('regular')
  useEffect(() => {
    const element = ref.current
    if (!element) return
    const update = (width: number): void => setBreakpoint(generativeUiBreakpointForWidth(width))
    update(element.getBoundingClientRect().width)
    const observer = new ResizeObserver(entries => update(entries[0]?.contentRect.width ?? 0))
    observer.observe(element)
    return () => observer.disconnect()
  }, [ref])
  return breakpoint
}

class GenerativeUiHostBoundary extends Component<{ onError: (message: string) => void; children: ReactNode }, { failed: boolean }> {
  override state = { failed: false }

  static getDerivedStateFromError(): { failed: boolean } {
    return { failed: true }
  }

  override componentDidCatch(error: Error): void {
    this.props.onError(error.message || 'A component failed to render.')
  }

  override render(): ReactNode {
    return this.state.failed ? null : this.props.children
  }
}
