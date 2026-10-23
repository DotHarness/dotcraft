import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { InputComposer } from '../components/conversation/InputComposer'
import { ChannelBindingCodeCard } from '../components/conversation/ChannelBindingCodeCard'
import { useAppBindingStore, type ThreadAppBinding } from '../stores/appBindingStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { useChannelBindingStore } from '../stores/channelBindingStore'
import { useThreadStore } from '../stores/threadStore'
import { useToastStore } from '../stores/toastStore'
import { installDesktopApiMock } from './desktopApiMock'

const appServerSendRequest = vi.fn()

class ResizeObserverMock {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
}

Object.defineProperty(globalThis, 'ResizeObserver', {
  configurable: true,
  writable: true,
  value: ResizeObserverMock
})

const activeQqBinding: ThreadAppBinding = {
  bindingId: 'channelbind_1',
  threadId: 'thread-1',
  appId: 'com.dotharness.channel.qq',
  bindingKind: 'channel',
  state: 'active',
  channelTarget: {
    channelName: 'qq',
    conversationKind: 'group',
    conversationId: 'g1',
    deliveryTarget: 'group:g1',
    displayName: 'Team chat'
  }
}

let bindings: ThreadAppBinding[] = []

function renderComposer(): void {
  render(
    <LocaleProvider>
      <InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />
    </LocaleProvider>
  )
}

describe('channel handoff', () => {
  beforeEach(() => {
    localStorage.clear()
    vi.clearAllMocks()
    bindings = []
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'channel/status') {
        return { channels: [{ name: 'qq', category: 'social', enabled: true, running: true }] }
      }
      if (method === 'thread/appBindings/list') return { bindings }
      if (method === 'thread/channelBindings/request/create') {
        return {
          bindingRequestId: 'channelreq_1',
          bindingId: 'channelbind_2',
          code: '482913',
          channelName: 'qq',
          expiresAt: new Date(Date.now() + 600_000).toISOString()
        }
      }
      return {}
    })
    installDesktopApiMock({
      settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
      appServer: { sendRequest: appServerSendRequest, onNotification: vi.fn(() => () => {}) },
      shell: { listOpenTargets: vi.fn().mockResolvedValue([]) },
      voice: undefined
    })
    useConversationStore.getState().reset()
    useConnectionStore.getState().reset()
    useThreadStore.getState().reset()
    useAppBindingStore.getState().reset()
    useChannelBindingStore.getState().reset()
    useToastStore.setState({ toasts: [] })
    useConnectionStore.setState({ status: 'connected', capabilities: { channelStatus: true, appBindingVersion: 1 } })
  })

  it('handles a typed /bind <channel> without starting a turn', async () => {
    renderComposer()
    await waitFor(() => expect(useChannelBindingStore.getState().channels).toHaveLength(1))

    const textbox = screen.getByRole('textbox')
    textbox.textContent = '/bind qq'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/channelBindings/request/create', {
        threadId: 'thread-1',
        channelName: 'qq'
      })
    })
    expect(appServerSendRequest).not.toHaveBeenCalledWith('turn/start', expect.anything())
  })

  it('drops the pending card once the binding becomes active', async () => {
    useChannelBindingStore.setState({
      pendingByThread: {
        'thread-1': {
          threadId: 'thread-1',
          bindingId: 'channelbind_2',
          bindingRequestId: 'channelreq_1',
          code: '482913',
          channelName: 'qq',
          expiresAt: new Date(Date.now() + 600_000).toISOString()
        }
      }
    })
    render(<LocaleProvider><ChannelBindingCodeCard threadId="thread-1" /></LocaleProvider>)
    expect(screen.getByRole('status')).toBeInTheDocument()

    act(() => {
      useAppBindingStore.setState({
        bindingsByThread: { 'thread-1': [{ ...activeQqBinding, bindingId: 'channelbind_2' }] }
      })
    })

    await waitFor(() => expect(screen.queryByRole('status')).toBeNull())
    expect(useChannelBindingStore.getState().pendingByThread['thread-1']).toBeUndefined()
    expect(appServerSendRequest).not.toHaveBeenCalledWith('thread/appBindings/revoke', expect.anything())
  })

  it('revokes the pending binding when the card is cancelled', async () => {
    useChannelBindingStore.setState({
      pendingByThread: {
        'thread-1': {
          threadId: 'thread-1',
          bindingId: 'channelbind_2',
          bindingRequestId: 'channelreq_1',
          code: '482913',
          channelName: 'qq',
          expiresAt: new Date(Date.now() + 600_000).toISOString()
        }
      }
    })
    render(<LocaleProvider><ChannelBindingCodeCard threadId="thread-1" /></LocaleProvider>)

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/appBindings/revoke', {
        threadId: 'thread-1',
        bindingId: 'channelbind_2'
      })
    })
    expect(useChannelBindingStore.getState().pendingByThread['thread-1']).toBeUndefined()
  })

  it('revokes the active binding on /unbind', async () => {
    bindings = [activeQqBinding]
    useAppBindingStore.setState({ bindingsByThread: { 'thread-1': bindings } })
    renderComposer()
    await waitFor(() => expect(useChannelBindingStore.getState().channels).toHaveLength(1))

    const textbox = screen.getByRole('textbox')
    textbox.textContent = '/unbind'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/appBindings/revoke', {
        threadId: 'thread-1',
        bindingId: 'channelbind_1'
      })
    })
    expect(appServerSendRequest).not.toHaveBeenCalledWith('turn/start', expect.anything())
  })
})
