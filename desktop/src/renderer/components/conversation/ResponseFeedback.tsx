import { useCallback, useEffect, useId, useRef, type ReactNode } from 'react'
import { ResponseSelectionOverlay } from './ResponseSelectionOverlay'
import { readSelectionRange, registerReplySelection } from './replySelection'
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

  const capture = useCallback((selection: Range | undefined): void => {
    const container = textRoot.current
    if (disabled || !container || !selection || !container.contains(selection.commonAncestorContainer)) return
    const text = selection.toString().trim()
    if (!text) return
    const range = selection.cloneRange()
    useResponseSelectionStore.getState().open({
      owner, threadId, turnId, itemId, container, range, text,
      draftKey: selectionDraftKey(container, range, JSON.stringify([threadId, turnId, itemId])),
      editing: false,
    })
  }, [disabled, owner, threadId, turnId, itemId])

  useEffect(() => {
    const container = textRoot.current
    if (container && !disabled) return registerReplySelection(container, capture)
  }, [capture, disabled])

  return (
    <>
      <div ref={textRoot} onKeyUp={(event) => {
        if (event.shiftKey && event.key !== 'Escape') capture(readSelectionRange())
      }}>
        {children}
      </div>
      {active && <ResponseSelectionOverlay key={active.draftKey} selection={active} />}
    </>
  )
}
