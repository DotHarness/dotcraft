// @vitest-environment jsdom

import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useConnectionStore } from '../stores/connectionStore'
import { useThreadRouteStore } from '../stores/threadRouteStore'
import { useThreadStore } from '../stores/threadStore'
import { installDesktopApiMock } from './desktopApiMock'

const sendRequest = vi.fn()
const settingsGet = vi.fn()
const settingsSet = vi.fn()

const THREAD_ID = 'thread_1'
const WORKSPACE_PATH = 'X:\\fixtures\\workspace'
const MEMORY_KEY = `${WORKSPACE_PATH}::${THREAD_ID}`

function host(overrides?: { online?: boolean; available?: boolean; busyOwner?: string }): unknown {
  return {
    hostId: 'sat_studio',
    displayName: 'Studio PC',
    online: overrides?.online ?? true,
    workspaces: [
      {
        workspaceId: 'ws_shaders',
        displayName: 'shaders',
        available: overrides?.available ?? true,
        ...(overrides?.busyOwner ? { busyOwner: overrides.busyOwner } : {})
      }
    ]
  }
}

function rememberedRoute(): Record<string, unknown> {
  return {
    satelliteRouteByThread: {
      [MEMORY_KEY]: { hostId: 'sat_studio', workspaceId: 'ws_shaders', at: new Date().toISOString() }
    }
  }
}

/** The re-apply is debounced by 400 ms, then awaits settings, list, and connect. */
async function settle(): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 500))
}

describe('threadRouteStore', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useThreadRouteStore.setState({
      supported: false,
      hosts: [],
      routes: {},
      pendingRoute: null,
      connecting: null,
      attempted: new Set<string>(),
      generation: 0
    })
    useConnectionStore.setState({ capabilities: { remoteToolHost: true } })
    useThreadStore.getState().reset()
    useThreadStore.getState().setActiveThread({
      id: THREAD_ID,
      displayName: null,
      status: 'active',
      originChannel: 'dotcraft-desktop',
      createdAt: new Date().toISOString(),
      lastActiveAt: new Date().toISOString(),
      workspacePath: WORKSPACE_PATH,
      userId: 'local',
      metadata: {},
      configuration: {},
      turns: []
    })

    settingsGet.mockResolvedValue(rememberedRoute())
    settingsSet.mockResolvedValue(undefined)
    sendRequest.mockImplementation(async (method: string, params: Record<string, unknown>) => {
      if (method === 'remoteToolHost/list') return { hosts: [host()], route: null }
      if (method === 'remoteToolHost/connect') {
        return {
          route: {
            threadId: params.threadId,
            hostId: params.hostId,
            workspaceId: params.workspaceId,
            status: 'connected'
          },
          matchedTools: [],
          unavailableTools: []
        }
      }
      return { disconnected: true }
    })

    installDesktopApiMock({
      appServer: { sendRequest },
      settings: { get: settingsGet, set: settingsSet }
    })
  })

  it('re-applies the remembered machine once per thread and connection', async () => {
    useThreadRouteStore.getState().maybeReapply(THREAD_ID)
    await settle()

    const connects = sendRequest.mock.calls.filter(([method]) => method === 'remoteToolHost/connect')
    expect(connects).toHaveLength(1)
    expect(connects[0][1]).toEqual({
      threadId: THREAD_ID,
      hostId: 'sat_studio',
      workspaceId: 'ws_shaders'
    })
    expect(useThreadRouteStore.getState().routes[THREAD_ID]?.hostId).toBe('sat_studio')

    useThreadRouteStore.setState({ routes: {} })
    useThreadRouteStore.getState().maybeReapply(THREAD_ID)
    await settle()

    expect(sendRequest.mock.calls.filter(([method]) => method === 'remoteToolHost/connect')).toHaveLength(1)
  })

  it('re-arms the single attempt when the AppServer connection is replaced', async () => {
    useThreadRouteStore.getState().maybeReapply(THREAD_ID)
    await settle()

    useThreadRouteStore.getState().resetForConnection()
    useThreadRouteStore.getState().maybeReapply(THREAD_ID)
    await settle()

    expect(sendRequest.mock.calls.filter(([method]) => method === 'remoteToolHost/connect')).toHaveLength(2)
  })

  it('stays out of the way while a turn is running', async () => {
    useThreadRouteStore.getState().maybeReapply(THREAD_ID, { turnRunning: true })
    await settle()

    expect(sendRequest).not.toHaveBeenCalled()
    expect(useThreadRouteStore.getState().attempted.size).toBe(0)
  })

  it('does not connect to an offline machine', async () => {
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'remoteToolHost/list') return { hosts: [host({ online: false })], route: null }
      throw new Error('unexpected call')
    })

    useThreadRouteStore.getState().maybeReapply(THREAD_ID)
    await settle()

    expect(sendRequest.mock.calls.filter(([method]) => method === 'remoteToolHost/connect')).toHaveLength(0)
    expect(useThreadRouteStore.getState().routes[THREAD_ID]).toBeUndefined()
  })

  it('does not connect to a folder another agent holds', async () => {
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'remoteToolHost/list') {
        return { hosts: [host({ available: false, busyOwner: 'other' })], route: null }
      }
      throw new Error('unexpected call')
    })

    useThreadRouteStore.getState().maybeReapply(THREAD_ID)
    await settle()

    expect(sendRequest.mock.calls.filter(([method]) => method === 'remoteToolHost/connect')).toHaveLength(0)
  })

  it('stays silent when the re-applied connect fails', async () => {
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'remoteToolHost/list') return { hosts: [host()], route: null }
      throw new Error('That machine is offline.')
    })

    useThreadRouteStore.getState().maybeReapply(THREAD_ID)
    await settle()

    expect(useThreadRouteStore.getState().routes[THREAD_ID]).toBeUndefined()
    expect(useThreadRouteStore.getState().connecting).toBeNull()
  })

  it('remembers an explicit connect and forgets it on an explicit This PC', async () => {
    settingsGet.mockResolvedValue({ satelliteRouteByThread: {} })
    await useThreadRouteStore.getState().connect(THREAD_ID, 'sat_studio', 'ws_shaders')

    const written = settingsSet.mock.calls[0][0] as {
      satelliteRouteByThread: Record<string, { hostId: string; workspaceId: string }>
    }
    expect(written.satelliteRouteByThread[MEMORY_KEY]).toMatchObject({
      hostId: 'sat_studio',
      workspaceId: 'ws_shaders'
    })

    settingsGet.mockResolvedValue(rememberedRoute())
    await useThreadRouteStore.getState().disconnect(THREAD_ID)

    const cleared = settingsSet.mock.calls[1][0] as { satelliteRouteByThread: Record<string, unknown> }
    expect(cleared.satelliteRouteByThread).not.toHaveProperty(MEMORY_KEY)
    expect(useThreadRouteStore.getState().routes[THREAD_ID]).toBeUndefined()
  })

  it('applies the welcome composer choice to the thread the first message creates', async () => {
    settingsGet.mockResolvedValue({ satelliteRouteByThread: {} })
    useThreadRouteStore.getState().setPendingRoute({ hostId: 'sat_studio', workspaceId: 'ws_shaders' })

    const failure = await useThreadRouteStore.getState().applyPendingRoute(THREAD_ID)

    expect(failure).toBeNull()
    expect(sendRequest).toHaveBeenCalledWith('remoteToolHost/connect', {
      threadId: THREAD_ID,
      hostId: 'sat_studio',
      workspaceId: 'ws_shaders'
    })
    expect(useThreadRouteStore.getState().routes[THREAD_ID]?.hostId).toBe('sat_studio')
    expect(useThreadRouteStore.getState().pendingRoute).toBeNull()
  })

  it('names the machine when the applied choice is refused, and keeps no pending route', async () => {
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'remoteToolHost/list') return { hosts: [host()], route: null }
      throw new Error('This folder is already in use.')
    })
    await useThreadRouteStore.getState().list(THREAD_ID)
    useThreadRouteStore.getState().setPendingRoute({ hostId: 'sat_studio', workspaceId: 'ws_shaders' })

    const failure = await useThreadRouteStore.getState().applyPendingRoute(THREAD_ID)

    expect(failure?.hostName).toBe('Studio PC')
    expect(useThreadRouteStore.getState().routes[THREAD_ID]).toBeUndefined()
    expect(useThreadRouteStore.getState().pendingRoute).toBeNull()
  })

  it('drops the pending route when the AppServer connection is replaced', () => {
    useThreadRouteStore.getState().setPendingRoute({ hostId: 'sat_studio', workspaceId: 'ws_shaders' })

    useThreadRouteStore.getState().resetForConnection()

    expect(useThreadRouteStore.getState().pendingRoute).toBeNull()
  })

  it('applies a route/changed notification and clears the route on disconnect', () => {
    useThreadRouteStore.getState().handleRouteChanged({
      threadId: THREAD_ID,
      reason: 'connected',
      route: {
        threadId: THREAD_ID,
        hostId: 'sat_studio',
        workspaceId: 'ws_shaders',
        status: 'connected'
      }
    })
    expect(useThreadRouteStore.getState().routes[THREAD_ID]?.workspaceId).toBe('ws_shaders')

    useThreadRouteStore.getState().handleRouteChanged({
      threadId: THREAD_ID,
      reason: 'disconnected',
      route: null
    })
    expect(useThreadRouteStore.getState().routes[THREAD_ID]).toBeUndefined()
  })
})
