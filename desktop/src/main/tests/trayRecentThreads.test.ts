import { EventEmitter } from 'events'
import { describe, expect, it, vi } from 'vitest'
import type { HubAppServerResponse } from '../desktopHub'
import {
  MAX_RECENT_THREAD_COUNT,
  TrayRecentThreadCatalog,
  type TrayThreadClientFactory
} from '../trayRecentThreads'

class FakeTrayThreadClient extends EventEmitter {
  readonly dispose = vi.fn()
  readonly sendRequest = vi.fn<(
    method: string,
    params?: unknown,
    timeoutMs?: number | null
  ) => Promise<unknown>>()

  constructor(result: unknown | Error) {
    super()
    this.sendRequest.mockImplementation(async () => {
      if (result instanceof Error) throw result
      return result
    })
    queueMicrotask(() => this.emit('ready', {}))
  }
}

function appServer(
  workspacePath: string,
  endpoint: string,
  state = 'running'
): HubAppServerResponse {
  return {
    workspacePath,
    canonicalWorkspacePath: workspacePath,
    state,
    endpoints: endpoint ? { appServerWebSocket: endpoint } : {},
    serviceStatus: {},
    startedByHub: true
  }
}

function thread(
  id: string,
  lastActiveAt: string,
  overrides: Record<string, unknown> = {}
): Record<string, unknown> {
  return {
    id,
    displayName: id,
    lastActiveAt,
    status: 'active',
    originChannel: 'desktop',
    ...overrides
  }
}

describe('TrayRecentThreadCatalog', () => {
  it('merges running workspace threads by global activity and sends a bounded workspace query', async () => {
    const clients = new Map<string, FakeTrayThreadClient>()
    const results = new Map<string, unknown>([
      ['ws://one', {
        data: [
          thread('one-newer', '2026-09-22T08:00:00Z'),
          thread('one-older', '2026-09-22T06:00:00Z')
        ]
      }],
      ['ws://two', {
        data: [thread('two-newest', '2026-09-22T09:00:00Z')]
      }]
    ])
    const factory: TrayThreadClientFactory = (endpoint) => {
      const client = new FakeTrayThreadClient(results.get(endpoint))
      clients.set(endpoint, client)
      return client
    }
    const catalog = new TrayRecentThreadCatalog(factory)

    const recent = await catalog.refresh([
      appServer('F:/work/one', 'ws://one'),
      appServer('F:/work/two', 'ws://two'),
      appServer('F:/work/stopped', 'ws://stopped', 'stopped')
    ])

    expect(recent.map((item) => item.id)).toEqual(['two-newest', 'one-newer', 'one-older'])
    expect(recent.map((item) => item.workspaceName)).toEqual(['two', 'one', 'one'])
    expect(clients.has('ws://stopped')).toBe(false)
    expect(clients.get('ws://one')?.sendRequest).toHaveBeenCalledWith(
      'thread/list',
      expect.objectContaining({
        identity: expect.objectContaining({ workspacePath: 'F:/work/one' }),
        scope: 'workspace',
        includeArchived: false,
        includeSubAgents: false,
        includeInternal: false,
        limit: MAX_RECENT_THREAD_COUNT
      }),
      expect.any(Number)
    )

    catalog.dispose()
  })

  it('filters archived, subagent, malformed, and excess thread summaries', async () => {
    const data = Array.from({ length: MAX_RECENT_THREAD_COUNT + 3 }, (_, index) => (
      thread(`thread-${index}`, new Date(Date.UTC(2026, 8, 22, 10, 0, index)).toISOString())
    ))
    data.push(
      thread('archived', '2026-09-23T10:00:00Z', { status: 'archived' }),
      thread('subagent', '2026-09-23T09:00:00Z', { source: { kind: 'subagent' } }),
      thread('invalid-date', 'not-a-date'),
      { displayName: 'missing id', lastActiveAt: '2026-09-23T08:00:00Z', status: 'active' }
    )
    const catalog = new TrayRecentThreadCatalog(() => new FakeTrayThreadClient({ data }))

    const recent = await catalog.refresh([appServer('F:/work/main', 'ws://main')])

    expect(recent).toHaveLength(MAX_RECENT_THREAD_COUNT)
    expect(recent.some((item) => ['archived', 'subagent', 'invalid-date'].includes(item.id))).toBe(false)
    expect(recent[0]?.id).toBe(`thread-${MAX_RECENT_THREAD_COUNT + 2}`)

    catalog.dispose()
  })

  it('keeps other workspace results when one thread list fails', async () => {
    const catalog = new TrayRecentThreadCatalog((endpoint) => new FakeTrayThreadClient(
      endpoint === 'ws://bad'
        ? new Error('thread/list failed')
        : { data: [thread('available', '2026-09-22T09:00:00Z')] }
    ))

    await expect(catalog.refresh([
      appServer('F:/work/bad', 'ws://bad'),
      appServer('F:/work/good', 'ws://good')
    ])).resolves.toMatchObject([{ id: 'available', workspacePath: 'F:/work/good' }])

    catalog.dispose()
  })

  it('reuses live clients and disposes them when endpoints disappear or the catalog closes', async () => {
    const clients: FakeTrayThreadClient[] = []
    const factory: TrayThreadClientFactory = () => {
      const client = new FakeTrayThreadClient({ data: [] })
      clients.push(client)
      return client
    }
    const catalog = new TrayRecentThreadCatalog(factory)
    const server = appServer('F:/work/main', 'ws://main')

    await catalog.refresh([server])
    await catalog.refresh([server])
    expect(clients).toHaveLength(1)
    expect(clients[0]?.sendRequest).toHaveBeenCalledTimes(2)

    await catalog.refresh([])
    expect(clients[0]?.dispose).toHaveBeenCalledOnce()

    await catalog.refresh([server])
    expect(clients).toHaveLength(2)
    catalog.dispose()
    expect(clients[1]?.dispose).toHaveBeenCalledOnce()
  })
})
