import { describe, expect, it } from 'vitest'
import { callHistory, clientFor, historyClient, item, read, type RecordValue } from './readThreadTestHelpers'

describe('Desktop history safe content', () => {
  it('shares the preview allowance across user body and saved context without repeating the body', async () => {
    const body = 'question'
    const snapshot = 'context '.repeat(1_000)
    const { client } = historyClient([{ id: 'turn-1', items: [item('user', 'userMessage', {
      text: body,
      nativeInputParts: [{ type: 'text', text: body }, { type: 'contextRef', context: { text: 'native reference' } }],
      materializedInputParts: [{ type: 'text', text: body }, { type: 'text', text: snapshot }]
    })] }])
    const { response, data } = await read(client, { threadId: 'thread-1', maxCharsPerItem: 80 })
    const projected = data.turns[0].items[0]
    expect(projected.text).toBe(body)
    expect(projected.content).toBeUndefined()
    expect(projected.contentPreview).toMatchObject({ truncated: true, originalChars: JSON.stringify([{ type: 'text', text: snapshot }]).length })
    expect(projected.text.length + projected.contentPreview.text.length).toBe(80)
    expect(projected.truncation).toEqual({ preview: true, outputs: false })
    expect(JSON.stringify(data).split(body)).toHaveLength(2)
    expect(JSON.parse(response.contentItems![0].text!)).toEqual(data)
  })

  it('bounds long body, native context, arguments and errors with explicit preview values', async () => {
    const long = '完整正文😀'.repeat(1_000)
    const argumentsValue = { prompt: long, nested: { flags: [true, false] } }
    const { client } = historyClient([{ id: 'turn-1', items: [
      item('agent', 'agentMessage', { text: long }),
      item('user', 'userMessage', { text: long, nativeInputParts: [{ type: 'text', text: long }] }),
      item('context', 'userMessage', { nativeInputParts: [{ type: 'contextRef', context: { text: long, comment: long } }] }),
      item('call', 'dynamicToolCall', { callId: 'call-1', toolName: 'Example', arguments: argumentsValue, errorMessage: long }),
      item('error', 'error', { code: 'ExampleError', message: long })
    ] }])
    const { response, data } = await read(client, { threadId: 'thread-1', maxCharsPerItem: 51 })
    const [agent, user, context, call, error] = data.turns[0].items
    for (const message of [agent, user]) {
      expect(message).toMatchObject({ text: long.slice(0, 51), textTruncated: true, textOriginalChars: long.length })
    }
    for (const value of [context.contentPreview, call.argumentsPreview, error.messagePreview]) {
      expect(value.truncated).toBe(true)
      expect(value.text.length).toBeLessThanOrEqual(51)
      expect(value.originalChars).toBeGreaterThan(51)
    }
    expect(user.content).toBeUndefined()
    expect(call.arguments).toBeUndefined()
    expect(call.errorMessagePreview.text).toBe('')
    expect(call).toMatchObject({ callId: 'call-1', toolName: 'Example' })
    expect(error.code).toBe('ExampleError')
    expect(JSON.parse(response.contentItems![0].text!)).toEqual(data)
  })

  it('uses default preview limits and shares output allowance across content and structured data', async () => {
    const long = 'x'.repeat(2_001)
    const { client } = historyClient([{ id: 'turn-1', items: [
      item('agent', 'agentMessage', { text: long }),
      item('call', 'mcpToolCall', {
        server: 'example', toolName: 'Read', arguments: { key: 1 },
        content: [{ type: 'text', text: long }], structuredContent: { result: long }
      })
    ] }])
    const { data } = await read(client, { threadId: 'thread-1', includeOutputs: true })
    const [agent, call] = data.turns[0].items
    expect(agent).toMatchObject({ text: 'x'.repeat(2_000), textTruncated: true, textOriginalChars: 2_001 })
    expect(call.arguments).toEqual({ key: 1 })
    expect(call.content).toBeUndefined()
    expect(call.contentPreview.text).toHaveLength(2_000)
    expect(call.structuredContent).toBeUndefined()
    expect(call.structuredContentPreview).toMatchObject({ text: '', truncated: true })
    expect(call.truncation).toEqual({ preview: false, outputs: true })
    const noOutput = await read(client, { threadId: 'thread-1', includeOutputs: true, maxOutputCharsPerItem: 0 })
    expect(noOutput.data.turns[0].items[1].contentPreview.text).toBe('')
    expect(noOutput.data.turns[0].items[1].structuredContentPreview.text).toBe('')
  })

  it('omits tool output and reasoning by default while keeping bounded call metadata', async () => {
    const secret = 'OUTPUT_MUST_BE_OMITTED'
    const { client } = historyClient([{ id: 'turn-1', items: [
      item('command', 'commandExecution', { command: 'test', aggregatedOutput: secret }),
      item('result', 'toolResult', { result: secret }),
      item('dynamic', 'dynamicToolCall', { contentItems: [{ type: 'text', text: secret }], structuredContent: { secret } }),
      item('reasoning', 'reasoningContent', { text: secret }),
      item('execution', 'toolExecution', { resultPreview: secret })
    ] }])
    const { response, data } = await read(client)
    expect(JSON.stringify(response)).not.toContain(secret)
    expect(data.turns[0].items[0]).toMatchObject({ command: 'test', outputChars: secret.length })
    expect(data.turns[0].items[1].resultChars).toBe(secret.length)
  })

  it('uses the same bounded projection for one Item discovery page', async () => {
    const { client } = historyClient([{ id: 'turn-1', items: [item('call', 'toolCall', {
      callId: 'call-1', arguments: { prompt: 'x'.repeat(100) }
    })] }])
    const { data } = await callHistory(client, 'ListThreadItems', {
      threadId: 'thread-1', turnId: 'turn-1', maxCharsPerItem: 10
    })
    expect(data.data[0]).toMatchObject({ turnId: 'turn-1', item: {
      id: 'call', type: 'toolCall', callId: 'call-1', argumentsPreview: { text: '{"prompt":', truncated: true },
      truncation: { preview: true, outputs: false }
    } })
  })

  it('reads saved message content when native input contains only a display label', async () => {
    const displayText = 'Message notification'
    const messageText = 'Saved message body 😀'.repeat(500)
    const { client } = historyClient([{ id: 'turn-1', items: [item('message', 'userMessage', {
      text: displayText,
      nativeInputParts: [{ type: 'text', text: displayText }],
      materializedInputParts: [{ type: 'text', text: messageText }]
    })] }])

    const { data } = await callHistory(client, 'ReadThreadItem', {
      threadId: 'thread-1', turnId: 'turn-1', itemId: 'message'
    })

    expect(data.item).toMatchObject({
      text: displayText,
      content: [{ type: 'text', text: messageText }],
      truncation: { preview: false, outputs: false }
    })
  })

  it('reads exactly one ID with complete text, arguments, context and outputs without preview limits', async () => {
    const long = 'full text 😀'.repeat(5_000)
    const values = [
      item('user', 'userMessage', {
        text: 'question',
        nativeInputParts: [{ type: 'contextRef', context: { kind: 'threadReferences', text: 'native' } }],
        materializedInputParts: [{ type: 'text', text: 'question' }, { type: 'text', text: long }]
      }),
      item('call', 'dynamicToolCall', {
        callId: 'call-1', arguments: { prompt: long }, contentItems: [{ type: 'text', text: long }],
        structuredContent: { result: long }, errorMessage: long
      })
    ]
    const { client, sendRequest } = historyClient([{ id: 'turn-1', items: values }])
    const user = await callHistory(client, 'ReadThreadItem', { threadId: 'thread-1', turnId: 'turn-1', itemId: 'user' })
    expect(user.data.item).toMatchObject({ text: 'question', content: [{ type: 'text', text: long }] })
    const { response, data } = await callHistory(client, 'ReadThreadItem', { threadId: 'thread-1', turnId: 'turn-1', itemId: 'call' })
    expect(data.item).toMatchObject({
      arguments: { prompt: long }, content: [{ type: 'text', text: long }], structuredContent: { result: long },
      errorMessage: long, truncation: { preview: false, outputs: false }
    })
    expect(sendRequest.mock.calls).toEqual([
      ['thread/items/read', { threadId: 'thread-1', turnId: 'turn-1', itemIds: ['user'] }],
      ['thread/items/read', { threadId: 'thread-1', turnId: 'turn-1', itemIds: ['call'] }]
    ])
    expect(JSON.parse(response.contentItems![0].text!)).toEqual(data)
  })

  it('excludes private boundary fields and inline media while preserving same-named business data', async () => {
    const secret = 'PRIVATE_DATA'
    const businessData = {
      _meta: { source: 'business metadata' }, blob: 'business blob', dataBase64: 'business base64',
      dataUrl: 'business data URL', url: 'data:application/example,business-value',
      nested: { _meta: { key: 'business key' }, type: 'image', data: 'business data' }
    }
    const values = [
      item('user', 'userMessage', { text: 'question', nativeInputParts: [
        { type: 'text', text: 'question' },
        { type: 'contextRef', context: { text: 'selected', image: { tempPath: 'evidence.png', dataUrl: secret } } },
        { type: 'image', url: 'DaTa:image/png;base64,' + secret, dataBase64: secret },
        { type: 'fileRef', path: 'example.txt' }
      ] }),
      item('tool', 'mcpToolCall', {
        _meta: { secret }, arguments: businessData,
        modelContentItems: [{ type: 'text', text: 'output', _meta: { secret } }, { type: 'image', url: 'image.png', dataBase64: secret }],
        structuredContent: businessData
      }),
      item('response', 'userInputResponse', { requestId: 'request-1', answers: { secret }, response: secret })
    ]
    const { client } = historyClient([{ id: 'turn-1', items: values }])
    const results: RecordValue[] = []
    for (const value of values) {
      const { response, data } = await callHistory(client, 'ReadThreadItem', { threadId: 'thread-1', turnId: 'turn-1', itemId: value.id })
      expect(response.success).toBe(true)
      expect(JSON.stringify(response)).not.toContain(secret)
      results.push(data.item)
    }
    expect(results[0].content).toEqual([
      { type: 'contextRef', context: { text: 'selected', image: { tempPath: 'evidence.png' } } },
      { type: 'image' }, { type: 'fileRef', path: 'example.txt' }
    ])
    expect(results[1].content).toEqual([{ type: 'text', text: 'output' }, { type: 'image', url: 'image.png' }])
    expect(results[1].arguments).toEqual(businessData)
    expect(results[1].structuredContent).toEqual(businessData)
    expect(results[2]).toEqual({
      id: 'response', type: 'userInputResponse', status: 'completed', requestId: 'request-1',
      truncation: { preview: false, outputs: false }
    })
  })

  it('fails a missing exact Item rather than returning an empty successful result', async () => {
    const { client, sendRequest } = clientFor(async () => ({ data: [] }))
    expect((await callHistory(client, 'ReadThreadItem', {
      threadId: 'thread-1', turnId: 'turn-1', itemId: 'missing'
    })).response).toMatchObject({ success: false, errorCode: 'ItemNotFound' })
    expect(sendRequest).toHaveBeenCalledTimes(1)
  })

  it('retains upstream truncation and original output size', async () => {
    const { client } = historyClient([{ id: 'turn-1', items: [item('command', 'commandExecution', {
      aggregatedOutput: 'short', truncated: true, originalOutputChars: 9_000, outputPath: 'result.txt', exitCode: 0
    })] }])
    const { data } = await read(client, { threadId: 'thread-1', includeOutputs: true })
    expect(data.turns[0].items[0]).toMatchObject({
      truncated: true, originalOutputChars: 9_000, outputPath: 'result.txt', exitCode: 0,
      outputPreview: { text: 'short', truncated: true, originalChars: 9_000 },
      truncation: { preview: false, outputs: true }
    })
  })
})
