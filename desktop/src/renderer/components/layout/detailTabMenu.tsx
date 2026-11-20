import { Copy, CopyPlus, ExternalLink, FolderOpen, Link, Plus, RotateCw, X } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useDetailPanelStore } from '../../stores/detailPanelStore'
import { addToast } from '../../stores/toastStore'
import type { BrowserViewerTab, FileViewerTab } from '../../../shared/viewer/types'
import { openBrowserTab } from '../../utils/detailTabActions'
import type { ContextMenuEntry } from '../ui/ContextMenu'
import {
  currentDetailTabOrder,
  detailTabMenuState,
  type DetailPanelTabClose,
  type DetailTabRef
} from './useDetailPanelTabClose'

type Translate = ReturnType<typeof useT>

function loadedPageUrl(tab: BrowserViewerTab): string | null {
  return /^https?:\/\//i.test(tab.currentUrl) ? tab.currentUrl : null
}

async function copyText(text: string, t: Translate): Promise<void> {
  try {
    await navigator.clipboard.writeText(text)
    addToast(t('toast.copied'), 'success')
  } catch {
    addToast(t('viewer.actionFailed'), 'warning')
  }
}

function browserItems(tab: BrowserViewerTab, t: Translate): ContextMenuEntry[] {
  const url = loadedPageUrl(tab)
  const items: ContextMenuEntry[] = [
    {
      label: t('detailPanel.tabMenu.newTabRight'),
      icon: <Plus size={14} aria-hidden />,
      onClick: () => openBrowserTab({ label: t('viewer.newBrowserTab'), insertAfterTabId: tab.id })
    },
    {
      label: t('viewer.browser.reload'),
      icon: <RotateCw size={14} aria-hidden />,
      onClick: () => { void window.api.workspace.viewer.browser.reload({ tabId: tab.id }) }
    },
    {
      label: t('detailPanel.tabMenu.duplicate'),
      icon: <CopyPlus size={14} aria-hidden />,
      onClick: () => openBrowserTab({
        url: tab.currentUrl,
        label: tab.title || tab.label,
        insertAfterTabId: tab.id
      })
    }
  ]
  if (url) {
    items.push(
      { label: t('detailPanel.tabMenu.copyUrl'), icon: <Link size={14} aria-hidden />, onClick: () => { void copyText(url, t) } },
      {
        label: t('viewer.browser.openExternal'),
        icon: <ExternalLink size={14} aria-hidden />,
        onClick: () => { void window.api.shell.openExternal(url) }
      }
    )
  }
  return items
}

function fileItems(tab: FileViewerTab, remote: boolean, t: Translate): ContextMenuEntry[] {
  const items: ContextMenuEntry[] = [
    { label: t('viewer.copyPath'), icon: <Copy size={14} aria-hidden />, onClick: () => { void copyText(tab.absolutePath, t) } }
  ]
  if (!remote) {
    items.push({
      label: t('conversation.reference.openInExplorer'),
      icon: <FolderOpen size={14} aria-hidden />,
      onClick: () => {
        window.api.shell.revealLocalPath(tab.absolutePath).catch(() => addToast(t('viewer.actionFailed'), 'warning'))
      }
    })
  }
  return items
}

export function useDetailTabMenuItems(
  { closeTab, closeOtherTabs, closeTabsToRight }: DetailPanelTabClose,
  remoteWorkspace: boolean
): (ref: DetailTabRef) => ContextMenuEntry[] {
  const t = useT()

  return (ref) => {
    const viewer = useDetailPanelStore.getState()
    const tab = ref.kind === 'viewer'
      ? viewer.getThreadState(viewer.currentThreadId ?? '').tabs.find((item) => item.id === ref.id)
      : undefined
    const leading = tab?.kind === 'browser'
      ? browserItems(tab, t)
      : tab?.kind === 'file'
        ? fileItems(tab, remoteWorkspace, t)
        : []
    const { canCloseOthers, canCloseRight } = detailTabMenuState(currentDetailTabOrder(), ref)
    const icon = <X size={14} aria-hidden />
    return [
      ...leading,
      ...(leading.length > 0 ? [{ type: 'separator' as const }] : []),
      { label: t('detailPanel.tabMenu.close'), icon, onClick: () => { void closeTab(ref) } },
      { label: t('detailPanel.tabMenu.closeOthers'), icon, disabled: !canCloseOthers, onClick: () => { void closeOtherTabs(ref) } },
      { label: t('detailPanel.tabMenu.closeRight'), icon, disabled: !canCloseRight, onClick: () => { void closeTabsToRight(ref) } }
    ]
  }
}
