import { EventEmitter } from 'node:events'
import { afterEach, describe, expect, it, vi } from 'vitest'

const sockets = vi.hoisted(() => [] as Array<EventEmitter & { close: ReturnType<typeof vi.fn>; removeAllListeners: () => any }>)

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

  it('does not publish an old workspace resolution after switching or closing its owner', async () => {
    let resolveFirst!: (value: never) => void
    const first = new Promise<never>((resolve) => { resolveFirst = resolve })
    const ensureManagedService = vi.fn()
      .mockImplementationOnce(() => first)
      .mockResolvedValue({ state: 'running', endpoint: 'http://127.0.0.1:5010', accessToken: 'new-token' })
    let workspace = 'F:/old'
    const provider = new OratorioProvider(() => ({ ensureManagedService } as never), () => workspace)
    const old = provider.getContext()
    await vi.waitFor(() => expect(ensureManagedService).toHaveBeenCalledOnce())
    workspace = 'F:/new'
    provider.contextChanged()
    await expect(provider.getContext()).resolves.toMatchObject({ workspacePath: 'F:/new' })
    resolveFirst({ state: 'running', endpoint: 'http://127.0.0.1:5009', accessToken: 'old-token' } as never)
    await expect(old).rejects.toMatchObject({ code: 'oratorio.serviceUnavailable' })
    await expect(provider.getContext()).resolves.toMatchObject({ workspacePath: 'F:/new' })
    provider.dispose()
    await expect(provider.getContext()).rejects.toMatchObject({ code: 'oratorio.serviceUnavailable' })
  })

  it('coalesces ensure and keeps endpoint and token in Main', async () => {
    const ensureManagedService = vi.fn().mockResolvedValue({
      serviceId: 'oratorio', state: 'running', pid: 42,
      endpoint: 'http://127.0.0.1:5010', accessToken: 'service-secret'
    })
    const provider = new OratorioProvider(
      () => ({ ensureManagedService } as never),
      () => 'F:/workspace'
    )

    const [first, second] = await Promise.all([provider.getContext(), provider.getContext()])

    expect(ensureManagedService).toHaveBeenCalledExactlyOnceWith('oratorio')
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
      () => null
    )

    await provider.request({ method: 'GET', path: '/api/v1/tasks' })
    await provider.retry()

    expect(fetchMock).toHaveBeenCalledWith('http://127.0.0.1:5010/api/v1/tasks', expect.objectContaining({
      headers: expect.objectContaining({ Authorization: 'Bearer service-secret' })
    }))
    expect(ensureManagedService).toHaveBeenCalledTimes(2)
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
})
