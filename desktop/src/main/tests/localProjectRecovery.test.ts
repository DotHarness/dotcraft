import { EventEmitter } from 'events'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { bindLocalProjectRecovery, type LocalProjectRecoveryContext } from '../localProjectRecovery'
import { LocalProjectList } from '../localProjects'
import type { HubProject } from '../desktopHub'

function context(overrides: Partial<LocalProjectRecoveryContext> = {}): LocalProjectRecoveryContext {
  return {
    refreshProjects: vi.fn(async () => true),
    refreshSecondaryConnections: vi.fn(async () => {}),
    isCurrent: () => true,
    ...overrides
  }
}

afterEach(() => { vi.restoreAllMocks() })

describe('local project recovery', () => {
  it('publishes the complete snapshot before secondary recovery after a failed initial connection', async () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    const projects: HubProject[] = Array.from({ length: 7 }, (_, index) => ({
      path: `/work/${index}`, displayName: `Project ${index}`,
      addedAt: '2026-10-01T08:00:00Z', lastOpenedAt: '2026-10-01T08:00:00Z', running: false
    }))
    const published: string[][] = []
    const listProjects = vi.fn(async () => projects).mockRejectedValueOnce(new Error('Initial connection failed'))
    const list = new LocalProjectList(() => ({
      listProjects, openProject: vi.fn(), removeProject: vi.fn(), subscribeEvents: vi.fn()
    }), () => { published.push(list.list().map((project) => project.path)) })
    await expect(list.refresh()).resolves.toBe(false)
    expect(list.list()).toEqual([])
    const secondarySnapshots: string[][] = []
    const ctx = context({
      refreshProjects: () => list.refresh(),
      refreshSecondaryConnections: async () => {
        expect(published).toHaveLength(1)
        secondarySnapshots.push(list.list().map((project) => project.path))
      }
    })
    const failedClient = new EventEmitter()
    let currentClient = failedClient
    bindLocalProjectRecovery(failedClient, { ...ctx, isCurrent: () => currentClient === failedClient })
    failedClient.emit('close')
    const retryClient = new EventEmitter()
    currentClient = retryClient
    bindLocalProjectRecovery(retryClient, { ...ctx, isCurrent: () => currentClient === retryClient })
    failedClient.emit('ready')
    expect(listProjects).toHaveBeenCalledOnce()
    retryClient.emit('ready')
    await vi.waitFor(() => expect(secondarySnapshots).toHaveLength(1))
    expect(published[0]).toHaveLength(7)
    expect(secondarySnapshots).toEqual(published)
    expect(listProjects).toHaveBeenCalledTimes(2)
  })

  it('does not start recovery when the local foreground guard rejects the connection', () => {
    const ctx = context({ isCurrent: () => false })
    const client = new EventEmitter()
    bindLocalProjectRecovery(client, ctx)
    client.emit('ready')
    client.emit('reconnected')
    expect(ctx.refreshProjects).not.toHaveBeenCalled()
    expect(ctx.refreshSecondaryConnections).not.toHaveBeenCalled()
  })

  it('recovers on ready and reconnected after the foreground handler runs', async () => {
    const client = new EventEmitter()
    let connected = false
    client.on('ready', () => { connected = true })
    client.on('reconnected', () => { connected = true })
    const ctx = context({ isCurrent: () => connected })
    bindLocalProjectRecovery(client, ctx)
    client.emit('ready')
    await vi.waitFor(() => expect(ctx.refreshSecondaryConnections).toHaveBeenCalledOnce())
    connected = false
    client.emit('reconnected')
    await vi.waitFor(() => expect(ctx.refreshSecondaryConnections).toHaveBeenCalledTimes(2))
  })

  it('stops secondary recovery when the connection is superseded during refresh', async () => {
    let finish!: (success: boolean) => void
    const pending = new Promise<boolean>((resolve) => { finish = resolve })
    let current = true
    const ctx = context({ refreshProjects: () => pending, isCurrent: () => current })
    const client = new EventEmitter()
    bindLocalProjectRecovery(client, ctx)
    client.emit('ready')
    current = false
    finish(true)
    await pending
    expect(ctx.refreshSecondaryConnections).not.toHaveBeenCalled()
  })

  it('does not restore secondary connections when the project snapshot fails', async () => {
    const ctx = context({ refreshProjects: vi.fn(async () => false) })
    const client = new EventEmitter()
    bindLocalProjectRecovery(client, ctx)
    client.emit('ready')
    await vi.waitFor(() => expect(ctx.refreshProjects).toHaveBeenCalledOnce())
    expect(ctx.refreshSecondaryConnections).not.toHaveBeenCalled()
  })

  it.each(['snapshot', 'secondary'] as const)('absorbs a background %s exception', async (stage) => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const ctx = context(stage === 'snapshot'
      ? { refreshProjects: vi.fn(async () => { throw new Error('Hub unavailable') }) }
      : { refreshSecondaryConnections: vi.fn(async () => { throw new Error('Hub unavailable') }) })
    const client = new EventEmitter()
    bindLocalProjectRecovery(client, ctx)
    client.emit('ready')
    await vi.waitFor(() => expect(warn).toHaveBeenCalledOnce())
  })
})
