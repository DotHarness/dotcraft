import { useEffect, useId, useRef, type ReactNode } from 'react'
import { ResponseSelectionOverlay } from './ResponseSelectionOverlay'
import { selectionDraftKey, useResponseSelectionStore } from './responseSelectionStore'

export function ResponseFeedback({ threadId, turnId, itemId, children }: {
  threadId: string
  turnId: string
  itemId: string
  children: ReactNode
}): JSX.Element {
  const owner = useId()
  const textRoot = useRef<HTMLDivElement>(null)
  const active = useResponseSelectionStore((state) => state.active?.owner === owner ? state.active : null)
  useEffect(() => () => {
    const state = useResponseSelectionStore.getState()
    if (state.active?.owner === owner) state.dismiss()
  }, [owner, threadId, turnId, itemId])

  function capture(): void {
    const selection = window.getSelection()
    const container = textRoot.current
    if (!selection?.rangeCount || !container?.contains(selection.anchorNode) ||
      !container.contains(selection.focusNode) || !selection.toString().trim()) return
    const range = selection.getRangeAt(0).cloneRange()
    useResponseSelectionStore.getState().open({
      owner, threadId, turnId, itemId, container, range,
      text: selection.toString().trim(),
      draftKey: selectionDraftKey(container, range, JSON.stringify([threadId, turnId, itemId])),
      editing: false,
    })
  }

  return (
    <>
      <div ref={textRoot}
        onMouseUp={(event) => { if (event.button === 0) capture() }}
        onKeyUp={(event) => { if (event.shiftKey) capture() }}>
        {children}
      </div>
      {active && <ResponseSelectionOverlay key={active.draftKey} selection={active} />}
    </>
  )
}
