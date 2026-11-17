import { threadTaskConnection } from '../desktopRuntimeThreadTools'
import type { DesktopWindowRuntime } from './windowRuntime'
import type { WorkspaceConnectionEntry } from './windowTypes'

export function recoverThreadQuestions(ctx: DesktopWindowRuntime, entry: WorkspaceConnectionEntry): void {
  for (const thread of entry.threads) {
    const threadId = (thread as { id?: unknown } | null)?.id
    if (typeof threadId !== 'string') continue
    const tasks = ctx.getThreadTasks()
    if (!tasks.snapshot(threadId).question) continue
    const connection = threadTaskConnection(entry.client, entry.workspacePath, ctx.desktopThreadToolOptions(entry))
    void tasks.reconcileQuestion(connection, threadId)
  }
}
