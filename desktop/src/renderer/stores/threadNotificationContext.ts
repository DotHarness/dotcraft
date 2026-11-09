import type { RawNotificationPayload } from '../../shared/appServerBoundary'
import type { AppLocale } from '../../shared/locales'
import type { Thread } from '../types/thread'
export interface ThreadNotificationContext {
  locale: AppLocale
  foreground: boolean
  updateWarmThread(thread: Thread): void
  releaseWarmThread(threadId: string): void
  reloadThreadList(): Promise<void>
}
export function notificationThreadId(payload: RawNotificationPayload): string | undefined {
  const params = (payload.params ?? {}) as Record<string, unknown>
  const nested = (params.thread ?? params.turn ?? params.terminal) as { threadId?: string; id?: string } | undefined
  return typeof params.threadId === 'string'
    ? params.threadId
    : typeof params.parentThreadId === 'string'
      ? params.parentThreadId
      : typeof params.rootThreadId === 'string'
        ? params.rootThreadId
        : payload.method.startsWith('thread/')
          ? nested?.id
          : nested?.threadId
}
