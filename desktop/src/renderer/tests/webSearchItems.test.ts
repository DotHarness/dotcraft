import { beforeEach, describe, expect, it } from 'vitest'
import { wireItemToConversationItem, type ConversationItem } from '../types/conversation'
import { useConversationStore } from '../stores/conversationStore'
import { getWebSearchDetail, getWebSearchDisplay, getWebSearchState, getWebSearchToolDisplay } from '../utils/webSearchDisplay'
import { isToolItemLive } from '../utils/toolCallAggregation'

const s = () => useConversationStore.getState()

const RESULT = {
  type: 'text_result',
  ref_id: 'turn0search0',
  url: 'https://www.example.com/release',
  title: 'Release notes',
  snippet: 'What changed',
  domain: 'example.com'
}

function webSearchWire(id: string, payload: Record<string, unknown>): Record<string, unknown> {
  return { id, type: 'webSearch', payload: { callId: `call-${id}`, ...payload }, createdAt: '2026-10-16T00:00:00.000Z' }
}

function webSearchItem(payload: Record<string, unknown>, status: ConversationItem['status'] = 'completed'): ConversationItem {
  return { ...wireItemToConversationItem(webSearchWire('ws', payload)), status }
}

beforeEach(() => {
  s().reset()
})

describe('webSearch item mapping', () => {
  it('maps the payload, keeping only object results, including the PascalCase persisted type and unknown actions', () => {
    const item = wireItemToConversationItem(webSearchWire('ws-1', {
      status: 'completed',
      query: 'release notes ...',
      action: { type: 'search', query: null, queries: ['release notes', 'changelog'] },
      results: [RESULT, 'ignored', null],
      output: 'model text',
      arguments: { search_query: [{ q: 'release notes' }] }
    }))

    expect(item.type).toBe('webSearch')
    expect(item.toolCallId).toBe('call-ws-1')
    expect(item.webSearch).toEqual({
      status: 'completed',
      query: 'release notes ...',
      action: { type: 'search', query: undefined, queries: ['release notes', 'changelog'] },
      results: [RESULT]
    })
    expect(item.result).toBeUndefined()

    const persisted = wireItemToConversationItem({
      id: 'ws-2',
      type: 'WebSearch',
      payload: { callId: 'c', status: 'failed', query: '', action: { type: 'weather' }, results: null, errorMessage: 'boom' }
    })

    expect(persisted.type).toBe('webSearch')
    expect(persisted.webSearch?.action).toEqual({ type: 'other' })
    expect(persisted.webSearch?.results).toEqual([])
    expect(persisted.errorMessage).toBe('boom')
  })
})

describe('webSearch item lifecycle in the conversation store', () => {
  it('starts live and completes in place', () => {
    s().onTurnStarted({ id: 'turn-1', threadId: 'thread-1', status: 'running', items: [], startedAt: '2026-10-16T00:00:00.000Z' })
    s().onItemStarted({
      turnId: 'turn-1',
      item: webSearchWire('ws-live', { status: 'inProgress', query: '', action: null, results: null })
    })

    let item = s().turns[0].items.find((candidate) => candidate.id === 'ws-live')
    expect(item?.status).toBe('started')
    expect(item?.webSearch?.status).toBe('inProgress')
    expect(item && isToolItemLive(item, { turnRunning: true })).toBe(true)

    s().onItemCompleted({
      turnId: 'turn-1',
      item: webSearchWire('ws-live', {
        status: 'completed',
        query: 'release notes',
        action: { type: 'search', query: 'release notes' },
        results: [RESULT]
      })
    })

    const items = s().turns[0].items.filter((candidate) => candidate.id === 'ws-live')
    expect(items).toHaveLength(1)
    item = items[0]
    expect(item.status).toBe('completed')
    expect(item.webSearch?.status).toBe('completed')
    expect(item.webSearch?.results).toEqual([RESULT])
    expect(isToolItemLive(item, { turnRunning: true })).toBe(false)
  })

  it('ignores a start that arrives after completion', () => {
    s().onTurnStarted({ id: 'turn-1', threadId: 'thread-1', status: 'running', items: [], startedAt: '2026-10-16T00:00:00.000Z' })
    s().onItemCompleted({
      turnId: 'turn-1',
      item: webSearchWire('ws-early', { status: 'completed', query: 'q', action: { type: 'search', query: 'q' }, results: [] })
    })
    s().onItemStarted({
      turnId: 'turn-1',
      item: webSearchWire('ws-early', { status: 'inProgress', query: '', action: null, results: null })
    })

    const items = s().turns[0].items.filter((candidate) => candidate.id === 'ws-early')
    expect(items).toHaveLength(1)
    expect(items[0].status).toBe('completed')
    expect(items[0].webSearch?.status).toBe('completed')
  })
})

describe('webSearch display model', () => {
  it('derives the detail from the action before the summary query', () => {
    const detail = (action: Record<string, unknown> | null, query = 'summary'): string =>
      getWebSearchDetail(webSearchItem({ status: 'completed', query, action, results: [] }).webSearch!)

    expect(detail({ type: 'search', query: 'dotnet 10' })).toBe('dotnet 10')
    expect(detail({ type: 'search', queries: [' ', 'first', 'second'] })).toBe('first ...')
    expect(detail({ type: 'search', queries: ['only'] })).toBe('only')
    expect(detail({ type: 'search', query: 'release site:www.github.com OR site:learn.microsoft.com' }))
      .toBe('release | github.com · learn.microsoft.com')
    expect(detail({ type: 'search', query: 'site:github.com' })).toBe('site:github.com')
    expect(detail({ type: 'openPage', url: 'https://example.com/a' })).toBe('https://example.com/a')
    expect(detail({ type: 'findInPage', url: 'https://example.com/a', pattern: 'price' })).toBe("'price' in https://example.com/a")
    expect(detail({ type: 'findInPage', url: null, pattern: 'price' })).toBe("'price'")
    expect(detail({ type: 'other' })).toBe('summary')
    expect(detail(null, '')).toBe('')
  })

  it('maps lifecycle status to the row state', () => {
    expect(getWebSearchState(webSearchItem({ status: 'inProgress', query: '', action: null, results: null }, 'started'))).toBe('running')
    expect(getWebSearchState(webSearchItem({ status: 'inProgress', query: '', action: null, results: null }))).toBe('completed')
    expect(getWebSearchState(webSearchItem({ status: 'completed', query: 'q', action: null, results: [] }))).toBe('completed')
    expect(getWebSearchState(webSearchItem({ status: 'failed', query: 'q', action: null, results: [] }))).toBe('failed')
  })

  it('lists sources only for a completed search and the error only for a failure', () => {
    const completed = getWebSearchDisplay(webSearchItem({
      status: 'completed',
      query: 'q',
      action: { type: 'search', query: 'q' },
      results: [RESULT, { title: 'No url' }]
    }), 'en')
    expect(completed).toEqual({
      state: 'completed',
      label: 'Searched the web',
      detail: ' for q',
      sources: [{ url: 'https://www.example.com/release', title: 'Release notes', domain: 'example.com' }],
      errorMessage: ''
    })

    const failed = getWebSearchDisplay(webSearchItem({
      status: 'failed',
      query: 'q',
      action: null,
      results: [RESULT],
      errorMessage: ' Web search request failed '
    }), 'en')
    expect(failed.label).toBe('Failed: Searched the web')
    expect(failed.sources).toEqual([])
    expect(failed.errorMessage).toBe('Web search request failed')

    const running = getWebSearchDisplay(webSearchItem({ status: 'inProgress', query: '', action: null, results: null }, 'started'), 'en')
    expect(running).toMatchObject({ state: 'running', label: 'Searching the web', detail: '', sources: [], errorMessage: '' })
  })

  it('treats the built-in WebSearch tool as a search on its query argument', () => {
    const results = { results: [{ title: 'Docs', url: 'https://docs.dotcraft.ai/start' }] }
    const tool = (patch: Partial<ConversationItem>): Partial<ConversationItem> => ({ arguments: { query: 'release site:github.com' }, ...patch })

    expect(getWebSearchToolDisplay(tool({ structuredResult: results }), { running: true, success: true }, 'en')).toMatchObject({
      state: 'running',
      label: 'Searching the web',
      detail: ' for release | github.com',
      sources: []
    })
    expect(getWebSearchToolDisplay(tool({ arguments: undefined, argumentsPreview: '{"query":"dotn' }), { running: true, success: true }, 'en').detail)
      .toBe(' for dotn')
    expect(getWebSearchToolDisplay(tool({ result: 'Web search results', structuredResult: results }), { running: false, success: true }, 'en')).toMatchObject({
      state: 'completed',
      label: 'Searched the web',
      sources: [{ title: 'Docs', url: 'https://docs.dotcraft.ai/start', domain: 'docs.dotcraft.ai' }],
      errorMessage: ''
    })
    expect(getWebSearchToolDisplay(tool({ result: 'Request failed: rate limited' }), { running: false, success: false }, 'en'))
      .toMatchObject({ state: 'failed', label: 'Failed: Searched the web', errorMessage: 'Request failed: rate limited' })
  })
})
