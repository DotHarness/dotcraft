import { useRef, useState, type ReactNode } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useComposerContextStore } from '../../stores/composerContextStore'
import { Button } from '../ui/Button'
import { ContextCommentEditor } from './ContextCommentEditor'

export function ResponseFeedback({
  threadId,
  turnId,
  itemId,
  children,
}: {
  threadId: string
  turnId: string
  itemId: string
  children: ReactNode
}): JSX.Element {
  const t = useT()
  const root = useRef<HTMLDivElement>(null)
  const textRoot = useRef<HTMLDivElement>(null)
  const [selected, setSelected] = useState<{
    text: string
    x: number
    y: number
  } | null>(null)
  const [editing, setEditing] = useState(false)
  function capture() {
    const selection = window.getSelection()
    const container = textRoot.current
    if (
      !selection?.rangeCount ||
      !container?.contains(selection.anchorNode) ||
      !container.contains(selection.focusNode)
    ) {
      setSelected(null)
      return
    }
    const text = selection.toString().trim()
    if (!text) {
      setSelected(null)
      return
    }
    const rect = selection.getRangeAt(0).getBoundingClientRect()
    const bounds = root.current!.getBoundingClientRect()
    setSelected({
      text,
      x: Math.max(
        0,
        Math.min(rect.right - bounds.left - 294, bounds.width - 294),
      ),
      y: rect.bottom - bounds.top + 8,
    })
    setEditing(false)
  }
  function add(comment: string) {
    if (!selected) return
    useComposerContextStore.getState().addContext(threadId, {
      kind: 'responseAnnotation',
      id: crypto.randomUUID(),
      threadId,
      turnId,
      itemId,
      selectedText: selected.text,
      comment,
    })
    setSelected(null)
    window.getSelection()?.removeAllRanges()
  }
  return (
    <div ref={root} className="dc-response-feedback">
      <div
        ref={textRoot}
        onMouseUp={capture}
        onKeyUp={(event) => {
          if (event.shiftKey) capture()
        }}
      >
        {children}
      </div>
      {selected && (
        <div
          className="dc-response-feedback__overlay"
          style={{ left: selected.x, top: selected.y }}
        >
          {editing ? (
            <ContextCommentEditor
              onSave={add}
              onCancel={() => setSelected(null)}
            />
          ) : (
            <div
              className="dc-response-feedback__actions"
              role="toolbar"
              aria-label={t('composer.context.selectedText')}
              onMouseDown={(event) => event.preventDefault()}
              onKeyDown={(event) => {
                if (event.key === 'Escape') setSelected(null)
              }}
            >
              <Button size="sm" variant="ghost" onClick={() => add('')}>
                {t('composer.context.addToTask')}
              </Button>
              <Button
                size="sm"
                variant="ghost"
                onClick={() => setEditing(true)}
              >
                {t('composer.context.comment')}
              </Button>
            </div>
          )}
        </div>
      )}
    </div>
  )
}
