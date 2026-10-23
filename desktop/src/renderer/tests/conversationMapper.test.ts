import { describe, it, expect } from 'vitest'
import { wireItemToConversationItem, wireTurnToConversationTurn } from '../types/conversation'

const createdAt = '2025-01-01T00:00:00Z'

describe('wireItemToConversationItem', () => {
  it('maps the persisted MCP App resource from the item payload', () => {
    const app = wireItemToConversationItem({
      id: 'mcp-1',
      type: 'mcpToolCall',
      payload: {
        toolName: 'chart',
        callId: 'call-1',
        status: 'completed',
        mcpAppResourceUri: 'ui://chart/view',
        content: [{ type: 'text', text: 'fallback' }],
        structuredContent: { points: 3 }
      },
      createdAt
    })

    expect(app.type).toBe('mcpToolCall')
    expect(app.mcpAppResourceUri).toBe('ui://chart/view')
    expect(app.result).toBe('fallback')
    expect(app.structuredResult).toEqual({ points: 3 })
  })

  it('does not infer presentation from the source or canonical tool name when the descriptor is missing', () => {
    const payloads = [
      { toolName: 'WriteFile', source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'WriteFile' } },
      { toolName: 'WriteFile', source: { kind: 'PluginNative', sourceId: 'plugin', sourceToolId: 'WriteFile' } },
      { toolName: 'SearchTools' }
    ]

    for (const payload of payloads) {
      expect(wireItemToConversationItem({ id: 'tool', type: 'toolCall', payload, createdAt }).presentation).toBeUndefined()
    }
  })

  it('resolves agent text from top-level text, then payload text, then legacy content', () => {
    const textOf = (fields: Record<string, unknown>) =>
      wireItemToConversationItem({ id: 'i1', type: 'agentMessage', status: 'completed', createdAt, ...fields }).text

    expect(textOf({ text: 'top wins', payload: { text: 'ignored' }, content: 'ignored' })).toBe('top wins')
    expect(textOf({ payload: { text: 'hello from payload' } })).toBe('hello from payload')
    expect(textOf({ content: 'legacy content' })).toBe('legacy content')
    expect(textOf({})).toBeUndefined()
    expect(textOf({ payload: {} })).toBeUndefined()

    const asyncMessage = wireItemToConversationItem({
      id: 'i2-async-agent',
      type: 'agentMessage',
      payload: { text: 'Which target should I use?', deliveryMode: 'async' },
      createdAt
    })
    expect(asyncMessage.deliveryMode).toBe('async')
  })

  it('maps userMessage text, delivery mode and image attachments', () => {
    const item = wireItemToConversationItem({
      id: 'i2b',
      type: 'userMessage',
      status: 'completed',
      payloadKind: 'userMessage',
      payload: {
        text: 'look at this',
        deliveryMode: 'guidance',
        images: [
          { path: '/ws/.craft/attachments/images/a.png', mimeType: 'image/png', fileName: 'a.png' },
          { path: '/ws/.craft/attachments/images/b.jpg' }
        ]
      },
      createdAt
    })

    expect(item.text).toBe('look at this')
    expect(item.deliveryMode).toBe('guidance')
    expect(item.images).toEqual([
      { path: '/ws/.craft/attachments/images/a.png', mimeType: 'image/png', fileName: 'a.png' },
      { path: '/ws/.craft/attachments/images/b.jpg' }
    ])
  })

  it.each([
    ['user typed this\n<system-reminder>\n## Runtime Context\nCurrentMode: Plan\n</system-reminder>', 'user typed this'],
    ['user typed this\n<system-reminder>\n## Runtime Context', 'user typed this'],
    ['please explain [Runtime Context]', 'please explain [Runtime Context]']
  ])('strips only trailing system reminders from user text: %j', (text, expected) => {
    expect(wireItemToConversationItem({ id: 'u', type: 'userMessage', payload: { text }, createdAt }).text).toBe(expected)
  })

  it('keeps known trigger provenance and drops unknown trigger kinds', () => {
    const triggerKinds = ['terminal', 'subagent', 'goal', 'workflow', 'subagentFollowupTask', 'subagentMailbox', 'subagentInput']
    for (const triggerKind of triggerKinds) {
      const item = wireItemToConversationItem({
        id: `i-${triggerKind}`,
        type: 'userMessage',
        payload: {
          text: 'synthetic message',
          triggerKind,
          triggerLabel: 'Inspect',
          triggerRefId: 'ref-1',
          triggerStatus: 'failed'
        },
        createdAt
      })
      expect(item).toMatchObject({ triggerKind, triggerLabel: 'Inspect', triggerRefId: 'ref-1', triggerStatus: 'failed' })
    }

    const unknown = wireItemToConversationItem({
      id: 'i-unknown',
      type: 'userMessage',
      payload: { text: 'synthetic message', triggerKind: 'surprise' },
      createdAt
    })
    expect(unknown.triggerKind).toBeUndefined()
  })

  it('maps reasoning text and elapsed seconds', () => {
    const reasoning = (fields: Record<string, unknown>) => wireItemToConversationItem({
      id: 'r',
      type: 'reasoningContent',
      status: 'completed',
      payload: { text: 'thinking' },
      createdAt: '2025-01-01T00:00:00.000Z',
      ...fields
    })

    expect(reasoning({ completedAt: '2025-01-01T00:00:02.400Z' })).toMatchObject({ reasoning: 'thinking', elapsedSeconds: 2 })
    expect(reasoning({ completedAt: '2025-01-01T00:00:00.100Z' }).elapsedSeconds).toBe(1)
    expect(reasoning({ completedAt: '2025-01-01T00:00:02.000Z', elapsedSeconds: 7 }).elapsedSeconds).toBe(7)
  })

  it('maps dynamicToolCall payload fields and display result', () => {
    const item = wireItemToConversationItem({
      id: 'dynamic-1',
      type: 'dynamicToolCall',
      payload: {
        namespace: 'workflow',
        toolName: 'ListBoardItems',
        callId: 'dynamic-call-1',
        arguments: { status: 'todo' },
        contentItems: [
          { type: 'text', text: '2 board items' },
          { type: 'image', mediaType: 'image/png', dataBase64: 'abc123' }
        ],
        structuredResult: { count: 2 },
        success: true
      },
      createdAt
    })

    expect(item).toMatchObject({
      type: 'dynamicToolCall',
      toolName: 'ListBoardItems',
      toolCallId: 'dynamic-call-1',
      pluginNamespace: 'workflow',
      arguments: { status: 'todo' },
      result: '2 board items',
      structuredResult: { count: 2 },
      success: true
    })
    expect(item.contentItems).toEqual([
      { type: 'text', text: '2 board items' },
      { type: 'image', mediaType: 'image/png', dataBase64: 'abc123' }
    ])
  })

  it('maps toolResult contentItems for ordinary tool output images', () => {
    const item = wireItemToConversationItem({
      id: 'tool-result-image-1',
      type: 'toolResult',
      payload: {
        callId: 'read-image-call-1',
        result: 'Image: sample.png (3 bytes, image/png)',
        contentItems: [
          { type: 'text', text: 'Image: sample.png (3 bytes, image/png)' },
          { type: 'image', mediaType: 'image/png', dataBase64: 'AQID' }
        ],
        success: true
      },
      createdAt
    })

    expect(item).toMatchObject({
      type: 'toolResult',
      toolCallId: 'read-image-call-1',
      result: 'Image: sample.png (3 bytes, image/png)',
      success: true
    })
    expect(item.contentItems?.[1]).toEqual({ type: 'image', mediaType: 'image/png', dataBase64: 'AQID' })
  })

  it.each(['imageGeneration', 'ImageGeneration'])('maps %s payload fields', (type) => {
    const item = wireItemToConversationItem({
      id: 'image-generation-1',
      type,
      payload: {
        callId: 'ig_123',
        status: 'completed',
        revisedPrompt: 'A red square',
        result: 'AQID',
        mediaType: 'image/png',
        savedPath: '<workspace>/.craft/generated_images/thread/ig_123.png'
      },
      createdAt
    })

    expect(item).toMatchObject({
      type: 'imageGeneration',
      toolCallId: 'ig_123',
      imageGenerationStatus: 'completed',
      revisedPrompt: 'A red square',
      result: 'AQID',
      mediaType: 'image/png',
      savedPath: '<workspace>/.craft/generated_images/thread/ig_123.png'
    })
  })

  it('maps command execution and error payload fields', () => {
    const command = wireItemToConversationItem({
      id: 'i4b',
      type: 'commandExecution',
      status: 'completed',
      payload: {
        callId: 'exec-1',
        command: 'npm test',
        workingDirectory: 'C:/repo',
        source: 'host',
        status: 'completed',
        aggregatedOutput: 'ok',
        exitCode: 0,
        durationMs: 1200
      },
      createdAt
    })
    expect(command).toMatchObject({
      command: 'npm test',
      workingDirectory: 'C:/repo',
      commandSource: 'host',
      aggregatedOutput: 'ok',
      exitCode: 0,
      executionStatus: 'completed',
      toolCallId: 'exec-1'
    })

    const error = wireItemToConversationItem({
      id: 'i5',
      type: 'error',
      payload: { message: 'Something went wrong', code: 'agent_error', fatal: true },
      createdAt
    })
    expect(error.text).toBe('Something went wrong')
  })
})

describe('wireTurnToConversationTurn', () => {
  it('maps a thread/read turn with nested-payload items', () => {
    const turn = wireTurnToConversationTurn({
      id: 'turn-1',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2025-01-01T00:00:00Z',
      completedAt: '2025-01-01T00:01:00Z',
      items: [
        { id: 'item-user', type: 'userMessage', payload: { text: 'What is 2+2?' }, createdAt },
        { id: 'item-reason', type: 'reasoningContent', payload: { text: 'Let me think...' }, createdAt },
        { id: 'item-tool', type: 'toolCall', payload: { toolName: 'searchWeb', callId: 'call-1', arguments: {} }, createdAt },
        { id: 'item-agent', type: 'agentMessage', payload: { text: '4' }, createdAt }
      ]
    })

    expect(turn).toMatchObject({ id: 'turn-1', status: 'completed' })
    expect(turn.items).toMatchObject([
      { type: 'userMessage', text: 'What is 2+2?' },
      { type: 'reasoningContent', reasoning: 'Let me think...' },
      { type: 'toolCall', toolName: 'searchWeb', toolCallId: 'call-1' },
      { type: 'agentMessage', text: '4' }
    ])
  })
})
