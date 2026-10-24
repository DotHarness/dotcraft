import { useThreadStore } from '../../../stores/threadStore'
import type { ThreadWorktreeInfoWire } from '../../../types/thread'

export interface ThreadWorktreeEnvironment {
  worktreePath: string
  environmentPath: string | null
}

function threadWorktree(threadId: string): ThreadWorktreeInfoWire | null {
  const { activeThread, threadList } = useThreadStore.getState()
  const thread = activeThread?.id === threadId ? activeThread : threadList.find((candidate) => candidate.id === threadId)
  return thread?.worktree ?? null
}

export function threadWorktreeEnvironment(threadId: string): ThreadWorktreeEnvironment | null {
  const worktree = threadWorktree(threadId)
  return worktree?.path ? { worktreePath: worktree.path, environmentPath: worktree.environmentPath ?? null } : null
}

export function onThreadWorktreeEnvironmentChange(
  threadId: string,
  listener: (environment: ThreadWorktreeEnvironment | null) => void
): () => void {
  let current = JSON.stringify(threadWorktreeEnvironment(threadId))
  return useThreadStore.subscribe(() => {
    const next = threadWorktreeEnvironment(threadId)
    const serialized = JSON.stringify(next)
    if (serialized === current) return
    current = serialized
    listener(next)
  })
}

export async function setThreadWorktreeEnvironment(request: {
  threadId: string
  environmentPath: string | null
}): Promise<void> {
  const worktree = threadWorktree(request.threadId)
  if (!worktree?.path) throw new Error('This conversation does not work in a worktree')
  const result = await window.api.appServer.sendRequest('worktree/setEnvironment', {
    path: worktree.path,
    environmentPath: request.environmentPath
  }) as { worktree?: ThreadWorktreeInfoWire }
  const environmentPath = result.worktree?.environmentPath ?? request.environmentPath
  useThreadStore.setState((state) => ({
    activeThread: state.activeThread?.id === request.threadId && state.activeThread.worktree
      ? { ...state.activeThread, worktree: { ...state.activeThread.worktree, environmentPath } }
      : state.activeThread,
    threadList: state.threadList.map((thread) => thread.id === request.threadId && thread.worktree
      ? { ...thread, worktree: { ...thread.worktree, environmentPath } }
      : thread)
  }))
}

export function sameEnvironmentPath(left: string | null | undefined, right: string | null | undefined): boolean {
  if (!left || !right) return false
  const normalize = (path: string): string => {
    const slashed = path.replace(/\\/g, '/').replace(/\/+$/, '')
    return /^[a-zA-Z]:\//.test(slashed) ? slashed.toLowerCase() : slashed
  }
  return normalize(left) === normalize(right)
}
