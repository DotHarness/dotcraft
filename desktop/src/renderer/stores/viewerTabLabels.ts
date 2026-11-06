import type { ViewerTab, FileViewerTab, TerminalViewerTab, FileNavigationHint, FileRevealRequest } from '../../shared/viewer/types'

export function computeLabels(tabs: ViewerTab[]): ViewerTab[] {
  if (tabs.length === 0) return tabs
  const fileTabs = tabs
    .map((tab, index) => ({
      tab,
      index
    }))
    .filter((entry): entry is { tab: FileViewerTab; index: number } => entry.tab.kind === 'file')
  if (fileTabs.length === 0) {
    return tabs.map((tab, index) => {
      if (tab.kind === 'browser') return {
        ...tab,
        label: browserDefaultLabel(tab)
      }
      if (tab.kind === 'terminal') return {
        ...tab,
        label: terminalDefaultLabel(tabs, index)
      }
      return tab
    })
  }
  const basenameMap = new Map<string, number[]>()
  for (const { tab, index } of fileTabs) {
    const parts = tab.relativePath.replace(/\\/g, '/').split('/')
    const base = parts[parts.length - 1] ?? tab.relativePath
    const existing = basenameMap.get(base)
    if (existing) {
      existing.push(index)
    } else {
      basenameMap.set(base, [index])
    }
  }
  const labels = tabs.map((tab) => {
    if (tab.kind === 'files') return tab.label
    if (tab.kind === 'browser') return browserDefaultLabel(tab)
    if (tab.kind === 'terminal') return tab.label
    if (tab.kind === 'workflow' || tab.kind === 'automation' || tab.kind === 'sideChat') return tab.label
    const parts = tab.relativePath.replace(/\\/g, '/').split('/')
    return parts[parts.length - 1] ?? tab.relativePath
  })
  for (const [, indices] of basenameMap.entries()) {
    if (indices.length <= 1) continue
    let depth = 1
    const maxDepth = Math.max(
      ...indices.map((i) => {
        const tab = tabs[i] as FileViewerTab
        return tab.relativePath.replace(/\\/g, '/').split('/').length - 1
      })
    )
    while (depth <= maxDepth) {
      const candidates = indices.map((i) => {
        const tab = tabs[i] as FileViewerTab
        const parts = tab.relativePath.replace(/\\/g, '/').split('/')
        const start = Math.max(0, parts.length - 1 - depth)
        return parts.slice(start).join('/')
      })
      const unique = new Set(candidates).size === candidates.length
      if (unique) {
        for (let j = 0; j < indices.length; j++) {
          labels[indices[j]!] = candidates[j]!
        }
        break
      }
      depth++
    }
    if (depth > maxDepth) {
      for (const i of indices) {
        const tab = tabs[i] as FileViewerTab
        labels[i] = tab.relativePath
      }
    }
  }
  return tabs.map((tab, i) => {
    if (tab.kind === 'browser') {
      return {
        ...tab,
        label: labels[i] ?? browserDefaultLabel(tab)
      }
    }
    if (tab.kind === 'terminal') {
      return {
        ...tab,
        label: terminalDefaultLabel(tabs, i)
      }
    }
    if (tab.kind === 'workflow' || tab.kind === 'automation' || tab.kind === 'sideChat' || tab.kind === 'files') return tab
    return {
      ...tab,
      label: labels[i] ?? tab.label
    }
  })
}

function browserDefaultLabel(tab: ViewerTab): string {
  if (tab.kind !== 'browser') return tab.label
  if (tab.title?.trim()) return tab.title.trim()
  const url = tab.currentUrl.trim()
  if (!url) return 'New Tab'
  try {
    const parsed = new URL(url)
    return parsed.host || 'New Tab'
  } catch {
    return 'New Tab'
  }
}

function terminalDefaultLabel(tabs: ViewerTab[], tabIndex: number): string {
  const tab = tabs[tabIndex]
  if (!tab || tab.kind !== 'terminal') return 'Terminal'
  if (tab.title) return tab.title
  const terminalTabs = tabs.filter((item): item is TerminalViewerTab => item.kind === 'terminal' && !item.title)
  const position = terminalTabs.findIndex((item) => item.id === tab.id)
  return position >= 0 ? `Terminal ${position + 1}` : 'Terminal'
}

export function applyFileNavigationHint(tab: FileViewerTab, navigationHint?: FileNavigationHint, reveal?: FileReveal): FileViewerTab {
  const next: FileViewerTab = {
    ...tab,
    navigationRevision: (tab.navigationRevision ?? 0) + 1
  }
  if (navigationHint) {
    next.navigationHint = {
      ...navigationHint
    }
  } else {
    delete next.navigationHint
  }
  if (reveal) next.pendingReveal = revealRequest(reveal)
  return next
}

export type FileReveal = Omit<FileRevealRequest, 'revision'>
let _revealRevision = 0

export function revealRequest(reveal: FileReveal): FileRevealRequest {
  return {
    targetKind: reveal.targetKind,
    location: {
      ...reveal.location
    },
    revision: ++_revealRevision
  }
}
