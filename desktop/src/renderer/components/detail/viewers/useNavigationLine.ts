// Only the hint's line is honoured; it also carries a column, and a read-only view
// has no cursor to put one on.
import { useEffect, useRef } from 'react'
import type { FileNavigationHint } from '../../../../shared/viewer/types'

export interface UseNavigationLineOptions {
  hint: FileNavigationHint | undefined
  lineCount: number
  /** False while the file is still loading, so the scroll is not aimed at nothing. */
  ready: boolean
  scrollToIndex: (index: number) => void
}

export function navigationRowIndex(
  hint: FileNavigationHint | undefined,
  lineCount: number
): number | undefined {
  const line = hint?.line
  if (line === undefined || !Number.isFinite(line) || line < 1 || lineCount === 0) return undefined
  return Math.min(Math.floor(line), lineCount) - 1
}

export function useNavigationLine(options: UseNavigationLineOptions): void {
  const { hint, lineCount, ready } = options
  const scrollRef = useRef(options.scrollToIndex)
  scrollRef.current = options.scrollToIndex

  const line = hint?.line
  const column = hint?.column

  useEffect(() => {
    if (!ready) return
    const index = navigationRowIndex(line === undefined ? undefined : { line, column }, lineCount)
    if (index === undefined) return
    scrollRef.current(index)
    // `column` is a dependency so a hint that moves within the same line still re-scrolls.
  }, [ready, line, column, lineCount])
}
