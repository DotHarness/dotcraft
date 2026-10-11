import { useCallback, useEffect, useRef, useState } from 'react'
import { useConnectionStore } from '../../../stores/connectionStore'
import { useNearMessageViewport } from '../useNearMessageViewport'
import { useGenerativeUiHostActions } from './generativeUiHostActions'
import type { GenerativeUiSavedState } from './generativeUiProtocol'
import { prewarmGenerativeUiRunner } from './generativeUiRunnerHost'
import { loadGenerativeUiState, retainGenerativeUiStates, saveGenerativeUiState, subscribeGenerativeUiState, type GenerativeUiBlockSource } from './generativeUiStates'
import { GenerativeUiView } from './GenerativeUiView'

interface Props {
  code: string
  closed: boolean
  blockIndex: number
  source: GenerativeUiBlockSource | null
  /** The message or its turn is still live, so the block starts as soon as its fence closes. */
  live: boolean
  locked: boolean
}

export function GenerativeUiMessageBlock({ code, closed, blockIndex, source, live, locked }: Props): JSX.Element {
  const rootRef = useRef<HTMLDivElement>(null)
  const supported = useConnectionStore(state => state.capabilities?.generativeUi === true)
  const persisted = supported && source !== null && !locked
  const near = useNearMessageViewport(rootRef, closed && !live)
  const [started, setStarted] = useState(false)
  if (closed && (live || near) && !started) setStarted(true)
  const [restored, setRestored] = useState<{ state?: GenerativeUiSavedState } | null>(null)
  const [remoteState, setRemoteState] = useState<GenerativeUiSavedState>()
  const onAction = useGenerativeUiHostActions(source?.threadId)
  const { threadId, turnId, itemId } = source ?? {}

  useEffect(() => prewarmGenerativeUiRunner(), [])

  useEffect(() => threadId ? retainGenerativeUiStates(threadId) : undefined, [threadId])

  useEffect(() => threadId && turnId && itemId
    ? subscribeGenerativeUiState({ threadId, turnId, itemId }, blockIndex, setRemoteState)
    : undefined, [blockIndex, itemId, threadId, turnId])

  useEffect(() => {
    if (!started || restored) return
    if (!threadId || !turnId || !itemId || !supported) {
      setRestored({})
      return
    }
    let cancelled = false
    void loadGenerativeUiState({ threadId, turnId, itemId }, blockIndex).then(state => {
      if (!cancelled) setRestored({ state })
    })
    return () => { cancelled = true }
  }, [blockIndex, itemId, restored, started, supported, threadId, turnId])

  const onStateChange = useCallback((state: GenerativeUiSavedState) => {
    if (threadId && turnId && itemId) saveGenerativeUiState({ threadId, turnId, itemId }, blockIndex, state)
  }, [blockIndex, itemId, threadId, turnId])

  return (
    <div ref={rootRef}>
      <GenerativeUiView
        code={code}
        streaming={!closed}
        deferred={!started || restored === null}
        locked={locked}
        initialState={restored?.state}
        restoredState={remoteState}
        onAction={locked ? undefined : onAction}
        onStateChange={persisted ? onStateChange : undefined}
      />
    </div>
  )
}
