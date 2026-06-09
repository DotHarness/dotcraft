import { describe, it, expect, vi } from 'vitest'
import { render } from '@testing-library/react'
import { InteractiveToolView } from '../components/conversation/InteractiveToolView'
import type { ConversationItem } from '../types/conversation'

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
  result?: { hostContext?: { theme?: string }; hostCapabilities?: { serverTools?: boolean } }
  params?: { structuredContent?: unknown; _meta?: unknown }
  error?: { code?: number }
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

  it('answers ui/initialize and pushes tool-input + tool-result', () => {
    const { container } = render(<InteractiveToolView item={makeItem()} threadId="t1" locale="en" />)
    const frameWindow = (container.querySelector('iframe') as HTMLIFrameElement).contentWindow!
    const postSpy = vi.spyOn(frameWindow, 'postMessage')

    window.dispatchEvent(
      new MessageEvent('message', {
        data: { jsonrpc: '2.0', id: 1, method: 'ui/initialize', params: {} },
        source: frameWindow
      })
    )

    const messages = postSpy.mock.calls.map((call) => call[0] as BridgeMessage)
    const init = messages.find((m) => m.id === 1)
    expect(init?.result?.hostContext?.theme).toBeDefined()
    expect(init?.result?.hostCapabilities?.serverTools).toBe(false)

    const toolResult = messages.find((m) => m.method === 'ui/notifications/tool-result')
    expect(toolResult?.params?.structuredContent).toEqual({ cardId: 'c1' })
    expect(toolResult?.params?._meta).toEqual({ highlight: true })
    expect(messages.some((m) => m.method === 'ui/notifications/tool-input')).toBe(true)
  })

  it('rejects UI→host requests in the read-only host', () => {
    const { container } = render(<InteractiveToolView item={makeItem()} threadId="t1" locale="en" />)
    const frameWindow = (container.querySelector('iframe') as HTMLIFrameElement).contentWindow!
    const postSpy = vi.spyOn(frameWindow, 'postMessage')

    window.dispatchEvent(
      new MessageEvent('message', {
        data: { jsonrpc: '2.0', id: 9, method: 'tools/call', params: {} },
        source: frameWindow
      })
    )

    const reply = postSpy.mock.calls.map((call) => call[0] as BridgeMessage).find((m) => m.id === 9)
    expect(reply?.error?.code).toBe(-32601)
  })
})
