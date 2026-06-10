import { describe, it, expect, vi, beforeEach } from 'vitest'
import { act, fireEvent, render, waitFor } from '@testing-library/react'
import { InteractiveToolView } from '../components/conversation/InteractiveToolView'
import type { ConversationItem } from '../types/conversation'
import { startTurnWithOptimisticUI } from '../utils/startTurn'
import { useConversationStore } from '../stores/conversationStore'
import { useDisplayModeStore } from '../stores/displayModeStore'
import { useAppBindingStore, type AppInfo, type ThreadAppBinding } from '../stores/appBindingStore'
import { THEME_CHANGED_EVENT } from '../../shared/theme'

vi.mock('../utils/startTurn', () => ({
  startTurnWithOptimisticUI: vi.fn().mockResolvedValue(true)
}))

function makeItem(): ConversationItem {
  return {
    id: 'i1',
    type: 'dynamicToolCall',
    status: 'completed',
    createdAt: new Date(0).toISOString(),
    toolName: 'CreateCard',
    pluginNamespace: 'oratorio',
    arguments: { title: 'Hi' },
    structuredResult: { cardId: 'c1' },
    meta: { highlight: true },
    success: true,
    toolUi: { resourceUri: 'ui://oratorio/board' }
  } as ConversationItem
}

type BridgeMessage = {
  id?: unknown
  method?: string
  result?: {
    bridgeToken?: unknown
    hostContext?: { theme?: string }
    hostCapabilities?: { serverTools?: boolean; openLinks?: boolean; updateModelContext?: boolean; message?: boolean }
    structuredContent?: unknown
    widgetState?: unknown
  }
  params?: { structuredContent?: unknown; _meta?: unknown; theme?: string }
  error?: { code?: number }
}

const sendRequest = vi.fn()
const openExternal = vi.fn()

function setWorkspace(): void {
  // ui/message needs a workspace path; the rest of the actions do not.
  useConversationStore.setState({ workspacePath: '/ws' })
}

beforeEach(() => {
  sendRequest.mockReset().mockResolvedValue({})
  openExternal.mockReset().mockResolvedValue(undefined)
  ;(startTurnWithOptimisticUI as unknown as ReturnType<typeof vi.fn>).mockClear()
  useDisplayModeStore.setState({ expanded: null })
  // Default to an active binding so the iframe renders (and the mount-time binding fetch is
  // skipped); degraded-state tests reset + reseed the store themselves.
  useAppBindingStore.getState().reset()
  seedBindings('active')
  ;(window as unknown as { api: unknown }).api = {
    appServer: { sendRequest },
    shell: { openExternal }
  }
})

function mountFrame(): { container: HTMLElement; iframe: HTMLIFrameElement; frameWindow: Window; postSpy: ReturnType<typeof vi.spyOn> } {
  const { container } = render(<InteractiveToolView item={makeItem()} threadId="t1" locale="en" />)
  const iframe = container.querySelector('iframe') as HTMLIFrameElement
  const frameWindow = iframe.contentWindow!
  const postSpy = vi.spyOn(frameWindow, 'postMessage')
  return { container, iframe, frameWindow, postSpy }
}

function dispatch(frameWindow: Window, message: Record<string, unknown>): void {
  act(() => {
    window.dispatchEvent(new MessageEvent('message', { data: { jsonrpc: '2.0', ...message }, source: frameWindow }))
  })
}

function replies(postSpy: ReturnType<typeof vi.spyOn>): BridgeMessage[] {
  return postSpy.mock.calls.map((call) => call[0] as BridgeMessage)
}

function initializeBridge(frameWindow: Window, postSpy: ReturnType<typeof vi.spyOn>, id = 1): string {
  dispatch(frameWindow, { id, method: 'ui/initialize', params: {} })
  const init = replies(postSpy).find((m) => m.id === id)
  expect(typeof init?.result?.bridgeToken).toBe('string')
  return init?.result?.bridgeToken as string
}

describe('InteractiveToolView', () => {
  it('renders a sandboxed dotcraft-app iframe', () => {
    const { container } = render(<InteractiveToolView item={makeItem()} threadId="t1" locale="en" />)
    const iframe = container.querySelector('iframe') as HTMLIFrameElement
    expect(iframe).toBeTruthy()
    expect(iframe.getAttribute('sandbox')).toBe('allow-scripts')
    expect(iframe.getAttribute('src')).toContain('dotcraft-app://resource')
    expect(iframe.getAttribute('src')).toContain('threadId=t1')
  })

  it('answers ui/initialize with live action capabilities and pushes tool data', () => {
    const { frameWindow, postSpy } = mountFrame()
    const bridgeToken = initializeBridge(frameWindow, postSpy)

    const init = replies(postSpy).find((m) => m.id === 1)
    expect(init?.result?.bridgeToken).toBe(bridgeToken)
    expect(init?.result?.hostContext?.theme).toBeDefined()
    expect(init?.result?.hostCapabilities).toMatchObject({
      serverTools: true,
      openLinks: true,
      updateModelContext: true,
      message: true
    })

    const toolResult = replies(postSpy).find((m) => m.method === 'ui/notifications/tool-result')
    expect(toolResult?.params?.structuredContent).toEqual({ cardId: 'c1' })
    expect(replies(postSpy).some((m) => m.method === 'ui/notifications/tool-input')).toBe(true)
  })

  it('forwards tools/call to ui/tool/call and returns the result', async () => {
    sendRequest.mockResolvedValue({ success: true, structuredResult: { ok: 1 }, contentItems: [], _meta: null })
    const { frameWindow, postSpy } = mountFrame()
    const bridgeToken = initializeBridge(frameWindow, postSpy)
    dispatch(frameWindow, { id: 5, method: 'tools/call', bridgeToken, params: { name: 'ListItems', arguments: { q: 'x' } } })

    await waitFor(() => expect(sendRequest).toHaveBeenCalled())
    expect(sendRequest).toHaveBeenCalledWith(
      'ui/tool/call',
      expect.objectContaining({ threadId: 't1', namespace: 'oratorio', tool: 'ListItems', sourceCallId: 'i1' }),
      expect.any(Number)
    )
    await waitFor(() => {
      const reply = replies(postSpy).find((m) => m.id === 5)
      expect(reply?.result?.structuredContent).toEqual({ ok: 1 })
    })
  })

  it('surfaces a gated tool-call rejection as an error to the UI', async () => {
    sendRequest.mockResolvedValue({ success: false, errorCode: 'AppBindingApprovalRequired', errorMessage: 'requires approval' })
    const { frameWindow, postSpy } = mountFrame()
    const bridgeToken = initializeBridge(frameWindow, postSpy)
    dispatch(frameWindow, { id: 7, method: 'tools/call', bridgeToken, params: { name: 'CreateCard' } })

    await waitFor(() => {
      const reply = replies(postSpy).find((m) => m.id === 7)
      expect(reply?.error?.code).toBeDefined()
    })
  })

  it('forwards ui/open-link and opens the cleared url', async () => {
    sendRequest.mockResolvedValue({ url: 'https://oratorio.example/board/1' })
    const { frameWindow, postSpy } = mountFrame()
    const bridgeToken = initializeBridge(frameWindow, postSpy)
    dispatch(frameWindow, { id: 6, method: 'ui/open-link', bridgeToken, params: { url: 'https://oratorio.example/board/1' } })

    await waitFor(() => expect(openExternal).toHaveBeenCalledWith('https://oratorio.example/board/1'))
    expect(sendRequest).toHaveBeenCalledWith('ui/open-link', expect.objectContaining({ url: 'https://oratorio.example/board/1' }), expect.any(Number))
  })

  it('forwards ui/update-model-context with the item-derived sourceCallId', async () => {
    const { frameWindow, postSpy } = mountFrame()
    const bridgeToken = initializeBridge(frameWindow, postSpy)
    dispatch(frameWindow, { id: 8, method: 'ui/update-model-context', bridgeToken, params: { title: 'state', content: 'selected card-7' } })

    await waitFor(() =>
      expect(sendRequest).toHaveBeenCalledWith(
        'ui/update-model-context',
        expect.objectContaining({ threadId: 't1', sourceCallId: 'i1', content: 'selected card-7' }),
        expect.any(Number)
      )
    )
  })

  it('clears model context and disables old-token actions when the card degrades', async () => {
    const { container, frameWindow, postSpy } = mountFrame()
    const bridgeToken = initializeBridge(frameWindow, postSpy)
    dispatch(frameWindow, { id: 13, method: 'ui/update-model-context', bridgeToken, params: { title: 'state', content: 'selected card-7' } })

    await waitFor(() =>
      expect(sendRequest).toHaveBeenCalledWith(
        'ui/update-model-context',
        expect.objectContaining({ threadId: 't1', sourceCallId: 'i1', content: 'selected card-7' }),
        expect.any(Number)
      )
    )
    sendRequest.mockClear()

    act(() => {
      useAppBindingStore.setState({ apps: [{ toolNamespace: 'oratorio', enabled: false }] as unknown as AppInfo[] })
    })

    await waitFor(() =>
      expect(sendRequest).toHaveBeenCalledWith(
        'ui/update-model-context',
        expect.objectContaining({ threadId: 't1', sourceCallId: 'i1', content: '' }),
        expect.any(Number)
      )
    )
    expect(container.querySelector('iframe')).toBeNull()
    expect(container.textContent).toContain('Plugin is disabled')

    dispatch(frameWindow, { id: 14, method: 'tools/call', bridgeToken, params: { name: 'ListItems' } })
    expect(sendRequest).not.toHaveBeenCalledWith('ui/tool/call', expect.anything(), expect.anything())
  })

  it('injects a visible turn for ui/message and rate-limits a rapid second message', async () => {
    setWorkspace()
    const { frameWindow, postSpy } = mountFrame()
    const bridgeToken = initializeBridge(frameWindow, postSpy)
    dispatch(frameWindow, { id: 10, method: 'ui/message', bridgeToken, params: { role: 'user', content: 'discuss this' } })
    await waitFor(() => expect(startTurnWithOptimisticUI).toHaveBeenCalledTimes(1))
    expect(startTurnWithOptimisticUI).toHaveBeenCalledWith(expect.objectContaining({ text: 'discuss this' }))

    // A second message within the min interval is rejected without starting another turn.
    dispatch(frameWindow, { id: 11, method: 'ui/message', bridgeToken, params: { content: 'again immediately' } })
    await Promise.resolve()
    expect(startTurnWithOptimisticUI).toHaveBeenCalledTimes(1)
  })

  it('restores persisted widgetState in the ui/initialize result', () => {
    const item = { ...makeItem(), widgetState: { tab: 2, scroll: 120 } } as ConversationItem
    const { container } = render(<InteractiveToolView item={item} threadId="t1" locale="en" />)
    const frameWindow = (container.querySelector('iframe') as HTMLIFrameElement).contentWindow!
    const postSpy = vi.spyOn(frameWindow, 'postMessage')
    dispatch(frameWindow, { id: 1, method: 'ui/initialize', params: {} })

    const init = replies(postSpy).find((m) => m.id === 1)
    expect(init?.result?.widgetState).toEqual({ tab: 2, scroll: 120 })
  })

  it('forwards ui/set-widget-state to item/widget-state/set keyed by callId', async () => {
    const { frameWindow, postSpy } = mountFrame()
    const bridgeToken = initializeBridge(frameWindow, postSpy)
    dispatch(frameWindow, { id: 12, method: 'ui/set-widget-state', bridgeToken, params: { widgetState: { tab: 3 } } })

    await waitFor(() =>
      expect(sendRequest).toHaveBeenCalledWith(
        'item/widget-state/set',
        expect.objectContaining({ threadId: 't1', callId: 'i1', widgetState: { tab: 3 } }),
        expect.any(Number)
      )
    )
  })

  it('pushes host-context-changed when the Desktop theme changes', () => {
    const { frameWindow, postSpy } = mountFrame()
    initializeBridge(frameWindow, postSpy)
    window.dispatchEvent(new CustomEvent(THEME_CHANGED_EVENT, { detail: { mode: 'light' } }))

    const pushed = replies(postSpy).find((m) => m.method === 'ui/notifications/host-context-changed')
    expect(pushed?.params?.theme).toBeDefined()
  })

  it('arbitrates ui/request-display-mode and grants fullscreen', () => {
    const { frameWindow, postSpy } = mountFrame()
    const bridgeToken = initializeBridge(frameWindow, postSpy)
    dispatch(frameWindow, { id: 20, method: 'ui/request-display-mode', bridgeToken, params: { mode: 'fullscreen' } })

    const reply = replies(postSpy).find((m) => m.id === 20)
    expect((reply?.result as { mode?: string } | undefined)?.mode).toBe('fullscreen')
    expect(useDisplayModeStore.getState().expanded?.mode).toBe('fullscreen')
    expect(useDisplayModeStore.getState().expanded?.item.id).toBe('i1')
  })

  it('rejects an invalid display mode', () => {
    const { frameWindow, postSpy } = mountFrame()
    const bridgeToken = initializeBridge(frameWindow, postSpy)
    dispatch(frameWindow, { id: 21, method: 'ui/request-display-mode', bridgeToken, params: { mode: 'huge' } })

    const reply = replies(postSpy).find((m) => m.id === 21)
    expect(reply?.error?.code).toBe(-32600)
    expect(useDisplayModeStore.getState().expanded).toBeNull()
  })

  it('shows a collapse placeholder (no iframe) when its card is expanded elsewhere', () => {
    const item = makeItem()
    useDisplayModeStore.setState({ expanded: { item, threadId: 't1', mode: 'fullscreen' } })
    const { container } = render(<InteractiveToolView item={item} threadId="t1" locale="en" />)

    expect(container.querySelector('iframe')).toBeNull()
    expect(container.textContent).toContain('Collapse')
  })

  it('rejects an unsupported bridge method', () => {
    const { frameWindow, postSpy } = mountFrame()
    dispatch(frameWindow, { id: 9, method: 'ui/bogus', params: {} })
    const reply = replies(postSpy).find((m) => m.id === 9)
    expect(reply?.error?.code).toBe(-32601)
  })

  it('blocks host actions before ui/initialize', () => {
    const { frameWindow, postSpy } = mountFrame()
    dispatch(frameWindow, { id: 30, method: 'tools/call', params: { name: 'ListItems' } })

    expect(sendRequest).not.toHaveBeenCalled()
    const reply = replies(postSpy).find((m) => m.id === 30)
    expect(reply?.error?.code).toBe(-32600)
  })

  it('blocks host actions with a missing or wrong bridgeToken', () => {
    const { frameWindow, postSpy } = mountFrame()
    initializeBridge(frameWindow, postSpy)

    dispatch(frameWindow, { id: 31, method: 'tools/call', params: { name: 'ListItems' } })
    dispatch(frameWindow, { id: 32, method: 'ui/update-model-context', bridgeToken: 'wrong', params: { content: 'x' } })

    expect(sendRequest).not.toHaveBeenCalled()
    expect(replies(postSpy).find((m) => m.id === 31)?.error?.code).toBe(-32600)
    expect(replies(postSpy).find((m) => m.id === 32)?.error?.code).toBe(-32600)
  })

  it('rejects duplicate ui/initialize without minting a second token and disables actions', () => {
    const { frameWindow, postSpy } = mountFrame()
    const bridgeToken = initializeBridge(frameWindow, postSpy)

    dispatch(frameWindow, { id: 33, method: 'ui/initialize', params: {} })
    const duplicate = replies(postSpy).find((m) => m.id === 33)
    expect(duplicate?.result?.bridgeToken).toBeUndefined()
    expect(duplicate?.error?.code).toBe(-32600)

    dispatch(frameWindow, { id: 34, method: 'tools/call', bridgeToken, params: { name: 'ListItems' } })
    expect(sendRequest).not.toHaveBeenCalledWith('ui/tool/call', expect.anything(), expect.anything())
  })

  it('rejects ui/initialize after the initial iframe load completes', () => {
    const { iframe, frameWindow, postSpy } = mountFrame()
    fireEvent.load(iframe)

    dispatch(frameWindow, { id: 36, method: 'ui/initialize', params: {} })
    const lateInit = replies(postSpy).find((m) => m.id === 36)
    expect(lateInit?.result?.bridgeToken).toBeUndefined()
    expect(lateInit?.error?.code).toBe(-32600)
  })

  it('disables the bridge after iframe self-navigation and blocks old-token actions', async () => {
    const { container, iframe, frameWindow, postSpy } = mountFrame()
    const bridgeToken = initializeBridge(frameWindow, postSpy)
    fireEvent.load(iframe)
    fireEvent.load(iframe)

    await waitFor(() =>
      expect(sendRequest).toHaveBeenCalledWith(
        'ui/update-model-context',
        expect.objectContaining({ threadId: 't1', sourceCallId: 'i1', content: '' }),
        expect.any(Number)
      )
    )
    // The disabled bridge drops to the degraded "failed" surface, which also surfaces the
    // recorded tool result as a readable fallback.
    expect(container.textContent).toContain("Couldn't load the app view")
    expect(container.querySelector('iframe')).toBeNull()
    expect(container.textContent).toContain('cardId')

    dispatch(frameWindow, { id: 35, method: 'tools/call', bridgeToken, params: { name: 'ListItems' } })
    expect(sendRequest).not.toHaveBeenCalledWith('ui/tool/call', expect.anything(), expect.anything())
  })
})

function seedBindings(state: string): void {
  useAppBindingStore.setState({
    bindingsByThread: {
      t1: [
        {
          bindingId: 'b1',
          threadId: 't1',
          appId: 'oratorio.app',
          toolNamespace: 'oratorio',
          state,
          connectionState: 'connected',
          grantedScopes: [],
          attachedToolCount: 1,
          lastChangedAt: new Date(0).toISOString()
        }
      ] as unknown as ThreadAppBinding[]
    }
  })
}

describe('InteractiveToolView degraded states', () => {
  beforeEach(() => {
    useAppBindingStore.getState().reset()
  })

  function renderView(): HTMLElement {
    return render(<InteractiveToolView item={makeItem()} threadId="t1" locale="en" />).container
  }

  it('renders the iframe when the binding is active', () => {
    seedBindings('active')
    const container = renderView()
    expect(container.querySelector('iframe')).toBeTruthy()
    expect(container.textContent).not.toContain('App disconnected')
  })

  it('shows the disconnected state + tool-result fallback when the binding is offline', () => {
    seedBindings('offline')
    const container = renderView()
    expect(container.querySelector('iframe')).toBeNull()
    expect(container.textContent).toContain('App disconnected')
    // The recorded structuredResult is still readable as a fallback.
    expect(container.textContent).toContain('cardId')
  })

  it.each([
    ['revoked', 'App access was removed'],
    ['expired', 'App access was removed'],
    ['error', "Couldn't load the app view"]
  ])('maps binding state %s to an explicit degraded surface', (state, title) => {
    seedBindings(state)
    const container = renderView()
    expect(container.querySelector('iframe')).toBeNull()
    expect(container.textContent).toContain(title)
  })

  it('shows the plugin-disabled state when the app is disabled, even with an active binding', () => {
    seedBindings('active')
    useAppBindingStore.setState({ apps: [{ toolNamespace: 'oratorio', enabled: false }] as unknown as AppInfo[] })
    const container = renderView()
    expect(container.querySelector('iframe')).toBeNull()
    expect(container.textContent).toContain('Plugin is disabled')
  })

  it('switches a live card to the degraded state when the binding goes offline', () => {
    seedBindings('active')
    const container = renderView()
    expect(container.querySelector('iframe')).toBeTruthy()

    act(() => seedBindings('offline'))
    expect(container.querySelector('iframe')).toBeNull()
    expect(container.textContent).toContain('App disconnected')
  })

  it('fetches the thread bindings when none are loaded yet', async () => {
    renderView()
    await act(async () => {
      await Promise.resolve()
    })
    expect(sendRequest).toHaveBeenCalledWith(
      'thread/appBindings/list',
      expect.objectContaining({ threadId: 't1', includeRevoked: true })
    )
  })
})
