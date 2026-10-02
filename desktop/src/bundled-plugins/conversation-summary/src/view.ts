import type { DesktopPluginConversationAsideLayout } from '@dotcraft/plugin'
import { useSyncExternalStore } from 'react'
import { getSettings, setPinned } from './settings'

export interface SummaryView {
  readonly layout: DesktopPluginConversationAsideLayout | null
  readonly popoverOpen: boolean
}

let current: SummaryView = { layout: null, popoverOpen: false }
const listeners = new Set<() => void>()

function update(patch: Partial<SummaryView>): void {
  const next = { ...current, ...patch }
  if (next.layout === current.layout && next.popoverOpen === current.popoverOpen) return
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

export function toggleSummary(): void {
  if (isPanelLayout(current.layout)) {
    const settings = getSettings()
    if (settings) setPinned(!settings.pinned)
    return
  }
  update({ popoverOpen: !current.popoverOpen })
}

export function resetView(): void {
  update({ layout: null, popoverOpen: false })
}
