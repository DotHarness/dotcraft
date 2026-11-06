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

  it.each([false, true])('separates environment discovery from state identity (worktree=%s)', async (worktree) => {
    const source = {
      id: 'source',
      workspacePath: 'C:/projects/app',
      effectiveWorkspacePath: worktree ? 'C:/worktrees/feature' : 'C:/projects/library',
      ...(worktree ? { worktree: { path: 'C:/worktrees/feature', sourceWorkspacePath: 'C:/projects/library' } } : {})
    } as unknown as ThreadSummary
    useThreadStore.setState({ activeThreadId: 'source', activeThread: source, threadList: [source] } as never)
    useConversationStore.setState({ workspacePath: source.effectiveWorkspacePath, remoteWorkspaceActive: false })
    vi.mocked(createForkWorktreeThread).mockResolvedValue({ id: 'fork' } as ThreadSummary)

    await runThreadFork({ threadId: 'source', mode: 'worktree', t: (key) => key })

    expect(createForkWorktreeThread).toHaveBeenCalledWith(expect.objectContaining({
      sourceThreadId: 'source',
      workspacePath: 'C:/projects/app',
      sourceWorkspacePath: 'C:/projects/library',
      local: true
    }))
  })
})
