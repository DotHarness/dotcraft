import type { ComposerFileAttachment, ImageAttachment } from '../types/conversation'
import type { ConversationItem, ConversationTurn } from '../types/conversation'
import type { ComposerDraftSegment } from '../types/composerDraft'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { buildComposerInputParts } from './composeInputParts'
import { getFallbackThreadName } from './threadFallbackName'

interface StartTurnParams {
  threadId: string
  workspacePath: string
  text: string
  segments?: ComposerDraftSegment[]
  images?: ImageAttachment[]
  files?: ComposerFileAttachment[]
  fallbackThreadName: string
  fileFallbackThreadName?: string
  attachmentFallbackThreadName?: string
  includeUserPreview?: boolean
  renameThreadFromText?: boolean
  throwOnStartError?: boolean
}

/**
 * Start a turn with optimistic UI and promote local turn ID when server responds.
 * Returns true when the turn/start RPC is issued, false when there is no input.
 */
export async function startTurnWithOptimisticUI({
  threadId,
  workspacePath,
  text,
  segments,
  images = [],
  files = [],
  fallbackThreadName,
  fileFallbackThreadName,
  attachmentFallbackThreadName,
  includeUserPreview = true,
  renameThreadFromText = true,
  throwOnStartError = false
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
  const optimisticItems: ConversationItem[] = includeUserPreview
    ? [{
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
      createdAt: optimisticNow,
      completedAt: optimisticNow
    }]
    : []

  const optimisticTurn: ConversationTurn = {
    id: optimisticTurnId,
    threadId,
    status: 'running',
    items: optimisticItems,
    startedAt: optimisticNow
  }
  useConversationStore.getState().addOptimisticTurn(optimisticTurn)

  try {
    const result = await window.api.appServer.sendRequest('turn/start', {
      threadId,
      input: inputParts,
      identity: {
        channelName: 'dotcraft-desktop',
        userId: 'local',
        channelContext: `workspace:${workspacePath}`,
        workspacePath
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
