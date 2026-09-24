/**
 * Reads stores through getState() so the "+" menu, the launcher cards, and the
 * window keydown handler can all dispatch without duplicating the open-a-tab logic.
 */
import type { AddTabMenuAction } from '../../shared/addTabMenu'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useViewerTabStore } from '../stores/viewerTabStore'
import { activeDetailScopeId } from './detailPanelScope'

type TranslateFn = (key: string, vars?: Record<string, string | number>) => string

interface AddTabActionContext {
  /** Active workspace path — required for browser/terminal tabs. */
  workspacePath: string
  /** Locale translator, used for initial browser/terminal tab labels. */
  t: TranslateFn
}

export function performAddTabAction(action: AddTabMenuAction, ctx: AddTabActionContext): void {
  const ui = useUIStore.getState()

  // System tabs (Diff / Progress) — no active workspace required.
  if (action === 'newChanges' || action === 'newPlan' || action === 'newSubagents') {
    if (!useThreadStore.getState().activeThreadId) return
    ui.setActiveDetailTab(action === 'newChanges' ? 'changes' : action === 'newPlan' ? 'plan' : 'subagents')
    return
  }

  const { workspacePath, t } = ctx
  const threadId = activeDetailScopeId()
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
