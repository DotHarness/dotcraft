import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { MessageStream } from '../components/conversation/MessageStream'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import {
  applyThreadHistoryHead,
  beginThreadHistory,
  openThreadHistoryAround,
  restartThreadHistory
} from '../stores/threadHistoryStore'
import { listThreadTurns } from '../utils/threadHistory'
import type { ThreadSummary } from '../types/thread'
import { installDesktopApiMock } from './desktopApiMock'
import {
  createFakeHistoryServer,
  loadedTurnIds,
  openThreadHistory,
  turnRange,
  type FakeHistoryServer
} from './threadHistoryFakeServer'

const THREAD = 'thread-1'
const VIEWPORT_HEIGHT = 600
const GAP_HEIGHT = 144
const NAVIGATION_LISTING_LIMIT = 100

function rect(top: number, height: number): DOMRect {
  return { top, bottom: top + height, height, left: 0, right: 800, width: 800, x: 0, y: top, toJSON: () => ({}) }
}

/** Lays turns and gaps out as a plain vertical stack so scroll geometry behaves like a browser. */
function installStackLayout(stream: HTMLElement, turnHeight: number): void {
  const blocks = (): HTMLElement[] => [...stream.querySelectorAll<HTMLElement>('[data-turn-id], [data-history-gap]')]
  const heightOf = (node: HTMLElement): number => node.hasAttribute('data-history-gap') ? GAP_HEIGHT : turnHeight
  const scrollHeight = (): number => Math.max(VIEWPORT_HEIGHT, blocks().reduce((sum, node) => sum + heightOf(node), 0))
  let scrollTop = 0
  Object.defineProperty(stream, 'clientHeight', { configurable: true, get: () => VIEWPORT_HEIGHT })
  Object.defineProperty(stream, 'scrollHeight', { configurable: true, get: scrollHeight })
  Object.defineProperty(stream, 'scrollTop', {
    configurable: true,
    get: () => scrollTop,
    set: (value: number) => {
      const next = Math.max(0, Math.min(value, scrollHeight() - VIEWPORT_HEIGHT))
      if (next === scrollTop) return
      scrollTop = next
      queueMicrotask(() => stream.dispatchEvent(new Event('scroll')))
    }
  })
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
    if (this === stream) return rect(0, VIEWPORT_HEIGHT)
    let top = -scrollTop
    for (const node of blocks()) {
      if (node === this) return rect(top, heightOf(node))
      top += heightOf(node)
    }
    return rect(0, 0)
  })
}

async function settle(): Promise<void> {
  for (let frame = 0; frame < 3; frame++) {
    await act(async () => { await new Promise((resolve) => setTimeout(resolve, 20)) })
  }
}

async function renderOpenedThread(server: FakeHistoryServer, turnHeight: number): Promise<HTMLElement> {
  useThreadStore.setState({ activeThreadId: THREAD })
  render(<LocaleProvider><MessageStream /></LocaleProvider>)
  const stream = screen.getByTestId('message-stream')
  installStackLayout(stream, turnHeight)
  await settle()
  await act(async () => { await openThreadHistory(server, THREAD) })
  await settle()
  return stream
}

function turnTop(stream: HTMLElement, turnId: string): number {
  const shell = [...stream.querySelectorAll<HTMLElement>('[data-turn-id]')].find((node) => node.dataset.turnId === turnId)
  if (!shell) throw new Error(`${turnId} is not rendered`)
  return shell.getBoundingClientRect().top
}

function serveThread(turnCount: number): FakeHistoryServer {
  const server = createFakeHistoryServer(THREAD, turnCount)
  installDesktopApiMock({
    settings: { get: async () => ({ locale: 'en' }) },
    appServer: { sendRequest: server.request },
    workspace: { readImageAsDataUrl: vi.fn().mockResolvedValue({ dataUrl: '' }) }
  })
  return server
}

describe('MessageStream history', () => {
  let server: FakeHistoryServer

  beforeEach(() => {
    useConversationStore.getState().reset()
    useThreadStore.getState().reset()
    beginThreadHistory(null)
    server = serveThread(12)
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('loads the gap before the oldest segment only once scrolling up brings it within reach', async () => {
    const stream = await renderOpenedThread(server, 300)
    expect(loadedTurnIds()).toEqual(turnRange(8, 12))
    const headReads = server.turnsListCalls().length

    await act(async () => { stream.scrollTop = 1000 })
    await settle()
    expect(server.turnsListCalls()).toHaveLength(headReads)

    await act(async () => { stream.scrollTop = 900 })
    await waitFor(() => expect(loadedTurnIds()).toEqual(turnRange(3, 12)))
    expect(server.turnsListCalls()).toHaveLength(headReads + 1)
  })

  it('keeps the visible turn in place when a page lands above it', async () => {
    const stream = await renderOpenedThread(server, 300)
    const release = server.holdNextTurnsList()
    await act(async () => { stream.scrollTop = 900 })
    const anchorTop = turnTop(stream, 'turn-10')

    release()
    await waitFor(() => expect(loadedTurnIds()).toEqual(turnRange(3, 12)))
    await settle()

    expect(turnTop(stream, 'turn-10')).toBe(anchorTop)
  })

  it('fills a viewport the newest page leaves unscrollable, then stops', async () => {
    await renderOpenedThread(server, 60)

    await waitFor(() => expect(loadedTurnIds()).toEqual(turnRange(3, 12)))
    await settle()

    expect(loadedTurnIds()).toEqual(turnRange(3, 12))
    expect(server.turnsListCalls().filter((call) => call.limit !== NAVIGATION_LISTING_LIMIT)).toHaveLength(2)
  })

  it('reads newer turns into a gap below the viewport only when scrolling down toward it', async () => {
    server = serveThread(30)
    const stream = await renderOpenedThread(server, 300)
    const listing = await listThreadTurns(server.request, THREAD, null, 'descending', 100)
    await act(async () => {
      await openThreadHistoryAround(THREAD, {
        backwardsCursor: listing.backwardsCursor ?? '',
        offset: listing.turns.findIndex((turn) => turn.id === 'turn-15')
      })
    })
    await settle()
    expect(loadedTurnIds()).toEqual([...turnRange(11, 19), ...turnRange(26, 30)])

    await act(async () => { stream.scrollTop = 1800 })
    await settle()
    expect(loadedTurnIds()).toEqual([...turnRange(11, 19), ...turnRange(26, 30)])

    await act(async () => { stream.scrollTop = 1900 })
    await waitFor(() => expect(loadedTurnIds()).toEqual([...turnRange(11, 24), ...turnRange(26, 30)]))
  })

  it('offers the originating thread only when the first turn of the thread is loaded', async () => {
    useThreadStore.setState({
      activeThreadId: THREAD,
      threadList: [
        { id: THREAD, displayName: 'Spawned', metadata: { spawnedFromThreadId: 'parent' } },
        { id: 'parent', displayName: 'Parent plan', metadata: {} }
      ] as unknown as ThreadSummary[]
    })
    useConversationStore.setState({
      turns: [{
        id: 'turn-5',
        threadId: THREAD,
        status: 'completed',
        startedAt: '2026-01-01T00:05:00.000Z',
        items: [{ id: 'turn-5-user', type: 'userMessage', status: 'completed', text: 'Continue', createdAt: '2026-01-01T00:05:00.000Z' }]
      }]
    })
    beginThreadHistory(THREAD)
    applyThreadHistoryHead(THREAD, [{ id: 'turn-5' }], 'older-turns')

    render(<LocaleProvider><MessageStream /></LocaleProvider>)
    expect(screen.queryByRole('button', { name: /Parent plan/ })).toBeNull()

    act(() => { restartThreadHistory(THREAD, [{ id: 'turn-5' }], null) })
    expect(screen.getByRole('button', { name: /Parent plan/ })).toBeInTheDocument()
  })
})
