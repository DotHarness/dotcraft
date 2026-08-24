// @vitest-environment jsdom

import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useAppBindingStore } from '../stores/appBindingStore'
import { installDesktopApiMock } from './desktopApiMock'

const sendRequest = vi.fn()

describe('appBindingStore', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useAppBindingStore.getState().reset()
    installDesktopApiMock({
        appServer: { sendRequest },
        shell: { getProtocolHandlerName: vi.fn().mockResolvedValue('') }
      })
  })

  it('loads apps with thread binding context and normalizes optional arrays', async () => {
    sendRequest.mockResolvedValueOnce({
      apps: [
        {
          appId: 'com.example.workflow',
          displayName: 'Workflow App',
          developerName: 'Example Labs',
          description: 'Board tools',
          pluginId: 'workflow',
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
    expect(app?.appId).toBe('com.example.workflow')
    expect(app?.handoffModes).toEqual([])
    expect(app?.managed).toBe(false)
    expect(app?.requiresExternalConnection).toBe(true)
  })

  it('enables a whole app without tool-selection fields', async () => {
    sendRequest.mockResolvedValueOnce({
      bindingRequestId: 'request-1',
      bindingId: 'binding-1',
      state: 'connecting',
      expiresAt: '2026-05-18T00:00:00Z',
      handoff: { mode: 'url', uri: 'http://127.0.0.1:39777/dotcraft/bind' }
    })

    const result = await useAppBindingStore.getState().createBindingRequest({
      threadId: 'thread-1',
      appId: 'com.example.dynamic-tools',
      source: 'pluginDetail'
    })

    expect(result.bindingId).toBe('binding-1')
    expect(sendRequest).toHaveBeenCalledWith('thread/appBindings/enable', {
      threadId: 'thread-1',
      appId: 'com.example.dynamic-tools'
    })
  })

  it('cancels a newly created binding directly when its binding id is known', async () => {
    sendRequest.mockResolvedValue({ bindings: [] })

    await useAppBindingStore.getState().cancelBindingRequest(
      'thread-1',
      'request-1',
      'activation_failed',
      'binding-1'
    )

    expect(sendRequest.mock.calls[0]).toEqual([
      'thread/appBindings/revoke',
      { threadId: 'thread-1', bindingId: 'binding-1', reason: 'activation_failed' }
    ])
  })

  it('refreshes and revokes thread bindings through AppServer RPCs', async () => {
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/appBindings/list') {
        return {
          bindings: [
            {
              bindingId: 'bind-1',
              threadId: 'thread-1',
              appId: 'com.example.workflow',
              state: 'active',
              authorityRevision: 1,
              approvedCapabilityRevision: 1
            }
          ]
        }
      }
      return {}
    })

    await useAppBindingStore.getState().refreshThreadBindings('thread-1', 'bind-1')
    expect(sendRequest).toHaveBeenCalledWith('thread/appBindings/list', {
      threadId: 'thread-1',
      includeRevoked: false
    })
    expect(useAppBindingStore.getState().bindingsByThread['thread-1']?.[0]?.approvedTools).toEqual([])
    expect(useAppBindingStore.getState().bindingsByThread['thread-1']?.[0]?.pendingChanges).toEqual([])

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
              appId: 'com.example.workflow',
              displayName: 'Workflow App',
              developerName: 'Example Labs',
              description: 'Board tools',
              pluginId: 'workflow',
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
      .waitForConnection('com.example.workflow', { timeoutMs: 2, intervalMs: 0 })

    expect(app.connectionState).toBe('connected')
    expect(appListCalls).toBe(2)
  })

  it('rejects when a waited app connection enters error', async () => {
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'app/list') {
        return {
          apps: [
            {
              appId: 'com.example.workflow',
              displayName: 'Workflow App',
              developerName: 'Example Labs',
              description: 'Board tools',
              pluginId: 'workflow',
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
      .waitForConnection('com.example.workflow', { timeoutMs: 1, intervalMs: 0 }))
      .rejects.toThrow('App connection failed')
  })

  it('waits for a thread binding to become active', async () => {
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
              appId: 'com.example.workflow',
              state: 'active',
              authorityRevision: 1,
              approvedCapabilityRevision: 1
            }
          ]
        }
      }
      return {}
    })

    const binding = await useAppBindingStore.getState().waitForThreadBinding(
      {
        threadId: 'thread-1',
        appId: 'com.example.workflow',
        bindingRequestId: 'request-1'
      },
      { timeoutMs: 2, intervalMs: 0 }
    )

    expect(binding.state).toBe('active')
    expect(binding.state).toBe('active')
    expect(listCalls).toBe(1)
  })

  it('treats an active social-channel binding as ready', async () => {
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/appBindings/list') {
        return {
          bindings: [
            {
              bindingRequestId: 'request-social-1',
              bindingId: 'binding-social-1',
              threadId: 'thread-1',
              appId: 'com.dotharness.channel.qq',
              bindingKind: 'socialChannel',
              state: 'active',
              authorityRevision: 3,
              approvedCapabilityRevision: 1,
              socialTarget: {
                channelName: 'qq',
                conversationKind: 'group',
                conversationId: '123456',
                deliveryTarget: 'group:123456',
                displayName: 'QQ group 123456'
              }
            }
          ]
        }
      }
      return {}
    })

    const binding = await useAppBindingStore.getState().waitForThreadBinding(
      {
        threadId: 'thread-1',
        appId: 'com.dotharness.channel.qq',
        bindingRequestId: 'request-social-1'
      },
      { timeoutMs: 1, intervalMs: 0 }
    )

    expect(binding.state).toBe('active')
    expect(binding.authorityRevision).toBe(3)
    expect(binding.socialTarget?.displayName).toBe('QQ group 123456')
  })

  it('treats an active MCP-backed binding as ready without attachment counts', async () => {
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/appBindings/list') {
        return {
          bindings: [
            {
              bindingRequestId: 'request-1',
              bindingId: 'binding-1',
              threadId: 'thread-1',
              appId: 'com.example.workflow',
              state: 'active',
              authorityRevision: 1,
              approvedCapabilityRevision: 1
            }
          ]
        }
      }
      return {}
    })

    await expect(useAppBindingStore.getState().waitForThreadBinding(
      {
        threadId: 'thread-1',
        appId: 'com.example.workflow',
        bindingRequestId: 'request-1'
      },
      { timeoutMs: 1, intervalMs: 0 }
    )).resolves.toMatchObject({ state: 'active', bindingId: 'binding-1' })
  })
})
