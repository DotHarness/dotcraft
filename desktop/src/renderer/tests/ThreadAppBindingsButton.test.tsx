import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ThreadAppBindingsButton } from '../components/conversation/ThreadAppBindingsButton'
import { useAppBindingStore } from '../stores/appBindingStore'
import { useConnectionStore } from '../stores/connectionStore'

const sendRequest = vi.fn()
const settingsGet = vi.fn()
const shellOpenAppHandoff = vi.fn()
const shellGetProtocolHandlerName = vi.fn()

function threadBinding(state = 'active') {
  return {
    bindingId: 'binding-1',
    threadId: 'thread-1',
    appId: 'com.dotharness.oratorio',
    displayName: 'Oratorio',
    icon: 'data:image/svg+xml;base64,PHN2Zy8+',
    toolNamespace: 'oratorio',
    state,
    connectionState: 'connected',
    grantedScopes: ['board.read', 'board.manage'],
    attachedToolCount: state === 'active' ? 4 : 0,
    lastChangedAt: '2026-05-16T00:00:00Z'
  }
}

function appInfo(overrides: Record<string, unknown> = {}) {
  return {
    appId: 'com.dotharness.oratorio',
    toolNamespace: 'oratorio',
    displayName: 'Oratorio',
    developerName: 'DotHarness',
    description: 'Board tools',
    pluginId: 'oratorio',
    installed: true,
    enabled: true,
    catalogVisible: true,
    connectionState: 'connected',
    nativeApp: { displayName: 'Oratorio', protocol: 'oratorio', status: 'installed' },
    handoffModes: [],
    scopes: [
      { id: 'board.read', displayName: 'Read boards', description: 'Read cards', risk: 'read' },
      { id: 'board.manage', displayName: 'Manage boards', description: 'Manage cards', risk: 'mutate' }
    ],
    toolCatalog: [{ name: 'CreateCard', scope: 'board.manage', risk: 'mutate', defaultExposure: 'direct' }],
    dynamicToolCatalog: { enabled: false },
    ...overrides
  }
}

function socialAppInfo(overrides: Record<string, unknown> = {}) {
  return appInfo({
    appId: 'com.dotharness.channel.qq',
    toolNamespace: 'qq',
    displayName: 'QQ',
    developerName: 'DotHarness',
    description: 'Continue this thread in QQ.',
    pluginId: 'channel-qq',
    managed: true,
    requiresExternalConnection: false,
    connectionState: 'connected',
    nativeApp: { displayName: 'QQ', protocol: '', status: 'installed' },
    scopes: [
      { id: 'conversation.receive', displayName: 'Receive messages', description: 'Receive QQ messages', risk: 'read' },
      { id: 'message.send', displayName: 'Send replies', description: 'Send QQ replies', risk: 'externalWrite' }
    ],
    toolCatalog: [{ name: 'SendMessageToBoundConversation', scope: 'message.send', risk: 'externalWrite', defaultExposure: 'deferred' }],
    ...overrides
  })
}

function socialBinding(state = 'active') {
  return {
    bindingId: 'binding-social-1',
    bindingRequestId: 'bind-req-social-1',
    threadId: 'thread-1',
    appId: 'com.dotharness.channel.qq',
    displayName: 'QQ',
    toolNamespace: 'qq',
    bindingKind: 'socialChannel',
    managed: true,
    requiresExternalConnection: false,
    state,
    connectionState: 'connected',
    grantedScopes: ['conversation.receive', 'message.send'],
    attachedToolCount: 0,
    lastChangedAt: '2026-05-16T00:00:00Z',
    socialTarget: state === 'active'
      ? {
          channelName: 'qq',
          conversationKind: 'group',
          conversationId: '123456',
          deliveryTarget: 'group:123456',
          displayName: 'QQ group 123456'
        }
      : null
  }
}

describe('ThreadAppBindingsButton', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ locale: 'en' })
    shellOpenAppHandoff.mockResolvedValue(undefined)
    shellGetProtocolHandlerName.mockResolvedValue('Oratorio')
    useConnectionStore.getState().reset()
    useAppBindingStore.getState().reset()
    useConnectionStore.getState().setStatus({
      status: 'connected',
      capabilities: { appBinding: true }
    })
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/appBindings/refresh') return { bindings: [{ bindingId: 'binding-1', state: 'active', attachedToolCount: 4 }] }
      if (method === 'thread/appBindings/list') {
        return {
          bindings: [threadBinding()]
        }
      }
      return {}
    })
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet },
        appServer: { sendRequest },
        shell: {
          openAppHandoff: shellOpenAppHandoff,
          getProtocolHandlerName: shellGetProtocolHandlerName
        }
      }
    })
  })

  it('keeps header button chrome and shows logo/name/status without scope text', async () => {
    const { container } = render(
      <LocaleProvider>
        <ThreadAppBindingsButton threadId="thread-1" />
      </LocaleProvider>
    )

    const button = await screen.findByRole('button', { name: 'Apps' })
    await waitFor(() => {
      expect(sendRequest).toHaveBeenCalledWith('thread/appBindings/refresh', { threadId: 'thread-1', bindingId: undefined })
    })

    fireEvent.click(button)

    expect(await screen.findByText('Oratorio')).toBeInTheDocument()
    expect(screen.getByText('Bound')).toBeInTheDocument()
    expect(screen.queryByText('board.read, board.manage')).toBeNull()
    expect(container.querySelector('img[src^="data:image/svg+xml"]')).not.toBeNull()
    await waitFor(() => {
      expect(sendRequest).toHaveBeenCalledWith('app/list', expect.objectContaining({
        threadId: 'thread-1',
        surface: 'threadBinding'
      }))
    })
  })

  it('shows installed enabled apps for an existing thread with no bindings', async () => {
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/appBindings/refresh') return { bindings: [] }
      if (method === 'thread/appBindings/list') return { bindings: [] }
      if (method === 'app/list') return { apps: [appInfo()] }
      return {}
    })

    render(
      <LocaleProvider>
        <ThreadAppBindingsButton threadId="thread-1" />
      </LocaleProvider>
    )

    fireEvent.click(await screen.findByRole('button', { name: 'Apps' }))

    expect(await screen.findByText('Oratorio')).toBeInTheDocument()
    expect(screen.getByText('Authorized')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Bind thread' })).toBeInTheDocument()
    await waitFor(() => {
      expect(sendRequest).toHaveBeenCalledWith('app/list', {
        includeCatalog: true,
        includeDisabled: true,
        threadId: 'thread-1',
        forceRefresh: false,
        surface: 'threadBinding'
      })
    })
  })

  it('binds a connected app to the existing thread from the app picker', async () => {
    let bound = false
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/appBindings/refresh') return { bindings: bound ? [{ bindingId: 'binding-1', state: 'active', attachedToolCount: 1 }] : [] }
      if (method === 'thread/appBindings/list') return { bindings: bound ? [threadBinding('active')] : [] }
      if (method === 'app/list') return { apps: [appInfo()] }
      if (method === 'app/binding/request/create') {
        bound = true
        return {
          bindingRequestId: 'bind-req-1',
          threadId: 'thread-1',
          appId: 'com.dotharness.oratorio',
          requestedScopes: ['board.read', 'board.manage'],
          state: 'pending',
          tokenExpiresAt: '2026-05-18T00:00:00Z',
          handoff: { mode: 'customProtocol', uri: 'oratorio://dotcraft/bind?request=bind-req-1' },
          confirmation: { required: true, risk: 'mutate', message: 'Grant Oratorio access to this thread?' }
        }
      }
      return {}
    })

    render(
      <LocaleProvider>
        <ThreadAppBindingsButton threadId="thread-1" />
      </LocaleProvider>
    )

    fireEvent.click(await screen.findByRole('button', { name: 'Apps' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Bind thread' }))

    await waitFor(() => {
      expect(sendRequest).toHaveBeenCalledWith('app/binding/request/create', {
        threadId: 'thread-1',
        appId: 'com.dotharness.oratorio',
        requestedScopes: ['board.read', 'board.manage'],
        requestedTools: ['CreateCard'],
        source: 'threadMenu'
      })
      expect(shellOpenAppHandoff).toHaveBeenCalledWith('oratorio://dotcraft/bind?request=bind-req-1')
    })
  })

  it('connects an unconnected app without auto-binding the existing thread', async () => {
    let connected = false
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/appBindings/refresh') return { bindings: [] }
      if (method === 'thread/appBindings/list') return { bindings: [] }
      if (method === 'app/list') {
        return {
          apps: [
            appInfo({
              connectionState: connected ? 'connected' : 'notConnected'
            })
          ]
        }
      }
      if (method === 'app/connection/start') {
        connected = true
        return {
          connectionRequestId: 'connection-1',
          appId: 'com.dotharness.oratorio',
          state: 'connecting',
          expiresAt: '2026-05-18T00:00:00Z',
          handoff: { mode: 'customProtocol', uri: 'oratorio://dotcraft/connect?request=connection-1' }
        }
      }
      return {}
    })

    render(
      <LocaleProvider>
        <ThreadAppBindingsButton threadId="thread-1" />
      </LocaleProvider>
    )

    fireEvent.click(await screen.findByRole('button', { name: 'Apps' }))
    expect(await screen.findByRole('button', { name: 'Connect' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Connect' }))

    await waitFor(() => {
      expect(shellOpenAppHandoff).toHaveBeenCalledWith('oratorio://dotcraft/connect?request=connection-1')
      expect(screen.getByRole('button', { name: 'Bind thread' })).toBeInTheDocument()
    })
    expect(sendRequest).not.toHaveBeenCalledWith('app/binding/request/create', expect.anything())
  })

  it('binds managed Teams without external connection or waiting confirmation copy', async () => {
    let bound = false
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/appBindings/refresh') return { bindings: bound ? [{ bindingId: 'binding-teams', state: 'active', attachedToolCount: 1 }] : [] }
      if (method === 'thread/appBindings/list') {
        return {
          bindings: bound
            ? [{
                ...threadBinding('active'),
                bindingId: 'binding-teams',
                appId: 'com.dotharness.dotcraft-teams',
                displayName: 'Agent Teams',
                managed: true,
                requiresExternalConnection: false,
                attachedToolCount: 1
              }]
            : []
        }
      }
      if (method === 'app/list') {
        return {
          apps: [
            appInfo({
              appId: 'com.dotharness.dotcraft-teams',
              toolNamespace: 'teams',
              displayName: 'Agent Teams',
              pluginId: 'agent-teams',
              managed: true,
              requiresExternalConnection: false,
              connectionState: 'connected',
              nativeApp: { displayName: 'Agent Teams', protocol: '', status: 'installed' },
              scopes: [{ id: 'teams.mission', displayName: 'Create missions', description: 'Create missions', risk: 'mutate' }],
              toolCatalog: [{ name: 'CreateTeam', scope: 'teams.mission', risk: 'mutate', defaultExposure: 'direct' }]
            })
          ]
        }
      }
      if (method === 'app/binding/request/create') {
        bound = true
        return {
          bindingRequestId: 'binding-teams',
          threadId: 'thread-1',
          appId: 'com.dotharness.dotcraft-teams',
          requestedScopes: ['teams.mission'],
          state: 'active',
          tokenExpiresAt: '2026-05-18T00:00:00Z',
          handoff: { mode: 'managed' },
          confirmation: { required: false, risk: 'mutate', message: '' }
        }
      }
      return {}
    })

    render(
      <LocaleProvider>
        <ThreadAppBindingsButton threadId="thread-1" />
      </LocaleProvider>
    )

    fireEvent.click(await screen.findByRole('button', { name: 'Apps' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Bind thread' }))

    await waitFor(() => {
      expect(sendRequest).toHaveBeenCalledWith('app/binding/request/create', expect.objectContaining({
        appId: 'com.dotharness.dotcraft-teams',
        requestedTools: ['CreateTeam'],
        source: 'threadMenu'
      }))
    })
    await waitFor(() => {
      expect(screen.getByText('Bound')).toBeInTheDocument()
    })
    expect(screen.queryByRole('button', { name: 'Bind thread' })).toBeNull()
    expect(sendRequest).not.toHaveBeenCalledWith('app/connection/start', expect.anything())
    expect(shellOpenAppHandoff).not.toHaveBeenCalled()
    expect(screen.queryByText('Waiting for confirmation in the app')).toBeNull()
  })

  it('shows offline thread bindings as open-app-to-use without connected copy', async () => {
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/appBindings/refresh') return { bindings: [{ bindingId: 'binding-1', state: 'offline', attachedToolCount: 0 }] }
      if (method === 'thread/appBindings/list') return { bindings: [threadBinding('offline')] }
      if (method === 'app/connection/start') {
        return {
          connectionRequestId: 'connection-1',
          appId: 'com.dotharness.oratorio',
          state: 'connecting',
          expiresAt: '2026-05-18T00:00:00Z',
          handoff: { mode: 'customProtocol', uri: 'oratorio://dotcraft/connect?request=connection-1' }
        }
      }
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
              connectionState: 'connected',
              nativeApp: { displayName: 'Oratorio', protocol: 'oratorio', status: 'installed' },
              handoffModes: [],
              scopes: [],
              toolCatalog: []
            }
          ]
        }
      }
      return {}
    })

    render(
      <LocaleProvider>
        <ThreadAppBindingsButton threadId="thread-1" />
      </LocaleProvider>
    )

    fireEvent.click(await screen.findByRole('button', { name: 'Apps' }))

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Open app' })).toBeInTheDocument()
    })
    expect(screen.queryByText('Offline')).not.toBeInTheDocument()
    expect(screen.queryByText('Connected')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Open app' }))

    await waitFor(() => {
      expect(shellOpenAppHandoff).toHaveBeenCalledWith('oratorio://dotcraft/connect?request=connection-1')
      expect(sendRequest).toHaveBeenCalledWith('app/connection/start', {
        appId: 'com.dotharness.oratorio',
        handoffMode: undefined
      })
      expect(sendRequest).toHaveBeenCalledWith('thread/appBindings/refresh', {
        threadId: 'thread-1',
        bindingId: 'binding-1'
      })
    })
  })

  it('does not render external open action for managed Teams bindings', async () => {
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/appBindings/refresh') return { bindings: [{ bindingId: 'binding-1', state: 'offline', attachedToolCount: 1 }] }
      if (method === 'thread/appBindings/list') {
        return {
          bindings: [
            {
              ...threadBinding('offline'),
              appId: 'com.dotharness.dotcraft-teams',
              displayName: 'Agent Teams',
              managed: true,
              requiresExternalConnection: false,
              attachedToolCount: 1
            }
          ]
        }
      }
      return {}
    })

    render(
      <LocaleProvider>
        <ThreadAppBindingsButton threadId="thread-1" />
      </LocaleProvider>
    )

    fireEvent.click(await screen.findByRole('button', { name: 'Apps' }))

    expect(await screen.findByText('Agent Teams')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Open app' })).toBeNull()
  })

  it('hides managed Teams mission-thread role bindings from the ordinary app switcher', async () => {
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/appBindings/refresh') return { bindings: [{ bindingId: 'binding-1', state: 'active', attachedToolCount: 7 }] }
      if (method === 'thread/appBindings/list') {
        return {
          bindings: [
            {
              ...threadBinding('active'),
              appId: 'com.dotharness.dotcraft-teams',
              displayName: 'Agent Teams',
              managed: true,
              requiresExternalConnection: false,
              attachedToolCount: 7
            }
          ]
        }
      }
      if (method === 'app/list') {
        return {
          apps: [
            appInfo({
              appId: 'com.dotharness.dotcraft-teams',
              toolNamespace: 'teams',
              displayName: 'Agent Teams',
              pluginId: 'agent-teams',
              managed: true,
              requiresExternalConnection: false,
              connectionState: 'connected',
              bindingSummary: {
                threadId: 'thread-1',
                bindingId: 'binding-1',
                appId: 'com.dotharness.dotcraft-teams',
                displayName: 'Agent Teams',
                state: 'active',
                connectionState: 'connected',
                managed: true,
                requiresExternalConnection: false,
                grantedScopes: ['teams.role']
              }
            })
          ]
        }
      }
      return {}
    })

    render(
      <LocaleProvider>
        <ThreadAppBindingsButton threadId="thread-1" />
      </LocaleProvider>
    )

    fireEvent.click(await screen.findByRole('button', { name: 'Apps' }))

    await waitFor(() => {
      expect(screen.getByText('No apps bound')).toBeInTheDocument()
      expect(screen.queryByText('Agent Teams')).toBeNull()
    })
  })

  it('starts a social binding request and keeps bind instructions visible while pending', async () => {
    let bindingState: string | null = null
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/appBindings/refresh') return { bindings: bindingState ? [socialBinding(bindingState)] : [] }
      if (method === 'thread/appBindings/list') return { bindings: bindingState ? [socialBinding(bindingState)] : [] }
      if (method === 'app/list') return { apps: [socialAppInfo()] }
      if (method === 'app/binding/request/create') {
        bindingState = 'pending'
        return {
          bindingRequestId: 'bind-req-social-1',
          threadId: 'thread-1',
          appId: 'com.dotharness.channel.qq',
          requestedScopes: ['conversation.receive', 'message.send'],
          state: 'pending',
          tokenExpiresAt: '2026-05-18T00:00:00Z',
          handoff: {
            mode: 'bindCode',
            bindCode: 'DTC-482913',
            instructions: 'Send /bind DTC-482913 in the QQ conversation to bind it to this thread.'
          }
        }
      }
      return {}
    })

    render(
      <LocaleProvider>
        <ThreadAppBindingsButton threadId="thread-1" />
      </LocaleProvider>
    )

    fireEvent.click(await screen.findByRole('button', { name: 'Apps' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Bind thread' }))

    await waitFor(() => {
      expect(sendRequest).toHaveBeenCalledWith('app/binding/request/create', expect.objectContaining({
        appId: 'com.dotharness.channel.qq',
        bindingKind: 'socialChannel',
        requestedScopes: ['conversation.receive', 'message.send'],
        requestedTools: ['SendMessageToBoundConversation'],
        socialIntent: {
          channelName: 'qq',
          targetSelection: 'confirmInChannel',
          displayHint: 'QQ'
        }
      }))
    })
    expect(await screen.findByText('Pending')).toBeInTheDocument()
    expect(screen.getByText('Send /bind DTC-482913 in the QQ conversation to bind it to this thread.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Cancel' })).toBeInTheDocument()
    expect(shellOpenAppHandoff).not.toHaveBeenCalled()
  })

  it('shows the active social target and can refresh or revoke it', async () => {
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/appBindings/refresh') return { bindings: [socialBinding('active')] }
      if (method === 'thread/appBindings/list') return { bindings: [socialBinding('active')] }
      if (method === 'app/list') return { apps: [socialAppInfo({ bindingSummary: socialBinding('active') })] }
      if (method === 'thread/appBindings/revoke') return {}
      return {}
    })

    render(
      <LocaleProvider>
        <ThreadAppBindingsButton threadId="thread-1" />
      </LocaleProvider>
    )

    fireEvent.click(await screen.findByRole('button', { name: 'Apps' }))

    await waitFor(() => {
      expect(screen.getByText('QQ group 123456').textContent).toBe('QQ group 123456')
    })

    const refreshButtons = screen.getAllByRole('button', { name: 'Refresh' })
    fireEvent.click(refreshButtons[refreshButtons.length - 1])
    await waitFor(() => {
      expect(sendRequest).toHaveBeenCalledWith('thread/appBindings/refresh', {
        threadId: 'thread-1',
        bindingId: 'binding-social-1'
      })
    })

    fireEvent.click(screen.getByRole('button', { name: 'Revoke' }))
    await waitFor(() => {
      expect(sendRequest).toHaveBeenCalledWith('thread/appBindings/revoke', {
        threadId: 'thread-1',
        bindingId: 'binding-social-1',
        reason: undefined
      })
    })
  })
})
