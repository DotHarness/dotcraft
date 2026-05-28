import { resolveConversationLink, type LinkRejectReason } from '../../shared/viewer/linkResolver'
import type { FileNavigationHint } from '../../shared/viewer/types'
import { useUIStore } from '../stores/uiStore'
import { addToast } from '../stores/toastStore'
import { useViewerTabStore } from '../stores/viewerTabStore'

type Translator = (key: string) => string

interface OpenFileViewerParams {
  threadId: string
  workspacePath: string
  absolutePath: string
  forceNew: boolean
  hint?: FileNavigationHint
  t: Translator
}

function normalizePath(value: string): string {
  return value.replace(/\\/g, '/')
}

function stripTrailingSlash(value: string): string {
  return value.replace(/\/+$/, '')
}

export function deriveRelativePathForViewer(absolutePath: string, workspacePath: string): string {
  const normalizedAbsolute = normalizePath(absolutePath)
  const normalizedWorkspace = stripTrailingSlash(normalizePath(workspacePath))
  if (!normalizedWorkspace) return normalizedAbsolute
  const lowerAbs = normalizedAbsolute.toLowerCase()
  const lowerWorkspace = normalizedWorkspace.toLowerCase()
  if (lowerAbs === lowerWorkspace) return normalizedAbsolute.split('/').pop() ?? normalizedAbsolute
  const prefix = `${lowerWorkspace}/`
  if (!lowerAbs.startsWith(prefix)) return normalizedAbsolute
  return normalizedAbsolute.slice(normalizedWorkspace.length + 1)
}

function rejectReasonToMessageKey(reason: LinkRejectReason): string {
  switch (reason) {
    case 'unsupported-scheme':
      return 'conversation.deepLink.rejectUnsupported'
    case 'empty':
    case 'malformed':
    default:
      return 'conversation.deepLink.rejectMalformed'
  }
}

async function openFileViewer(params: OpenFileViewerParams): Promise<boolean> {
  try {
    const authorized = await window.api.workspace.viewer.authorizeFile({ absolutePath: params.absolutePath })
    const absolutePath = authorized.absolutePath
    const classified = await window.api.workspace.viewer.classify({ absolutePath })
    const relativePath = deriveRelativePathForViewer(absolutePath, params.workspacePath)
    const tabId = useViewerTabStore.getState().openFile({
      threadId: params.threadId,
      absolutePath,
      relativePath,
      contentClass: classified.contentClass,
      sizeBytes: classified.sizeBytes,
      forceNew: params.forceNew,
      navigationHint: params.hint
    })
    const ui = useUIStore.getState()
    ui.setActiveViewerTab(tabId)
    ui.setDetailPanelVisible(true)
    return true
  } catch {
    addToast(params.t('conversation.deepLink.rejectUnreadable'), 'warning')
    return false
  }
}

interface OpenConversationLinkParams {
  target: string
  workspacePath: string
  threadId: string
  forceNew?: boolean
  sourceContextDir?: string
  t: Translator
}

export async function openConversationLink(params: OpenConversationLinkParams): Promise<boolean> {
  const resolution = resolveConversationLink({
    target: params.target,
    workspacePath: params.workspacePath,
    ...(params.sourceContextDir ? { sourceContextDir: params.sourceContextDir } : {})
  })
  const forceNew = params.forceNew === true

  if (resolution.kind === 'reject') {
    addToast(params.t(rejectReasonToMessageKey(resolution.reason)), 'warning')
    return false
  }

  if (resolution.kind === 'external') {
    try {
      await window.api.shell.openExternal(resolution.url)
      return true
    } catch {
      addToast(params.t('conversation.deepLink.rejectUnsupported'), 'warning')
      return false
    }
  }

  if (resolution.kind === 'file') {
    return openFileViewer({
      threadId: params.threadId,
      workspacePath: params.workspacePath,
      absolutePath: resolution.absolutePath,
      forceNew,
      hint: resolution.hint,
      t: params.t
    })
  }

  const viewerStore = useViewerTabStore.getState()
  if (!forceNew) {
    const focused = viewerStore.focusBrowserTabByUrl({
      threadId: params.threadId,
      url: resolution.url
    })
    if (focused) {
      const ui = useUIStore.getState()
      ui.setActiveViewerTab(focused)
      ui.setDetailPanelVisible(true)
      return true
    }
  }

  const createdTabId = viewerStore.openBrowser({
    threadId: params.threadId,
    initialUrl: resolution.url,
    initialLabel: resolution.url,
    forceNew: true
  })
  const ui = useUIStore.getState()
  ui.setActiveViewerTab(createdTabId)
  ui.setDetailPanelVisible(true)
  return true
}

export async function openImagePathInViewer(params: {
  absolutePath: string
  workspacePath: string
  threadId: string
  t: Translator
}): Promise<boolean> {
  return openFileViewer({
    threadId: params.threadId,
    workspacePath: params.workspacePath,
    absolutePath: params.absolutePath,
    forceNew: false,
    t: params.t
  })
}
