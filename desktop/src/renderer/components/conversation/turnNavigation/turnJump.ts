import { openThreadHistoryAround } from '../../../stores/threadHistoryStore'
import type { TurnNavigationEntry } from './navigationIndex'

const POSITION_TOLERANCE_PX = 24
const DRIFT_RECHECK_MS = 350
const REVEAL_RENDER_TIMEOUT_MS = 1500
const FLASH_FILL = 'color-mix(in srgb, var(--text-primary) 14%, var(--user-message-bg))'
const FLASH_KEYFRAMES: Keyframe[] = [
  { backgroundColor: FLASH_FILL },
  { backgroundColor: FLASH_FILL, offset: 0.35 },
  { backgroundColor: 'var(--user-message-bg)' }
]
const FLASH_TIMING: KeyframeAnimationOptions = { duration: 1400, easing: 'cubic-bezier(0.23, 1, 0.32, 1)' }

export type JumpMode = 'jump' | 'scrub'
type AdjacentDirection = 'previous' | 'next'

interface EntryTarget {
  scrollElement: HTMLElement
  bubble: HTMLElement | null
}

function prefersReducedMotion(): boolean {
  const setting = document.documentElement.dataset.reduceMotion
  if (setting === 'on') return true
  if (setting === 'off') return false
  return typeof window.matchMedia === 'function' && window.matchMedia('(prefers-reduced-motion: reduce)').matches
}

function turnShell(scrollEl: HTMLElement, turnId: string): HTMLElement | undefined {
  return [...scrollEl.querySelectorAll<HTMLElement>('[data-turn-id]')].find((node) => node.dataset.turnId === turnId)
}

export function userMessageElements(scrollEl: HTMLElement): Map<string, HTMLElement> {
  const elements = new Map<string, HTMLElement>()
  for (const message of scrollEl.querySelectorAll<HTMLElement>('[data-user-message-id]')) {
    const turnId = message.closest<HTMLElement>('[data-turn-id]')?.dataset.turnId
    if (turnId) elements.set(`${turnId}:${message.dataset.userMessageId}`, message)
  }
  return elements
}

export function bubbleOf(message: HTMLElement): HTMLElement {
  return message.querySelector<HTMLElement>('[data-user-message-bubble]') ?? message
}

function findEntryTarget(scrollEl: HTMLElement, entry: TurnNavigationEntry): EntryTarget | null {
  const shell = turnShell(scrollEl, entry.turnId)
  if (!shell) return null
  const messages = [...shell.querySelectorAll<HTMLElement>('[data-user-message-id]')]
  const message = messages.find((node) => node.dataset.userMessageId === entry.content?.userItemId) ?? messages[0]
  if (!message) return { scrollElement: shell, bubble: null }
  return { scrollElement: message, bubble: message.querySelector<HTMLElement>('[data-user-message-bubble]') }
}

function waitForEntryTarget(scrollEl: HTMLElement, entry: TurnNavigationEntry): Promise<EntryTarget | null> {
  const startedAt = performance.now()
  return new Promise((resolve) => {
    const check = (): void => {
      const target = scrollEl.isConnected ? findEntryTarget(scrollEl, entry) : null
      if (target || !scrollEl.isConnected || performance.now() - startedAt > REVEAL_RENDER_TIMEOUT_MS) {
        resolve(target)
        return
      }
      requestAnimationFrame(check)
    }
    check()
  })
}

function scrollToTarget(scrollEl: HTMLElement, target: EntryTarget, smooth: boolean): void {
  const reduced = prefersReducedMotion()
  const animate = smooth && !reduced
  target.scrollElement.scrollIntoView({ behavior: animate ? 'smooth' : 'instant', block: 'start' })
  if (!reduced) target.bubble?.animate?.(FLASH_KEYFRAMES, FLASH_TIMING)
  if (!animate) return
  const measured = target.bubble ?? target.scrollElement
  window.setTimeout(() => {
    if (!scrollEl.isConnected || !measured.isConnected) return
    const drift = measured.getBoundingClientRect().top - scrollEl.getBoundingClientRect().top
    if (Math.abs(drift) > POSITION_TOLERANCE_PX) {
      target.scrollElement.scrollIntoView({ behavior: 'smooth', block: 'start' })
    }
  }, DRIFT_RECHECK_MS)
}

export function isReachable(scrollEl: HTMLElement, entry: TurnNavigationEntry): boolean {
  return entry.position !== null || findEntryTarget(scrollEl, entry) !== null
}

export async function jumpToEntry(
  scrollEl: HTMLElement,
  threadId: string,
  entry: TurnNavigationEntry,
  mode: JumpMode
): Promise<void> {
  const target = findEntryTarget(scrollEl, entry)
  if (target) {
    scrollToTarget(scrollEl, target, mode === 'jump')
    return
  }
  if (mode === 'scrub' || !entry.position) return
  try {
    await openThreadHistoryAround(threadId, entry.position)
  } catch (error) {
    console.error('turn navigation reveal failed:', error)
    return
  }
  const revealed = await waitForEntryTarget(scrollEl, entry)
  if (revealed) scrollToTarget(scrollEl, revealed, false)
}

/**
 * Picks the entry `Alt+ArrowUp` / `Alt+ArrowDown` jumps to. `rendered` holds the bubble top
 * of each entry the transcript renders, in entry order.
 */
export function pickAdjacentEntry(
  entries: readonly TurnNavigationEntry[],
  rendered: ReadonlyArray<{ index: number; top: number }>,
  viewportTop: number,
  direction: AdjacentDirection
): TurnNavigationEntry | null {
  if (rendered.length === 0) return (direction === 'next' ? entries[0] : entries[entries.length - 1]) ?? null
  if (direction === 'next') {
    const below = rendered.findIndex(({ top }) => top > viewportTop + POSITION_TOLERANCE_PX)
    const after = below === -1 ? rendered[rendered.length - 1].index : (rendered[below - 1]?.index ?? -1)
    return entries[after + 1] ?? null
  }
  for (let position = rendered.length - 1; position >= 0; position--) {
    const { index, top } = rendered[position]
    if (Math.abs(top - viewportTop) <= POSITION_TOLERANCE_PX) return entries[index - 1] ?? null
    if (top < viewportTop) return entries[index]
  }
  return entries[rendered[0].index] ?? null
}
