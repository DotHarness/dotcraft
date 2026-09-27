import {
  memo,
  useCallback,
  useEffect,
  useId,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type FocusEvent,
  type JSX,
  type KeyboardEvent,
  type MouseEvent,
  type PointerEvent,
  type RefObject
} from 'react'
import { useT } from '../../../contexts/LocaleContext'
import { groupEntries, type EntryGroup } from './entryGroups'
import type { TurnNavigationEntry } from './navigationIndex'
import type { JumpMode } from './turnJump'
import { TurnPreviewCard } from './TurnPreviewCard'
import { useActiveEntries } from './useActiveEntries'
import { usePreviewCardVisibility } from './usePreviewCardVisibility'
import styles from './TurnNavigationRail.module.css'

const ENTRY_HEIGHT_PX = 10
const PREVIEW_REST_MS = 150
const NEIGHBOR_REACH = 3

interface TurnNavigationRailProps {
  entries: TurnNavigationEntry[]
  bookmarkedIds: ReadonlySet<string>
  onToggleBookmark: ((entry: TurnNavigationEntry) => void) | null
  scrollRef: RefObject<HTMLDivElement | null>
  fadeIn: boolean
  onFadeInEnd: () => void
  onRequestPreview: (entry: TurnNavigationEntry) => void
  onJump: (entry: TurnNavigationEntry, mode: JumpMode) => void
}

interface Scrub {
  key: string
  pointerId: number
  button: HTMLElement
}

function findEntryButton(list: HTMLElement | null, key: string | null): HTMLElement | null {
  if (!list || key === null) return null
  for (const button of list.querySelectorAll<HTMLElement>('[data-entry-key]')) {
    if (button.dataset.entryKey === key) return button
  }
  return null
}

function keepInView(list: HTMLElement, button: HTMLElement): void {
  let top = button.offsetTop
  for (
    let parent = button.offsetParent;
    parent instanceof HTMLElement && parent !== list && parent !== list.offsetParent;
    parent = parent.offsetParent
  ) {
    top += parent.offsetTop
  }
  if (top < list.scrollTop) list.scrollTop = top
  else if (top + button.offsetHeight > list.scrollTop + list.clientHeight) {
    list.scrollTop = top + button.offsetHeight - list.clientHeight + 1
  }
}

function isPlainTab(event: KeyboardEvent<HTMLElement>): boolean {
  return event.key === 'Tab' && !event.altKey && !event.ctrlKey && !event.metaKey
}

function isFocusVisible(element: HTMLElement): boolean {
  try {
    return element.matches(':focus-visible')
  } catch {
    return false
  }
}

function useStableGroups(entries: readonly TurnNavigationEntry[]): EntryGroup[] {
  const keysSignature = entries.map((entry) => entry.key).join('\u0000')
  const [grouping, setGrouping] = useState(() => ({
    signature: keysSignature,
    groups: groupEntries(entries.map((entry) => entry.key))
  }))
  if (grouping.signature === keysSignature) return grouping.groups
  const groups = groupEntries(entries.map((entry) => entry.key), grouping.groups)
  setGrouping({ signature: keysSignature, groups })
  return groups
}

export function TurnNavigationRail({
  entries,
  bookmarkedIds,
  onToggleBookmark,
  scrollRef,
  fadeIn,
  onFadeInEnd,
  onRequestPreview,
  onJump
}: TurnNavigationRailProps): JSX.Element {
  const t = useT()
  const cardId = useId()
  const listRef = useRef<HTMLDivElement | null>(null)
  const cardRef = useRef<HTMLDivElement | null>(null)
  const toggleRef = useRef<HTMLButtonElement | null>(null)
  const groups = useStableGroups(entries)
  const active = useActiveEntries(scrollRef, entries)
  const [hoveredKey, setHoveredKey] = useState<string | null>(null)
  const [focusedKey, setFocusedKey] = useState<string | null>(null)
  const [scrubKey, setScrubKey] = useState<string | null>(null)
  const [cardKey, setCardKey] = useState<string | null>(null)
  const [fade, setFade] = useState({ top: false, bottom: false })
  const scrubRef = useRef<Scrub | null>(null)
  const suppressClickRef = useRef(false)
  const pointerInsideRef = useRef(false)
  const pointerInCardRef = useRef(false)
  const suppressFocusOpenRef = useRef(false)
  const previewTimerRef = useRef<{ key: string; timer: number } | null>(null)

  const focusInCard = (): boolean => cardRef.current?.contains(document.activeElement) ?? false
  const card = usePreviewCardVisibility(() => {
    const entryButton = findEntryButton(listRef.current, cardKey)
    if (!entryButton || !focusInCard()) return
    suppressFocusOpenRef.current = true
    entryButton.focus()
  })

  // A card removed under the pointer never reports pointer leave.
  useEffect(() => {
    if (card.open || !pointerInCardRef.current) return
    pointerInCardRef.current = false
    setHoveredKey(null)
  }, [card.open])

  const entryByKey = useMemo(
    () => new Map(entries.map((entry, index) => [entry.key, { entry, index }])),
    [entries]
  )
  const entryByKeyRef = useRef(entryByKey)
  entryByKeyRef.current = entryByKey

  const targetKey = scrubKey ?? hoveredKey ?? focusedKey
  const targetIndex = targetKey === null ? -1 : entryByKey.get(targetKey)?.index ?? -1
  const firstActiveKey = entries.find((entry) => active.has(entry.id))?.key ?? null
  const cardEntry = cardKey === null ? undefined : entryByKey.get(cardKey)?.entry
  const describedKey = card.open && cardEntry ? cardEntry.key : null

  const cancelPreview = useCallback((): void => {
    if (!previewTimerRef.current) return
    window.clearTimeout(previewTimerRef.current.timer)
    previewTimerRef.current = null
  }, [])

  const schedulePreview = useCallback((entry: TurnNavigationEntry): void => {
    if (scrubRef.current || entry.content || !entry.position || previewTimerRef.current?.key === entry.key) return
    cancelPreview()
    const timer = window.setTimeout(() => {
      previewTimerRef.current = null
      const current = entryByKeyRef.current.get(entry.key)?.entry
      if (current) onRequestPreview(current)
    }, PREVIEW_REST_MS)
    previewTimerRef.current = { key: entry.key, timer }
  }, [cancelPreview, onRequestPreview])

  useEffect(() => cancelPreview, [cancelPreview])

  const entryAt = (element: EventTarget | Element | null): { entry: TurnNavigationEntry; button: HTMLElement } | null => {
    const button = element instanceof Element ? element.closest<HTMLElement>('[data-entry-key]') : null
    if (!button || !listRef.current?.contains(button)) return null
    const found = entryByKey.get(button.dataset.entryKey ?? '')
    return found ? { entry: found.entry, button } : null
  }

  const updateFade = useCallback((): void => {
    const list = listRef.current
    if (!list) return
    const top = list.scrollTop > 0
    const bottom = list.scrollTop + list.clientHeight < list.scrollHeight - 1
    setFade((current) => current.top === top && current.bottom === bottom ? current : { top, bottom })
  }, [])

  useLayoutEffect(updateFade, [entries.length, updateFade])

  useEffect(() => {
    window.addEventListener('resize', updateFade)
    return () => window.removeEventListener('resize', updateFade)
  }, [updateFade])

  useLayoutEffect(() => {
    if (scrubKey !== null) return
    const list = listRef.current
    const button = findEntryButton(list, firstActiveKey)
    if (list && button) keepInView(list, button)
  }, [firstActiveKey, scrubKey])

  const endScrub = (event: PointerEvent<HTMLDivElement>): void => {
    const scrub = scrubRef.current
    if (!scrub || scrub.pointerId !== event.pointerId) return
    scrubRef.current = null
    setScrubKey(null)
    setHoveredKey(pointerInsideRef.current ? scrub.key : null)
    if (scrub.button.hasPointerCapture?.(event.pointerId)) scrub.button.releasePointerCapture(event.pointerId)
    if (!pointerInsideRef.current) card.hide()
    window.setTimeout(() => { suppressClickRef.current = false }, 0)
  }

  const onPointerDown = (event: PointerEvent<HTMLDivElement>): void => {
    if (event.button !== 0) return
    const hit = entryAt(event.target)
    if (!hit) return
    cancelPreview()
    suppressClickRef.current = false
    pointerInsideRef.current = true
    scrubRef.current = { key: hit.entry.key, pointerId: event.pointerId, button: hit.button }
    setScrubKey(hit.entry.key)
    setCardKey(hit.entry.key)
    card.show()
    hit.button.setPointerCapture?.(event.pointerId)
  }

  const onPointerMove = (event: PointerEvent<HTMLDivElement>): void => {
    const scrub = scrubRef.current
    if (!scrub) {
      const hit = entryAt(event.target)
      setHoveredKey(hit?.entry.key ?? null)
      if (!hit) return
      schedulePreview(hit.entry)
      setCardKey(hit.entry.key)
      card.show()
      return
    }
    if (scrub.pointerId !== event.pointerId) return
    if ((event.buttons & 1) === 0) {
      endScrub(event)
      return
    }
    const bounds = event.currentTarget.getBoundingClientRect()
    const hit = entryAt(document.elementFromPoint(
      bounds.left + bounds.width / 2,
      Math.max(bounds.top, Math.min(event.clientY, bounds.bottom - 1))
    ))
    if (!hit || hit.entry.key === scrub.key) return
    scrubRef.current = { ...scrub, key: hit.entry.key }
    suppressClickRef.current = true
    setScrubKey(hit.entry.key)
    setCardKey(hit.entry.key)
    onJump(hit.entry, 'scrub')
  }

  const onPointerLeave = (): void => {
    cancelPreview()
    pointerInsideRef.current = false
    setHoveredKey(null)
    if (!scrubRef.current && !focusInCard()) card.hideSoon()
  }

  const onClick = (event: MouseEvent<HTMLDivElement>): void => {
    const hit = entryAt(event.target)
    if (!hit) return
    if (suppressClickRef.current) {
      suppressClickRef.current = false
      return
    }
    setCardKey(hit.entry.key)
    card.show()
    onJump(hit.entry, 'jump')
  }

  const onFocus = (event: FocusEvent<HTMLDivElement>): void => {
    const hit = entryAt(event.target)
    if (!hit) return
    cancelPreview()
    setFocusedKey(isFocusVisible(hit.button) ? hit.entry.key : null)
    if (suppressFocusOpenRef.current) {
      suppressFocusOpenRef.current = false
      return
    }
    onRequestPreview(hit.entry)
    setCardKey(hit.entry.key)
    card.show()
  }

  const onBlur = (event: FocusEvent<HTMLElement>): void => {
    const next = event.relatedTarget
    if (next instanceof Node && (listRef.current?.contains(next) || cardRef.current?.contains(next))) return
    setFocusedKey(null)
    if (!pointerInsideRef.current && !pointerInCardRef.current) card.hide()
  }

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>): void => {
    const toggle = toggleRef.current
    if (!isPlainTab(event) || event.shiftKey || !card.open || !toggle || toggle.disabled) return
    if (entryAt(event.target)?.entry.key !== cardKey) return
    event.preventDefault()
    toggle.focus()
  }

  const onCardKeyDown = (event: KeyboardEvent<HTMLDivElement>): void => {
    const entryButton = findEntryButton(listRef.current, cardKey)
    if (!isPlainTab(event) || !entryButton) return
    // Without preventDefault, Tab continues from the entry to the element after it.
    if (event.shiftKey) event.preventDefault()
    entryButton.focus()
  }

  const onCardPointerEnter = (): void => {
    pointerInCardRef.current = true
    setHoveredKey(cardKey)
    card.show()
  }

  const onCardPointerLeave = (): void => {
    pointerInCardRef.current = false
    setHoveredKey(null)
    if (!focusInCard()) card.hideSoon()
  }

  return (
    <nav
      aria-label={t('turnNavigation.railLabel')}
      className={fadeIn ? `${styles.nav} ${styles.fadeIn}` : styles.nav}
      onAnimationEnd={(event) => {
        if (fadeIn && event.target === event.currentTarget) onFadeInEnd()
      }}
    >
      <div
        ref={listRef}
        className={styles.list}
        data-scrubbing={scrubKey !== null || undefined}
        data-has-target={targetKey !== null || undefined}
        data-fade-top={fade.top || undefined}
        data-fade-bottom={fade.bottom || undefined}
        onScroll={updateFade}
        onPointerDownCapture={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerEnter={() => { pointerInsideRef.current = true }}
        onPointerLeave={onPointerLeave}
        onPointerUpCapture={endScrub}
        onPointerCancelCapture={endScrub}
        onLostPointerCapture={endScrub}
        onClick={onClick}
        onFocus={onFocus}
        onBlur={onBlur}
        onKeyDown={onKeyDown}
      >
        {groups.map((group) => (
          <RailGroup
            key={group.key}
            entries={entries}
            start={group.startIndex}
            count={group.entryKeys.length}
            active={active}
            targetIndex={
              targetIndex >= group.startIndex - NEIGHBOR_REACH &&
              targetIndex < group.startIndex + group.entryKeys.length + NEIGHBOR_REACH
                ? targetIndex
                : -1
            }
            describedKey={describedKey}
            cardId={cardId}
            bookmarkedIds={bookmarkedIds}
          />
        ))}
      </div>
      {card.open && cardEntry && (
        <TurnPreviewCard
          id={cardId}
          entry={cardEntry}
          bookmarked={bookmarkedIds.has(cardEntry.id)}
          onToggleBookmark={onToggleBookmark && cardEntry.content?.bookmarkable
            ? () => onToggleBookmark(cardEntry)
            : null}
          cardRef={cardRef}
          toggleRef={toggleRef}
          getAnchor={() => findEntryButton(listRef.current, cardEntry.key)}
          onPointerEnter={onCardPointerEnter}
          onPointerLeave={onCardPointerLeave}
          onBlur={onBlur}
          onKeyDown={onCardKeyDown}
        />
      )}
    </nav>
  )
}

interface RailGroupProps {
  entries: readonly TurnNavigationEntry[]
  start: number
  count: number
  active: ReadonlySet<string>
  targetIndex: number
  describedKey: string | null
  cardId: string
  bookmarkedIds: ReadonlySet<string>
}

const RailGroup = memo(function RailGroup({
  entries,
  start,
  count,
  active,
  targetIndex,
  describedKey,
  cardId,
  bookmarkedIds
}: RailGroupProps): JSX.Element {
  const t = useT()
  const buttons: JSX.Element[] = []
  for (let index = start; index < start + count; index++) {
    const entry = entries[index]
    const distance = targetIndex < 0 ? -1 : Math.abs(index - targetIndex)
    const bookmarked = bookmarkedIds.has(entry.id)
    buttons.push(
      <button
        key={entry.key}
        type="button"
        className={styles.entry}
        data-entry-key={entry.key}
        data-target={distance === 0 || undefined}
        data-neighbor-distance={distance > 0 && distance <= NEIGHBOR_REACH ? distance : undefined}
        data-bookmarked={bookmarked || undefined}
        aria-current={active.has(entry.id) ? 'true' : undefined}
        aria-describedby={describedKey === entry.key ? cardId : undefined}
        aria-label={t(
          bookmarked ? 'turnNavigation.jumpToBookmarkedMessage' : 'turnNavigation.jumpToMessage',
          { position: index + 1 }
        )}
      >
        <span className={styles.row} aria-hidden="true">
          <span className={styles.marker}>
            <span className={styles.line} />
            {bookmarked && <span className={styles.dot} />}
          </span>
        </span>
      </button>
    )
  }
  return (
    <div className={styles.group} style={{ height: count * ENTRY_HEIGHT_PX }}>
      {buttons}
    </div>
  )
})
