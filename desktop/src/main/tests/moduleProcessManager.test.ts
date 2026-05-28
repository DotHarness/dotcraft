import { afterEach, describe, expect, it, vi } from 'vitest'
import { mkdir, mkdtemp, rm, writeFile } from 'fs/promises'
import { tmpdir } from 'os'
import { join } from 'path'
import { ModuleProcessManager } from '../moduleProcessManager'
import type { DiscoveredModule } from '../moduleScanner'
import type { WireProtocolClient } from '../WireProtocolClient'

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

describe('ModuleProcessManager', () => {
  let tempRoot = ''

  afterEach(async () => {
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
    const manager = new ModuleProcessManager({
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
      await manager.stopAll({ preserveExternalChannels: true })
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
    const manager = new ModuleProcessManager({
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
      await manager.stopAll({ preserveExternalChannels: true })
    }
  })
})

function makeFakeClient(requests: Array<{ method: string; params: unknown }>): WireProtocolClient {
  return {
    sendRequest: vi.fn(async (method: string, params?: unknown) => {
      requests.push({ method, params })
      if (method === 'channel/status') {
        return { channels: [] }
      }
      if (method === 'externalChannel/logs') {
        return { lines: [] }
      }
      return {}
    })
  } as unknown as WireProtocolClient
}
