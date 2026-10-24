import { describe, expect, it, vi } from 'vitest'
import { removeAnyway, removeWorktree } from '../components/settings/panels/worktrees/worktreeRemoval'

function cleanupFailed(): Error {
  return Object.assign(new Error('The environment cleanup script failed with exit code 1.'), {
    rpcCode: -32106,
    data: { code: 'cleanupFailed', params: { path: 'C:\\repo\\.craft\\worktrees\\a', exitCode: 1, outputTail: 'docker: not found' } }
  })
}

describe('worktree removal', () => {
  it('offers Remove anyway after a failed cleanup and then removes without cleanup, keeping the discard choice', async () => {
    const request = vi.fn(async (_method: string, params: { skipCleanup?: boolean }) => {
      if (!params.skipCleanup) throw cleanupFailed()
      return {}
    })

    const outcome = await removeWorktree(request, { path: 'C:\\repo\\.craft\\worktrees\\a', discardChanges: true })
    expect(outcome).toMatchObject({ kind: 'cleanupFailed', failure: { exitCode: 1, outputTail: 'docker: not found' } })
    if (outcome.kind !== 'cleanupFailed') return

    await expect(removeAnyway(request, outcome)).resolves.toEqual({ kind: 'removed' })
    expect(request).toHaveBeenLastCalledWith('worktree/remove', {
      path: 'C:\\repo\\.craft\\worktrees\\a',
      discardChanges: true,
      skipCleanup: true
    })
  })

  it('reports other failures without offering Remove anyway', async () => {
    const request = vi.fn(async () => {
      throw new Error('worktree has a bound thread')
    })

    await expect(removeWorktree(request, { path: 'C:\\repo\\.craft\\worktrees\\a', discardChanges: false }))
      .resolves.toEqual({ kind: 'failed', message: 'worktree has a bound thread' })
  })
})
