import { useEffect, useState, type RefObject } from 'react'
import type { TurnNavigationEntry } from './navigationIndex'

const ACTIVE_ROOT_MARGIN = '-16px 0px 0px 0px'
const TRACKED_SELECTOR = '[data-turn-id], [data-user-message-id]'

function touchesTrackedContent(records: MutationRecord[]): boolean {
  return records.some((record) =>
    [...record.addedNodes, ...record.removedNodes].some((node) =>
      node instanceof Element && (node.matches(TRACKED_SELECTOR) || node.querySelector(TRACKED_SELECTOR) !== null)))
}

function sameMembers(current: ReadonlySet<string>, next: readonly string[]): boolean {
  return current.size === next.length && next.every((id) => current.has(id))
}

/**
 * Entry ids whose part of the transcript intersects the viewport. A Turn's first entry
 * owns the whole Turn block; a later entry owns its own user message.
 */
export function useActiveEntries(
  scrollRef: RefObject<HTMLDivElement | null>,
  entries: readonly TurnNavigationEntry[]
): ReadonlySet<string> {
  const idsKey = entries.map((entry) => entry.id).join('\u0000')
  const [active, setActive] = useState<ReadonlySet<string>>(() => {
    const newest = entries[entries.length - 1]
    return new Set(newest ? [newest.id] : [])
  })

  useEffect(() => {
    const scrollEl = scrollRef.current
    if (!scrollEl || typeof IntersectionObserver === 'undefined') return
    const ids = idsKey.length === 0 ? [] : idsKey.split('\u0000')
    const known = new Set(ids)
    const intersecting = new Set<string>()
    const observed = new Map<Element, string>()

    const publish = (): void => {
      const next = ids.filter((id) => intersecting.has(id))
      if (next.length > 0) setActive((current) => sameMembers(current, next) ? current : new Set(next))
    }
    const observer = new IntersectionObserver((records) => {
      for (const record of records) {
        const id = observed.get(record.target)
        if (id === undefined) continue
        if (record.isIntersecting) intersecting.add(id)
        else intersecting.delete(id)
      }
      publish()
    }, { root: scrollEl, rootMargin: ACTIVE_ROOT_MARGIN })

    const scan = (): void => {
      const owners = new Set<Element>()
      for (const message of scrollEl.querySelectorAll<HTMLElement>('[data-user-message-id]')) {
        const shell = message.closest<HTMLElement>('[data-turn-id]')
        const id = shell ? `${shell.dataset.turnId}:${message.dataset.userMessageId}` : ''
        if (!shell || !known.has(id)) continue
        const owner = owners.has(shell) ? message : shell
        owners.add(owner)
        const previous = observed.get(owner)
        if (previous === id) continue
        if (previous !== undefined) {
          intersecting.delete(previous)
          observer.unobserve(owner)
        }
        observed.set(owner, id)
        observer.observe(owner)
      }
      for (const [owner, id] of observed) {
        if (owners.has(owner)) continue
        intersecting.delete(id)
        observed.delete(owner)
        observer.unobserve(owner)
      }
      publish()
    }

    const mutations = new MutationObserver((records) => {
      if (touchesTrackedContent(records)) scan()
    })
    mutations.observe(scrollEl, { childList: true, subtree: true })
    scan()
    return () => {
      mutations.disconnect()
      observer.disconnect()
    }
  }, [idsKey, scrollRef])

  return active
}
