import { createOptimisticUserMessage } from './inputPresentation'
import type { ComposerContextRecord } from '../../shared/composerContext'
import type { TypedAppServerRequestApi } from '../../shared/appServerBoundary'
import type { ComposerFileAttachment, ImageAttachment, InputPart } from '../types/conversation'
import type { ConversationTurn, ThreadMode } from '../types/conversation'
import type { ComposerDraftSegment } from '../types/composerDraft'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { isLineCommentContext } from '../../shared/messageComments'
import { buildComposerInputParts } from './composeInputParts'
import { generateThreadTitle, threadTitleRequest, type ThreadTitleRequest } from './threadTitleGeneration'
import { getFallbackThreadName } from './threadFallbackName'
import { threadWorkspaceUpdateFor } from './workspaceRuntimeRoots'
import { modelSettingsForThread, type TurnModelSettings } from './turnModelSettings'

interface StartTurnParams {
  clientUserMessageId?: string
  request?: TypedAppServerRequestApi['sendRequest']
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
  mode?: ThreadMode
  modelSettings?: TurnModelSettings
  title?: ThreadTitleRequest
}

export interface OptimisticTurn {
  optimisticTurnId: string
  clientUserMessageId: string
  inputParts: InputPart[]
  onScreen: boolean
  modelSettings: TurnModelSettings
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
  modelSettings,
  title: namedTitle
}: StartTurnParams): OptimisticTurn | null {
  const built = providedInputParts ? null : buildComposerInputParts({ text, threadId, segments, files, images, contexts })
  const inputParts = providedInputParts ?? built!.inputParts
  const visibleText = built?.visibleText ?? text
  if (inputParts.length === 0) return null
  const capturedSettings = modelSettings
    ? { ...modelSettings, ...(modelSettings.reasoning ? { reasoning: { ...modelSettings.reasoning } } : {}) }
    : modelSettingsForThread(threadId)

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
  return { optimisticTurnId, clientUserMessageId, inputParts, onScreen, modelSettings: capturedSettings, title }
}

export async function submitOptimisticTurn(
  echo: OptimisticTurn,
  {
    threadId,
    workspacePath,
    identityWorkspacePath,
    sentAsGoal = false,
    mode,
    request = window.api.appServer.sendRequest,
    throwOnStartError = false
  }: Pick<StartTurnParams, 'threadId' | 'workspacePath' | 'identityWorkspacePath' | 'sentAsGoal' | 'mode' | 'request' | 'throwOnStartError'>
): Promise<void> {
  const conversation = echo.onScreen ? useConversationStore.getState() : null
  try {
    const identityPath = identityWorkspacePath ?? workspacePath
    const workspaceUpdate = threadWorkspaceUpdateFor(threadId, identityPath)
    if (echo.title?.rename) {
      await request('thread/rename', {
        threadId,
        displayName: echo.title.request.provisionalTitle
      }).catch((error) => console.debug('[thread-title] provisional rename failed', { threadId, error }))
    }
    const result = await request('turn/start', {
      threadId,
      input: echo.inputParts,
      clientUserMessageId: echo.clientUserMessageId,
      ...(sentAsGoal ? { sentAsGoal: true } : {}),
      ...(mode ? { mode } : {}),
      ...echo.modelSettings,
      ...workspaceUpdate
    })
    const res = result as { turn?: { id?: string } }
    if (res.turn?.id && echo.onScreen) {
      conversation?.promoteOptimisticTurn(echo.optimisticTurnId, res.turn.id)
    }
    if (echo.title) void generateThreadTitle(threadId, echo.title.request)
  } catch (err) {
    console.error('turn/start failed:', err)
    conversation?.removeOptimisticTurn(echo.optimisticTurnId)
    if (throwOnStartError) {
      throw err
    }
  }
}

export async function reissueFailedTurn({
  threadId,
  workspacePath,
  identityWorkspacePath,
  modelSettings = modelSettingsForThread(threadId)
}: Pick<StartTurnParams, 'threadId' | 'workspacePath' | 'identityWorkspacePath' | 'modelSettings'>): Promise<void> {
  const identityPath = identityWorkspacePath ?? workspacePath
  const workspaceUpdate = threadWorkspaceUpdateFor(threadId, identityPath)
  await window.api.appServer.sendRequest('turn/start', {
    threadId,
    input: [],
    ...modelSettings,
    ...workspaceUpdate
  })
}

export async function startTurnWithOptimisticUI(params: StartTurnParams): Promise<void> {
  const echo = echoOptimisticTurn(params)
  if (echo) await submitOptimisticTurn(echo, params)
}
