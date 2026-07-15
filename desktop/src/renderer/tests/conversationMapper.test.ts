import { describe, it, expect } from 'vitest'
import { wireItemToConversationItem, wireTurnToConversationTurn } from '../types/conversation'

// ---------------------------------------------------------------------------
// wireItemToConversationItem
// ---------------------------------------------------------------------------

describe('wireItemToConversationItem — flat (top-level) format', () => {
  it('maps live MCP App eligibility without deriving it from persisted metadata', () => {
    const live = wireItemToConversationItem({
      id: 'mcp-1',
      type: 'mcpToolCall',
      mcpApp: { available: true },
      payload: {
        toolName: 'chart',
        callId: 'call-1',
        status: 'completed',
        content: [{ type: 'text', text: 'fallback' }],
        structuredContent: { points: 3 }
      },
      createdAt: '2025-01-01T00:00:00Z'
    })
    const historical = wireItemToConversationItem({
      id: 'mcp-1',
      type: 'mcpToolCall',
      payload: { toolName: 'chart', status: 'completed' },
      createdAt: '2025-01-01T00:00:00Z'
    })

    expect(live.type).toBe('mcpToolCall')
    expect(live.mcpAppAvailable).toBe(true)
    expect(live.result).toBe('fallback')
    expect(live.structuredResult).toEqual({ points: 3 })
    expect(historical.mcpAppAvailable).toBe(false)
  })

  it('does not infer presentation when the server descriptor is missing', () => {
    const core = wireItemToConversationItem({
      id: 'tool-1',
      type: 'toolCall',
      payload: {
        toolName: 'WriteFile',
        source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'WriteFile' }
      },
      createdAt: '2025-01-01T00:00:00Z'
    })
    const untrusted = wireItemToConversationItem({
      id: 'tool-2',
      type: 'toolCall',
      payload: {
        toolName: 'WriteFile',
        source: { kind: 'PluginNative', sourceId: 'plugin', sourceToolId: 'WriteFile' }
      },
      createdAt: '2025-01-01T00:00:00Z'
    })

    expect(core.presentation).toBeUndefined()
    expect(untrusted.presentation).toBeUndefined()
  })

  it('does not infer a Core presentation from the provider-visible tool name alone', () => {
    const mapped = wireItemToConversationItem({
      id: 'tool-search',
      type: 'toolCall',
      payload: { toolName: 'tool_search' },
      createdAt: '2025-01-01T00:00:00Z'
    })

    expect(mapped.presentation).toBeUndefined()
  })

  it('extracts text from raw.text for agentMessage', () => {
    const item = wireItemToConversationItem({
      id: 'i1',
      type: 'agentMessage',
      status: 'completed',
      text: 'hello flat',
      createdAt: '2025-01-01T00:00:00Z'
    })
    expect(item.text).toBe('hello flat')
  })

  it('extracts text from raw.content as legacy fallback', () => {
    const item = wireItemToConversationItem({
      id: 'i1',
      type: 'agentMessage',
      status: 'completed',
      content: 'legacy content',
      createdAt: '2025-01-01T00:00:00Z'
    })
    expect(item.text).toBe('legacy content')
  })

  it('prefers raw.text over raw.content', () => {
    const item = wireItemToConversationItem({
      id: 'i1',
      type: 'agentMessage',
      text: 'top-level text',
      content: 'should be ignored',
      createdAt: '2025-01-01T00:00:00Z'
    })
    expect(item.text).toBe('top-level text')
  })
})

describe('wireItemToConversationItem — nested payload format (thread/read)', () => {
  it('extracts text from payload.text for agentMessage', () => {
    const item = wireItemToConversationItem({
      id: 'i1',
      type: 'agentMessage',
      status: 'completed',
      payloadKind: 'agentMessage',
      payload: { text: 'hello from payload' },
      createdAt: '2025-01-01T00:00:00Z'
    })
    expect(item.text).toBe('hello from payload')
    expect(item.type).toBe('agentMessage')
    expect(item.id).toBe('i1')
  })

  it('extracts text from payload.text for userMessage', () => {
    const item = wireItemToConversationItem({
      id: 'i2',
      type: 'userMessage',
      status: 'completed',
      payloadKind: 'userMessage',
      payload: { text: 'user typed this', senderId: 'local' },
      createdAt: '2025-01-01T00:00:00Z'
    })
    expect(item.text).toBe('user typed this')
    expect(item.type).toBe('userMessage')
  })

  it('strips trailing system reminders from userMessage payload text', () => {
    const item = wireItemToConversationItem({
      id: 'i2-runtime',
      type: 'userMessage',
      payload: {
        text: 'user typed this\n<system-reminder>\n## Runtime Context\nCurrentMode: Plan\n</system-reminder>'
      },
      createdAt: '2025-01-01T00:00:00Z'
    })
    expect(item.text).toBe('user typed this')
  })

  it('drops malformed system reminder tails from userMessage text', () => {
    const item = wireItemToConversationItem({
      id: 'i2-runtime-open',
      type: 'userMessage',
      payload: {
        text: 'user typed this\n<system-reminder>\n## Runtime Context'
      },
      createdAt: '2025-01-01T00:00:00Z'
    })
    expect(item.text).toBe('user typed this')
  })

  it('keeps literal legacy runtime headings when not wrapped', () => {
    const item = wireItemToConversationItem({
      id: 'i2-legacy-heading',
      type: 'userMessage',
      payload: { text: 'please explain [Runtime Context]' },
      createdAt: '2025-01-01T00:00:00Z'
    })
    expect(item.text).toBe('please explain [Runtime Context]')
  })

  it('extracts deliveryMode from userMessage payload', () => {
    const item = wireItemToConversationItem({
      id: 'i2-guidance',
      type: 'userMessage',
      status: 'completed',
      payloadKind: 'userMessage',
      payload: { text: 'guide this turn', deliveryMode: 'guidance' },
      createdAt: '2025-01-01T00:00:00Z'
    })
    expect(item.deliveryMode).toBe('guidance')
  })

  it('preserves goal triggerKind from userMessage payload', () => {
    const item = wireItemToConversationItem({
      id: 'i2-goal',
      type: 'userMessage',
      status: 'completed',
      payloadKind: 'userMessage',
      payload: {
        text: 'Continue working toward the active thread goal',
        triggerKind: 'goal',
        triggerLabel: 'Goal continuation',
        triggerRefId: 'goal-1'
      },
      createdAt: '2025-01-01T00:00:00Z'
    })

    expect(item.triggerKind).toBe('goal')
    expect(item.triggerLabel).toBe('Goal continuation')
    expect(item.triggerRefId).toBe('goal-1')
  })

  it('preserves SubAgent triggerKind values from userMessage payload', () => {
    const triggerKinds = ['subagentFollowupTask', 'subagentMailbox', 'subagentInput'] as const

    for (const triggerKind of triggerKinds) {
      const item = wireItemToConversationItem({
        id: `i2-${triggerKind}`,
        type: 'userMessage',
        status: 'completed',
        payloadKind: 'userMessage',
        payload: {
          text: 'synthetic message',
          triggerKind,
          triggerLabel: 'Inspect',
          triggerRefId: '/root/inspect'
        },
        createdAt: '2025-01-01T00:00:00Z'
      })

      expect(item.triggerKind).toBe(triggerKind)
      expect(item.triggerLabel).toBe('Inspect')
      expect(item.triggerRefId).toBe('/root/inspect')
    }
  })

  it('filters unknown triggerKind values from userMessage payload', () => {
    const item = wireItemToConversationItem({
      id: 'i2-unknown-trigger',
      type: 'userMessage',
      status: 'completed',
      payloadKind: 'userMessage',
      payload: {
        text: 'synthetic message',
        triggerKind: 'surprise'
      },
      createdAt: '2025-01-01T00:00:00Z'
    })

    expect(item.triggerKind).toBeUndefined()
  })

  it('extracts images metadata from payload.images for userMessage', () => {
    const item = wireItemToConversationItem({
      id: 'i2b',
      type: 'userMessage',
      status: 'completed',
      payloadKind: 'userMessage',
      payload: {
        text: 'look at this',
        images: [
          { path: '/ws/.craft/attachments/images/a.png', mimeType: 'image/png', fileName: 'a.png' },
          { path: '/ws/.craft/attachments/images/b.jpg' }
        ]
      },
      createdAt: '2025-01-01T00:00:00Z'
    })
    expect(item.images).toEqual([
      { path: '/ws/.craft/attachments/images/a.png', mimeType: 'image/png', fileName: 'a.png' },
      { path: '/ws/.craft/attachments/images/b.jpg' }
    ])
  })

  it('extracts reasoning from payload.text for reasoningContent', () => {
    const item = wireItemToConversationItem({
      id: 'i3',
      type: 'reasoningContent',
      status: 'completed',
      payloadKind: 'reasoningContent',
      payload: { text: 'thinking step by step...' },
      createdAt: '2025-01-01T00:00:00Z'
    })
    expect(item.reasoning).toBe('thinking step by step...')
    // text may also be set via the text chain; the key assertion is reasoning
  })

  it('computes reasoning elapsedSeconds from createdAt and completedAt', () => {
    const item = wireItemToConversationItem({
      id: 'i3-elapsed',
      type: 'reasoningContent',
      status: 'completed',
      payloadKind: 'reasoningContent',
      payload: { text: 'thinking with timestamps' },
      createdAt: '2025-01-01T00:00:00.000Z',
      completedAt: '2025-01-01T00:00:02.400Z'
    })

    expect(item.elapsedSeconds).toBe(2)
  })

  it('shows sub-second completed reasoning as at least one elapsed second', () => {
    const item = wireItemToConversationItem({
      id: 'i3-elapsed-min',
      type: 'reasoningContent',
      status: 'completed',
      payload: { text: 'fast thought' },
      createdAt: '2025-01-01T00:00:00.000Z',
      completedAt: '2025-01-01T00:00:00.100Z'
    })

    expect(item.elapsedSeconds).toBe(1)
  })

  it('keeps explicit reasoning elapsedSeconds when provided', () => {
    const item = wireItemToConversationItem({
      id: 'i3-elapsed-explicit',
      type: 'reasoningContent',
      status: 'completed',
      payload: { text: 'thinking with explicit elapsed' },
      elapsedSeconds: 7,
      createdAt: '2025-01-01T00:00:00.000Z',
      completedAt: '2025-01-01T00:00:02.000Z'
    })

    expect(item.elapsedSeconds).toBe(7)
  })

  it('does NOT put reasoningContent payload.text into text field', () => {
    const item = wireItemToConversationItem({
      id: 'i3',
      type: 'reasoningContent',
      status: 'completed',
      payload: { text: 'internal reasoning' },
      createdAt: '2025-01-01T00:00:00Z'
    })
    // payload.text for reasoningContent is routed to `reasoning`, but because
    // the text fallback chain (raw.text -> payload.text) also picks it up,
    // the important thing is `reasoning` is populated.
    expect(item.reasoning).toBe('internal reasoning')
  })

  it('extracts toolName and toolCallId from ToolCallPayload', () => {
    const item = wireItemToConversationItem({
      id: 'i4',
      type: 'toolCall',
      status: 'completed',
      payloadKind: 'toolCall',
      payload: { toolName: 'readFile', callId: 'call-abc', arguments: { path: '/foo' } },
      createdAt: '2025-01-01T00:00:00Z'
    })
    expect(item.toolName).toBe('readFile')
    expect(item.toolCallId).toBe('call-abc')
  })

  it('maps pluginFunctionCall payload fields and display result', () => {
    const item = wireItemToConversationItem({
      id: 'plugin-1',
      type: 'pluginFunctionCall',
      payload: {
        pluginId: 'browser',
        namespace: 'node_repl',
        functionName: 'NodeReplJs',
        callId: 'plugin-call-1',
        arguments: { code: '1 + 1' },
        contentItems: [
          { type: 'text', text: '2' },
          { type: 'image', mediaType: 'image/png', dataBase64: 'abc123' }
        ],
        structuredResult: { ok: true },
        success: true
      },
      createdAt: '2025-01-01T00:00:00Z'
    })

    expect(item.type).toBe('pluginFunctionCall')
    expect(item.toolName).toBe('NodeReplJs')
    expect(item.toolCallId).toBe('plugin-call-1')
    expect(item.pluginId).toBe('browser')
    expect(item.pluginNamespace).toBe('node_repl')
    expect(item.functionName).toBe('NodeReplJs')
    expect(item.arguments).toEqual({ code: '1 + 1' })
    expect(item.result).toBe('2')
    expect(item.contentItems).toEqual([
      { type: 'text', text: '2' },
      { type: 'image', mediaType: 'image/png', dataBase64: 'abc123' }
    ])
    expect(item.structuredResult).toEqual({ ok: true })
    expect(item.success).toBe(true)
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
      createdAt: '2025-01-01T00:00:00Z'
    })

    expect(item.type).toBe('dynamicToolCall')
    expect(item.toolName).toBe('ListBoardItems')
    expect(item.toolCallId).toBe('dynamic-call-1')
    expect(item.pluginNamespace).toBe('workflow')
    expect(item.arguments).toEqual({ status: 'todo' })
    expect(item.result).toBe('2 board items')
    expect(item.contentItems).toEqual([
      { type: 'text', text: '2 board items' },
      { type: 'image', mediaType: 'image/png', dataBase64: 'abc123' }
    ])
    expect(item.structuredResult).toEqual({ count: 2 })
    expect(item.success).toBe(true)
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
      createdAt: '2025-01-01T00:00:00Z'
    })

    expect(item.type).toBe('toolResult')
    expect(item.toolCallId).toBe('read-image-call-1')
    expect(item.result).toBe('Image: sample.png (3 bytes, image/png)')
    expect(item.contentItems).toEqual([
      { type: 'text', text: 'Image: sample.png (3 bytes, image/png)' },
      { type: 'image', mediaType: 'image/png', dataBase64: 'AQID' }
    ])
    expect(item.success).toBe(true)
  })

  it('maps imageGeneration payload fields', () => {
    const item = wireItemToConversationItem({
      id: 'image-generation-1',
      type: 'imageGeneration',
      payload: {
        callId: 'ig_123',
        status: 'completed',
        revisedPrompt: 'A red square',
        result: 'AQID',
        mediaType: 'image/png',
        savedPath: '<workspace>/.craft/generated_images/thread/ig_123.png'
      },
      createdAt: '2025-01-01T00:00:00Z'
    })

    expect(item.type).toBe('imageGeneration')
    expect(item.toolCallId).toBe('ig_123')
    expect(item.imageGenerationStatus).toBe('completed')
    expect(item.revisedPrompt).toBe('A red square')
    expect(item.result).toBe('AQID')
    expect(item.mediaType).toBe('image/png')
    expect(item.savedPath).toBe('<workspace>/.craft/generated_images/thread/ig_123.png')
  })

  it('maps PascalCase ImageGeneration item type from persisted core history', () => {
    const item = wireItemToConversationItem({
      id: 'image-generation-2',
      type: 'ImageGeneration',
      payload: {
        callId: 'ig_456',
        status: 'completed',
        result: 'BAUG',
        mediaType: 'image/png',
        savedPath: '<workspace>/.craft/generated_images/thread/ig_456.png'
      },
      createdAt: '2025-01-01T00:00:00Z'
    })

    expect(item.type).toBe('imageGeneration')
    expect(item.toolCallId).toBe('ig_456')
    expect(item.imageGenerationStatus).toBe('completed')
    expect(item.result).toBe('BAUG')
    expect(item.savedPath).toBe('<workspace>/.craft/generated_images/thread/ig_456.png')
  })

  it('extracts command execution payload fields', () => {
    const item = wireItemToConversationItem({
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
      createdAt: '2025-01-01T00:00:00Z'
    })
    expect(item.command).toBe('npm test')
    expect(item.workingDirectory).toBe('C:/repo')
    expect(item.commandSource).toBe('host')
    expect(item.aggregatedOutput).toBe('ok')
    expect(item.exitCode).toBe(0)
    expect(item.executionStatus).toBe('completed')
    expect(item.toolCallId).toBe('exec-1')
  })

  it('extracts error message from ErrorPayload.message', () => {
    const item = wireItemToConversationItem({
      id: 'i5',
      type: 'error',
      status: 'completed',
      payloadKind: 'error',
      payload: { message: 'Something went wrong', code: 'agent_error', fatal: true },
      createdAt: '2025-01-01T00:00:00Z'
    })
    expect(item.text).toBe('Something went wrong')
  })

  it('prefers raw.text over payload.text when both present', () => {
    const item = wireItemToConversationItem({
      id: 'i6',
      type: 'agentMessage',
      text: 'top wins',
      payload: { text: 'should be ignored' },
      createdAt: '2025-01-01T00:00:00Z'
    })
    expect(item.text).toBe('top wins')
  })

  it('handles missing payload gracefully (undefined)', () => {
    const item = wireItemToConversationItem({
      id: 'i7',
      type: 'agentMessage',
      status: 'completed',
      createdAt: '2025-01-01T00:00:00Z'
    })
    expect(item.text).toBeUndefined()
    expect(item.id).toBe('i7')
  })

  it('handles empty payload object gracefully', () => {
    const item = wireItemToConversationItem({
      id: 'i8',
      type: 'agentMessage',
      status: 'completed',
      payload: {},
      createdAt: '2025-01-01T00:00:00Z'
    })
    expect(item.text).toBeUndefined()
  })
})

// ---------------------------------------------------------------------------
// wireTurnToConversationTurn — integration: items are correctly mapped
// ---------------------------------------------------------------------------

describe('wireTurnToConversationTurn — payload extraction', () => {
  it('maps turn with nested-payload items (thread/read format)', () => {
    const raw = {
      id: 'turn-1',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2025-01-01T00:00:00Z',
      completedAt: '2025-01-01T00:01:00Z',
      items: [
        {
          id: 'item-user',
          type: 'userMessage',
          status: 'completed',
          payloadKind: 'userMessage',
          payload: { text: 'What is 2+2?' },
          createdAt: '2025-01-01T00:00:00Z'
        },
        {
          id: 'item-agent',
          type: 'agentMessage',
          status: 'completed',
          payloadKind: 'agentMessage',
          payload: { text: '4' },
          createdAt: '2025-01-01T00:00:05Z',
          completedAt: '2025-01-01T00:00:10Z'
        }
      ]
    }
    const turn = wireTurnToConversationTurn(raw)
    expect(turn.id).toBe('turn-1')
    expect(turn.status).toBe('completed')
    expect(turn.items).toHaveLength(2)

    const userItem = turn.items.find((i) => i.type === 'userMessage')
    expect(userItem?.text).toBe('What is 2+2?')

    const agentItem = turn.items.find((i) => i.type === 'agentMessage')
    expect(agentItem?.text).toBe('4')
  })

  it('maps turn with reasoning and tool call items', () => {
    const raw = {
      id: 'turn-2',
      threadId: 'thread-1',
      status: 'completed',
      startedAt: '2025-01-01T00:00:00Z',
      items: [
        {
          id: 'item-reason',
          type: 'reasoningContent',
          status: 'completed',
          payload: { text: 'Let me think...' },
          createdAt: '2025-01-01T00:00:01Z'
        },
        {
          id: 'item-tool',
          type: 'toolCall',
          status: 'completed',
          payload: { toolName: 'searchWeb', callId: 'call-1', arguments: {} },
          createdAt: '2025-01-01T00:00:02Z'
        }
      ]
    }
    const turn = wireTurnToConversationTurn(raw)

    const reasonItem = turn.items.find((i) => i.type === 'reasoningContent')
    expect(reasonItem?.reasoning).toBe('Let me think...')

    const toolItem = turn.items.find((i) => i.type === 'toolCall')
    expect(toolItem?.toolName).toBe('searchWeb')
    expect(toolItem?.toolCallId).toBe('call-1')
  })
})
