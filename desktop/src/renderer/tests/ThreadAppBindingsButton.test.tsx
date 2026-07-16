import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ThreadAppBindingsButton } from '../components/conversation/ThreadAppBindingsButton'
import { useAppBindingStore } from '../stores/appBindingStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useToastStore } from '../stores/toastStore'

const sendRequest = vi.fn()
const settingsGet = vi.fn()
const shellOpenAppHandoff = vi.fn()
const shellGetProtocolHandlerName = vi.fn()

function threadBinding(state = 'active') {
  return {
    bindingId: 'binding-1',
    threadId: 'thread-1',
    appId: 'com.example.workflow',
    displayName: 'Workflow App',
    icon: 'data:image/svg+xml;base64,PHN2Zy8+',
    toolNamespace: 'workflow',
    state,
    connectionState: 'connected',
    grantedScopes: ['board.read', 'board.manage'],
    attachedToolCount: state === 'active' ? 4 : 0,
    lastChangedAt: '2026-05-16T00:00:00Z'
  }
}

function appInfo(overrides: Record<string, unknown> = {}) {
  return {
    appId: 'com.example.workflow',
    toolNamespace: 'workflow',
    displayName: 'Workflow App',
    developerName: 'Example Labs',
    description: 'Board tools',
    pluginId: 'workflow',
    installed: true,
    enabled: true,
    catalogVisible: true,
    connectionState: 'connected',
    nativeApp: { displayName: 'Workflow App', protocol: 'workflow', status: 'installed' },
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
    developerName: 'Example Labs',
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
    toolCatalog: [{ name: 'QQSendImageToCurrentChat', scope: 'message.send', risk: 'externalWrite', defaultExposure: 'direct' }],
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
    shellGetProtocolHandlerName.mockResolvedValue('Workflow App')
    useConnectionStore.getState().reset()
    useAppBindingStore.getState().reset()
    useToastStore.setState({ toasts: [] })
    useConnectionStore.getState().setStatus({
      status: 'connected',
      capabilities: { appBindingVersion: 2 }
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
      expect(sendRequest).toHaveBeenCalledWith('thread/appBindings/list', { threadId: 'thread-1', includeRevoked: false })
    })

    fireEvent.click(button)

    expect(await screen.findByText('Workflow App')).toBeInTheDocument()
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

    expect(await screen.findByText('Workflow App')).toBeInTheDocument()
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
      if (method === 'thread/appBindings/enable') {
        bound = true
        return {
          bindingRequestId: 'bind-req-1',
          threadId: 'thread-1',
          appId: 'com.example.workflow',
          requestedScopes: ['board.read', 'board.manage'],
          state: 'connecting',
          tokenExpiresAt: '2026-05-18T00:00:00Z',
          handoff: { mode: 'customProtocol', uri: 'workflow://dotcraft/bind?request=bind-req-1' },
          confirmation: { required: true, risk: 'mutate', message: 'Grant Workflow App access to this thread?' }
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
      expect(sendRequest).toHaveBeenCalledWith('thread/appBindings/enable', {
        threadId: 'thread-1',
        appId: 'com.example.workflow'
      })
      expect(shellOpenAppHandoff).toHaveBeenCalledWith('workflow://dotcraft/bind?request=bind-req-1')
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
          appId: 'com.example.workflow',
          state: 'connecting',
          expiresAt: '2026-05-18T00:00:00Z',
          handoff: { mode: 'customProtocol', uri: 'workflow://dotcraft/connect?request=connection-1' }
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
      expect(shellOpenAppHandoff).toHaveBeenCalledWith('workflow://dotcraft/connect?request=connection-1')
      expect(screen.getByRole('button', { name: 'Bind thread' })).toBeInTheDocument()
    })
    expect(sendRequest).not.toHaveBeenCalledWith('app/binding/request/create', expect.anything())
  })

  it('shows offline thread bindings as open-app-to-use without connected copy', async () => {
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/appBindings/refresh') return { bindings: [{ bindingId: 'binding-1', state: 'offline', attachedToolCount: 0 }] }
      if (method === 'thread/appBindings/list') return { bindings: [threadBinding('offline')] }
      if (method === 'app/connection/start') {
        return {
          connectionRequestId: 'connection-1',
          appId: 'com.example.workflow',
          state: 'connecting',
          expiresAt: '2026-05-18T00:00:00Z',
          handoff: { mode: 'customProtocol', uri: 'workflow://dotcraft/connect?request=connection-1' }
        }
      }
      if (method === 'app/list') {
        return {
          apps: [
            {
              appId: 'com.example.workflow',
              toolNamespace: 'workflow',
              displayName: 'Workflow App',
              developerName: 'Example Labs',
              description: 'Board tools',
              pluginId: 'workflow',
              installed: true,
              enabled: true,
              catalogVisible: true,
              connectionState: 'connected',
              nativeApp: { displayName: 'Workflow App', protocol: 'workflow', status: 'installed' },
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
      expect(shellOpenAppHandoff).toHaveBeenCalledWith('workflow://dotcraft/connect?request=connection-1')
      expect(sendRequest).toHaveBeenCalledWith('app/connection/start', {
        appId: 'com.example.workflow',
        handoffMode: undefined
      })
      expect(sendRequest).toHaveBeenCalledWith('thread/appBindings/list', {
        threadId: 'thread-1',
        includeRevoked: false
      })
    })
  })

  it('does not render an external open action for managed bindings', async () => {
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/appBindings/refresh') return { bindings: [{ bindingId: 'binding-1', state: 'offline', attachedToolCount: 1 }] }
      if (method === 'thread/appBindings/list') {
        return {
          bindings: [
            {
              ...threadBinding('offline'),
              appId: 'com.example.managed',
              displayName: 'Managed Workflow',
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

    expect(await screen.findByText('Managed Workflow')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Open app' })).toBeNull()
  })

  it('starts a social binding request and keeps bind instructions visible while pending', async () => {
    let bindingState: string | null = null
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/appBindings/refresh') return { bindings: bindingState ? [socialBinding(bindingState)] : [] }
      if (method === 'thread/appBindings/list') return { bindings: bindingState ? [socialBinding(bindingState)] : [] }
      if (method === 'app/list') return { apps: [socialAppInfo()] }
      if (method === 'thread/socialBindings/request/create') {
        bindingState = 'connecting'
        return {
          bindingRequestId: 'bind-req-social-1',
          threadId: 'thread-1',
          appId: 'com.dotharness.channel.qq',
          requestedScopes: ['conversation.receive', 'message.send'],
          state: 'connecting',
          tokenExpiresAt: '2026-05-18T00:00:00Z',
          handoff: {
            mode: 'bindCode',
            bindCode: '482913',
            instructions: 'Send /bind 482913 in the QQ conversation to bind it to this thread.'
          }
        }
      }
      if (method === 'thread/appBindings/revoke') {
        bindingState = null
        return {
          bindingRequestId: 'bind-req-social-1',
          threadId: 'thread-1',
          appId: 'com.dotharness.channel.qq',
          state: 'cancelled'
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
      expect(sendRequest).toHaveBeenCalledWith('thread/socialBindings/request/create', {
        threadId: 'thread-1',
        channelName: 'qq'
      })
    })
    expect(await screen.findByText('Pending')).toBeInTheDocument()
    expect(screen.getByText('Send /bind 482913 in the QQ conversation to bind it to this thread.')).toBeInTheDocument()
    expect(useToastStore.getState().toasts.some((toast) => toast.message.includes('/bind 482913'))).toBe(false)
    expect(screen.getAllByRole('button', { name: 'Refresh' })).toHaveLength(1)
    expect(sendRequest).not.toHaveBeenCalledWith('thread/appBindings/refresh', {
      threadId: 'thread-1',
      bindingId: 'bind-req-social-1'
    })
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))
    await waitFor(() => {
      expect(sendRequest).toHaveBeenCalledWith('thread/appBindings/revoke', {
        threadId: 'thread-1',
        bindingId: 'binding-social-1',
        reason: undefined
      })
    })
    await waitFor(() => {
      expect(useToastStore.getState().toasts.some((toast) => toast.message === 'App binding request canceled')).toBe(true)
    })
    expect(useToastStore.getState().toasts.some((toast) => toast.message === 'App binding revoked')).toBe(false)
    expect(shellOpenAppHandoff).not.toHaveBeenCalled()
  })

  it('clears social bind instructions when the pending request disappears on refresh', async () => {
    let binding: ReturnType<typeof socialBinding> | null = null
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/appBindings/refresh') return { bindings: binding ? [binding] : [] }
      if (method === 'thread/appBindings/list') return { bindings: binding ? [binding] : [] }
      if (method === 'app/list') return { apps: [socialAppInfo()] }
      if (method === 'thread/socialBindings/request/create') {
        binding = socialBinding('connecting')
        return {
          bindingRequestId: 'bind-req-social-1',
          threadId: 'thread-1',
          appId: 'com.dotharness.channel.qq',
          requestedScopes: ['conversation.receive', 'message.send'],
          state: 'connecting',
          tokenExpiresAt: '2026-05-18T00:00:00Z',
          handoff: {
            mode: 'bindCode',
            bindCode: '482913',
            instructions: 'Send /bind 482913 in the QQ conversation to bind it to this thread.'
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
    expect(await screen.findByText('Send /bind 482913 in the QQ conversation to bind it to this thread.')).toBeInTheDocument()

    binding = null
    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))

    await waitFor(() => {
      expect(screen.queryByText('Send /bind 482913 in the QQ conversation to bind it to this thread.')).toBeNull()
      expect(screen.getByRole('button', { name: 'Bind thread' })).toBeInTheDocument()
    })
  })

  it('does not reuse a stale social bind handoff for another pending request on the same app', async () => {
    let binding: ReturnType<typeof socialBinding> | null = null
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/appBindings/refresh') return { bindings: binding ? [binding] : [] }
      if (method === 'thread/appBindings/list') return { bindings: binding ? [binding] : [] }
      if (method === 'app/list') return { apps: [socialAppInfo()] }
      if (method === 'thread/socialBindings/request/create') {
        binding = {
          ...socialBinding('connecting'),
          bindingId: 'binding-social-2',
          bindingRequestId: 'bind-req-social-2'
        }
        return {
          bindingRequestId: 'bind-req-social-1',
          threadId: 'thread-1',
          appId: 'com.dotharness.channel.qq',
          requestedScopes: ['conversation.receive', 'message.send'],
          state: 'connecting',
          tokenExpiresAt: '2026-05-18T00:00:00Z',
          handoff: {
            mode: 'bindCode',
            bindCode: '482913',
            instructions: 'Send /bind 482913 in the QQ conversation to bind it to this thread.'
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
      expect(screen.getByText('Pending')).toBeInTheDocument()
      expect(screen.queryByText('Send /bind 482913 in the QQ conversation to bind it to this thread.')).toBeNull()
    })
  })

  it('hides offline social channels without thread bindings', async () => {
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/appBindings/refresh') return { bindings: [] }
      if (method === 'thread/appBindings/list') return { bindings: [] }
      if (method === 'app/list') return { apps: [socialAppInfo({ connectionState: 'notConnected' })] }
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
      expect(screen.queryByText('QQ')).toBeNull()
    })
    expect(screen.queryByText('Not connected')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Bind thread' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Connect' })).toBeNull()
  })

  it('does not show a disconnected active social binding as bound', async () => {
    const disconnectedBinding = { ...socialBinding('active'), connectionState: 'notConnected' }
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/appBindings/refresh') return { bindings: [disconnectedBinding] }
      if (method === 'thread/appBindings/list') return { bindings: [disconnectedBinding] }
      if (method === 'app/list') {
        return {
          apps: [
            socialAppInfo({
              connectionState: 'notConnected',
              bindingSummary: disconnectedBinding
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
      expect(screen.getByLabelText('QQ')).toBeInTheDocument()
      expect(document.body.textContent).toContain('Not connected')
      expect(document.body.textContent).not.toContain('Bound')
    })
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
      expect(sendRequest).toHaveBeenCalledWith('thread/appBindings/list', {
        threadId: 'thread-1',
        includeRevoked: false
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
