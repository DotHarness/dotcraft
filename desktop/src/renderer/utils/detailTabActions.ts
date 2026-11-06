import type { AddTabMenuAction } from '../../shared/addTabMenu'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useDetailPanelStore, type SystemDetailTab } from '../stores/detailPanelStore'
import { activeDetailScopeId } from './detailPanelScope'
import { openActiveSideChat } from './sideChat'

const SYSTEM_TAB_ACTIONS = {
  newChanges: 'changes',
  newPlan: 'plan',
  newSubagents: 'subagents',
  newBoard: 'board'
} satisfies Partial<Record<AddTabMenuAction, SystemDetailTab>>

type TranslateFn = (key: string, vars?: Record<string, string | number>) => string

interface AddTabActionContext {

  workspacePath: string

  t: TranslateFn
}

export function performAddTabAction(action: AddTabMenuAction, ctx: AddTabActionContext): void {
  const ui = useUIStore.getState()

  const systemTab = SYSTEM_TAB_ACTIONS[action as keyof typeof SYSTEM_TAB_ACTIONS]
  if (systemTab) {
    if (!useThreadStore.getState().activeThreadId) return
    useDetailPanelStore.getState().setActiveDetailTab(systemTab)
    return
  }

  if (action === 'newSideChat') {
    openActiveSideChat(ctx.t)
    return
  }

  const { workspacePath, t } = ctx
  const threadId = activeDetailScopeId()
  if (!threadId || !workspacePath) return
  const viewer = useDetailPanelStore.getState()

  if (action === 'openFile') {
    const tabId = viewer.openFiles({ threadId, initialLabel: t('detailPanel.launcherFilesTitle') })
    ui.setExplorerVisible(true)
    useDetailPanelStore.getState().setActiveViewerTab(tabId)
    return
  }

  if (action === 'newBrowser') {
    const tabId = viewer.openBrowser({ threadId, initialLabel: t('viewer.newBrowserTab') })
    useDetailPanelStore.getState().setActiveViewerTab(tabId)
    const tab = viewer.getThreadState(threadId).tabs.find(item => item.id === tabId)
    if (tab?.kind === 'browser') void window.api.workspace.viewer.browser.create({
      tabId,
      threadId,
      workspacePath: viewer.currentWorkspacePath!,
      projectKey: viewer.currentProjectKey,
      browserStorageId: tab.browserStorageId,
      restore: tab.restore ?? 'none'
    })
    return
  }

  if (action === 'newTerminal') {
    const tabId = viewer.openTerminal({ threadId, cwd: workspacePath, initialLabel: t('viewer.newTerminalTab') })
    useDetailPanelStore.getState().setActiveViewerTab(tabId)
  }
}
