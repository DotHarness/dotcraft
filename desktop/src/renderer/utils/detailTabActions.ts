/**
 * Shared dispatcher for the detail panel's add-tab actions, used by the "+"
 * menu, the empty-state launcher cards, and the global keyboard shortcuts.
 *
 * Reads stores through getState() so it can run from anywhere — React
 * components or the window keydown handler — without duplicating the
 * open-a-tab logic in each call site.
 */
import type { AddTabMenuAction } from '../../shared/addTabMenu'
import { useUIStore } from '../stores/uiStore'
import { useViewerTabStore } from '../stores/viewerTabStore'

type TranslateFn = (key: string, vars?: Record<string, string | number>) => string

interface AddTabActionContext {
  /** Active thread id — required for browser/terminal tabs. */
  threadId: string | null
  /** Active workspace path — required for browser/terminal tabs. */
  workspacePath: string
  /** Locale translator, used for initial browser/terminal tab labels. */
  t: TranslateFn
}

export function performAddTabAction(action: AddTabMenuAction, ctx: AddTabActionContext): void {
  const ui = useUIStore.getState()

  // System tabs (Diff / Progress) — no active workspace required.
  if (action === 'newChanges') {
    ui.setActiveDetailTab('changes')
    return
  }
  if (action === 'newPlan') {
    ui.setActiveDetailTab('plan')
    return
  }
  if (action === 'newSubagents') {
    ui.setActiveDetailTab('subagents')
    return
  }

  const { threadId, workspacePath, t } = ctx
  if (!threadId || !workspacePath) return
  const viewer = useViewerTabStore.getState()

  if (action === 'openFile') {
    const tabId = viewer.openFiles({ threadId, initialLabel: t('detailPanel.launcherFilesTitle') })
    ui.setExplorerVisible(true)
    ui.setActiveViewerTab(tabId)
    return
  }

  if (action === 'newBrowser') {
    const tabId = viewer.openBrowser({ threadId, initialLabel: t('viewer.newBrowserTab') })
    ui.setActiveViewerTab(tabId)
    void window.api.workspace.viewer.browser.create({ tabId, threadId, workspacePath })
    return
  }

  if (action === 'newTerminal') {
    const tabId = viewer.openTerminal({ threadId, cwd: workspacePath, initialLabel: t('viewer.newTerminalTab') })
    ui.setActiveViewerTab(tabId)
  }
}
