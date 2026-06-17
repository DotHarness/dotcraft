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
  it('reconnects the current workspace for a plain local retry', async () => {
    const ctx = context()

    await retryAppServerConnection({ restartManaged: false }, ctx)

    expect(ctx.reconnect).toHaveBeenCalledOnce()
    expect(ctx.restartManaged).not.toHaveBeenCalled()
  })

  it('restarts a Hub-managed local AppServer when requested', async () => {
    const ctx = context()

    await retryAppServerConnection({ restartManaged: true }, ctx)

    expect(ctx.restartManaged).toHaveBeenCalledOnce()
    expect(ctx.reconnect).not.toHaveBeenCalled()
  })

  it('reconnects remote mode instead of restarting even when restart is requested', async () => {
    const ctx = context({ connectionMode: 'remote' })

    await retryAppServerConnection({ restartManaged: true }, ctx)

    expect(ctx.reconnect).toHaveBeenCalledOnce()
    expect(ctx.restartManaged).not.toHaveBeenCalled()
  })

  it('reconnects CLI remote mode instead of restarting even when restart is requested', async () => {
    const ctx = context({ launchedWithRemote: true })

    await retryAppServerConnection({ restartManaged: true }, ctx)

    expect(ctx.reconnect).toHaveBeenCalledOnce()
    expect(ctx.restartManaged).not.toHaveBeenCalled()
  })

  it('requires a workspace before retrying', async () => {
    await expect(
      retryAppServerConnection(undefined, context({ currentWorkspacePath: '' }))
    ).rejects.toThrow('Open a workspace before retrying the AppServer connection.')
  })
})
