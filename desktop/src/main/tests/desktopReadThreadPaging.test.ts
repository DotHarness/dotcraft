import { describe, expect, it } from 'vitest'
import { JsonRpcError } from '@dotcraft/sdk/wire'
import { callHistory, clientFor, historyClient, item, read, type RecordValue } from './readThreadTestHelpers'

describe('Desktop history discovery', () => {
  it('requests summary Turns directly and navigates their cursor without hydrating Items', async () => {
    const first = item('user', 'userMessage', { text: 'question' })
    const final = item('answer', 'agentMessage', { text: 'answer' })
    const { client, sendRequest } = historyClient([
      { id: 'turn-2', items: [first, ...Array.from({ length: 1_001 }, (_, i) => item(`tool-${i}`, 'toolResult', {})), final],
        summaryItems: [first, final] },
      { id: 'turn-1', items: [item('earlier', 'userMessage', { text: 'earlier' })] }
    ])
    const { response, data } = await read(client)
    expect(response.success).toBe(true)
    expect(data.page).toEqual({ order: 'newest_first', limit: 1, nextCursor: 'turns:1', hasMore: true })
    expect(data.turns[0]).toMatchObject({ id: 'turn-2', itemsView: 'summary', itemCount: 1_003, itemsTruncated: true })
    expect(data.turns[0].items.map((entry: RecordValue) => entry.id)).toEqual(['user', 'answer'])
    expect(sendRequest.mock.calls).toEqual([
      ['thread/read', { threadId: 'thread-1' }],
      ['thread/turns/list', { threadId: 'thread-1', limit: 1, sortDirection: 'descending', itemsView: 'summary' }]
    ])
    const older = await read(client, { threadId: 'thread-1', cursor: data.page.nextCursor, turnLimit: 10 })
    expect(older.data.turns.map((turn: RecordValue) => turn.id)).toEqual(['turn-1'])
    expect(older.data.page).toMatchObject({ nextCursor: null, hasMore: false })
    expect(JSON.parse(response.contentItems![0].text!)).toEqual(data)
  })

  it('requests metadata only with includeItems false', async () => {
    const { client, sendRequest } = historyClient([{ id: 'turn-1', items: [item('body', 'userMessage', { text: 'hidden body' })] }])
    const { data } = await read(client, { threadId: 'thread-1', includeItems: false })
    expect(sendRequest.mock.calls[1][1]).toMatchObject({ itemsView: 'none' })
    expect(data.turns[0]).toEqual({ id: 'turn-1', itemsView: 'none', items: [] })
    expect(sendRequest).toHaveBeenCalledTimes(2)
  })

  it('reads the header and Turn page concurrently', async () => {
    let releaseHeader!: (value: unknown) => void
    let pageStarted = false
    const { client } = clientFor(async method => {
      if (method === 'thread/read') return new Promise(resolve => { releaseHeader = resolve })
      pageStarted = true
      releaseHeader({ thread: { id: 'thread-1' } })
      return { data: [] }
    })
    expect((await read(client)).response.success).toBe(true)
    expect(pageStarted).toBe(true)
  })

  it('returns ThreadNotFound for a missing header', async () => {
    const { client } = clientFor(async method => method === 'thread/read' ? {} : { data: [] })
    expect((await read(client)).response).toMatchObject({ success: false, errorCode: 'ThreadNotFound' })
  })

  it('reads only one ascending Item page, even when the page is empty with continuation', async () => {
    for (const entries of [[], [{ turnId: 'turn-1', item: item('call', 'toolCall', { toolName: 'Example', arguments: { value: 1 } }) }]]) {
      const { client, sendRequest } = clientFor(async () => ({ data: entries, nextCursor: 'items:21' }))
      const { response, data } = await callHistory(client, 'ListThreadItems', { threadId: 'thread-1', turnId: 'turn-1', cursor: 'items:1' })
      expect(sendRequest.mock.calls).toEqual([['thread/items/list', {
        threadId: 'thread-1', turnId: 'turn-1', cursor: 'items:1', limit: 20, sortDirection: 'ascending'
      }]])
      expect(data.page).toEqual({ order: 'oldest_first', limit: 20, nextCursor: 'items:21', hasMore: true })
      expect(data.data.map((entry: RecordValue) => entry.item.id)).toEqual(entries.map(entry => entry.item.id))
      expect(JSON.parse(response.contentItems![0].text!)).toEqual(data)
    }
  })

  it('forwards search arguments and occurrence results without altering ranges or cursors', async () => {
    const result = { data: [{
      turnId: 'turn-2', itemId: 'message-1', snippet: '😀 Match match', snippetMatchRange: { start: 3, end: 8 }, turnCursor: 'turn:opaque'
    }], nextCursor: 'occurrence:next' }
    const { client, sendRequest } = clientFor(async () => result)
    const { response, data } = await callHistory(client, 'SearchThreadMessages', {
      threadId: ' thread-1 ', searchTerm: ' Match ', cursor: 'occurrence:prior', limit: 100
    })
    expect(sendRequest.mock.calls).toEqual([['thread/searchOccurrences', {
      threadId: 'thread-1', searchTerm: 'Match', cursor: 'occurrence:prior', limit: 100
    }]])
    expect(data).toEqual(result)
    expect(JSON.parse(response.contentItems![0].text!)).toEqual(result)
  })

  it('maps structured invalid-parameter RPC errors without inferring codes from messages', async () => {
    const cases = [
      { error: new JsonRpcError(-32602, 'Cursor scope mismatch'), errorCode: 'InvalidArguments' },
      { error: new JsonRpcError(-32000, 'History unavailable'), errorCode: 'AppServerRequestFailed' },
      { error: new Error('InvalidParams: cursor scope mismatch'), errorCode: 'AppServerRequestFailed' }
    ]
    for (const { error, errorCode } of cases) {
      const { client } = clientFor(async () => { throw error })
      const { response } = await callHistory(client, 'SearchThreadMessages', {
        threadId: 'thread-1', searchTerm: 'query', cursor: 'invalid-cursor'
      })
      expect(response).toEqual({ success: false, errorCode, errorMessage: error.message })
    }
  })

  it('rejects nulls, unknown fields and invalid ranges before making any requests', async () => {
    const bases: Record<string, RecordValue> = {
      ReadThread: { threadId: 'thread-1' },
      ListThreadItems: { threadId: 'thread-1', turnId: 'turn-1' },
      SearchThreadMessages: { threadId: 'thread-1', searchTerm: 'query' },
      ReadThreadItem: { threadId: 'thread-1', turnId: 'turn-1', itemId: 'item-1' }
    }
    const { client, sendRequest } = historyClient([])
    for (const [tool, base] of Object.entries(bases)) {
      const invalid: unknown[] = [null, [], 'thread-1', {}, { ...base, unexpected: 1 }]
      for (const key of Object.keys(base)) for (const value of [null, '', ' ']) invalid.push({ ...base, [key]: value })
      if (tool !== 'ReadThreadItem') for (const cursor of [null, '', ' ', 1]) invalid.push({ ...base, cursor })
      if (tool === 'ReadThread' || tool === 'ListThreadItems') {
        for (const key of ['includeItems', 'includeOutputs']) {
          if (key === 'includeItems' && tool !== 'ReadThread') continue
          for (const value of [null, 'true']) invalid.push({ ...base, [key]: value })
        }
        for (const maxCharsPerItem of [0, -1, 20_001, 0.5, '1', null]) invalid.push({ ...base, maxCharsPerItem })
        for (const maxOutputCharsPerItem of [-1, 20_001, 0.5, '1', null]) invalid.push({ ...base, maxOutputCharsPerItem })
      }
      if (tool !== 'ReadThreadItem') {
        const key = tool === 'ReadThread' ? 'turnLimit' : 'limit'
        for (const value of [0, -1, tool === 'ReadThread' ? 11 : 101, 0.5, '1', null]) invalid.push({ ...base, [key]: value })
      }
      for (const args of invalid) {
        expect((await callHistory(client, tool, args)).response, JSON.stringify(args))
          .toMatchObject({ success: false, errorCode: 'InvalidArguments' })
      }
    }
    expect(sendRequest).not.toHaveBeenCalled()
  })
})
