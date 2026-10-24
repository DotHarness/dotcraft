import { beforeEach, describe, expect, it, vi } from 'vitest'
import { runThreadFork } from '../utils/threadFork'
import { createForkWorktreeThread } from '../utils/worktreeThreadCreation'
import { useThreadStore } from '../stores/threadStore'
import { useConversationStore } from '../stores/conversationStore'
import type { ThreadSummary } from '../types/thread'

vi.mock('../utils/worktreeThreadCreation', () => ({
  createForkWorktreeThread: vi.fn()
}))

describe('runThreadFork', () => {
  beforeEach(() => {
    vi.mocked(createForkWorktreeThread).mockReset()
  })

  it('looks up the worktree environment in the source thread project, not the execution workspace', async () => {
    const source = {
      id: 'source',
      workspacePath: 'C:/projects/app',
      worktree: { path: 'C:/projects/app/.craft/worktrees/feature', sourceWorkspacePath: 'C:/projects/app' }
    } as unknown as ThreadSummary
    useThreadStore.setState({ activeThreadId: 'source', activeThread: source, threadList: [source] } as never)
    useConversationStore.setState({ workspacePath: 'C:/projects/app/.craft/worktrees/feature', remoteWorkspaceActive: false } as never)
    vi.mocked(createForkWorktreeThread).mockResolvedValue({ id: 'fork' } as ThreadSummary)

    await runThreadFork({ threadId: 'source', mode: 'worktree', t: (key) => key })

    expect(createForkWorktreeThread).toHaveBeenCalledWith(expect.objectContaining({
      sourceThreadId: 'source',
      workspacePath: 'C:/projects/app',
      local: true
    }))
  })
})
