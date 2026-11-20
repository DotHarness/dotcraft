import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { useT } from '../../contexts/LocaleContext'
import { useComposerContextStore } from '../../stores/composerContextStore'
import { useThreadStore } from '../../stores/threadStore'
import { askInSideChat, useCanOpenSideChat } from '../../utils/sideChat'
import { Button } from '../ui/Button'
import { ContextCommentEditor } from './ContextCommentEditor'
import { focusComposer } from './composerFocus'
import { sendComposer } from './composerSend'
import { readSelectionRange, sameSelectionRange } from './replySelection'
import { useResponseSelectionStore, type ResponseSelection } from './responseSelectionStore'

export function ResponseSelectionOverlay({ selection }: { selection: ResponseSelection }): JSX.Element {
  const t = useT()
  const root = useRef<HTMLDivElement>(null)
  const [position, setPosition] = useState({ left: 0, top: 0 })
  const { dismiss, cancel, edit, setDraft } = useResponseSelectionStore.getState()
  const sideChatAvailable = useCanOpenSideChat()
  const isActiveThread = useThreadStore((state) => state.activeThreadId === selection.threadId)

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
      const range = readSelectionRange()
      if (!range || !sameSelectionRange(range, selection.range)) dismiss()
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

  function annotation(comment: string) {
    return {
      kind: 'responseAnnotation' as const, id: crypto.randomUUID(), threadId: selection.threadId,
      turnId: selection.turnId, itemId: selection.itemId, selectedText: selection.text, comment,
    }
  }

  function add(comment: string): void {
    useComposerContextStore.getState().addContext(selection.threadId, annotation(comment))
    cancel()
    window.getSelection()?.removeAllRanges()
  }

  function addToChat(): void {
    add('')
    if (isActiveThread) focusComposer()
  }

  function addAndSend(comment: string): void {
    add(comment)
    sendComposer(selection.threadId, t)
  }

  function askSide(): void {
    askInSideChat(selection.threadId, annotation(''), t)
    cancel()
    window.getSelection()?.removeAllRanges()
  }

  return createPortal(
    <div ref={root} className="dc-response-feedback__overlay" data-editing={selection.editing} style={position}
      onContextMenu={(event) => event.stopPropagation()}>
      {selection.editing ? (
        <ContextCommentEditor initialComment={useResponseSelectionStore.getState().drafts[selection.draftKey] ?? ''}
          onCommentChange={setDraft} onSave={add} onSend={addAndSend} onCancel={cancel} />
      ) : (
        <div className="dc-response-feedback__actions" role="toolbar" aria-label={t('composer.context.selectedText')}
          onMouseDown={(event) => event.preventDefault()}>
          <Button size="sm" variant="ghost" onClick={addToChat}>{t('conversation.selection.addToChat')}</Button>
          <Button size="sm" variant="ghost" onClick={edit}>{t('conversation.selection.comment')}</Button>
          {sideChatAvailable && isActiveThread && (
            <Button size="sm" variant="ghost" onClick={askSide}>{t('conversation.selection.askInSideChat')}</Button>
          )}
        </div>
      )}
    </div>, document.body,
  )
}
