import { devServerManager } from './devServerManager'

export function handleDevServerThreadNotification(method: string, params: unknown): void {
  if (method !== 'thread/archived' && method !== 'thread/deleted') return
  const threadId = (params as { threadId?: unknown } | null)?.threadId
  if (typeof threadId === 'string') void devServerManager.forgetThread(threadId)
}
