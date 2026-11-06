import type { PanelActiveTab, PanelCheckpoint, PanelSystemTab } from './panelState'
import type { ViewerTab, ViewerContentClass } from './types'
const SYSTEM_TABS: PanelSystemTab[] = ['changes', 'plan', 'subagents', 'board']
const CONTENT_CLASSES: ViewerContentClass[] = ['text', 'image', 'pdf', 'spreadsheet', 'presentation', 'video', 'unsupported']
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value)
const string = (value: unknown): value is string => typeof value === 'string'
const finite = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value)

function restoreTab(value: unknown): ViewerTab | null {
  if (!record(value) || !string(value.id) || !value.id || !string(value.label)) return null
  const base = {
    id: value.id,
    label: value.label
  }
  switch (value.kind) {
    case 'files': return {
      ...base,
      kind: 'files'
    }
    case 'file': {
      if (!string(value.absolutePath) || !string(value.relativePath) || !CONTENT_CLASSES.includes(value.contentClass as ViewerContentClass)) return null
      const tab: ViewerTab = {
        ...base,
        kind: 'file',
        absolutePath: value.absolutePath,
        relativePath: value.relativePath,
        contentClass: value.contentClass as ViewerContentClass
      }
      if (typeof value.wordWrap === 'boolean') tab.wordWrap = value.wordWrap
      if (record(value.navigationHint)) {
        const hint = value.navigationHint
        tab.navigationHint = {
          ...(finite(hint.line) ? {
            line: Math.max(1, Math.floor(hint.line))
          } : {}),
          ...(finite(hint.column) ? {
            column: Math.max(1, Math.floor(hint.column))
          } : {}),
          ...(finite(hint.endLine) ? {
            endLine: Math.max(1, Math.floor(hint.endLine))
          } : {}),
          ...(string(hint.fragment) ? {
            fragment: hint.fragment
          } : {}),
          ...(string(hint.query) ? {
            query: hint.query
          } : {})
        }
      }
      return tab
    }
    case 'browser': {
      if (!string(value.currentUrl)) return null
      const tab: ViewerTab = {
        ...base,
        kind: 'browser',
        target: base.id,
        currentUrl: value.currentUrl,
        loading: false,
        canGoBack: false,
        canGoForward: false
      }
      if (string(value.browserStorageId)) tab.browserStorageId = value.browserStorageId
      if (string(value.title)) tab.title = value.title
      return tab
    }
    case 'terminal':
      return string(value.cwd) ? {
        ...base,
        kind: 'terminal',
        cwd: value.cwd,
        hasStarted: false,
        ...(string(value.title) ? {
          title: value.title
        } : {})
      } : null
    case 'workflow':
      return string(value.threadId) && string(value.runId) ? {
        ...base,
        kind: 'workflow',
        threadId: value.threadId,
        runId: value.runId
      } : null
    case 'automation':
      return string(value.threadId) && string(value.automationId) ? {
        ...base,
        kind: 'automation',
        threadId: value.threadId,
        automationId: value.automationId
      } : null
    default: return null
  }
}

export function normalizePanelCheckpoint(value: unknown): PanelCheckpoint | null {
  if (!record(value) || value.version !== 1 || !Array.isArray(value.tabs) || !Array.isArray(value.systemTabs) || typeof value.visible !== 'boolean' || typeof value.fullView !== 'boolean' || !finite(value.width)) return null
  const ids = new Set<string>()
  const tabs = value.tabs.flatMap((candidate) => {
    const tab = restoreTab(candidate)
    if (!tab || ids.has(tab.id)) return []
    ids.add(tab.id)
    return [tab]
  })
  const savedSystemTabs = value.systemTabs
  const systemTabs = SYSTEM_TABS.filter((id) => savedSystemTabs.includes(id))
  let activeTab: PanelActiveTab = {
    kind: 'launcher'
  }
  const active = value.activeTab
  if (record(active) && active.kind === 'viewer' && string(active.id) && ids.has(active.id)) activeTab = {
    kind: 'viewer',
    id: active.id
  }
  else if (record(active) && active.kind === 'system' && systemTabs.includes(active.id as PanelSystemTab)) activeTab = {
    kind: 'system',
    id: active.id as PanelSystemTab
  }
  else if (tabs.length) activeTab = {
    kind: 'viewer',
    id: tabs[tabs.length - 1].id
  }
  else if (systemTabs.length) activeTab = {
    kind: 'system',
    id: systemTabs[systemTabs.length - 1]
  }
  return {
    version: 1,
    tabs,
    systemTabs,
    activeTab,
    visible: value.visible,
    fullView: value.fullView,
    width: Math.max(300, value.width)
  }
}

export function toPanelCheckpoint(value: PanelCheckpoint): PanelCheckpoint {
  const checkpoint = normalizePanelCheckpoint(value)
  if (!checkpoint) throw new Error('Invalid panel checkpoint.')
  return checkpoint
}
