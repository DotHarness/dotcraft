import { createOptimisticUserMessage } from './inputPresentation'
import type { ComposerContextRecord } from '../../shared/composerContext'
import type { ComposerFileAttachment, ImageAttachment, InputPart } from '../types/conversation'
import type { ConversationTurn } from '../types/conversation'
import type { ComposerDraftSegment } from '../types/composerDraft'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { isLineCommentContext } from '../../shared/messageComments'
import { buildComposerInputParts } from './composeInputParts'
import { generateThreadTitle, threadTitleRequest, type ThreadTitleRequest } from './threadTitleGeneration'
import { getFallbackThreadName } from './threadFallbackName'
import { threadWorkspaceUpdateFor } from './workspaceRuntimeRoots'

interface StartTurnParams {
  clientUserMessageId?: string
  threadId: string
  workspacePath: string
  identityWorkspacePath?: string
  text: string
  inputParts?: InputPart[]
  contexts?: ComposerContextRecord[]
  segments?: ComposerDraftSegment[]
  images?: ImageAttachment[]
  files?: ComposerFileAttachment[]
  fallbackThreadName: string
  fileFallbackThreadName?: string
  attachmentFallbackThreadName?: string
  renameThreadFromText?: boolean
  throwOnStartError?: boolean
  /** False when the turn belongs to a thread the user is no longer looking at. */
  onScreen?: boolean
  onOptimisticEcho?: () => void
  sentAsGoal?: boolean
  title?: ThreadTitleRequest
}

export interface OptimisticTurn {
  optimisticTurnId: string
  clientUserMessageId: string
  inputParts: InputPart[]
  onScreen: boolean
  title?: { request: ThreadTitleRequest; rename: boolean }
}

/** Echoes the submission; callers gating the RPC pass the result to `submitOptimisticTurn` later. */
export function echoOptimisticTurn({
  clientUserMessageId = crypto.randomUUID(),
  threadId,
  text,
  inputParts: providedInputParts,
  segments,
  contexts,
  images = [],
  files = [],
  fallbackThreadName,
  fileFallbackThreadName,
  attachmentFallbackThreadName,
  renameThreadFromText = true,
  sentAsGoal = false,
  onScreen = true,
  onOptimisticEcho,
  title: namedTitle
}: StartTurnParams): OptimisticTurn | null {
  const built = providedInputParts ? null : buildComposerInputParts({ text, threadId, segments, files, images, contexts })
  const inputParts = providedInputParts ?? built!.inputParts
  const visibleText = built?.visibleText ?? text
  if (inputParts.length === 0) return null

  let title: OptimisticTurn['title']
  if (namedTitle) {
    title = { request: namedTitle, rename: false }
  } else if (renameThreadFromText) {
    const threadEntry = useThreadStore.getState().threadList.find((t) => t.id === threadId)
    if (!threadEntry?.displayName) {
      const fallbackName = getFallbackThreadName({
        visibleText,
        imagesCount: images.length,
        filesCount: files.length,
        fallbackThreadName,
        fileFallbackThreadName,
        attachmentFallbackThreadName
      })
      const request = threadTitleRequest({
        bodyText: built?.bodyText ?? text,
        comments: contexts?.filter(isLineCommentContext),
        visibleText,
        referencesTitle: fallbackName
      })
      if (request) title = { request, rename: true }
      useThreadStore.getState().renameThread(threadId, request?.provisionalTitle ?? fallbackName)
    }
  }

  const optimisticTurnId = `local-turn-${clientUserMessageId}`
  const optimisticTurn: ConversationTurn = {
    id: optimisticTurnId,
    threadId,
    status: 'running',
    items: [createOptimisticUserMessage(inputParts, visibleText, clientUserMessageId, sentAsGoal)],
    startedAt: new Date().toISOString()
  }
  if (onScreen) {
    useConversationStore.getState().addOptimisticTurn(optimisticTurn)
    onOptimisticEcho?.()
  }
  return { optimisticTurnId, clientUserMessageId, inputParts, onScreen, title }
}

export async function submitOptimisticTurn(
  echo: OptimisticTurn,
  {
    threadId,
    workspacePath,
    identityWorkspacePath,
    sentAsGoal = false,
    throwOnStartError = false
  }: Pick<StartTurnParams, 'threadId' | 'workspacePath' | 'identityWorkspacePath' | 'sentAsGoal' | 'throwOnStartError'>
): Promise<void> {
  try {
    const identityPath = identityWorkspacePath ?? workspacePath
    const workspaceUpdate = threadWorkspaceUpdateFor(threadId, identityPath)
    if (echo.title?.rename) {
      await window.api.appServer.sendRequest('thread/rename', {
        threadId,
        displayName: echo.title.request.provisionalTitle
      }).catch((error) => console.debug('[thread-title] provisional rename failed', { threadId, error }))
    }
    const result = await window.api.appServer.sendRequest('turn/start', {
      threadId,
      input: echo.inputParts,
      clientUserMessageId: echo.clientUserMessageId,
      ...(sentAsGoal ? { sentAsGoal: true } : {}),
      ...workspaceUpdate
    })
    const res = result as { turn?: { id?: string } }
    if (res.turn?.id && echo.onScreen) {
      useConversationStore.getState().promoteOptimisticTurn(echo.optimisticTurnId, res.turn.id)
    }
    if (echo.title) void generateThreadTitle(threadId, echo.title.request)
  } catch (err) {
    console.error('turn/start failed:', err)
    if (echo.onScreen) useConversationStore.getState().removeOptimisticTurn(echo.optimisticTurnId)
    if (throwOnStartError) {
      throw err
    }
  }
}

export async function reissueFailedTurn({
  threadId,
  workspacePath,
  identityWorkspacePath
}: Pick<StartTurnParams, 'threadId' | 'workspacePath' | 'identityWorkspacePath'>): Promise<void> {
  const identityPath = identityWorkspacePath ?? workspacePath
  const workspaceUpdate = threadWorkspaceUpdateFor(threadId, identityPath)
  await window.api.appServer.sendRequest('turn/start', {
    threadId,
    input: [],
    ...workspaceUpdate
  })
}

export async function startTurnWithOptimisticUI(params: StartTurnParams): Promise<void> {
  const echo = echoOptimisticTurn(params)
  if (echo) await submitOptimisticTurn(echo, params)
}
