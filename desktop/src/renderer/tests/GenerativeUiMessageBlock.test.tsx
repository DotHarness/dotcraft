import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, render, renderHook, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { GenerativeUiMessageBlock } from '../components/conversation/generativeUi/GenerativeUiMessageBlock'
import { useGenerativeUiHostActions } from '../components/conversation/generativeUi/generativeUiHostActions'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import type { Thread } from '../types/thread'
import { installDesktopApiMock } from './desktopApiMock'

const addToast = vi.hoisted(() => vi.fn())
const confirm = vi.hoisted(() => vi.fn())
const runner = vi.hoisted(() => ({ acquire: vi.fn(), prewarm: vi.fn() }))
const observers: Array<{ callback: IntersectionObserverCallback; element?: Element }> = []

vi.mock('../stores/toastStore', () => ({ addToast }))
vi.mock('../components/ui/ConfirmDialog', () => ({ useConfirmDialog: () => confirm }))
vi.mock('../components/conversation/generativeUi/generativeUiRunnerHost', () => ({
  acquireGenerativeUiRunner: runner.acquire,
  prewarmGenerativeUiRunner: runner.prewarm
}))

class TestIntersectionObserver {
  private readonly entry: { callback: IntersectionObserverCallback; element?: Element }
  constructor(callback: IntersectionObserverCallback) {
    this.entry = { callback }
    observers.push(this.entry)
  }
  observe(element: Element): void { this.entry.element = element }
  disconnect(): void { this.entry.element = undefined }
}

function intersect(): void {
  for (const observer of observers) {
    if (observer.element) observer.callback([{ isIntersecting: true, target: observer.element } as IntersectionObserverEntry], observer as unknown as IntersectionObserver)
  }
}

const wrapper = ({ children }: { children: ReactNode }) => <LocaleProvider loadSettings={false}>{children}</LocaleProvider>
const source = { threadId: 'thread-1', turnId: 'turn-1', itemId: 'item-1' }
const sendRequest = vi.fn()
const openExternal = vi.fn()
const notificationListeners = new Set<(payload: unknown) => void>()
const onNotification = vi.fn((listener: (payload: unknown) => void) => {
  notificationListeners.add(listener)
  return () => { notificationListeners.delete(listener) }
})

beforeEach(() => {
  vi.clearAllMocks()
  observers.length = 0
  notificationListeners.clear()
  runner.acquire.mockImplementation(() => () => undefined)
  vi.stubGlobal('IntersectionObserver', TestIntersectionObserver)
  vi.stubGlobal('ResizeObserver', class { observe(): void {} disconnect(): void {} })
  sendRequest.mockImplementation(async (method: string) => method === 'generativeUi/state/read' ? { states: [] } : {})
  openExternal.mockResolvedValue(undefined)
  installDesktopApiMock({
    initialLocale: 'en',
    settings: { get: vi.fn(async () => ({ locale: 'en' })) },
    appServer: { sendRequest, onNotification },
    shell: { openExternal }
  })
  useThreadStore.setState({ activeThread: { id: 'thread-1', workspacePath: '/workspace' } as Thread })
  useConversationStore.setState({ turnStatus: 'idle', workspacePath: '/workspace' })
  useConnectionStore.setState({ capabilities: { generativeUi: true } as never })
})

describe('Generative UI host actions', () => {
  it('sends a message only while the thread is idle', async () => {
    const { result } = renderHook(() => useGenerativeUiHostActions('thread-1'), { wrapper })

    useConversationStore.setState({ turnStatus: 'running' })
    act(() => result.current({ kind: 'sendMessage', text: 'Explain question 2' }))
    expect(addToast).toHaveBeenCalledWith(expect.any(String), 'info', 3000)
    expect(sendRequest).not.toHaveBeenCalledWith('turn/start', expect.anything())

    useConversationStore.setState({ turnStatus: 'idle' })
    act(() => result.current({ kind: 'sendMessage', text: 'Explain question 2' }))
    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('turn/start', expect.objectContaining({ threadId: 'thread-1' })))
    expect(JSON.stringify(sendRequest.mock.calls.find(([method]) => method === 'turn/start'))).toContain('Explain question 2')
  })

  it('opens a link only after the user confirms it', async () => {
    const { result } = renderHook(() => useGenerativeUiHostActions('thread-1'), { wrapper })

    confirm.mockResolvedValueOnce(false)
    act(() => result.current({ kind: 'openUrl', url: 'https://example.com/a' }))
    await waitFor(() => expect(confirm).toHaveBeenCalledTimes(1))
    expect(confirm.mock.calls[0][0]).toMatchObject({ message: 'https://example.com/a' })

    confirm.mockResolvedValueOnce(true)
    await act(async () => result.current({ kind: 'openUrl', url: 'https://example.com/b' }))
    await waitFor(() => expect(openExternal).toHaveBeenCalledWith('https://example.com/b'))
    expect(openExternal).toHaveBeenCalledTimes(1)
  })
})

describe('GenerativeUiMessageBlock', () => {
  const block = (props: Partial<Parameters<typeof GenerativeUiMessageBlock>[0]> = {}) => (
    <LocaleProvider loadSettings={false}>
      <GenerativeUiMessageBlock code="DotCraft.render(<Text value='Hi' />)" closed blockIndex={0} source={source} live={false} locked={false} {...props} />
    </LocaleProvider>
  )

  it('prewarms the shared runner at once but starts a historical block only once it nears the viewport', async () => {
    render(block())

    expect(runner.prewarm).toHaveBeenCalled()
    expect(runner.acquire).not.toHaveBeenCalled()
    expect(sendRequest).not.toHaveBeenCalled()

    act(() => intersect())
    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('generativeUi/state/read', { threadId: 'thread-1' }))
    await waitFor(() => expect(runner.acquire).toHaveBeenCalledTimes(1))
  })

  it('holds an unclosed block and starts a closed live block at once', async () => {
    const { container, rerender } = render(block({ closed: false, live: true }))

    expect(container.querySelector('[role="status"]')).not.toBeNull()
    expect(runner.acquire).not.toHaveBeenCalled()

    rerender(block({ closed: true, live: true }))
    await waitFor(() => expect(runner.acquire).toHaveBeenCalledTimes(1))
    expect(observers.every(observer => observer.element === undefined)).toBe(true)
  })

  it('restores a state saved by another client into the running block without restarting it', async () => {
    const messages: Array<{ type?: string; state?: unknown }> = []
    runner.acquire.mockImplementation(({ onPort }: { onPort: (port: MessagePort) => void }) => {
      const channel = new MessageChannel()
      channel.port2.onmessage = event => messages.push(event.data)
      onPort(channel.port1)
      return () => channel.port2.close()
    })
    const notify = (params: unknown): void => act(() => notificationListeners.forEach(listener =>
      listener({ method: 'generativeUi/state/updated', params })))
    render(block({ live: true }))
    await waitFor(() => expect(messages.some(message => message.type === 'start')).toBe(true))

    notify({ ...source, blockIndex: 1, state: { 'r#App': { 0: 2 } } })
    notify({ ...source, blockIndex: 0, state: null })
    notify({ ...source, blockIndex: 0, state: { 'r#App': { 0: 5 } } })

    await waitFor(() => expect(messages.filter(message => message.type === 'restore')).toEqual([
      expect.objectContaining({ state: { 'r#App': { 0: 5 } } })
    ]))
    expect(messages.filter(message => message.type === 'start')).toHaveLength(1)
  })
})
