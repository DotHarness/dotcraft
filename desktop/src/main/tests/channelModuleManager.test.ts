import { afterEach, describe, expect, it, vi } from 'vitest'
import { mkdir, mkdtemp, rm, writeFile } from 'fs/promises'
import { tmpdir } from 'os'
import { join } from 'path'
import { ChannelModuleManager } from '../channelModuleManager'
import type { DiscoveredModule } from '../moduleScanner'
import type { DesktopAppServerClient } from '../DesktopAppServerClient'

function makeModule(overrides: Partial<DiscoveredModule>): DiscoveredModule {
  return {
    moduleId: 'telegram-default',
    channelName: 'telegram',
    displayName: 'Telegram',
    packageName: '@dotcraft/channel-telegram',
    configFileName: 'telegram.json',
    supportedTransports: ['websocket'],
    requiresInteractiveSetup: false,
    variant: 'default',
    source: 'bundled',
    absolutePath: join('C:\\dotcraft', 'resources', 'modules', 'channel-telegram'),
    configDescriptors: [],
    ...overrides
  }
}

describe('ChannelModuleManager', () => {
  let tempRoot = ''

  afterEach(async () => {
    vi.useRealTimers()
    if (tempRoot) {
      await rm(tempRoot, { recursive: true, force: true })
      tempRoot = ''
    }
  })

  it('upserts WebSocket-capable bundled modules as managed WebSocket channels', async () => {
    tempRoot = await mkdtemp(join(tmpdir(), 'dotcraft-module-manager-'))
    await mkdir(join(tempRoot, '.craft'), { recursive: true })
    await writeFile(join(tempRoot, '.craft', 'telegram.json'), '{}', 'utf-8')
    const module = makeModule({})
    const requests: Array<{ method: string; params: unknown }> = []
    const client = makeFakeClient(requests)
    const manager = new ChannelModuleManager({
      workspacePath: tempRoot,
      getWireClient: () => client,
      onStatusChanged: () => {},
      getCachedModules: () => [module],
      onQrUpdate: () => {}
    })

    try {
      const result = await manager.start(module.moduleId)
      expect(result).toEqual({ ok: true })

      const upsert = requests.find((request) => request.method === 'externalChannel/upsert')
      expect(upsert?.params).toEqual({
        channel: {
          name: 'telegram',
          enabled: true,
          transport: 'managedWebsocket',
          builtinModule: 'channel-telegram'
        }
      })
    } finally {
      await manager.dispose()
    }
  })

  it('upserts stdio-only bundled modules as subprocess channels', async () => {
    tempRoot = await mkdtemp(join(tmpdir(), 'dotcraft-module-manager-'))
    await mkdir(join(tempRoot, '.craft'), { recursive: true })
    await writeFile(join(tempRoot, '.craft', 'stdio.json'), '{}', 'utf-8')
    const module = makeModule({
      moduleId: 'stdio-default',
      channelName: 'stdio',
      configFileName: 'stdio.json',
      supportedTransports: ['stdio'],
      absolutePath: join('C:\\dotcraft', 'resources', 'modules', 'channel-stdio')
    })
    const requests: Array<{ method: string; params: unknown }> = []
    const client = makeFakeClient(requests)
    const manager = new ChannelModuleManager({
      workspacePath: tempRoot,
      getWireClient: () => client,
      onStatusChanged: () => {},
      getCachedModules: () => [module],
      onQrUpdate: () => {}
    })

    try {
      const result = await manager.start(module.moduleId)
      expect(result).toEqual({ ok: true })

      const upsert = requests.find((request) => request.method === 'externalChannel/upsert')
      expect(upsert?.params).toEqual({
        channel: {
          name: 'stdio',
          enabled: true,
          transport: 'subprocess',
          builtinModule: 'channel-stdio'
        }
      })
    } finally {
      await manager.dispose()
    }
  })

  it('maps a permanent AppServer failure to crashed and stops polling', async () => {
    vi.useFakeTimers()
    tempRoot = await mkdtemp(join(tmpdir(), 'dotcraft-module-manager-'))
    await mkdir(join(tempRoot, '.craft'), { recursive: true })
    await writeFile(join(tempRoot, '.craft', 'telegram.json'), '{}', 'utf-8')
    const module = makeModule({})
    const requests: Array<{ method: string; params: unknown }> = []
    const client = makeFakeClient(requests, [
      {
        name: 'telegram',
        enabled: true,
        running: false,
        runtimeState: 'failed',
        failureCode: 'externalChannelStartFailed'
      }
    ])
    const manager = new ChannelModuleManager({
      workspacePath: tempRoot,
      getWireClient: () => client,
      onStatusChanged: () => {},
      getCachedModules: () => [module],
      onQrUpdate: () => {}
    })

    try {
      expect(await manager.start(module.moduleId)).toEqual({ ok: true })
      expect(manager.getStatusMap()[module.moduleId]).toMatchObject({
        processState: 'crashed',
        connected: false,
        failureCode: 'externalChannelStartFailed'
      })
      const statusRequestCount = requests.filter((request) => request.method === 'channel/status').length
      await vi.advanceTimersByTimeAsync(9_000)
      expect(requests.filter((request) => request.method === 'channel/status')).toHaveLength(statusRequestCount)
    } finally {
      await manager.dispose()
    }
  })

  it('falls back to legacy running booleans when runtimeState is absent', async () => {
    tempRoot = await mkdtemp(join(tmpdir(), 'dotcraft-module-manager-'))
    await mkdir(join(tempRoot, '.craft'), { recursive: true })
    await writeFile(join(tempRoot, '.craft', 'telegram.json'), '{}', 'utf-8')
    const module = makeModule({})
    const requests: Array<{ method: string; params: unknown }> = []
    const client = makeFakeClient(requests, [{ name: 'telegram', enabled: true, running: true }])
    const manager = new ChannelModuleManager({
      workspacePath: tempRoot,
      getWireClient: () => client,
      onStatusChanged: () => {},
      getCachedModules: () => [module],
      onQrUpdate: () => {}
    })

    try {
      expect(await manager.start(module.moduleId)).toEqual({ ok: true })
      expect(manager.getStatusMap()[module.moduleId]).toMatchObject({
        processState: 'running',
        connected: true
      })
    } finally {
      await manager.dispose()
    }
  })

  it('restores enabled module tracking without replacing the AppServer channel', async () => {
    tempRoot = await mkdtemp(join(tmpdir(), 'dotcraft-module-manager-'))
    const module = makeModule({})
    const requests: Array<{ method: string; params: unknown }> = []
    const client = makeFakeClient(requests, [{ name: 'telegram', enabled: true, running: true }])
    const manager = new ChannelModuleManager({
      workspacePath: tempRoot,
      getWireClient: () => client,
      onStatusChanged: () => {},
      getCachedModules: () => [module],
      onQrUpdate: () => {}
    })

    try {
      await manager.restoreModules([module.moduleId])
      expect(manager.getStatusMap()[module.moduleId]).toMatchObject({
        processState: 'running',
        connected: true
      })
      expect(requests.some((request) => request.method === 'externalChannel/upsert')).toBe(false)

      await manager.restoreModules([])
      expect(manager.getStatusMap()).toEqual({})
      expect(requests.some((request) => request.method === 'externalChannel/upsert')).toBe(false)
    } finally {
      await manager.dispose()
    }
  })
})

function makeFakeClient(
  requests: Array<{ method: string; params: unknown }>,
  channels: Array<{
    name: string
    enabled: boolean
    running: boolean
    runtimeState?: string
    failureCode?: string
  }> = []
): DesktopAppServerClient {
  return {
    sendRequest: vi.fn(async (method: string, params?: unknown) => {
      requests.push({ method, params })
      if (method === 'channel/status') {
        return { channels }
      }
      if (method === 'externalChannel/logs') {
        return { lines: [] }
      }
      return {}
    })
  } as unknown as DesktopAppServerClient
}
