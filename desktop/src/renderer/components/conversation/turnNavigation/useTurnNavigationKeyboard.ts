import { useEffect, type RefObject } from 'react'
import type { TurnNavigationEntry } from './navigationIndex'
import { bubbleOf, isReachable, pickAdjacentEntry, userMessageElements } from './turnJump'

const TEXT_ENTRY_SELECTOR = [
  'input',
  'textarea',
  'select',
  '[contenteditable=""]',
  '[contenteditable="true"]',
  '[contenteditable="plaintext-only"]'
].join(', ')

function suppressesShortcut(target: EventTarget | null): boolean {
  return target instanceof Element &&
    target.closest(`${TEXT_ENTRY_SELECTOR}, dialog, [role="dialog"], [role="alertdialog"]`) !== null
}

function renderedTops(scrollEl: HTMLElement, entries: readonly TurnNavigationEntry[]): Array<{ index: number; top: number }> {
  const messages = userMessageElements(scrollEl)
  return entries.flatMap((entry, index) => {
    const message = messages.get(entry.id)
    return message ? [{ index, top: bubbleOf(message).getBoundingClientRect().top }] : []
  })
}

export function useTurnNavigationKeyboard(
  scrollRef: RefObject<HTMLDivElement | null>,
  entriesRef: RefObject<TurnNavigationEntry[]>,
  onJump: (entry: TurnNavigationEntry) => void
): void {
  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.defaultPrevented || !event.altKey || event.shiftKey || event.ctrlKey || event.metaKey) return
      if (event.key !== 'ArrowUp' && event.key !== 'ArrowDown') return
      const scrollEl = scrollRef.current
      const entries = entriesRef.current
      if (!scrollEl || !entries || suppressesShortcut(event.target)) return
      const target = pickAdjacentEntry(
        entries,
        renderedTops(scrollEl, entries),
        scrollEl.getBoundingClientRect().top,
        event.key === 'ArrowUp' ? 'previous' : 'next'
      )
      if (!target || !isReachable(scrollEl, target)) return
      event.preventDefault()
      onJump(target)
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [entriesRef, onJump, scrollRef])
}
