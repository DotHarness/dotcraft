import { describe, expect, it, vi } from 'vitest'
import { retryAppServerConnection } from '../appServerRetry'

function context(overrides: Partial<Parameters<typeof retryAppServerConnection>[1]> = {}) {
  return {
    currentWorkspacePath: '/workspace',
    launchedWithRemote: false,
    connectionMode: 'local' as const,
    reconnect: vi.fn().mockResolvedValue(undefined),
    restartManaged: vi.fn().mockResolvedValue(undefined),
    ...overrides
  }
}

describe('retryAppServerConnection', () => {
  it('reconnects a plain local retry and restarts the Hub-managed AppServer only when requested', async () => {
    const plain = context()
    await retryAppServerConnection({ restartManaged: false }, plain)
    expect(plain.reconnect).toHaveBeenCalledOnce()
    expect(plain.restartManaged).not.toHaveBeenCalled()

    const restart = context()
    await retryAppServerConnection({ restartManaged: true }, restart)
    expect(restart.restartManaged).toHaveBeenCalledOnce()
    expect(restart.reconnect).not.toHaveBeenCalled()
  })

  it('reconnects remote and CLI remote modes instead of restarting even when restart is requested', async () => {
    for (const ctx of [context({ connectionMode: 'remote' }), context({ launchedWithRemote: true })]) {
      await retryAppServerConnection({ restartManaged: true }, ctx)
      expect(ctx.reconnect).toHaveBeenCalledOnce()
      expect(ctx.restartManaged).not.toHaveBeenCalled()
    }
  })

  it('requires a workspace before retrying', async () => {
    await expect(
      retryAppServerConnection(undefined, context({ currentWorkspacePath: '' }))
    ).rejects.toThrow('Open a workspace before retrying the AppServer connection.')
  })
})
