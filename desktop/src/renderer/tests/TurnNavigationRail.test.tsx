import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { MessageStream } from '../components/conversation/MessageStream'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { beginThreadHistory } from '../stores/threadHistoryStore'
import { useTurnBookmarkStore } from '../stores/turnBookmarkStore'
import type { ConversationTurn } from '../types/conversation'
import type { Thread } from '../types/thread'
import { installDesktopApiMock } from './desktopApiMock'
import {
  createFakeHistoryServer,
  loadedTurnIds,
  openThreadHistory,
  turnRange,
  userMessageAndReply,
  type FakeHistoryServer
} from './threadHistoryFakeServer'

const THREAD = 'thread-1'
const VIEWPORT_HEIGHT = 600
const GAP_HEIGHT = 144
const RAIL_NAME = 'User messages'

let scrolledTo: HTMLElement[] = []
const settingsSet = vi.fn()

function rect(top: number, height: number, left = 0, width = 800): DOMRect {
  return { top, bottom: top + height, height, left, right: left + width, width, x: left, y: top, toJSON: () => ({}) }
}

/**
 * Stacks turns and gaps in the scroll element and places the reading column `columnLeft`
 * px from its leading edge. Programmatic scrolls do not emit scroll events.
 */
function installLayout(stream: HTMLElement, turnHeight: number, columnLeft = 120): void {
  const blocks = (): HTMLElement[] => [...stream.querySelectorAll<HTMLElement>('[data-turn-id], [data-history-gap]')]
  const heightOf = (node: HTMLElement): number => node.hasAttribute('data-history-gap') ? GAP_HEIGHT : turnHeight
  const scrollHeight = (): number => Math.max(VIEWPORT_HEIGHT, blocks().reduce((sum, node) => sum + heightOf(node), 0))
  let scrollTop = 0
  Object.defineProperty(stream, 'clientHeight', { configurable: true, get: () => VIEWPORT_HEIGHT })
  Object.defineProperty(stream, 'scrollHeight', { configurable: true, get: scrollHeight })
  Object.defineProperty(stream, 'scrollTop', {
    configurable: true,
    get: () => scrollTop,
    set: (value: number) => { scrollTop = Math.max(0, Math.min(value, scrollHeight() - VIEWPORT_HEIGHT)) }
  })
  vi.spyOn(HTMLElement.prototype, 'getBoundingClientRect').mockImplementation(function (this: HTMLElement) {
    if (this === stream) return rect(0, VIEWPORT_HEIGHT, 0, 1000)
    const block = this.closest<HTMLElement>('[data-turn-id], [data-history-gap]')
    if (!block || !stream.contains(block)) return rect(0, 0, columnLeft)
    let top = -scrollTop
    for (const node of blocks()) {
      if (node === block) break
      top += heightOf(node)
    }
    return this === block ? rect(top, heightOf(block)) : rect(top, 40)
  })
}

function userMessageOf(turnId: string): HTMLElement {
  const message = document.querySelector<HTMLElement>(`[data-turn-id="${turnId}"] [data-user-message-id]`)
  if (!message) throw new Error(`${turnId} has no rendered user message`)
  return message
}

function loadedTurns(count: number): ConversationTurn[] {
  return Array.from({ length: count }, (_unused, index) => ({
    id: `turn-${index + 1}`,
    threadId: THREAD,
    status: 'completed' as const,
    startedAt: '2026-01-01T00:00:00.000Z',
    items: [
      { id: `turn-${index + 1}-user`, type: 'userMessage' as const, status: 'completed' as const, text: `ask ${index + 1}`, createdAt: '2026-01-01T00:00:00.000Z' },
      { id: `turn-${index + 1}-reply`, type: 'agentMessage' as const, status: 'completed' as const, text: `reply ${index + 1}`, createdAt: '2026-01-01T00:00:00.000Z' }
    ]
  }))
}

async function settle(): Promise<void> {
  for (let frame = 0; frame < 3; frame++) {
    await act(async () => { await new Promise((resolve) => setTimeout(resolve, 20)) })
  }
}

async function renderLoadedThread(turnCount: number, columnLeft = 120): Promise<HTMLElement> {
  useConversationStore.setState({ turns: loadedTurns(turnCount) })
  render(<LocaleProvider><MessageStream /><textarea aria-label="Composer" /></LocaleProvider>)
  const stream = screen.getByTestId('message-stream')
  installLayout(stream, 200, columnLeft)
  await settle()
  return stream
}

async function renderHistoryThread(server: FakeHistoryServer): Promise<HTMLElement> {
  render(<LocaleProvider><MessageStream /></LocaleProvider>)
  const stream = screen.getByTestId('message-stream')
  installLayout(stream, 1000)
  await settle()
  await act(async () => { await openThreadHistory(server, THREAD) })
  await settle()
  return stream
}

function jumpButton(position: number): HTMLElement {
  return within(screen.getByRole('navigation', { name: RAIL_NAME }))
    .getByRole('button', { name: `Jump to user message ${position}` })
}

function bookmarkedButtons(): HTMLElement[] {
  return within(screen.getByRole('navigation', { name: RAIL_NAME }))
    .queryAllByRole('button', { name: /^Jump to user message \d+, bookmarked turn$/ })
}

function pressAlt(key: 'ArrowUp' | 'ArrowDown', target: Element = document.body): void {
  fireEvent.keyDown(target, { key, altKey: true })
}

describe('turn navigation rail', () => {
  let server: FakeHistoryServer

  beforeEach(() => {
    useConversationStore.getState().reset()
    useThreadStore.getState().reset()
    beginThreadHistory(null)
    useThreadStore.setState({ activeThreadId: THREAD })
    useTurnBookmarkStore.setState({ loaded: false, byThread: {} })
    server = createFakeHistoryServer(THREAD, 30, userMessageAndReply)
    settingsSet.mockReset()
    settingsSet.mockResolvedValue(undefined)
    installDesktopApiMock({
      settings: { get: async () => ({ locale: 'en' }), set: settingsSet },
      shell: { listEditors: vi.fn().mockResolvedValue([]) },
      appServer: { sendRequest: server.request },
      workspace: { readImageAsDataUrl: vi.fn().mockResolvedValue({ dataUrl: '' }) }
    })
    scrolledTo = []
    Object.defineProperty(HTMLElement.prototype, 'scrollIntoView', {
      configurable: true,
      value: function (this: HTMLElement) { scrolledTo.push(this) }
    })
  })

  afterEach(() => {
    vi.restoreAllMocks()
    vi.unstubAllGlobals()
    delete (HTMLElement.prototype as Partial<HTMLElement>).scrollIntoView
  })

  it('appears only for four or more user messages with room beside the reading column', async () => {
    await renderLoadedThread(3)
    expect(screen.queryByRole('navigation', { name: RAIL_NAME })).toBeNull()

    act(() => { useConversationStore.setState({ turns: loadedTurns(4) }) })
    await waitFor(() => expect(screen.getByRole('navigation', { name: RAIL_NAME })).toBeInTheDocument())
    expect(within(screen.getByRole('navigation', { name: RAIL_NAME })).getAllByRole('button')).toHaveLength(4)
  })

  it('stays hidden when the reading column leaves less than 48px of gutter', async () => {
    await renderLoadedThread(6, 40)

    expect(screen.queryByRole('navigation', { name: RAIL_NAME })).toBeNull()
  })

  it('marks the entries whose turns intersect the viewport as current', async () => {
    const observers: Array<{ callback: IntersectionObserverCallback; targets: Element[] }> = []
    vi.stubGlobal('IntersectionObserver', class {
      readonly targets: Element[] = []
      constructor(readonly callback: IntersectionObserverCallback) { observers.push(this) }
      observe(target: Element): void { this.targets.push(target) }
      unobserve(): void {}
      disconnect(): void {}
    })
    const stream = await renderLoadedThread(5)
    await waitFor(() => expect(observers.at(-1)?.targets).toHaveLength(5))
    expect(jumpButton(5)).toHaveAttribute('aria-current', 'true')

    const observer = observers.at(-1)!
    const shells = ['turn-2', 'turn-3'].map((id) => stream.querySelector(`[data-turn-id="${id}"]`)!)
    act(() => {
      observer.callback(
        shells.map((target) => ({ target, isIntersecting: true }) as unknown as IntersectionObserverEntry),
        observer as unknown as IntersectionObserver
      )
    })

    expect([1, 2, 3, 4, 5].map((position) => jumpButton(position).getAttribute('aria-current')))
      .toEqual([null, 'true', 'true', null, null])
  })

  it('describes a focused entry with its preview card', async () => {
    await renderLoadedThread(5)
    await waitFor(() => jumpButton(2))

    act(() => { jumpButton(2).focus() })

    await waitFor(() => expect(jumpButton(2)).toHaveAttribute('aria-describedby'))
    const card = document.getElementById(jumpButton(2).getAttribute('aria-describedby') ?? '')
    expect(card).toHaveAttribute('role', 'dialog')
    expect(card).toHaveTextContent('ask 2')
    expect(card).toHaveTextContent('reply 2')
    expect(jumpButton(3)).not.toHaveAttribute('aria-describedby')
  })

  it('scrolls to the user message of a clicked entry', async () => {
    await renderLoadedThread(5)
    await waitFor(() => jumpButton(3))

    fireEvent.click(jumpButton(3))

    expect(scrolledTo[0]).toBe(userMessageOf('turn-3'))
  })

  it('reveals an unloaded turn around its listed position before scrolling to it', async () => {
    await renderHistoryThread(server)
    await waitFor(() => expect(within(screen.getByRole('navigation', { name: RAIL_NAME })).getAllByRole('button')).toHaveLength(30))
    expect(loadedTurnIds()).not.toContain('turn-15')

    fireEvent.click(jumpButton(15))

    await waitFor(() => expect(scrolledTo).toContain(userMessageOf('turn-15')))
    expect(loadedTurnIds()).toEqual([...turnRange(11, 19), ...turnRange(26, 30)])
  })

  it('keeps keyboard focus on a placeholder entry while its preview loads', async () => {
    await renderHistoryThread(server)
    await waitFor(() => jumpButton(12))

    act(() => { jumpButton(12).focus() })

    await waitFor(() => expect(document.getElementById(jumpButton(12).getAttribute('aria-describedby') ?? ''))
      .toHaveTextContent('turn-12 ask'))
    expect(document.activeElement).toBe(jumpButton(12))
  })

  it('moves between user messages with Alt+ArrowUp and Alt+ArrowDown', async () => {
    const stream = await renderLoadedThread(5)
    stream.scrollTop = 400

    pressAlt('ArrowUp')
    pressAlt('ArrowDown')

    expect(scrolledTo.slice(0, 2)).toEqual([userMessageOf('turn-2'), userMessageOf('turn-4')])
  })

  it('leaves Alt+Arrow keys to a focused text input', async () => {
    const stream = await renderLoadedThread(5)
    stream.scrollTop = 400

    pressAlt('ArrowUp', screen.getByRole('textbox', { name: 'Composer' }))

    expect(scrolledTo).toEqual([])
  })

  it('reveals an unloaded previous turn from the keyboard', async () => {
    const stream = await renderHistoryThread(server)
    await waitFor(() => expect(within(screen.getByRole('navigation', { name: RAIL_NAME })).getAllByRole('button')).toHaveLength(30))
    stream.scrollTop = GAP_HEIGHT + 10

    pressAlt('ArrowUp')

    await waitFor(() => expect(scrolledTo).toContain(userMessageOf('turn-25')))
    expect(loadedTurnIds()).toEqual(turnRange(21, 30))
  })

  describe('bookmarks', () => {
    const BOOKMARK_KEY = 'c:/fixtures/ws::thread-1'

    beforeEach(() => {
      useThreadStore.setState({ activeThread: { id: THREAD, workspacePath: 'C:\\fixtures\\ws' } as Thread })
    })

    async function openCard(position: number): Promise<HTMLElement> {
      await waitFor(() => jumpButton(position))
      act(() => { jumpButton(position).focus() })
      return await screen.findByRole('dialog')
    }

    it('bookmarks a turn from its preview card without jumping to it', async () => {
      await renderLoadedThread(5)
      const card = await openCard(2)
      expect(within(card).getByRole('button', { name: 'Bookmark turn' })).toHaveAttribute('aria-pressed', 'false')

      fireEvent.click(within(card).getByRole('button', { name: 'Bookmark turn' }))

      expect(settingsSet).toHaveBeenLastCalledWith({ turnBookmarksByThread: { [BOOKMARK_KEY]: ['turn-2:turn-2-user'] } })
      expect(within(card).getByRole('button', { name: 'Remove bookmark' })).toHaveAttribute('aria-pressed', 'true')
      expect(bookmarkedButtons()).toEqual([screen.getByRole('button', { name: 'Jump to user message 2, bookmarked turn' })])
      expect(scrolledTo).toEqual([])

      fireEvent.click(within(card).getByRole('button', { name: 'Remove bookmark' }))

      expect(settingsSet).toHaveBeenLastCalledWith({ turnBookmarksByThread: { [BOOKMARK_KEY]: [] } })
      expect(bookmarkedButtons()).toEqual([])
    })

    it('marks loaded bookmarks and ignores those whose entry no longer exists', async () => {
      useTurnBookmarkStore.getState().hydrate({ [BOOKMARK_KEY]: ['turn-3:turn-3-user', 'turn-9:turn-9-user'] })

      await renderLoadedThread(5)

      await waitFor(() => expect(bookmarkedButtons()).toHaveLength(1))
      expect(bookmarkedButtons()[0]).toHaveAccessibleName('Jump to user message 3, bookmarked turn')
      expect(settingsSet).not.toHaveBeenCalled()
    })

    it('marks an unloaded turn that holds a bookmark before its preview loads', async () => {
      useTurnBookmarkStore.getState().hydrate({ [BOOKMARK_KEY]: ['turn-2:turn-2-user'] })

      await renderHistoryThread(server)

      await waitFor(() => expect(bookmarkedButtons()).toHaveLength(1))
      expect(bookmarkedButtons()[0]).toHaveAccessibleName('Jump to user message 2, bookmarked turn')
      expect(loadedTurnIds()).not.toContain('turn-2')
    })

    it('does not offer a bookmark for a message the server has not confirmed', async () => {
      await renderLoadedThread(5)
      const turns = loadedTurns(5)
      turns[4] = {
        ...turns[4],
        id: 'local-turn-c5',
        status: 'running',
        items: [{ ...turns[4].items[0], id: 'local-c5', clientUserMessageId: 'c5' }]
      }
      act(() => { useConversationStore.setState({ turns }) })

      const card = await openCard(5)

      expect(within(card).getByRole('button', { name: 'Bookmark turn' })).toBeDisabled()
    })

    it('keeps the preview card open while the pointer moves from the rail into it', async () => {
      await renderLoadedThread(5)
      await waitFor(() => jumpButton(2))
      fireEvent.pointerMove(jumpButton(2))
      const card = await screen.findByRole('dialog')

      fireEvent.pointerLeave(screen.getByRole('navigation', { name: RAIL_NAME }).firstElementChild!)
      fireEvent.pointerEnter(card)
      await act(async () => { await new Promise((resolve) => setTimeout(resolve, 200)) })

      expect(screen.getByRole('dialog')).toBe(card)

      fireEvent.pointerLeave(card)

      await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    })

    it('moves keyboard focus from an entry to its bookmark toggle and back on Escape', async () => {
      await renderLoadedThread(5)
      const card = await openCard(2)
      const toggle = within(card).getByRole('button', { name: 'Bookmark turn' })

      fireEvent.keyDown(jumpButton(2), { key: 'Tab' })
      expect(document.activeElement).toBe(toggle)

      fireEvent.keyDown(toggle, { key: 'Escape' })

      await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
      expect(document.activeElement).toBe(jumpButton(2))
    })
  })
})
