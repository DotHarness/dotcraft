import { useRef } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useDetailPanelStore, type SystemDetailTab } from '../../stores/detailPanelStore'
import { useThreadStore } from '../../stores/threadStore'
import { useFileEditorStore } from '../../stores/fileEditorStore'
import { useSideChatStore } from '../../stores/sideChatStore'
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
  const viewer = useDetailPanelStore.getState()
  const system = useThreadStore.getState().activeThreadId != null
    ? useDetailPanelStore.getState().getCurrentPanel().openSystemTabs
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
    const closing = useDetailPanelStore.getState().getThreadState(threadId).tabs.find((tab) => tab.id === tabId)
    if (!closing) return false
    if (closing.kind === 'file' && closing.contentClass === 'text') {
      const saved = await useFileEditorStore.getState().save(tabId)
      if (!saved) {
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
    if (closing.kind === 'sideChat' && (useSideChatStore.getState().chats.get(closing.sideChatKey)?.turns.length ?? 0) > 0) {
      const discard = await confirm({
        title: t('sideChat.closeTitle'),
        message: t('sideChat.closeMessage'),
        confirmLabel: t('sideChat.closeConfirm'),
        danger: true
      })
      if (!discard) return false
    }
    if (closing.kind === 'browser') {
      void window.api.workspace.viewer.browser.destroy({ tabId: closing.id })
    } else if (closing.kind === 'terminal') {
      void window.api.workspace.viewer.terminal.dispose({ tabId: closing.id })
    }
    useDetailPanelStore.getState().closeTab(threadId, tabId)
    return true
  }

  const closeTab = async (ref: DetailTabRef): Promise<boolean> => {
    const threadId = useDetailPanelStore.getState().currentThreadId
    if (ref.kind === 'system') {
      const viewerTabs = useDetailPanelStore.getState().getThreadState(threadId ?? '').tabs
      useDetailPanelStore.getState().closeSystemTab(ref.id, viewerTabs[0]?.id ?? null)
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
    const threadId = useDetailPanelStore.getState().currentThreadId
    for (const target of [...targets].reverse()) {
      if (useDetailPanelStore.getState().currentThreadId !== threadId) return
      await closeTab(target)
    }
  }

  const closeOtherTabs = async (ref: DetailTabRef): Promise<void> => {
    const threadId = useDetailPanelStore.getState().currentThreadId
    await closeMany(currentDetailTabOrder().filter((tab) => !sameTab(tab, ref)))
    if (useDetailPanelStore.getState().currentThreadId !== threadId) return
    if (!currentDetailTabOrder().some((tab) => sameTab(tab, ref))) return
    if (ref.kind === 'system') useDetailPanelStore.getState().setActiveDetailTab(ref.id)
    else useDetailPanelStore.getState().setActiveViewerTab(ref.id)
  }

  const closeTabsToRight = async (ref: DetailTabRef): Promise<void> => {
    const order = currentDetailTabOrder()
    const index = order.findIndex((tab) => sameTab(tab, ref))
    if (index === -1) return
    await closeMany(order.slice(index + 1))
  }

  return { closeTab, closeOtherTabs, closeTabsToRight }
}
