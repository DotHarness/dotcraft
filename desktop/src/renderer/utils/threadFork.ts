import type { ServerCapabilities } from '../stores/connectionStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { addToast } from '../stores/toastStore'
import type { ThreadSummary } from '../types/thread'

export type ThreadForkMode = 'local' | 'worktree'

export interface ThreadForkPointWire {
  turnId: string
  itemId?: string
  position: 'before' | 'after'
}

export interface RunThreadForkOptions {
  threadId: string
  mode: ThreadForkMode
  forkPoint?: ThreadForkPointWire
  t: (key: string, vars?: Record<string, string | number>) => string
}

interface ThreadForkResult {
  thread?: ThreadSummary
}

export function canForkThread(capabilities: ServerCapabilities | null): boolean {
  return capabilities?.threadFork === true
}

export function canForkWorktree(capabilities: ServerCapabilities | null): boolean {
  return canForkThread(capabilities) && capabilities?.gitWorktrees === true
}

export async function runThreadFork({
  threadId,
  mode,
  forkPoint,
  t
}: RunThreadForkOptions): Promise<ThreadSummary | null> {
  try {
    const result = await window.api.appServer.sendRequest(
      mode === 'worktree' ? 'worktree/createAndFork' : 'thread/fork',
      mode === 'worktree'
        ? {
            sourceThreadId: threadId,
            ...(forkPoint ? { forkPoint: { ...forkPoint } } : {}),
            copyDirtyChanges: true
          }
        : {
            threadId,
            ...(forkPoint ? { forkPoint: { ...forkPoint } } : {})
          },
      mode === 'worktree' ? 180_000 : undefined
    ) as unknown as ThreadForkResult

    const thread = result.thread
    if (!thread?.id) {
      throw new Error(t('fork.toast.missingThread'))
    }

    useThreadStore.getState().upsertThreads([thread])
    useThreadStore.getState().setActiveThreadId(thread.id)
    useUIStore.getState().setActiveMainView('conversation')
    addToast(
      mode === 'worktree'
        ? t('fork.toast.worktreeCreated')
        : t('fork.toast.localCreated'),
      'success'
    )
    return thread
  } catch (err) {
    addToast(
      t('fork.toast.failed', { error: err instanceof Error ? err.message : String(err) }),
      'error'
    )
    return null
  }
}
