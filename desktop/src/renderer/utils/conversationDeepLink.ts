import { resolveConversationLink, type LinkRejectReason } from '../../shared/viewer/linkResolver'
import type { FileNavigationHint } from '../../shared/viewer/types'
import { useThreadStore } from '../stores/threadStore'
import { addToast } from '../stores/toastStore'
import { useDetailPanelStore, type FileReveal } from '../stores/detailPanelStore'
import { containingRoot, relativeToRoot, viewerRootsFor } from './viewerRoots'
import { openWebLink, type WebLinkDestination } from './webLinks'
import { prepareFileViewer } from './fileViewer'

type Translator = (key: string) => string

interface OpenFileViewerParams {
  threadId: string
  workspacePath: string
  absolutePath: string
  forceNew: boolean
  hint?: FileNavigationHint
  reveal?: FileReveal
  t: Translator
}

function normalizePath(value: string): string {
  return value.replace(/\\/g, '/')
}

export function deriveRelativePathForViewer(
  absolutePath: string,
  workspacePath: string,
  identityWorkspacePath: string = workspacePath,
  worktreeSourcePath?: string
): string {
  const root = containingRoot(absolutePath, viewerRootsFor(workspacePath, identityWorkspacePath, worktreeSourcePath))
  return root ? relativeToRoot(absolutePath, root) : normalizePath(absolutePath)
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

export async function openWorkspaceFileViewer(params: Omit<OpenFileViewerParams, 't'>): Promise<void> {
  const panel = useDetailPanelStore.getState().getCurrentPanel()
  const prepared = await prepareFileViewer(params.absolutePath, panel.browserWorkspacePath || params.workspacePath)
  const current = useDetailPanelStore.getState().getCurrentPanel()
  if (current.scope.projectKey !== panel.scope.projectKey || current.scope.threadId !== panel.scope.threadId) return
  const { absolutePath } = prepared
  const { activeThread, threadList } = useThreadStore.getState()
  const thread = activeThread?.id === params.threadId
    ? activeThread
    : threadList.find((candidate) => candidate.id === params.threadId)
  const worktreeSourcePath = thread?.worktree
    ? (activeThread?.id === params.threadId ? activeThread.configuration?.cwd : undefined) ?? thread.worktree.sourceWorkspacePath
    : undefined
  const relativePath = deriveRelativePathForViewer(
    absolutePath, params.workspacePath, thread?.workspacePath, worktreeSourcePath
  )
  const tabId = useDetailPanelStore.getState().openFile({
    threadId: params.threadId,
    absolutePath,
    relativePath,
    contentClass: prepared.contentClass,
    sizeBytes: prepared.sizeBytes,
    forceNew: params.forceNew,
    navigationHint: params.hint,
    reveal: params.reveal
  })
  useDetailPanelStore.getState().setActiveViewerTab(tabId)
  useDetailPanelStore.getState().setDetailPanelVisible(true)
}

export async function openFileViewer(params: OpenFileViewerParams): Promise<boolean> {
  try {
    await openWorkspaceFileViewer(params)
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
  destination?: WebLinkDestination
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

  return openWebLink({
    url: resolution.url,
    destination: params.destination ?? (forceNew ? 'newBrowserTab' : 'preferred'),
    threadId: params.threadId,
    t: params.t
  })
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
