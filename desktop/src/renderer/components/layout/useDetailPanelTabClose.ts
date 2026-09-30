import { useRef } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useUIStore, type SystemDetailTab } from '../../stores/uiStore'
import { useViewerTabStore } from '../../stores/viewerTabStore'
import { useThreadStore } from '../../stores/threadStore'
import { useFileEditorStore } from '../../stores/fileEditorStore'
import { useConfirmDialog } from '../ui/ConfirmDialog'

export type DetailTabRef =
  | { kind: 'system'; id: SystemDetailTab }
  | { kind: 'viewer'; id: string }

export interface DetailTabMenuState {
  canCloseOthers: boolean
  canCloseRight: boolean
}

function sameTab(a: DetailTabRef, b: DetailTabRef): boolean {
  return a.kind === b.kind && a.id === b.id
}

export function detailTabMenuState(order: DetailTabRef[], ref: DetailTabRef): DetailTabMenuState {
  const index = order.findIndex((tab) => sameTab(tab, ref))
  return {
    canCloseOthers: order.some((tab) => !sameTab(tab, ref)),
    canCloseRight: index !== -1 && index < order.length - 1
  }
}

export function currentDetailTabOrder(): DetailTabRef[] {
  const viewer = useViewerTabStore.getState()
  const system = useThreadStore.getState().activeThreadId != null
    ? useUIStore.getState().openSystemTabs
    : []
  return [
    ...system.map((id): DetailTabRef => ({ kind: 'system', id })),
    ...viewer.getThreadState(viewer.currentThreadId ?? '').tabs.map((tab): DetailTabRef => ({ kind: 'viewer', id: tab.id }))
  ]
}

export interface DetailPanelTabClose {
  closeTab: (ref: DetailTabRef) => Promise<boolean>
  closeOtherTabs: (ref: DetailTabRef) => Promise<void>
  closeTabsToRight: (ref: DetailTabRef) => Promise<void>
}

export function useDetailPanelTabClose(): DetailPanelTabClose {
  const t = useT()
  const confirm = useConfirmDialog()
  const closingTabs = useRef(new Set<string>())

  const closeViewerTab = async (threadId: string, tabId: string): Promise<boolean> => {
    const closing = useViewerTabStore.getState().getThreadState(threadId).tabs.find((tab) => tab.id === tabId)
    if (!closing) return false
    if (closing.kind === 'file' && closing.contentClass === 'text') {
      const saved = await useFileEditorStore.getState().save(tabId)
      if (!saved) {
        if (useFileEditorStore.getState().sessions.get(tabId)?.review) return false
        const discard = await confirm({
          title: t('viewer.discardChangesTitle'),
          message: t('viewer.discardChangesMessage'),
          cancelLabel: t('viewer.continueViewing'),
          confirmLabel: t('viewer.discardChanges'),
          danger: true
        })
        if (!discard) return false
      }
      useFileEditorStore.getState().discard(tabId)
    }
    if (closing.kind === 'browser') {
      void window.api.workspace.viewer.browser.destroy({ tabId: closing.id })
    } else if (closing.kind === 'terminal') {
      void window.api.workspace.viewer.terminal.dispose({ tabId: closing.id })
    }
    const latestTabs = useViewerTabStore.getState().getThreadState(threadId).tabs
    useViewerTabStore.getState().closeTab(threadId, tabId)
    if (useViewerTabStore.getState().currentThreadId !== threadId) return true
    const remaining = latestTabs.filter((tab) => tab.id !== tabId)
    const ui = useUIStore.getState()
    const active = ui.activeDetailTab
    if (active.kind === 'viewer' && active.id === tabId) {
      const index = latestTabs.findIndex((tab) => tab.id === tabId)
      const next = index > 0 ? remaining[index - 1] : remaining[0]
      if (next) ui.setActiveViewerTab(next.id)
      else ui.closeViewerTab()
    }
    return true
  }

  const closeTab = async (ref: DetailTabRef): Promise<boolean> => {
    const threadId = useViewerTabStore.getState().currentThreadId
    if (ref.kind === 'system') {
      const viewerTabs = useViewerTabStore.getState().getThreadState(threadId ?? '').tabs
      useUIStore.getState().closeSystemTab(ref.id, viewerTabs[0]?.id ?? null)
      return true
    }
    if (!threadId || closingTabs.current.has(ref.id)) return false
    closingTabs.current.add(ref.id)
    try {
      return await closeViewerTab(threadId, ref.id)
    } finally {
      closingTabs.current.delete(ref.id)
    }
  }

  // Sequential because the confirm dialog shows one unsaved-changes prompt at a time.
  const closeMany = async (targets: DetailTabRef[]): Promise<void> => {
    const threadId = useViewerTabStore.getState().currentThreadId
    for (const target of [...targets].reverse()) {
      if (useViewerTabStore.getState().currentThreadId !== threadId) return
      await closeTab(target)
    }
  }

  const closeOtherTabs = async (ref: DetailTabRef): Promise<void> => {
    const threadId = useViewerTabStore.getState().currentThreadId
    await closeMany(currentDetailTabOrder().filter((tab) => !sameTab(tab, ref)))
    if (useViewerTabStore.getState().currentThreadId !== threadId) return
    if (!currentDetailTabOrder().some((tab) => sameTab(tab, ref))) return
    const ui = useUIStore.getState()
    if (ref.kind === 'system') ui.setActiveDetailTab(ref.id)
    else ui.setActiveViewerTab(ref.id)
  }

  const closeTabsToRight = async (ref: DetailTabRef): Promise<void> => {
    const order = currentDetailTabOrder()
    const index = order.findIndex((tab) => sameTab(tab, ref))
    if (index === -1) return
    await closeMany(order.slice(index + 1))
  }

  return { closeTab, closeOtherTabs, closeTabsToRight }
}
