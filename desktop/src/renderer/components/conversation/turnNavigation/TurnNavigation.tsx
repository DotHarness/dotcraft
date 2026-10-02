import { memo, useCallback, useEffect, useMemo, useRef, useState, type JSX, type RefObject } from 'react'
import { useThreadStore } from '../../../stores/threadStore'
import { turnBookmarkKey, useTurnBookmarkStore } from '../../../stores/turnBookmarkStore'
import { useConversationAsideControl, useConversationColumnShift } from '../conversationAside/ConversationAside'
import type { TurnNavigationEntry } from './navigationIndex'
import { jumpToEntry, type JumpMode } from './turnJump'
import { TurnNavigationRail } from './TurnNavigationRail'
import { useRailGutter } from './useRailGutter'
import { useTurnNavigationIndex } from './useTurnNavigationIndex'
import { useTurnNavigationKeyboard } from './useTurnNavigationKeyboard'

const MIN_RAIL_ENTRIES = 4
const NO_BOOKMARKS: string[] = []

interface TurnNavigationProps {
  scrollRef: RefObject<HTMLDivElement | null>
  columnRef: RefObject<HTMLDivElement | null>
}

export const TurnNavigation = memo(function TurnNavigation({
  scrollRef,
  columnRef
}: TurnNavigationProps): JSX.Element | null {
  const { threadId, entries, railAllowed, requestPreview } = useTurnNavigationIndex()
  const roomy = useRailGutter(scrollRef, columnRef, useConversationColumnShift())
  const { reportRail } = useConversationAsideControl()
  const [introducedThreadId, setIntroducedThreadId] = useState<string | null>(null)
  const entriesRef = useRef(entries)
  entriesRef.current = entries

  const workspacePath = useThreadStore((s) => s.activeThread?.id === threadId ? s.activeThread?.workspacePath : undefined)
  const bookmarkKey = threadId && workspacePath ? turnBookmarkKey(workspacePath, threadId) : null
  const bookmarkIds = useTurnBookmarkStore((s) => (bookmarkKey && s.byThread[bookmarkKey]) || NO_BOOKMARKS)
  const bookmarkedIds = useMemo(
    () => new Set(bookmarkIds.flatMap((id) => [id, id.slice(0, id.indexOf(':'))])),
    [bookmarkIds]
  )
  const toggleBookmark = useMemo(
    () => bookmarkKey
      ? (entry: TurnNavigationEntry) => useTurnBookmarkStore.getState().toggle(bookmarkKey, entry.id)
      : null,
    [bookmarkKey]
  )

  const jump = useCallback((entry: TurnNavigationEntry, mode: JumpMode): void => {
    const scrollEl = scrollRef.current
    if (scrollEl && threadId) void jumpToEntry(scrollEl, threadId, entry, mode)
  }, [scrollRef, threadId])
  const jumpFromKeyboard = useCallback((entry: TurnNavigationEntry) => jump(entry, 'jump'), [jump])
  useTurnNavigationKeyboard(scrollRef, entriesRef, jumpFromKeyboard)

  const shown = Boolean(threadId) && railAllowed && entries.length >= MIN_RAIL_ENTRIES && roomy
  useEffect(() => {
    reportRail(shown)
  }, [reportRail, shown])
  useEffect(() => () => reportRail(false), [reportRail])

  if (!shown) return null
  return (
    <TurnNavigationRail
      key={threadId}
      entries={entries}
      bookmarkedIds={bookmarkedIds}
      onToggleBookmark={toggleBookmark}
      scrollRef={scrollRef}
      fadeIn={introducedThreadId !== threadId}
      onFadeInEnd={() => setIntroducedThreadId(threadId)}
      onRequestPreview={requestPreview}
      onJump={jump}
    />
  )
})
