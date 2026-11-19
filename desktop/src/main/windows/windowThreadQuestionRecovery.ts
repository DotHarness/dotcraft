import { threadTaskConnection } from '../desktopRuntimeThreadTools'
import type { DesktopWindowRuntime } from './windowRuntime'
import type { WorkspaceConnectionEntry } from './windowTypes'

export function recoverThreadQuestions(ctx: DesktopWindowRuntime, entry: WorkspaceConnectionEntry): void {
  const tasks = ctx.getThreadTasks()
  const connection = threadTaskConnection(entry.client, entry.workspacePath, ctx.desktopThreadToolOptions(entry))
  for (const thread of entry.threads) {
    const threadId = (thread as { id?: unknown } | null)?.id
    if (typeof threadId === 'string') void tasks.reconcileQuestion(connection, threadId)
  }
}
