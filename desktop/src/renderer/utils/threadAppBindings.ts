import {
  AppBindingActivationError,
  useAppBindingStore,
  type AppHandoff
} from '../stores/appBindingStore'
import { addToast } from '../stores/toastStore'

type TranslateFn = (key: string, vars?: Record<string, string | number>) => string

export async function openAppHandoff(handoff: AppHandoff): Promise<void> {
  if (!handoff.uri) return
  await (window.api.shell.openAppHandoff ?? window.api.shell.openExternal)(handoff.uri)
}

export async function activateThreadAppBindings({
  threadId,
  appIds,
  translate,
  signal
}: {
  threadId: string
  appIds: string[]
  translate: TranslateFn
  signal?: AbortSignal
}): Promise<void> {
  if (appIds.length === 0) return

  const known = useAppBindingStore.getState().apps
  if (appIds.some((appId) => !known.some((app) => app.appId === appId))) {
    await useAppBindingStore.getState().fetchApps(threadId, false, 'welcome')
  }

  const outcomes = await Promise.allSettled(
    appIds.map((appId) => activateOne({ threadId, appId, translate, signal }))
  )
  const failed = outcomes.find((outcome) => outcome.status === 'rejected')
  if (failed?.status === 'rejected') throw failed.reason
}

async function activateOne({
  threadId,
  appId,
  translate,
  signal
}: {
  threadId: string
  appId: string
  translate: TranslateFn
  signal?: AbortSignal
}): Promise<void> {
  const app = useAppBindingStore.getState().apps.find((candidate) => candidate.appId === appId)
  if (!app) {
    throw new Error(translate('appBinding.welcomeAppNotConnected', { name: appId }))
  }
  if (app.requiresExternalConnection !== false && app.connectionState !== 'connected') {
    throw new Error(translate('appBinding.welcomeAppNotConnected', { name: app.displayName || appId }))
  }

  let request: Awaited<ReturnType<ReturnType<typeof useAppBindingStore.getState>['createBindingRequest']>> | null = null
  try {
    request = await useAppBindingStore.getState().createBindingRequest({ threadId, appId, source: 'welcome' })
    if (request.handoff?.uri) await openAppHandoff(request.handoff)
    if (request.state !== 'active') addToast(translate('appBinding.bindingStarted'), 'info')
    await useAppBindingStore.getState().waitForThreadBinding(
      { threadId, appId, bindingRequestId: request.bindingRequestId },
      { signal }
    )
  } catch (err) {
    if (request) {
      try {
        await useAppBindingStore.getState().cancelBindingRequest(
          threadId,
          request.bindingRequestId,
          'activation_failed',
          request.bindingId
        )
      } catch {
        // The activation failure is what the caller reports; a stale request expires on its own.
      }
    }
    if (err instanceof AppBindingActivationError) {
      throw new Error(translate('appBinding.bindingFailed', {
        name: app.displayName || app.appId,
        state: err.state,
        reason: err.failureReason || '—'
      }))
    }
    throw err
  }
}
