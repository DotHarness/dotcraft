// @vitest-environment jsdom

import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useAppBindingStore } from '../stores/appBindingStore'

const sendRequest = vi.fn()

describe('appBindingStore', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useAppBindingStore.getState().reset()
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        appServer: { sendRequest },
        shell: { getProtocolHandlerName: vi.fn().mockResolvedValue('') }
      }
    })
  })

  it('loads apps with thread binding context and normalizes optional arrays', async () => {
    sendRequest.mockResolvedValueOnce({
      apps: [
        {
          appId: 'com.dotharness.oratorio',
          toolNamespace: 'oratorio',
          displayName: 'Oratorio',
          developerName: 'DotHarness',
          description: 'Board tools',
          pluginId: 'oratorio',
          installed: true,
          enabled: true,
          catalogVisible: true,
          connectionState: 'connected'
        }
      ]
    })

    await useAppBindingStore.getState().fetchApps('thread-1')

    expect(sendRequest).toHaveBeenCalledWith('app/list', {
      includeCatalog: true,
      includeDisabled: true,
      threadId: 'thread-1',
      forceRefresh: false,
      surface: 'sdk/default'
    })
    const [app] = useAppBindingStore.getState().apps
    expect(app?.appId).toBe('com.dotharness.oratorio')
    expect(app?.handoffModes).toEqual([])
    expect(app?.scopes).toEqual([])
    expect(app?.toolCatalog).toEqual([])
    expect(app?.dynamicToolCatalog).toEqual({ enabled: false })
    expect(app?.managed).toBe(false)
    expect(app?.requiresExternalConnection).toBe(true)
  })

  it('omits requestedTools when creating a dynamic catalog binding request', async () => {
    sendRequest.mockResolvedValueOnce({
      bindingRequestId: 'request-1',
      threadId: 'thread-1',
      appId: 'com.dotharness.dotcraft-unity',
      requestedScopes: ['unity.read'],
      state: 'pending',
      tokenExpiresAt: '2026-05-18T00:00:00Z',
      handoff: { mode: 'url', uri: 'http://127.0.0.1:39777/dotcraft/bind' }
    })

    await useAppBindingStore.getState().createBindingRequest({
      threadId: 'thread-1',
      appId: 'com.dotharness.dotcraft-unity',
      requestedScopes: ['unity.read'],
      requestedTools: undefined,
      source: 'pluginDetail'
    })

    expect(sendRequest).toHaveBeenCalledWith('app/binding/request/create', {
      threadId: 'thread-1',
      appId: 'com.dotharness.dotcraft-unity',
      requestedScopes: ['unity.read'],
      source: 'pluginDetail'
    })
  })

  it('refreshes and revokes thread bindings through AppServer RPCs', async () => {
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/appBindings/list') {
        return {
          bindings: [
            {
              bindingId: 'bind-1',
              threadId: 'thread-1',
              appId: 'com.dotharness.oratorio',
              state: 'active',
              connectionState: 'connected',
              lastChangedAt: '2026-05-16T00:00:00Z'
            }
          ]
        }
      }
      return {}
    })

    await useAppBindingStore.getState().refreshThreadBindings('thread-1', 'bind-1')
    expect(sendRequest).toHaveBeenCalledWith('thread/appBindings/refresh', {
      threadId: 'thread-1',
      bindingId: 'bind-1'
    })
    expect(useAppBindingStore.getState().bindingsByThread['thread-1']?.[0]?.grantedScopes).toEqual([])
    expect(useAppBindingStore.getState().bindingsByThread['thread-1']?.[0]?.attachedToolCount).toBe(0)

    await useAppBindingStore.getState().revokeThreadBinding('thread-1', 'bind-1', 'done')
    expect(sendRequest).toHaveBeenCalledWith('thread/appBindings/revoke', {
      threadId: 'thread-1',
      bindingId: 'bind-1',
      reason: 'done'
    })
    expect(sendRequest).toHaveBeenCalledWith('thread/appBindings/list', {
      threadId: 'thread-1',
      includeRevoked: true
    })
  })

  it('routes App Binding notifications to the relevant refresh calls', () => {
    sendRequest.mockResolvedValue({})
    useAppBindingStore.setState({ appsThreadId: 'thread-1' })

    useAppBindingStore.getState().handleNotification('thread/appBindings/changed', {
      threadId: 'thread-1'
    })

    expect(sendRequest).toHaveBeenCalledWith('thread/appBindings/list', {
      threadId: 'thread-1',
      includeRevoked: false
    })
    expect(sendRequest).toHaveBeenCalledWith('app/list', {
      includeCatalog: true,
      includeDisabled: true,
      threadId: 'thread-1',
      forceRefresh: false,
      surface: 'sdk/default'
    })
  })

  it('waits for an app connection to become connected', async () => {
    let appListCalls = 0
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'app/list') {
        appListCalls += 1
        return {
          apps: [
            {
              appId: 'com.dotharness.oratorio',
              toolNamespace: 'oratorio',
              displayName: 'Oratorio',
              developerName: 'DotHarness',
              description: 'Board tools',
              pluginId: 'oratorio',
              installed: true,
              enabled: true,
              catalogVisible: true,
              connectionState: appListCalls === 1 ? 'connecting' : 'connected'
            }
          ]
        }
      }
      return {}
    })

    const app = await useAppBindingStore
      .getState()
      .waitForConnection('com.dotharness.oratorio', { timeoutMs: 2, intervalMs: 0 })

    expect(app.connectionState).toBe('connected')
    expect(appListCalls).toBe(2)
  })

  it('rejects when a waited app connection enters error', async () => {
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'app/list') {
        return {
          apps: [
            {
              appId: 'com.dotharness.oratorio',
              toolNamespace: 'oratorio',
              displayName: 'Oratorio',
              developerName: 'DotHarness',
              description: 'Board tools',
              pluginId: 'oratorio',
              installed: true,
              enabled: true,
              catalogVisible: true,
              connectionState: 'error'
            }
          ]
        }
      }
      return {}
    })

    await expect(useAppBindingStore
      .getState()
      .waitForConnection('com.dotharness.oratorio', { timeoutMs: 1, intervalMs: 0 }))
      .rejects.toThrow('App connection failed')
  })

  it('waits for a thread binding to be active with attached tools', async () => {
    let listCalls = 0
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/appBindings/list') {
        listCalls += 1
        return {
          bindings: [
            {
              bindingRequestId: 'request-1',
              bindingId: 'binding-1',
              threadId: 'thread-1',
              appId: 'com.dotharness.oratorio',
              state: 'active',
              connectionState: 'connected',
              grantedScopes: ['board.read'],
              attachedToolCount: listCalls === 1 ? 0 : 4,
              lastChangedAt: '2026-05-16T00:00:00Z'
            }
          ]
        }
      }
      return {}
    })

    const binding = await useAppBindingStore.getState().waitForThreadBinding(
      {
        threadId: 'thread-1',
        appId: 'com.dotharness.oratorio',
        bindingRequestId: 'request-1'
      },
      { timeoutMs: 2, intervalMs: 0 }
    )

    expect(binding.state).toBe('active')
    expect(binding.attachedToolCount).toBe(4)
    expect(listCalls).toBe(2)
  })

  it('times out while a thread binding has no attached tools', async () => {
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/appBindings/list') {
        return {
          bindings: [
            {
              bindingRequestId: 'request-1',
              bindingId: 'binding-1',
              threadId: 'thread-1',
              appId: 'com.dotharness.oratorio',
              state: 'active',
              connectionState: 'connected',
              grantedScopes: ['board.read'],
              attachedToolCount: 0,
              lastChangedAt: '2026-05-16T00:00:00Z'
            }
          ]
        }
      }
      return {}
    })

    await expect(useAppBindingStore.getState().waitForThreadBinding(
      {
        threadId: 'thread-1',
        appId: 'com.dotharness.oratorio',
        bindingRequestId: 'request-1'
      },
      { timeoutMs: 1, intervalMs: 0 }
    )).rejects.toThrow('Timed out waiting for app binding')
  })
})
