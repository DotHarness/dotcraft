import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { getThreadConversation, useConversationStore } from '../stores/conversationStore'
import { openThreadState, releaseThreadState } from '../stores/threadState'
import { loadAllTurnItems, loadTurnItems, useTurnItemsLoadStore } from '../stores/turnItemsLoadStore'
import type { ConversationItem, ConversationTurn } from '../types/conversation'

const THREAD = 'paged-items'
const at = (n: number) => new Date(Date.UTC(2026, 0, 1, 0, n)).toISOString()
const item = (id: string, minute: number, extra: Partial<ConversationItem> = {}): ConversationItem => ({
  id, type: 'agentMessage', status: 'completed', text: id, createdAt: at(minute), ...extra
})
const turn = (id = 'turn-1'): ConversationTurn => ({
  id, threadId: THREAD, status: 'completed', startedAt: at(0),
  items: [item('last', 9)], summary: { itemCount: 201 }
})

function installServer(read: (params: any) => Promise<any> | any) {
  const request = vi.fn(async (_method, params, _timeout, options) => {
    const page = await read(params)
    options?.onResponse?.(page)
    return page
  })
  vi.stubGlobal('window', { api: { appServer: { sendRequest: request } } })
  return request
}

function page(items: ConversationItem[], nextCursor: string | null = null) {
  return { data: items.map(value => ({ turnId: 'turn-1', item: {
    ...value, payload: { text: value.text, phase: value.phase }
  } })), nextCursor }
}

describe('Turn Item loading', () => {
  beforeEach(() => {
    releaseThreadState(THREAD)
    openThreadState(THREAD)
    useConversationStore.getState().setTurns([turn()])
    useTurnItemsLoadStore.setState({ statuses: {} })
  })
  afterEach(() => {
    releaseThreadState(THREAD)
    vi.unstubAllGlobals()
  })

  it('keeps a partial Turn, reuses its summary Items, and continues only when requested', async () => {
    const request = installServer(params => params.cursor == null
      ? page([item('first', 1)], 'remaining') : page([item('last', 9)]))

    const a = loadTurnItems(THREAD, 'turn-1')
    expect(loadTurnItems(THREAD, 'turn-1')).toBe(a)
    await a
    expect(request).toHaveBeenCalledTimes(1)
    expect(useConversationStore.getState().turns[0]).toMatchObject({
      summary: { itemCount: 201, nextCursor: 'remaining' },
      items: [{ id: 'first' }, { id: 'last' }]
    })

    await loadTurnItems(THREAD, 'turn-1')
    expect(request.mock.calls[1][1]).toMatchObject({ cursor: 'remaining', limit: 100 })
    expect(useConversationStore.getState().turns[0].summary).toBeUndefined()
    expect(useConversationStore.getState().turns[0].items.map(value => value.id)).toEqual(['first', 'last'])
    await loadTurnItems(THREAD, 'turn-1')
    expect(request).toHaveBeenCalledTimes(2)
  })

  it('reads all remaining pages only for explicit expansion', async () => {
    const request = installServer(params => params.cursor == null
      ? page([item('first', 1)], 'next') : page([item('last', 9)]))
    await loadAllTurnItems(THREAD, 'turn-1')
    expect(request).toHaveBeenCalledTimes(2)
    expect(useConversationStore.getState().turns[0].summary).toBeUndefined()
  })

  it('does not launch queued detail work after its viewport is no longer relevant', async () => {
    useConversationStore.getState().setTurns(Array.from({ length: 5 }, (_, index) => turn(`turn-${index}`)))
    const releases: Array<() => void> = []
    const request = installServer(() => new Promise(resolve => { releases.push(() => resolve(page([]))) }))
    let current = true
    const loading = Promise.all(Array.from({ length: 5 }, (_, index) =>
      loadTurnItems(THREAD, `turn-${index}`, () => current)))
    expect(request).toHaveBeenCalledTimes(4)
    current = false
    for (const release of releases) release()
    await loading
    expect(request).toHaveBeenCalledTimes(4)
    expect(useConversationStore.getState().turns[0].items.map(value => value.id)).toEqual(['last'])
    expect(useTurnItemsLoadStore.getState().statuses).toEqual({})
  })

  it('seeds the live text base synchronously before subsequent deltas are delivered', async () => {
    useConversationStore.setState({ activeTurnId: 'turn-1', activeItemId: 'live', streamingMessage: 'old' })
    installServer(() => page([item('live', 3, { status: 'streaming', text: 'complete base' })], 'next'))
    await loadTurnItems(THREAD, 'turn-1')
    expect(useConversationStore.getState().streamingMessage).toBe('complete base')
    useConversationStore.getState().onAgentMessageDelta(' suffix')
    await vi.waitFor(() => expect(useConversationStore.getState().streamingMessage).toBe('complete base suffix'))
  })

  it('does not regress a completed live terminal when an older page arrives', async () => {
    const finished = item('exec', 2, {
      type: 'toolCall', toolName: 'Exec', status: 'completed', toolCallId: 'call-1',
      executionStatus: 'completed', terminalRunning: false, aggregatedOutput: 'done', exitCode: 0
    })
    useConversationStore.getState().setTurns([{ ...turn(), items: [finished, item('last', 9)] }])
    useConversationStore.getState().applyTurnItemsPage('turn-1', [{
      ...finished, executionStatus: 'inProgress', terminalRunning: true, aggregatedOutput: 'old', exitCode: null
    }], 'more')
    expect(useConversationStore.getState().turns[0].items[0]).toMatchObject({
      executionStatus: 'completed', terminalRunning: false, aggregatedOutput: 'done', exitCode: 0
    })
  })

  it('ignores an in-flight page after the conversation is released and reopened', async () => {
    let finish!: (page: unknown) => void
    installServer(() => new Promise(resolve => { finish = resolve }))
    const old = getThreadConversation(THREAD)
    const loading = loadTurnItems(THREAD, 'turn-1')
    releaseThreadState(THREAD)
    openThreadState(THREAD)
    useConversationStore.getState().setTurns([turn()])
    finish(page([item('obsolete', 1)]))
    await loading
    expect(getThreadConversation(THREAD)).not.toBe(old)
    expect(useConversationStore.getState().turns[0].items.map(value => value.id)).toEqual(['last'])
  })
})
