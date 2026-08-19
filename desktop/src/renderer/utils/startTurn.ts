import type { ComposerFileAttachment, ImageAttachment } from '../types/conversation'
import type { ConversationItem, ConversationTurn } from '../types/conversation'
import type { ComposerDraftSegment } from '../types/composerDraft'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { buildComposerInputParts } from './composeInputParts'
import { getFallbackThreadName } from './threadFallbackName'
import { runtimeWorkspaceRootsFor } from './workspaceRuntimeRoots'

interface StartTurnParams {
  threadId: string
  workspacePath: string
  identityWorkspacePath?: string
  text: string
  segments?: ComposerDraftSegment[]
  images?: ImageAttachment[]
  files?: ComposerFileAttachment[]
  fallbackThreadName: string
  fileFallbackThreadName?: string
  attachmentFallbackThreadName?: string
  renameThreadFromText?: boolean
  throwOnStartError?: boolean
  /** Marks this submission as the one that established the thread goal (durable "sent as goal"). */
  sentAsGoal?: boolean
}

/**
 * Start a turn with optimistic UI and promote local turn ID when server responds.
 * Returns true when the turn/start RPC is issued, false when there is no input.
 */
export async function startTurnWithOptimisticUI({
  threadId,
  workspacePath,
  identityWorkspacePath,
  text,
  segments,
  images = [],
  files = [],
  fallbackThreadName,
  fileFallbackThreadName,
  attachmentFallbackThreadName,
  renameThreadFromText = true,
  throwOnStartError = false,
  sentAsGoal = false
}: StartTurnParams): Promise<boolean> {
  const { inputParts, visibleText } = buildComposerInputParts({ text, segments, files, images })
  if (inputParts.length === 0) {
    return false
  }

  if (renameThreadFromText) {
    const threadEntry = useThreadStore.getState().threadList.find((t) => t.id === threadId)
    if (!threadEntry?.displayName) {
      const autoName = getFallbackThreadName({
        visibleText,
        imagesCount: images.length,
        filesCount: files.length,
        fallbackThreadName,
        fileFallbackThreadName,
        attachmentFallbackThreadName
      })
      useThreadStore.getState().renameThread(threadId, autoName)
    }
  }

  const optimisticTurnId = `local-turn-${Date.now()}`
  const optimisticNow = new Date().toISOString()
  const optimisticItems: ConversationItem[] = [{
    id: `local-${Date.now()}`,
    type: 'userMessage',
    status: 'completed',
    text: visibleText,
    nativeInputParts: inputParts.filter((part) => part.type !== 'localImage' && part.type !== 'image'),
    imageDataUrls: images.map((i) => i.dataUrl),
    images: images.map((i) => ({
      path: i.tempPath,
      mimeType: i.mimeType,
      fileName: i.fileName
    })),
    sentAsGoal: sentAsGoal ? true : undefined,
    createdAt: optimisticNow,
    completedAt: optimisticNow
  }]

  const optimisticTurn: ConversationTurn = {
    id: optimisticTurnId,
    threadId,
    status: 'running',
    items: optimisticItems,
    startedAt: optimisticNow
  }
  useConversationStore.getState().addOptimisticTurn(optimisticTurn)

  try {
    const identityPath = identityWorkspacePath ?? workspacePath
    // Keep the multi-folder project's runtime roots in sync (sticky). Sending only
    // runtimeWorkspaceRoots is a complete replacement with no cwd retargeting, so
    // the thread's existing working directory is preserved.
    const runtimeWorkspaceRoots = runtimeWorkspaceRootsFor(identityPath)
    const result = await window.api.appServer.sendRequest('turn/start', {
      threadId,
      input: inputParts,
      ...(sentAsGoal ? { sentAsGoal: true } : {}),
      ...(runtimeWorkspaceRoots ? { runtimeWorkspaceRoots } : {}),
      identity: {
        channelName: 'dotcraft-desktop',
        userId: 'local',
        channelContext: `workspace:${identityPath}`,
        workspacePath: identityPath
      }
    })
    const res = result as { turn?: { id?: string } }
    if (res.turn?.id) {
      useConversationStore.getState().promoteOptimisticTurn(optimisticTurnId, res.turn.id)
    }
  } catch (err) {
    console.error('turn/start failed:', err)
    useConversationStore.getState().removeOptimisticTurn(optimisticTurnId)
    if (throwOnStartError) {
      throw err
    }
  }

  return true
}
