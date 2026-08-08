import { EventEmitter } from 'node:events'
import { afterEach, describe, expect, it, vi } from 'vitest'

const sockets = vi.hoisted(() => [] as Array<EventEmitter & { close: ReturnType<typeof vi.fn>; removeAllListeners: () => any }>)

vi.mock('electron', () => ({
  app: { isPackaged: false, getAppPath: () => 'C:/example/dotcraft/desktop' }
}))

vi.mock('ws', () => ({
  default: class FakeWebSocket extends EventEmitter {
    close = vi.fn()
    constructor() {
      super()
      sockets.push(this)
    }
  }
}))

import { OratorioProvider } from './OratorioProvider'

describe('OratorioProvider', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    sockets.length = 0
  })

  it('coalesces ensure and keeps endpoint and token in Main', async () => {
    const ensureManagedService = vi.fn().mockResolvedValue({
      serviceId: 'oratorio', state: 'running', pid: 42,
      endpoint: 'http://127.0.0.1:5010', accessToken: 'service-secret'
    })
    const provider = new OratorioProvider(
      () => ({ ensureManagedService } as never),
      () => 'F:/workspace',
      () => 'F:/oratorio-server.exe'
    )

    const [first, second] = await Promise.all([provider.getContext(), provider.getContext()])

    expect(ensureManagedService).toHaveBeenCalledTimes(1)
    expect(first).toEqual(second)
    expect(first).toMatchObject({ provider: 'local', workspacePath: 'F:/workspace', connected: true })
    expect(JSON.stringify(first)).not.toContain('service-secret')
    expect(JSON.stringify(first)).not.toContain('5010')
  })

  it('injects bearer for bounded requests and re-ensures on retry', async () => {
    const ensureManagedService = vi.fn().mockResolvedValue({
      serviceId: 'oratorio', state: 'running', pid: 42,
      endpoint: 'http://127.0.0.1:5010', accessToken: 'service-secret'
    })
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ tasks: [] }), {
      status: 200, headers: { 'content-type': 'application/json' }
    }))
    vi.stubGlobal('fetch', fetchMock)
    const provider = new OratorioProvider(
      () => ({ ensureManagedService } as never),
      () => null,
      () => 'F:/oratorio-server.exe'
    )

    await provider.request({ method: 'GET', path: '/api/v1/tasks' })
    await provider.retry()

    expect(fetchMock).toHaveBeenCalledWith('http://127.0.0.1:5010/api/v1/tasks', expect.objectContaining({
      headers: expect.objectContaining({ Authorization: 'Bearer service-secret' })
    }))
    expect(ensureManagedService).toHaveBeenCalledTimes(2)
  })

  it('resolves desktop-service handoffs in Main without returning AppServer credentials to Renderer', async () => {
    const ensureManagedService = vi.fn().mockResolvedValue({
      serviceId: 'oratorio', state: 'running', pid: 42,
      endpoint: 'http://127.0.0.1:5010', accessToken: 'service-secret'
    })
    const ensureAppServer = vi.fn().mockResolvedValue({
      workspacePath: 'F:/workspace', canonicalWorkspacePath: 'F:/workspace', state: 'running',
      endpoints: { appServerWebSocket: 'ws://127.0.0.1:9100/ws?token=appserver-secret' },
      serviceStatus: {}
    })
    const fetchMock = vi.fn().mockImplementation(async () => new Response(JSON.stringify({ state: 'connected' }), {
      status: 200, headers: { 'content-type': 'application/json' }
    }))
    vi.stubGlobal('fetch', fetchMock)
    const provider = new OratorioProvider(
      () => ({ ensureManagedService, ensureAppServer } as never),
      () => 'F:/workspace',
      () => 'F:/oratorio-server.exe'
    )

    const handoff = await provider.prepareHandoff(
      'dotcraft-service://oratorio/connect?app=com.dotharness.oratorio&request=req-1&token=request-token&workspace=F%3A%2Fworkspace&identity=local%3AF%3A%2Fworkspace'
    )
    await provider.resolveHandoff(handoff.requestId, true)

    expect(ensureAppServer).toHaveBeenCalledWith('F:/workspace', { startIfMissing: true })
    expect(fetchMock.mock.calls[0][0]).toContain('/api/v1/dotcraft/app-binding/inspect')
    const [, options] = fetchMock.mock.calls[1]
    expect(options.headers.Authorization).toBe('Bearer service-secret')
    const body = JSON.parse(options.body as string) as { url: string }
    expect(body.url).toContain('endpoint=ws%3A%2F%2F127.0.0.1%3A9100')
    expect(body.url).toContain('identity=local%3AF%3A%2Fworkspace')
  })

  it('uses tunneled remote services and clears the stream when the active context changes', async () => {
    const ensureManagedService = vi.fn()
    const resolveRemote = vi.fn().mockResolvedValue({
      endpoint: 'http://127.0.0.1:49124',
      accessToken: 'remote-oratorio-secret',
      workspacePath: '/workspace',
      appServerEndpoint: 'ws://127.0.0.1:49123/ws?token=remote-appserver-secret',
      appServerIdentity: 'remote:cloud:prod:/workspace'
    })
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ tasks: [] }), {
      status: 200, headers: { 'content-type': 'application/json' }
    }))
    vi.stubGlobal('fetch', fetchMock)
    const provider = new OratorioProvider(
      () => ({ ensureManagedService } as never),
      () => null,
      () => 'unused',
      () => {},
      resolveRemote
    )

    provider.subscribe()
    const context = await provider.getContext()
    await provider.request({ method: 'GET', path: '/api/v1/tasks' })

    expect(context).toMatchObject({ provider: 'remote', workspacePath: '/workspace', connected: true })
    expect(JSON.stringify(context)).not.toContain('49124')
    expect(JSON.stringify(context)).not.toContain('secret')
    expect(ensureManagedService).not.toHaveBeenCalled()
    expect(fetchMock).toHaveBeenCalledWith('http://127.0.0.1:49124/api/v1/tasks', expect.objectContaining({
      headers: expect.objectContaining({ Authorization: 'Bearer remote-oratorio-secret' })
    }))
    expect(sockets).toHaveLength(1)

    provider.contextChanged()
    expect(sockets[0].close).toHaveBeenCalledOnce()
    await provider.getContext()
    expect(resolveRemote).toHaveBeenCalledTimes(2)
  })

  it('forwards only credential-free realtime fields to Renderer', async () => {
    const onDataChanged = vi.fn()
    const provider = new OratorioProvider(
      () => ({ ensureManagedService: vi.fn().mockResolvedValue({ state: 'running', endpoint: 'http://127.0.0.1:5010', accessToken: 'secret' }) } as never),
      () => 'F:/workspace',
      () => 'F:/oratorio-server.exe',
      onDataChanged
    )
    provider.subscribe()
    await provider.getContext()
    sockets[0].emit('message', Buffer.from(JSON.stringify({
      type: 'drawer/item.delta', runId: 'run-1', token: 'must-not-leak', endpoint: 'http://secret',
      payload: { type: 'agentMessage', payload: { text: 'latest output', token: 'nested-secret' } }
    })))
    const event = onDataChanged.mock.calls[0][1]
    expect(event).toEqual({ type: 'drawer/item.delta', runId: 'run-1', payload: { type: 'agentMessage', status: undefined, text: 'latest output' } })
    expect(JSON.stringify(event)).not.toContain('secret')
  })

  it('injects the remote AppServer endpoint and identity into handoff approval', async () => {
    const fetchMock = vi.fn().mockImplementation(async () => new Response(JSON.stringify({ state: 'connected' }), {
      status: 200, headers: { 'content-type': 'application/json' }
    }))
    vi.stubGlobal('fetch', fetchMock)
    const provider = new OratorioProvider(
      () => ({ ensureManagedService: vi.fn(), ensureAppServer: vi.fn() } as never),
      () => null,
      () => 'unused',
      () => {},
      async () => ({
        endpoint: 'http://127.0.0.1:49124',
        accessToken: 'remote-oratorio-secret',
        workspacePath: '/workspace',
        appServerEndpoint: 'ws://127.0.0.1:49123/ws?token=remote-appserver-secret',
        appServerIdentity: 'remote:cloud:prod:/workspace'
      })
    )

    const handoff = await provider.prepareHandoff(
      'dotcraft-service://oratorio/connect?app=com.dotharness.oratorio&request=req-1&token=request-token&workspace=%2Fworkspace'
    )
    await provider.resolveHandoff(handoff.requestId, true)

    const [, options] = fetchMock.mock.calls[1]
    const body = JSON.parse(options.body as string) as { url: string }
    expect(body.url).toContain('endpoint=ws%3A%2F%2F127.0.0.1%3A49123')
    expect(body.url).toContain('identity=remote%3Acloud%3Aprod%3A%2Fworkspace')
  })

  it('requires explicit handoff resolution and does not approve a cancelled request', async () => {
    const fetchMock = vi.fn().mockImplementation(async () => new Response(JSON.stringify({ operation: 'connect', connection: {} }), { status: 200 }))
    vi.stubGlobal('fetch', fetchMock)
    const provider = new OratorioProvider(
      () => ({
        ensureManagedService: vi.fn().mockResolvedValue({ state: 'running', endpoint: 'http://127.0.0.1:5010', accessToken: 'secret' }),
        ensureAppServer: vi.fn().mockResolvedValue({ endpoints: { appServerWebSocket: 'ws://127.0.0.1:9100/ws' }, serviceStatus: {}, canonicalWorkspacePath: 'F:/workspace' })
      } as never),
      () => 'F:/workspace',
      () => 'F:/oratorio-server.exe'
    )
    const handoff = await provider.prepareHandoff('dotcraft-service://oratorio/connect?app=com.dotharness.oratorio&request=req-1&token=request-token&workspace=F%3A%2Fworkspace')
    expect(provider.getPendingHandoff()).toEqual(handoff)
    await provider.resolveHandoff(handoff.requestId, false)
    expect(provider.getPendingHandoff()).toBeNull()
    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(fetchMock.mock.calls[0][0]).toContain('/inspect')
  })
})
