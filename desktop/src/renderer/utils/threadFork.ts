import type { ServerCapabilities } from '../stores/connectionStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { addToast } from '../stores/toastStore'
import { useConversationStore } from '../stores/conversationStore'
import { isWorktreeCreationCancelled } from '../stores/worktreeCreationStore'
import { createForkWorktreeThread } from './worktreeThreadCreation'
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
  const localParams = {
    threadId,
    ...(forkPoint ? { forkPoint: { ...forkPoint } } : {})
  }
  try {
    const { workspacePath, remoteWorkspaceActive } = useConversationStore.getState()
    if (mode === 'worktree' && useThreadStore.getState().activeThreadId !== threadId) {
      useThreadStore.getState().setActiveThreadId(threadId)
      useUIStore.getState().setActiveMainView('conversation')
    }
    const thread = mode === 'worktree'
      ? await createForkWorktreeThread({
          sourceThreadId: threadId,
          workspacePath,
          local: !remoteWorkspaceActive,
          params: {
            sourceThreadId: threadId,
            ...(forkPoint ? { forkPoint: { ...forkPoint } } : {}),
            copyDirtyChanges: true
          },
          localParams
        })
      : (await window.api.appServer.sendRequest('thread/fork', localParams) as unknown as ThreadForkResult).thread
    if (!thread?.id) {
      throw new Error(t('fork.toast.missingThread'))
    }

    useThreadStore.getState().upsertThreads([thread])
    useThreadStore.getState().setActiveThreadId(thread.id)
    useUIStore.getState().setActiveMainView('conversation')
    addToast(
      thread.worktree
        ? t('fork.toast.worktreeCreated')
        : t('fork.toast.localCreated'),
      'success'
    )
    return thread
  } catch (err) {
    if (isWorktreeCreationCancelled(err)) return null
    addToast(
      t('fork.toast.failed', { error: err instanceof Error ? err.message : String(err) }),
      'error'
    )
    return null
  }
}
