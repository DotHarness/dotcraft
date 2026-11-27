import { useCallback, useEffect, useId, useRef, type ReactNode } from 'react'
import { ResponseSelectionOverlay } from './ResponseSelectionOverlay'
import {
  REPLY_SELECTION_TARGET_ATTR,
  registerReplySelection,
  type ResolvedReplySelection,
} from './replySelection'
import { selectionDraftKey, useResponseSelectionStore } from './responseSelectionStore'

export function ResponseFeedback({ threadId, turnId, itemId, disabled = false, children }: {
  threadId: string
  turnId: string
  itemId: string
  disabled?: boolean
  children: ReactNode
}): JSX.Element {
  const owner = useId()
  const textRoot = useRef<HTMLDivElement>(null)
  const active = useResponseSelectionStore((state) => state.active?.owner === owner ? state.active : null)
  useEffect(() => () => {
    const state = useResponseSelectionStore.getState()
    if (state.active?.owner === owner) state.dismiss()
  }, [owner, threadId, turnId, itemId])

  const capture = useCallback((resolved: ResolvedReplySelection): void => {
    const container = textRoot.current
    if (!container) return
    useResponseSelectionStore.getState().open({
      owner,
      threadId,
      turnId,
      itemId,
      container,
      element: resolved.element,
      range: resolved.range,
      text: resolved.text,
      draftKey: selectionDraftKey(container, resolved.range, JSON.stringify([threadId, turnId, itemId])),
      editing: false,
    })
  }, [owner, threadId, turnId, itemId])

  useEffect(() => {
    const container = textRoot.current
    if (container && !disabled) return registerReplySelection(container, capture)
  }, [capture, disabled])

  const targetAttr = disabled ? undefined : ''

  return (
    <>
      <div ref={textRoot} {...{ [REPLY_SELECTION_TARGET_ATTR]: targetAttr }}>
        {children}
      </div>
      {active && <ResponseSelectionOverlay key={active.draftKey} selection={active} />}
    </>
  )
}
