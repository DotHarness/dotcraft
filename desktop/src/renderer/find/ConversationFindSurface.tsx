// Segments are read from the rendered blocks, not from the store: the conversation is
// not windowed, so the rendered text is the complete text.
import { useCallback } from 'react'
import { useFindSurface } from './useFindSurface'
import type { FindSegment } from './types'

export const FIND_SEGMENT_ATTRIBUTE = 'data-find-segment'

export interface ConversationFindSurfaceProps {
  threadId: string | null
  getContainer: () => HTMLElement | null
  contentKey: string | number
}

export function ConversationFindSurface({
  threadId,
  getContainer,
  contentKey
}: ConversationFindSurfaceProps): null {
  const blocks = useCallback((): HTMLElement[] => {
    const container = getContainer()
    return container === null
      ? []
      : [...container.querySelectorAll<HTMLElement>(`[${FIND_SEGMENT_ATTRIBUTE}]`)]
  }, [getContainer])

  const getSegments = useCallback(
    (): FindSegment[] => blocks().map((block, index) => ({
      key: String(index),
      text: block.textContent ?? ''
    })),
    [blocks]
  )

  useFindSurface({
    id: threadId === null ? undefined : `conversation:${threadId}`,
    domain: 'conversation',
    priority: 10,
    getSegments,
    getContainer,
    // Addressed by position rather than a `data-line` attribute, so nothing has to be
    // written back into markup React owns.
    resolveElement: (match) => blocks()[Number(match.segmentKey)] ?? null,
    contentKey
  })

  return null
}
