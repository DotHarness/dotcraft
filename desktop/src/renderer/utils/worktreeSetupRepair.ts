import { repairWorktreeSetupPrompt } from '../components/projectActions/environmentAgentPrompts'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import type { WorktreeCreation } from '../stores/worktreeCreationStore'
import type { ThreadSummary } from '../types/thread'

const WORKTREE_CREATION_TIMEOUT_MS = 180_000

export async function startWorktreeSetupRepair(creation: WorktreeCreation, title: string): Promise<ThreadSummary> {
  if (!creation.environment) throw new Error('This worktree was created without an environment')
  const text = repairWorktreeSetupPrompt({
    environmentPath: creation.environment.path,
    exitCode: creation.setup?.exitCode,
    error: creation.error,
    output: creation.setup?.outputTail || creation.output
  })
  const { workspacePath } = creation
  const result = await window.api.appServer.sendRequestRaw('worktree/createAndStart', {
    identity: {
      channelName: 'dotcraft-desktop',
      userId: 'local',
      channelContext: `workspace:${workspacePath}`,
      workspacePath
    },
    historyMode: 'server',
    displayName: title,
    config: { mode: 'agent' }
  }, WORKTREE_CREATION_TIMEOUT_MS) as { thread?: ThreadSummary }
  const thread = result.thread
  if (!thread?.id) throw new Error('worktree/createAndStart did not return a thread')

  useThreadStore.getState().addThread({ ...thread, displayName: thread.displayName || title })
  useUIStore.getState().setPendingWelcomeTurn({
    threadId: thread.id,
    text,
    inputParts: [{ type: 'text', text }]
  })
  useThreadStore.getState().setActiveThreadId(thread.id)
  useUIStore.getState().setActiveMainView('conversation')
  return thread
}
