import type { PendingWelcomeTurnInput } from '../stores/uiStore'
import { AppBindingWaitAbortedError } from '../stores/appBindingStore'
import { useConversationStore } from '../stores/conversationStore'
import { showThreadRouteFailureToast, useThreadRouteStore } from '../stores/threadRouteStore'
import { useThreadStore } from '../stores/threadStore'
import { addToast } from '../stores/toastStore'
import { activateThreadAppBindings } from './threadAppBindings'
import { acceptWelcomeInput, restoreRejectedWelcomeInput } from './welcomeSubmissionRecovery'
import { echoOptimisticTurn, submitOptimisticTurn, type OptimisticTurn } from './startTurn'

type TranslateFn = (key: string, vars?: Record<string, string | number>) => string

export async function startPendingWelcomeTurn({
  threadId,
  pending,
  workspacePath,
  translate,
  signal
}: {
  threadId: string
  pending: Omit<PendingWelcomeTurnInput, 'threadId'>
  workspacePath: string
  translate: TranslateFn
  signal?: AbortSignal
}): Promise<void> {
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
    onScreen
  })
  if (!echo) return

  const appIds = pending.appIds ?? []
  if (appIds.length > 0 && !(await activateStagedApps({ threadId, echo, appIds, translate, signal }))) return

  // Must land before `turn/start`: the thread now exists but has no turn yet.
  const routeFailure = await useThreadRouteStore.getState().applyPendingRoute(threadId)
  if (routeFailure) {
    showThreadRouteFailureToast(routeFailure.hostName, routeFailure.error, translate)
  }

  try {
    await submitOptimisticTurn(echo, {
      threadId,
      workspacePath,
      sentAsGoal: pending.sentAsGoal,
      throwOnStartError: true
    })
    acceptWelcomeInput(threadId, echo.inputParts)
  } catch {
    void restoreRejectedWelcomeInput(threadId, echo.inputParts)
      .catch((restoreError) => console.error('Unable to restore Welcome input:', restoreError))
  }
}

async function activateStagedApps({
  threadId,
  echo,
  appIds,
  translate,
  signal
}: {
  threadId: string
  echo: OptimisticTurn
  appIds: string[]
  translate: TranslateFn
  signal?: AbortSignal
}): Promise<boolean> {
  // Activation can outlive the user's stay, so conversation state moves only while this thread is on screen.
  const onScreen = (): boolean => useThreadStore.getState().activeThreadId === threadId
  const abandon = async (): Promise<void> => {
    if (onScreen()) {
      useConversationStore.getState().setSystemLabel(null)
      useConversationStore.getState().removeOptimisticTurn(echo.optimisticTurnId)
    }
    await restoreRejectedWelcomeInput(threadId, echo.inputParts)
      .catch((restoreError) => console.error('Unable to restore Welcome input:', restoreError))
  }

  if (onScreen()) useConversationStore.getState().setSystemLabel('systemStatus.connectingApps')
  try {
    await activateThreadAppBindings({ threadId, appIds, translate, signal })
  } catch (err) {
    await abandon()
    if (!(err instanceof AppBindingWaitAbortedError)) {
      console.error('Welcome app binding activation failed:', err)
      addToast(err instanceof Error ? err.message : String(err), 'error')
    }
    return false
  }
  if (signal?.aborted) {
    await abandon()
    return false
  }
  if (onScreen()) useConversationStore.getState().setSystemLabel(null)
  return true
}
