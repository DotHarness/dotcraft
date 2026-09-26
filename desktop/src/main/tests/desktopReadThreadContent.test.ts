import { describe, expect, it } from 'vitest'
import { historyClient, item, read } from './readThreadTestHelpers'

describe('ReadThread content projection', () => {
  it.each([false, true])('preserves complete messages independently of output controls (%s)', async includeOutputs => {
    const text = '完整正文😀\n'.repeat(5_000)
    const argumentsValue = { prompt: text, nested: { flags: [true, false] } }
    const { client } = historyClient([{ id: 'turn-1', status: 'completed', items: [
      item('user', 'userMessage', { text, nativeInputParts: [{ type: 'text', text }] }),
      item('call', 'toolCall', { toolName: 'Example', callId: 'call-1', arguments: argumentsValue }),
      item('assistant', 'agentMessage', { text, deliveryMode: 'final' })
    ] }])
    const { response, data } = await read(client, { threadId: 'thread-1', includeOutputs, maxOutputCharsPerItem: 0 })
    expect(response.success).toBe(true)
    expect(data.turns[0].items[0].text).toBe(text)
    expect(data.turns[0].items[0].content[0].text).toBe(text)
    expect(data.turns[0].items[1].arguments).toEqual(argumentsValue)
    expect(data.turns[0].items[2].text).toBe(text)
    expect(JSON.parse(response.contentItems![0].text!)).toEqual(data)
  })

  it.each([false, true])('preserves the final answer in large tool-heavy turns (%s)', async includeOutputs => {
    const answer = '答'.repeat(4_266)
    const items = Array.from({ length: 51 }, (_, index) => item(`result-${index}`, 'toolResult', {
      callId: `call-${index}`, success: true, result: 'output'.repeat(2_000)
    }))
    items.push(item('final', 'agentMessage', { text: answer }))
    const { client } = historyClient([{ id: 'turn-1', items }])
    for (const maxOutputCharsPerItem of [0, 2_000, 6_000, 20_000]) {
      const { response, data } = await read(client, { threadId: 'thread-1', includeOutputs, maxOutputCharsPerItem })
      expect(response.success).toBe(true)
      expect(data.turns[0].items.at(-1).text).toBe(answer)
      expect(data.turns[0].items).toHaveLength(52)
      if (includeOutputs && maxOutputCharsPerItem > 0) expect(JSON.stringify(data).length).toBeGreaterThan(30_000)
    }
  })

  it.each([0, 5, 6, 10])('bounds each output with explicit metadata at limit %i', async limit => {
    const text = 'abcdef'
    const args = { value: 'full arguments' }
    const { client } = historyClient([{ id: 'turn-1', items: [
      item('command', 'commandExecution', { command: 'test', aggregatedOutput: text }),
      item('result', 'toolResult', { result: text }),
      item('dynamic', 'dynamicToolCall', {
        toolName: 'Example', arguments: args, contentItems: [{ type: 'text', text }], structuredContent: { text }
      }),
      item('reasoning', 'reasoningContent', { text }),
      item('execution', 'toolExecution', { resultPreview: text, durationMs: 25 })
    ] }])
    const { data } = await read(client, { threadId: 'thread-1', includeOutputs: true, maxOutputCharsPerItem: limit })
    const projected = data.turns[0].items
    const expected = {
      text: text.slice(0, limit), truncated: text.length > limit,
      ...(text.length > limit ? { originalChars: text.length } : {})
    }
    for (const output of [projected[0].output, projected[1].result, projected[2].content,
      projected[3].text, projected[4].resultPreview]) expect(output).toEqual(expected)
    const structured = JSON.stringify({ text })
    expect(projected[2].structuredContent).toEqual({
      text: structured.slice(0, limit), truncated: true, originalChars: structured.length
    })
    expect(projected[2].arguments).toEqual(args)
    expect(projected[4].durationMs).toBe(25)
  })

  it('omits all tool output and reasoning by default, including execution previews', async () => {
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
    expect(data.turns[0].items[0].outputChars).toBe(secret.length)
    expect(data.turns[0].items[1].resultChars).toBe(secret.length)
  })

  it('uses the default 2,000 character limit for MCP output and preserves call metadata', async () => {
    const text = 'x'.repeat(2_001)
    for (const field of ['content', 'modelContentItems']) {
      const { client } = historyClient([{ id: 'turn-1', items: [item('mcp', 'mcpToolCall', {
        server: 'example', toolName: 'Read', arguments: { path: 'example.txt' },
        durationMs: 50, success: true, [field]: [{ type: 'text', text }, { type: 'image', dataBase64: 'BINARY' }]
      })] }])
      const { data } = await read(client, { threadId: 'thread-1', includeOutputs: true })
      expect(data.turns[0].items[0]).toMatchObject({
        server: 'example', toolName: 'Read', arguments: { path: 'example.txt' }, durationMs: 50, success: true,
        content: { text: 'x'.repeat(2_000), truncated: true, originalChars: 2_001 }
      })
      expect(JSON.stringify(data)).not.toContain('BINARY')
    }
  })

  it('retains upstream truncation, output paths and the original length', async () => {
    const { client } = historyClient([{ id: 'turn-1', items: [item('command', 'commandExecution', {
      aggregatedOutput: 'short', truncated: true, originalOutputChars: 9_000,
      outputPath: 'result.txt', exitCode: 0
    })] }])
    const { data } = await read(client, { threadId: 'thread-1', includeOutputs: true })
    expect(data.turns[0].items[0]).toMatchObject({
      truncated: true, originalOutputChars: 9_000, outputPath: 'result.txt', exitCode: 0,
      output: { text: 'short', truncated: true, originalChars: 9_000 }
    })
  })

  it('preserves runtime and bounded queued input metadata, projecting media references', async () => {
    const { client } = historyClient([{ id: 'turn-1', items: [item('user', 'userMessage', {
      materializedInputParts: [
        { type: 'text', text: 'materialized text' },
        { type: 'localImage', path: 'image.png', dataBase64: 'BINARY_IMAGE_DATA' },
        { type: 'image', url: 'data:image/png;base64,BINARY_IMAGE_DATA' },
        { type: 'fileRef', path: 'file.txt', displayPath: 'file.txt' }
      ]
    })] }], {
      runtime: { running: true, waitingOnInput: true },
      queuedInputs: Array.from({ length: 12 }, (_, index) => ({
        id: `queued-${index}`, status: 'queued', displayText: 'x'.repeat(1_000),
        sender: { displayName: 'Sender', userId: 'sender-1', channelName: 'cli' }, readyAfterTurnId: 'turn-1'
      }))
    })
    const { data } = await read(client)
    expect(data.thread.runtime).toEqual({ running: true, waitingOnInput: true })
    expect(data.thread.queuedInputCount).toBe(12)
    expect(data.thread.queuedInputs).toHaveLength(10)
    expect(data.thread.queuedInputs[0].displayText).toHaveLength(500)
    expect(data.thread.queuedInputs[0].sender.userId).toBe('sender-1')
    expect(data.turns[0].items[0].content[0].text).toBe('materialized text')
    expect(data.turns[0].items[0].content[1].path).toBe('image.png')
    expect(JSON.stringify(data)).not.toContain('BINARY_IMAGE_DATA')
  })
})
