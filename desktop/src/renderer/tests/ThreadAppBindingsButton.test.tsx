import { beforeEach, describe, expect, it, vi } from 'vitest'
import { installDesktopApiMock } from './desktopApiMock'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
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
      if (method === 'app/list') {
        return {
          apps: [appInfo({ bindingSummary: threadBinding() })]
        }
      }
      return {}
    })
    installDesktopApiMock({
      settings: { get: settingsGet },
      appServer: { sendRequest },
      shell: {
        openAppHandoff: shellOpenAppHandoff,
        getProtocolHandlerName: shellGetProtocolHandlerName
      }
    })
    ;(window as Window & { __confirmDialog?: () => Promise<boolean> }).__confirmDialog = vi.fn().mockResolvedValue(true)
  })

  it('keeps header button chrome and shows a checked switch without status or scope text', async () => {
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
    expect(screen.getByRole('switch', { name: 'Use Workflow App in this chat' })).toHaveAttribute('aria-checked', 'true')
    expect(screen.queryByRole('button', { name: 'Added' })).not.toBeInTheDocument()
    expect(screen.queryByText('board.read, board.manage')).toBeNull()
    expect(container.querySelector('img[src^="data:image/svg+xml"]')).not.toBeNull()
    expect(button).not.toHaveAttribute('data-bordered')
    expect(container.querySelector('.dc-app-bindings-picker__count')).toBeNull()
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
    expect(screen.getByRole('switch', { name: 'Use Workflow App in this chat' })).toHaveAttribute('aria-checked', 'false')
    expect(screen.queryByRole('button', { name: 'Add' })).not.toBeInTheDocument()
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
    fireEvent.click(await screen.findByRole('switch', { name: 'Use Workflow App in this chat' }))

    await waitFor(() => {
      expect(sendRequest).toHaveBeenCalledWith('thread/appBindings/enable', {
        threadId: 'thread-1',
        appId: 'com.example.workflow'
      })
      expect(shellOpenAppHandoff).toHaveBeenCalledWith('workflow://dotcraft/bind?request=bind-req-1')
    })
  })

  it('hides unconnected apps instead of offering connection or setup actions', async () => {
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/appBindings/refresh') return { bindings: [] }
      if (method === 'thread/appBindings/list') return { bindings: [] }
      if (method === 'app/list') {
        return { apps: [appInfo({ connectionState: 'notConnected' })] }
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
      expect(sendRequest).toHaveBeenCalledWith('app/list', expect.objectContaining({
        threadId: 'thread-1',
        surface: 'threadBinding'
      }))
      expect(screen.getByText('No connected apps available.')).toBeInTheDocument()
    })
    expect(screen.queryByText('Workflow App')).not.toBeInTheDocument()
    expect(screen.queryByRole('switch')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Set up' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Connect' })).not.toBeInTheDocument()
    expect(sendRequest).not.toHaveBeenCalledWith('app/connection/start', expect.anything())
    expect(sendRequest).not.toHaveBeenCalledWith('app/binding/request/create', expect.anything())
  })

  it('directly revokes an existing binding when its switch is turned off', async () => {
    let bound = true
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/appBindings/refresh') return { bindings: bound ? [{ bindingId: 'binding-1', state: 'active', attachedToolCount: 1 }] : [] }
      if (method === 'thread/appBindings/list') return { bindings: bound ? [threadBinding('active')] : [] }
      if (method === 'app/list') return { apps: [appInfo()] }
      if (method === 'thread/appBindings/revoke') {
        bound = false
        return {}
      }
      return {}
    })

    render(
      <LocaleProvider>
        <ThreadAppBindingsButton threadId="thread-1" />
      </LocaleProvider>
    )

    fireEvent.click(await screen.findByRole('button', { name: 'Apps' }))
    const switchControl = await screen.findByRole('switch', { name: 'Use Workflow App in this chat' })
    expect(switchControl).toHaveAttribute('aria-checked', 'true')
    fireEvent.click(switchControl)

    await waitFor(() => {
      expect(sendRequest).toHaveBeenCalledWith('thread/appBindings/revoke', {
        threadId: 'thread-1',
        bindingId: 'binding-1',
        reason: undefined
      })
      expect(switchControl).toHaveAttribute('aria-checked', 'false')
    })
    expect((window as Window & { __confirmDialog?: ReturnType<typeof vi.fn> }).__confirmDialog).not.toHaveBeenCalled()
  })

  it('keeps the switch checked when direct revoke fails', async () => {
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/appBindings/refresh') return { bindings: [{ bindingId: 'binding-1', state: 'active', attachedToolCount: 1 }] }
      if (method === 'thread/appBindings/list') return { bindings: [threadBinding('active')] }
      if (method === 'app/list') return { apps: [appInfo()] }
      if (method === 'thread/appBindings/revoke') throw new Error('Revoke failed')
      return {}
    })

    render(
      <LocaleProvider>
        <ThreadAppBindingsButton threadId="thread-1" />
      </LocaleProvider>
    )

    fireEvent.click(await screen.findByRole('button', { name: 'Apps' }))
    const switchControl = await screen.findByRole('switch', { name: 'Use Workflow App in this chat' })
    fireEvent.click(switchControl)

    await waitFor(() => {
      expect(sendRequest).toHaveBeenCalledWith('thread/appBindings/revoke', {
        threadId: 'thread-1',
        bindingId: 'binding-1',
        reason: undefined
      })
      expect(switchControl).toHaveAttribute('aria-checked', 'true')
      expect(useToastStore.getState().toasts.some((toast) => toast.message === 'Revoke failed')).toBe(true)
    })
  })

  it('shows managed apps that require no external connection even when not connected', async () => {
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/appBindings/refresh') return { bindings: [] }
      if (method === 'thread/appBindings/list') return { bindings: [] }
      if (method === 'app/list') return { apps: [appInfo({
        appId: 'com.example.managed',
        displayName: 'Managed Workflow',
        managed: true,
        requiresExternalConnection: false,
        connectionState: 'notConnected'
      })] }
      return {}
    })

    render(
      <LocaleProvider>
        <ThreadAppBindingsButton threadId="thread-1" />
      </LocaleProvider>
    )

    fireEvent.click(await screen.findByRole('button', { name: 'Apps' }))

    expect(await screen.findByText('Managed Workflow')).toBeInTheDocument()
    expect(screen.getByRole('switch', { name: 'Use Managed Workflow in this chat' })).toHaveAttribute('aria-checked', 'false')
    expect(screen.queryByRole('button', { name: 'Set up' })).not.toBeInTheDocument()
  })

  it('starts a social binding request and cancels it directly when switched off', async () => {
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
    fireEvent.click(await screen.findByRole('switch', { name: 'Use QQ in this chat' }))

    await waitFor(() => {
      expect(sendRequest).toHaveBeenCalledWith('thread/socialBindings/request/create', {
        threadId: 'thread-1',
        channelName: 'qq'
      })
    })
    expect(await screen.findByRole('switch', { name: 'Use QQ in this chat' })).toHaveAttribute('aria-checked', 'true')
    expect(screen.getByText('Send /bind 482913 in the QQ conversation to bind it to this thread.')).toBeInTheDocument()
    expect(useToastStore.getState().toasts.some((toast) => toast.message.includes('/bind 482913'))).toBe(false)
    expect(screen.queryByRole('button', { name: 'Refresh' })).not.toBeInTheDocument()
    expect(sendRequest).not.toHaveBeenCalledWith('thread/appBindings/refresh', {
      threadId: 'thread-1',
      bindingId: 'bind-req-social-1'
    })
    expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument()
    expect(shellOpenAppHandoff).not.toHaveBeenCalled()

    fireEvent.click(screen.getByRole('switch', { name: 'Use QQ in this chat' }))
    await waitFor(() => {
      expect(sendRequest).toHaveBeenCalledWith('thread/appBindings/revoke', {
        threadId: 'thread-1',
        bindingId: 'binding-social-1',
        reason: undefined
      })
      expect(screen.getByRole('switch', { name: 'Use QQ in this chat' })).toHaveAttribute('aria-checked', 'false')
      expect(screen.queryByText('Send /bind 482913 in the QQ conversation to bind it to this thread.')).toBeNull()
    })
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
    fireEvent.click(await screen.findByRole('switch', { name: 'Use QQ in this chat' }))
    expect(await screen.findByText('Send /bind 482913 in the QQ conversation to bind it to this thread.')).toBeInTheDocument()

    binding = null
    await act(async () => {
      await useAppBindingStore.getState().refreshThreadBindings('thread-1')
      await useAppBindingStore.getState().fetchApps('thread-1', false, 'threadBinding')
    })

    await waitFor(() => {
      expect(screen.queryByText('Send /bind 482913 in the QQ conversation to bind it to this thread.')).toBeNull()
      expect(screen.getByRole('switch', { name: 'Use QQ in this chat' })).toHaveAttribute('aria-checked', 'false')
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
    fireEvent.click(await screen.findByRole('switch', { name: 'Use QQ in this chat' }))

    await waitFor(() => {
      expect(screen.getByRole('switch', { name: 'Use QQ in this chat' })).toHaveAttribute('aria-checked', 'true')
      expect(screen.queryByText('Send /bind 482913 in the QQ conversation to bind it to this thread.')).toBeNull()
    })
  })

  it('shows ready social apps without setup even when they require no external connection', async () => {
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
      expect(screen.getByText('QQ')).toBeInTheDocument()
      expect(screen.getByRole('switch', { name: 'Use QQ in this chat' })).toHaveAttribute('aria-checked', 'false')
    })
    expect(screen.queryByRole('button', { name: 'Add' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Connect' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Set up' })).toBeNull()
  })

  it('hides a disconnected external app even when a thread binding still exists', async () => {
    const disconnectedBinding = { ...threadBinding('active'), connectionState: 'notConnected' }
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/appBindings/refresh') return { bindings: [disconnectedBinding] }
      if (method === 'thread/appBindings/list') return { bindings: [disconnectedBinding] }
      if (method === 'app/list') {
        return {
          apps: [
            appInfo({
              requiresExternalConnection: true,
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
      expect(screen.getByText('No connected apps available.')).toBeInTheDocument()
      expect(screen.queryByText('Workflow App')).not.toBeInTheDocument()
      expect(screen.queryByRole('switch')).not.toBeInTheDocument()
    })
  })

  it('shows the active social target and revokes it directly from the switch', async () => {
    let bound = true
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/appBindings/refresh') return { bindings: bound ? [socialBinding('active')] : [] }
      if (method === 'thread/appBindings/list') return { bindings: bound ? [socialBinding('active')] : [] }
      if (method === 'app/list') return { apps: [socialAppInfo({ bindingSummary: bound ? socialBinding('active') : undefined })] }
      if (method === 'thread/appBindings/revoke') {
        bound = false
        return {}
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
      expect(screen.getByText('QQ group 123456').textContent).toBe('QQ group 123456')
    })

    expect(screen.queryByRole('button', { name: 'Refresh' })).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('switch', { name: 'Use QQ in this chat' }))
    await waitFor(() => {
      expect(sendRequest).toHaveBeenCalledWith('thread/appBindings/revoke', {
        threadId: 'thread-1',
        bindingId: 'binding-social-1',
        reason: undefined
      })
      expect(screen.getByRole('switch', { name: 'Use QQ in this chat' })).toHaveAttribute('aria-checked', 'false')
    })
  })

  it('reviews and accepts a capability expansion through the existing confirmation endpoint', async () => {
    const reviewBinding = {
      ...threadBinding('needsConfirmation'),
      candidateCapabilityRevision: 3,
      pendingChanges: [{ kind: 'added', tool: 'workflow.DeleteCard', detail: 'Delete cards' }]
    }
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/appBindings/refresh') return { bindings: [reviewBinding] }
      if (method === 'thread/appBindings/list') return { bindings: [reviewBinding] }
      if (method === 'app/list') return { apps: [appInfo({ bindingSummary: reviewBinding })] }
      return {}
    })

    render(
      <LocaleProvider>
        <ThreadAppBindingsButton threadId="thread-1" />
      </LocaleProvider>
    )

    fireEvent.click(await screen.findByRole('button', { name: 'Apps' }))
    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('app/list', expect.anything()))
    fireEvent.click(screen.getByRole('button', { name: 'Review' }))
    expect(await screen.findByText('Delete cards')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Accept capabilities' }))

    await waitFor(() => {
      expect(sendRequest).toHaveBeenCalledWith('thread/appBindings/confirmCapabilities', {
        threadId: 'thread-1',
        bindingId: 'binding-1',
        candidateRevision: 3,
        decision: 'accept'
      })
    })
  })
})
