import { useEffect, useMemo, useState, type DragEvent, type RefObject } from 'react'
import { useConnectionStore } from '../../stores/connectionStore'
import { useThreadStore } from '../../stores/threadStore'
import type { ThreadSummary } from '../../types/thread'
import { THREAD_DRAG_MIME, mentionedThreadIds, threadMentionTitle } from '../../utils/threadReferences'
import type { RichInputAreaHandle } from './RichInputArea'

export interface ThreadMentionSource {
  excludedThreadIds: () => string[]
  onSelect: (thread: ThreadSummary) => void
}

export interface ComposerThreadReferences {
  mentions?: ThreadMentionSource
  isThreadDrag: (event: DragEvent) => boolean
  drop: (event: DragEvent) => void
}

/** `threadId` is null until the composer's thread exists; `thread/start` always declares the tools. */
export function useComposerThreadReferences(
  threadId: string | null,
  richRef: RefObject<RichInputAreaHandle | null>
): ComposerThreadReferences {
  const capabilities = useConnectionStore((s) => s.capabilities)
  const [toolsBound, setToolsBound] = useState(false)

  useEffect(() => {
    if (!threadId) return
    let current = true
    void window.api.appServer.hasDesktopThreadTools(threadId).then((bound) => {
      if (current) setToolsBound(bound)
    })
    return () => { current = false }
  }, [capabilities, threadId])

  return useMemo(() => {
    const excludedThreadIds = (): string[] => [
      ...(threadId ? [threadId] : []),
      ...mentionedThreadIds(richRef.current?.getSegments() ?? [])
    ]
    const mentions = threadId == null || toolsBound
      ? {
          excludedThreadIds,
          onSelect: (thread: ThreadSummary) => richRef.current?.insertThreadTag(thread.id, threadMentionTitle(thread))
        }
      : undefined
    return {
      mentions,
      isThreadDrag: (event: DragEvent) => event.dataTransfer.types.includes(THREAD_DRAG_MIME),
      drop: (event: DragEvent) => {
        const droppedId = event.dataTransfer.getData(THREAD_DRAG_MIME)
        const thread = useThreadStore.getState().threadList.find((candidate) => candidate.id === droppedId)
        if (!mentions || !thread || excludedThreadIds().includes(thread.id)) return
        richRef.current?.insertThreadTagAtSelection(thread.id, threadMentionTitle(thread))
      }
    }
  }, [richRef, threadId, toolsBound])
}
