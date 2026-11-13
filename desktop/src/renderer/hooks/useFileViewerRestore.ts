import { useEffect } from 'react'
import type { FileViewerTab, ViewerTab } from '../../shared/viewer/types'
import { useDetailPanelStore } from '../stores/detailPanelStore'
import { prepareFileViewer } from '../utils/fileViewer'

export function useFileViewerRestore(tab: ViewerTab | null | undefined): boolean {
  const panel = useDetailPanelStore(state => state.getCurrentPanel())
  const { projectKey, threadId } = panel.scope
  const workspacePath = panel.browserWorkspacePath

  useEffect(() => {
    if (tab?.kind !== 'file' || tab.restore !== 'required') return
    let cancelled = false
    const finish = (patch: Partial<FileViewerTab>): void => {
      if (cancelled) return
      const store = useDetailPanelStore.getState()
      const scope = store.getCurrentScope()
      if (scope?.projectKey !== projectKey || scope.threadId !== threadId) return
      const current = store.getPanel(threadId, projectKey)
      if (!current.tabs.includes(tab)) return
      store.patchPanel({
        tabs: current.tabs.map(candidate => candidate === tab ? { ...tab, ...patch, restore: 'none' } : candidate)
      }, threadId, projectKey)
    }
    void prepareFileViewer(tab.absolutePath, workspacePath).then(
      prepared => finish({ ...prepared, errorMessage: undefined }),
      error => finish({ errorMessage: error instanceof Error ? error.message : String(error) })
    )
    return () => { cancelled = true }
  }, [tab, projectKey, threadId, workspacePath])

  return tab?.kind === 'file' && tab.restore === 'required'
}
