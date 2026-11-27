import { captureComposerAttachmentTarget } from './prepareComposerAttachments'
import type { PendingWelcomeTurnInput } from '../stores/uiStore'
import { showThreadRouteFailureToast, useThreadRouteStore } from '../stores/threadRouteStore'
import { useThreadStore } from '../stores/threadStore'
import { restoreRejectedWelcomeInput } from './welcomeSubmissionRecovery'
import { echoOptimisticTurn, submitOptimisticTurn } from './startTurn'

type TranslateFn = (key: string, vars?: Record<string, string | number>) => string

export async function startPendingWelcomeTurn({
  threadId,
  pending,
  workspacePath,
  translate
}: {
  threadId: string
  pending: Omit<PendingWelcomeTurnInput, 'threadId'>
  workspacePath: string
  translate: TranslateFn
}): Promise<void> {
  const target = captureComposerAttachmentTarget(threadId)
  const onScreen = useThreadStore.getState().activeThreadId === threadId
  const echo = echoOptimisticTurn({
    threadId,
    workspacePath,
    text: pending.text.trim(),
    inputParts: pending.inputParts,
    images: pending.images,
    files: pending.files,
    fallbackThreadName: translate('toast.imageMessage'),
    fileFallbackThreadName: translate('toast.fileReferenceMessage'),
    attachmentFallbackThreadName: translate('toast.attachmentMessage'),
    sentAsGoal: pending.sentAsGoal,
    modelSettings: pending.modelSettings,
    onScreen,
    title: pending.title
  })
  if (!echo) return

  const routeFailure = await useThreadRouteStore.getState().applyPendingRoute(threadId)
  if (routeFailure) {
    showThreadRouteFailureToast(routeFailure.hostName, routeFailure.error, translate)
  }

  try {
    await submitOptimisticTurn(echo, {
      threadId,
      workspacePath,
      sentAsGoal: pending.sentAsGoal,
      target,
      throwOnStartError: true
    })
  } catch {
    void restoreRejectedWelcomeInput(threadId, echo.inputParts)
      .catch((restoreError) => console.error('Unable to restore Welcome input:', restoreError))
  }
}
