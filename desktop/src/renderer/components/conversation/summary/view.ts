import type { DesktopPluginConversationAsideLayout } from '@dotcraft/plugin'
import { useSyncExternalStore } from 'react'

export interface SummaryView {
  readonly layout: DesktopPluginConversationAsideLayout | null
  readonly popoverOpen: boolean
  readonly pinned: boolean
}

let current: SummaryView = { layout: null, popoverOpen: false, pinned: true }
const listeners = new Set<() => void>()

function update(patch: Partial<SummaryView>): void {
  const next = { ...current, ...patch }
  if (next.layout === current.layout && next.popoverOpen === current.popoverOpen && next.pinned === current.pinned) return
  current = next
  for (const listener of listeners) listener()
}

function subscribe(listener: () => void): () => void {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

function getView(): SummaryView {
  return current
}

export function useSummaryView(): SummaryView {
  return useSyncExternalStore(subscribe, getView, getView)
}

export function isPanelLayout(layout: DesktopPluginConversationAsideLayout | null): boolean {
  return layout === 'gutter' || layout === 'shift'
}

export function reportLayout(layout: DesktopPluginConversationAsideLayout | null): void {
  update(isPanelLayout(layout) ? { layout, popoverOpen: false } : { layout })
}

export function setPopoverOpen(popoverOpen: boolean): void {
  update({ popoverOpen })
}

export function hydrateSummaryPinned(pinned: boolean): void {
  update({ pinned })
}

export function setSummaryPinned(pinned: boolean): void {
  update({ pinned })
  void window.api.settings
    .set({ summaryHidden: !pinned })
    .catch((err: unknown) => console.error('settings:set summaryHidden failed:', err))
}

export function toggleSummary(): void {
  setSummaryShown(!isSummaryShown(current))
}

function isSummaryShown(view: SummaryView): boolean {
  return isPanelLayout(view.layout) ? view.pinned : view.popoverOpen
}

export function setSummaryShown(shown: boolean): void {
  if (isPanelLayout(current.layout)) setSummaryPinned(shown)
  else update({ popoverOpen: shown })
}

export function resetView(): void {
  update({ layout: null, popoverOpen: false })
}
