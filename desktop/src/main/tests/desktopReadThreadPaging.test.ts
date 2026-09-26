import { describe, expect, it } from 'vitest'
import { clientFor, historyClient, item, read, type RecordValue } from './readThreadTestHelpers'

describe('ReadThread turn pagination', () => {
  it('defaults to the newest complete turn and returns older turns through one cursor', async () => {
    const turns = [
      { id: 'turn-2', items: Array.from({ length: 1_001 }, (_, index) => item(`item-${index}`, 'agentMessage', { text: `${index}` })) },
      { id: 'turn-1', items: [item('first', 'userMessage', { text: 'earlier' })] }
    ]
    const { client, sendRequest } = historyClient(turns)
    const { response, data } = await read(client)
    expect(response.success).toBe(true)
    expect(data.schemaVersion).toBe(1)
    expect(data.page).toEqual({ order: 'newest_first', limit: 1, nextCursor: 'turns:1', hasMore: true })
    expect(data.turns.map((turn: RecordValue) => turn.id)).toEqual(['turn-2'])
    expect(data.turns[0].items.map((item: RecordValue) => item.id)).toEqual(turns[0].items.map(item => item.id))
    expect(sendRequest.mock.calls.filter(([method]) => method === 'thread/items/list')).toHaveLength(3)
    for (const [method, params] of sendRequest.mock.calls) {
      expect(['thread/read', 'thread/turns/list', 'thread/items/list']).toContain(method)
      if (method === 'thread/items/list') expect(params).toMatchObject({ turnId: 'turn-2', limit: 500, sortDirection: 'ascending' })
    }
    const older = await read(client, { threadId: 'thread-1', cursor: data.page.nextCursor })
    expect(older.data.turns.map((turn: RecordValue) => turn.id)).toEqual(['turn-1'])
    expect(older.data.turns[0].items[0].text).toBe('earlier')
    expect(older.data.page).toMatchObject({ nextCursor: null, hasMore: false })
  })

  it('preserves newest-first turn order and chronological item order across a multi-turn page', async () => {
    const { client } = historyClient([3, 2, 1].map(index => ({
      id: `turn-${index}`,
      items: [item('user', 'userMessage', { text: 'question' }), item('agent', 'agentMessage', { text: 'answer' })]
    })))
    const { data } = await read(client, { threadId: 'thread-1', turnLimit: 3 })
    expect(data.turns.map((turn: RecordValue) => turn.id)).toEqual(['turn-3', 'turn-2', 'turn-1'])
    for (const turn of data.turns) expect(turn.items.map((item: RecordValue) => item.id)).toEqual(['user', 'agent'])
  })

  it('continues across empty item pages when a next cursor exists', async () => {
    const { client, sendRequest } = clientFor(async (method, params) => {
      if (method === 'thread/read') return { thread: { id: 'thread-1' } }
      if (method === 'thread/turns/list') return { data: [{ id: 'turn-1' }] }
      return params.cursor == null ? { data: [], nextCursor: 'after-empty' } : {
        data: [{ turnId: 'turn-1', item: item('answer', 'agentMessage', { text: 'complete' }) }]
      }
    })
    const { data } = await read(client)
    expect(data.turns[0].items[0].text).toBe('complete')
    expect(sendRequest.mock.calls.filter(([method]) => method === 'thread/items/list')).toHaveLength(2)
  })

  it('returns an exhausted empty page without requesting items for an empty thread', async () => {
    const { client, sendRequest } = historyClient([])
    const { data } = await read(client)
    expect(data.turns).toEqual([])
    expect(data.page).toEqual({ order: 'newest_first', limit: 1, nextCursor: null, hasMore: false })
    expect(sendRequest.mock.calls.map(([method]) => method)).toEqual(['thread/read', 'thread/turns/list'])
  })

  it('returns ThreadNotFound for a missing header without fetching items', async () => {
    const { client, sendRequest } = clientFor(async method => method === 'thread/read' ? {} : { data: [] })
    const { response } = await read(client)
    expect(response).toMatchObject({ success: false, errorCode: 'ThreadNotFound' })
    expect(sendRequest.mock.calls.map(([method]) => method)).toEqual(['thread/read', 'thread/turns/list'])
  })

  it('limits concurrent turn hydration to five without changing result order', async () => {
    const turns = Array.from({ length: 10 }, (_, index) => ({ id: `turn-${index}` }))
    let active = 0
    let maximum = 0
    const { client } = clientFor(async method => {
      if (method === 'thread/read') return { thread: { id: 'thread-1' } }
      if (method === 'thread/turns/list') return { data: turns }
      active++
      maximum = Math.max(maximum, active)
      await new Promise(resolve => setTimeout(resolve, 0))
      active--
      return { data: [] }
    })
    const { data } = await read(client, { threadId: 'thread-1', turnLimit: 10 })
    expect(maximum).toBe(5)
    expect(data.turns.map((turn: RecordValue) => turn.id)).toEqual(turns.map(turn => turn.id))
  })

  it.each([['a', 'a'], ['a', 'b', 'a']])('fails repeated item cursors %j', async (...cursors) => {
    let index = 0
    const { client } = clientFor(async method => {
      if (method === 'thread/read') return { thread: { id: 'thread-1' } }
      if (method === 'thread/turns/list') return { data: [{ id: 'turn-1' }] }
      return { data: [], nextCursor: cursors[index++] ?? null }
    })
    const { response } = await read(client)
    expect(response.success).toBe(false)
    expect(response.errorCode).toBe('AppServerRequestFailed')
    expect(response.structuredContent).toBeUndefined()
  })

  it('fails the call if a later item page fails instead of exposing a partial turn', async () => {
    const { client } = clientFor(async (method, params) => {
      if (method === 'thread/read') return { thread: { id: 'thread-1' } }
      if (method === 'thread/turns/list') return { data: [{ id: 'turn-1' }] }
      if (params.cursor) throw new Error('History unavailable')
      return { data: [{ turnId: 'turn-1', item: item('partial', 'agentMessage', { text: 'partial' }) }], nextCursor: 'next' }
    })
    const { response } = await read(client)
    expect(response.success).toBe(false)
    expect(response.structuredContent).toBeUndefined()
    expect(response.contentItems).toBeUndefined()
  })

  it('rejects a history item from another turn', async () => {
    const { client } = clientFor(async method => {
      if (method === 'thread/read') return { thread: { id: 'thread-1' } }
      if (method === 'thread/turns/list') return { data: [{ id: 'turn-1' }] }
      return { data: [{ turnId: 'wrong-turn', item: item('wrong', 'userMessage', { text: 'wrong' }) }] }
    })
    expect((await read(client)).response.success).toBe(false)
  })

  it('propagates server cursor failures and rejects non-advancing turn cursors', async () => {
    for (const invalidCursor of [true, false]) {
      const { client } = clientFor(async method => {
        if (method === 'thread/read') return { thread: { id: 'thread-1' } }
        if (invalidCursor) throw new Error('InvalidParams: cursor scope mismatch')
        return { data: [], nextCursor: 'cursor' }
      })
      const { response } = await read(client, { threadId: 'thread-1', cursor: 'cursor' })
      expect(response.success).toBe(false)
      expect(response.structuredContent).toBeUndefined()
    }
  })

  it.each([
    null, [], 'thread-1', {}, { threadId: '' }, { threadId: ' ' },
    ...['turnCursor', 'itemCursor', 'itemLimit', 'unexpected'].map(key => ({ threadId: 'thread-1', [key]: 1 })),
    ...[0, -1, 11, 1.5, '1', null].map(turnLimit => ({ threadId: 'thread-1', turnLimit })),
    ...[-1, 20_001, 0.5, '100', null].map(maxOutputCharsPerItem => ({ threadId: 'thread-1', maxOutputCharsPerItem })),
    { threadId: 'thread-1', includeOutputs: 'true' },
    { threadId: 'thread-1', cursor: '' }, { threadId: 'thread-1', cursor: null }
  ])('rejects invalid and obsolete arguments without making requests: %j', async args => {
    const { client, sendRequest } = historyClient([])
    const { response } = await read(client, args)
    expect(response).toMatchObject({ success: false, errorCode: 'InvalidArguments' })
    expect(sendRequest).not.toHaveBeenCalled()
  })
})
