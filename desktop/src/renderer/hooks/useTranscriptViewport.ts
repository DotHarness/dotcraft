import { useLayoutEffect } from 'react'
import { readTranscriptScrollPosition, useAutoScroll, type TranscriptScrollPosition } from './useAutoScroll'

const positions = new Map<string, TranscriptScrollPosition>()
const LATEST: TranscriptScrollPosition = { scrollTop: 0, followBottom: true }

export function useTranscriptViewport(
  contentLength: number, identity: string, ready = true
): ReturnType<typeof useAutoScroll> {
  const viewport = useAutoScroll(contentLength, ready)
  const { scrollRef, restorePosition } = viewport

  useLayoutEffect(() => {
    const element = scrollRef.current
    if (!ready || !element) return
    restorePosition(positions.get(identity) ?? LATEST)
    return () => { positions.set(identity, readTranscriptScrollPosition(element)) }
  }, [identity, ready, restorePosition, scrollRef])

  return viewport
}
