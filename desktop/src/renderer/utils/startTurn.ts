import { createOptimisticUserMessage } from './inputPresentation'
import type { ComposerContextRecord } from '../../shared/composerContext'
import type { ComposerFileAttachment, ImageAttachment, InputPart } from '../types/conversation'
import type { ConversationTurn } from '../types/conversation'
import type { ComposerDraftSegment } from '../types/composerDraft'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { buildComposerInputParts } from './composeInputParts'
import { getFallbackThreadName } from './threadFallbackName'
import { runtimeWorkspaceRootsFor } from './workspaceRuntimeRoots'

interface StartTurnParams {
  clientUserMessageId?: string
  threadId: string
  workspacePath: string
  identityWorkspacePath?: string
  text: string
  /** Replaces the parts otherwise built from text, segments, contexts and attachments. */
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
  /** Marks this submission as the one that established the thread goal (durable "sent as goal"). */
  sentAsGoal?: boolean
}

export interface OptimisticTurn {
  optimisticTurnId: string
  clientUserMessageId: string
  inputParts: InputPart[]
  onScreen: boolean
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
  onScreen = true
}: StartTurnParams): OptimisticTurn | null {
  const built = providedInputParts ? null : buildComposerInputParts({ text, segments, files, images, contexts })
  const inputParts = providedInputParts ?? built!.inputParts
  const visibleText = built?.visibleText ?? text
  if (inputParts.length === 0) return null

  if (renameThreadFromText) {
    const threadEntry = useThreadStore.getState().threadList.find((t) => t.id === threadId)
    if (!threadEntry?.displayName) {
      useThreadStore.getState().renameThread(threadId, getFallbackThreadName({
        visibleText,
        imagesCount: images.length,
        filesCount: files.length,
        fallbackThreadName,
        fileFallbackThreadName,
        attachmentFallbackThreadName
      }))
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
  if (onScreen) useConversationStore.getState().addOptimisticTurn(optimisticTurn)
  return { optimisticTurnId, clientUserMessageId, inputParts, onScreen }
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
    // Keep the multi-folder project's runtime roots in sync (sticky). Sending only
    // runtimeWorkspaceRoots is a complete replacement with no cwd retargeting, so
    // the thread's existing working directory is preserved.
    const runtimeWorkspaceRoots = runtimeWorkspaceRootsFor(identityPath)
    const result = await window.api.appServer.sendRequest('turn/start', {
      threadId,
      input: echo.inputParts,
      clientUserMessageId: echo.clientUserMessageId,
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
    if (res.turn?.id && echo.onScreen) {
      useConversationStore.getState().promoteOptimisticTurn(echo.optimisticTurnId, res.turn.id)
    }
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
  const runtimeWorkspaceRoots = runtimeWorkspaceRootsFor(identityPath)
  await window.api.appServer.sendRequest('turn/start', {
    threadId,
    input: [],
    ...(runtimeWorkspaceRoots ? { runtimeWorkspaceRoots } : {}),
    identity: {
      channelName: 'dotcraft-desktop',
      userId: 'local',
      channelContext: `workspace:${identityPath}`,
      workspacePath: identityPath
    }
  })
}

export async function startTurnWithOptimisticUI(params: StartTurnParams): Promise<void> {
  const echo = echoOptimisticTurn(params)
  if (echo) await submitOptimisticTurn(echo, params)
}
