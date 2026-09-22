import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { useT } from '../../contexts/LocaleContext'
import { useComposerContextStore } from '../../stores/composerContextStore'
import { useThreadStore } from '../../stores/threadStore'
import { Button } from '../ui/Button'
import { ContextCommentEditor } from './ContextCommentEditor'
import { useResponseSelectionStore, type ResponseSelection } from './responseSelectionStore'

export function ResponseSelectionOverlay({ selection }: { selection: ResponseSelection }): JSX.Element {
  const t = useT()
  const root = useRef<HTMLDivElement>(null)
  const [position, setPosition] = useState({ left: 0, top: 0 })
  const { dismiss, cancel, edit, setDraft } = useResponseSelectionStore.getState()

  useLayoutEffect(() => {
    function update(): void {
      if (!selection.container.isConnected) { dismiss(); return }
      const rect = selection.range.getBoundingClientRect()
      const size = root.current!.getBoundingClientRect()
      const left = Math.max(8, Math.min(rect.left, window.innerWidth - size.width - 8))
      const preferredTop = rect.top >= size.height + 16 ? rect.top - size.height - 8 : rect.bottom + 8
      const top = Math.max(8, Math.min(preferredTop, window.innerHeight - size.height - 8))
      setPosition((previous) => previous.left === left && previous.top === top ? previous : { left, top })
    }
    update()
    const observer = new ResizeObserver(update)
    observer.observe(root.current!)
    window.addEventListener('resize', update)
    document.addEventListener('scroll', update, true)
    return () => {
      observer.disconnect()
      window.removeEventListener('resize', update)
      document.removeEventListener('scroll', update, true)
    }
  }, [selection, dismiss])

  useEffect(() => {
    const outside = (event: Event): void => {
      if (!root.current?.contains(event.target as Node)) dismiss()
    }
    const escape = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') dismiss()
    }
    const contextMenu = (event: MouseEvent): void => {
      if (root.current?.contains(event.target as Node) && selection.editing) return
      dismiss()
    }
    const selectionChanged = (): void => {
      if (selection.editing) return
      const current = window.getSelection()
      if (!current?.rangeCount || current.isCollapsed) { dismiss(); return }
      const range = current.getRangeAt(0)
      if (range.startContainer !== selection.range.startContainer || range.startOffset !== selection.range.startOffset ||
        range.endContainer !== selection.range.endContainer || range.endOffset !== selection.range.endOffset) dismiss()
    }
    document.addEventListener('pointerdown', outside, true)
    document.addEventListener('contextmenu', contextMenu, true)
    document.addEventListener('keydown', escape)
    document.addEventListener('selectionchange', selectionChanged)
    window.addEventListener('blur', dismiss)
    const unsubscribe = useThreadStore.subscribe((state, previous) => {
      if (state.activeThreadId !== previous.activeThreadId) dismiss()
    })
    return () => {
      document.removeEventListener('pointerdown', outside, true)
      document.removeEventListener('contextmenu', contextMenu, true)
      document.removeEventListener('keydown', escape)
      document.removeEventListener('selectionchange', selectionChanged)
      window.removeEventListener('blur', dismiss)
      unsubscribe()
    }
  }, [selection, dismiss])

  function add(comment: string): void {
    useComposerContextStore.getState().addContext(selection.threadId, {
      kind: 'responseAnnotation', id: crypto.randomUUID(), threadId: selection.threadId,
      turnId: selection.turnId, itemId: selection.itemId, selectedText: selection.text, comment,
    })
    cancel()
    window.getSelection()?.removeAllRanges()
  }

  return createPortal(
    <div ref={root} className="dc-response-feedback__overlay" data-editing={selection.editing} style={position}
      onContextMenu={(event) => event.stopPropagation()}>
      {selection.editing ? (
        <ContextCommentEditor initialComment={useResponseSelectionStore.getState().drafts[selection.draftKey] ?? ''}
          onCommentChange={setDraft} onSave={add} onCancel={cancel} />
      ) : (
        <div className="dc-response-feedback__actions" role="toolbar" aria-label={t('composer.context.selectedText')}
          onMouseDown={(event) => event.preventDefault()}>
          <Button size="sm" variant="ghost" onClick={() => add('')}>{t('conversation.selection.addToChat')}</Button>
          <Button size="sm" variant="ghost" onClick={edit}>{t('conversation.selection.comment')}</Button>
        </div>
      )}
    </div>, document.body,
  )
}
