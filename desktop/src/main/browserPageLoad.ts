import type { BrowserEventPayload } from '../shared/viewer/types'

function isNavigationAbortError(error: unknown): boolean {
  if (typeof error === 'string') return error.includes('ERR_ABORTED') || error.includes('(-3)')
  if (!error || typeof error !== 'object') return false
  const details = error as { code?: unknown; errno?: unknown; message?: unknown }
  if (details.code === -3 || details.errno === -3 || details.code === 'ERR_ABORTED') return true
  if (typeof details.message !== 'string') return false
  return details.message.includes('ERR_ABORTED') || details.message.includes('(-3)')
}

export async function loadOrReport(params: {
  tabId: string
  threadId?: string
  url: string
  load: () => Promise<unknown>
  emit: (payload: BrowserEventPayload) => void
  throwOnFailure?: boolean
}): Promise<void> {
  try {
    await params.load()
  } catch (error: unknown) {
    if (isNavigationAbortError(error)) return
    const message = error instanceof Error ? error.message : String(error)
    const details = error && typeof error === 'object' ? error as { code?: unknown; errno?: unknown } : {}
    const numericCode = typeof details.code === 'number'
      ? details.code
      : typeof details.errno === 'number'
        ? details.errno
        : undefined
    params.emit({
      tabId: params.tabId,
      threadId: params.threadId,
      type: 'did-fail-load',
      url: params.url,
      message,
      errorCode: numericCode,
      errorDescription: message,
      validatedURL: params.url,
      finalURL: params.url,
      isMainFrame: true
    })
    params.emit({
      tabId: params.tabId,
      threadId: params.threadId,
      type: 'did-stop-loading',
      url: params.url
    })
    if (params.throwOnFailure) {
      throw new Error(`NavigationFailed: ${message}`)
    }
  }
}
