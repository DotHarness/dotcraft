import { describe, expect, it, vi } from 'vitest'
import { openWorkspaceThread } from './openWorkspaceThread'

describe('openWorkspaceThread', () => {
  it('defers selection until a different Workspace becomes foreground', async () => {
    const switchWorkspace = vi.fn().mockResolvedValue(undefined)
    const setPending = vi.fn()
    const activateThread = vi.fn()

    await openWorkspaceThread({
      threadId: 'thread-2', workspacePath: 'F:/two', foregroundWorkspacePath: 'F:/one',
      switchWorkspace, setPending, clearPending: vi.fn(), activateThread
    })

    expect(setPending).toHaveBeenCalledWith(expect.objectContaining({ workspacePath: 'F:/two', threadId: 'thread-2' }))
    expect(switchWorkspace).toHaveBeenCalledWith('F:/two')
    expect(activateThread).not.toHaveBeenCalled()
  })

  it('selects immediately inside the foreground Workspace', async () => {
    const activateThread = vi.fn()
    await openWorkspaceThread({
      threadId: 'thread-1', workspacePath: 'F:/one', foregroundWorkspacePath: 'F:/one',
      switchWorkspace: vi.fn(), setPending: vi.fn(), clearPending: vi.fn(), activateThread
    })
    expect(activateThread).toHaveBeenCalledWith('thread-1')
  })
})
