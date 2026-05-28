import { describe, expect, it } from 'vitest'
import {
  BUILTIN_TOOLS,
  extractPartialJsonStringValue,
  formatCollapsedToolLabel,
  formatExpandedInvocation,
  getStreamingToolDisplay,
  isBuiltinTool
} from '../utils/toolCallDisplay'

describe('getStreamingToolDisplay', () => {
  it('returns generic external placeholder for MCP-style tool names', () => {
    const display = getStreamingToolDisplay(
      'acme_mcp_tool',
      '{"query":"secret"',
      'en'
    )
    expect(display.label).toBe('Generating parameters for acme_mcp_tool...')
    expect(display.parsedPreview).toBeUndefined()
  })

  it('renders WriteFile streaming label with parsed path', () => {
    const display = getStreamingToolDisplay(
      'WriteFile',
      '{"path":"src/demo.rs","content":"let x',
      'en'
    )
    expect(display.label).toBe('Writing to demo.rs...')
    expect(display.parsedPreview?.path).toBe('src/demo.rs')
    expect(display.parsedPreview?.content).toBe('let x')
  })

  it('renders EditFile streaming label using newText', () => {
    const display = getStreamingToolDisplay(
      'EditFile',
      '{"path":"a/b.ts","oldText":"foo","newText":"bar',
      'en'
    )
    expect(display.label).toBe('Editing b.ts...')
    expect(display.parsedPreview?.content).toBe('bar')
  })

  it('renders Exec streaming label with command first line', () => {
    const display = getStreamingToolDisplay('Exec', '{"command":"npm test', 'en')
    expect(display.label).toBe('Running: npm test')
  })

  it('renders GrepFiles streaming label with pattern and path', () => {
    const display = getStreamingToolDisplay(
      'GrepFiles',
      '{"pattern":"TODO","path":"src',
      'en'
    )
    expect(display.label).toBe('Searching "TODO" in src...')
  })

  it('renders CreatePlan streaming label with title and exposes draft preview', () => {
    const display = getStreamingToolDisplay(
      'CreatePlan',
      '{"plan":"# Ship feature X\\n\\n## Summary\\n\\nNot yet',
      'en'
    )
    expect(display.label).toBe('Drafting plan: Ship feature X...')
    expect(display.parsedPreview?.planDraft?.title).toBe('Ship feature X')
    expect(display.parsedPreview?.planDraft?.plan).toBe('# Ship feature X\n\n## Summary\n\nNot yet')
  })

  it('falls back to generic draft label when title is missing', () => {
    const display = getStreamingToolDisplay('CreatePlan', '{"overview":', 'en')
    expect(display.label).toBe('Drafting plan...')
  })

  it('renders WebSearch streaming label', () => {
    const display = getStreamingToolDisplay('WebSearch', '{"query":"rust streams', 'en')
    expect(display.label).toBe('Searching the web for "rust streams"...')
  })

  it('renders native tool_search streaming label', () => {
    const display = getStreamingToolDisplay('tool_search', '{"query":"board task', 'en')
    expect(display.label).toBe('Searching tools: "board task"...')
  })

  it('renders SpawnAgent streaming label from the new argument names', () => {
    const display = getStreamingToolDisplay(
      'SpawnAgent',
      '{"agentPrompt":"Build tests","agentNickname":"tester","profile":"native"',
      'en'
    )
    expect(display.label).toBe('Spawning agent: tester...')
  })

  it('renders RequestUserInput streaming label with question count', () => {
    const display = getStreamingToolDisplay(
      'RequestUserInput',
      '{"questions":[{"id":"one"},{"id":"two"},{"id":"three"}]}',
      'en'
    )
    expect(display.label).toBe('Asking 3 questions...')
  })

  it('truncates large SpawnAgent task previews while streaming', () => {
    const display = getStreamingToolDisplay(
      'SpawnAgent',
      `{"agentPrompt":"${'x'.repeat(1000)}`,
      'en'
    )
    expect(display.label).toMatch(/^Spawning agent for: x+/)
    expect(display.label.length).toBeLessThan(90)
  })

  it('renders WaitAgent streaming label without exposing child thread ids', () => {
    const display = getStreamingToolDisplay(
      'WaitAgent',
      '{"childThreadId":"thread_20260503_child"',
      'en'
    )
    expect(display.label).toBe('Waiting for agent')
    expect(display.label).not.toContain('thread_20260503_child')
  })

  it('renders generic builtin label for recognised but unsupported tool', () => {
    const display = getStreamingToolDisplay('CommitSuggest', '{}', 'en')
    expect(display.label).toBe('Preparing commit message...')
  })

  it('honours zh-Hans locale for streaming labels', () => {
    const display = getStreamingToolDisplay('WebFetch', '{"url":"https://a', 'zh-Hans')
    expect(display.label).toBe('正在获取 https://a...')
  })

  it('honours zh-Hans locale for RequestUserInput streaming labels', () => {
    const display = getStreamingToolDisplay(
      'RequestUserInput',
      '{"questions":[{"id":"one"},{"id":"two"}]}',
      'zh-Hans'
    )
    expect(display.label).toBe('正在询问 2 个问题...')
  })
})

describe('RequestUserInput tool labels', () => {
  it('formats collapsed and expanded labels without generic tool-call copy', () => {
    const args = {
      questions: [
        { id: 'one', question: 'One?' },
        { id: 'two', question: 'Two?' },
        { id: 'three', question: 'Three?' }
      ]
    }

    expect(formatCollapsedToolLabel('RequestUserInput', args, 'en')).toBe('Ask 3 questions')
    expect(formatExpandedInvocation('RequestUserInput', args, 'en')).toBe('Ask 3 questions')
    expect(formatCollapsedToolLabel('RequestUserInput', args, 'zh-Hans')).toBe('询问 3 个问题')
  })

  it('falls back to generic question copy when count is unknown', () => {
    expect(formatCollapsedToolLabel('RequestUserInput', {}, 'en')).toBe('Ask questions')
    expect(getStreamingToolDisplay('RequestUserInput', '{"questions":[', 'en').label)
      .toBe('Asking questions')
  })
})

describe('isBuiltinTool / BUILTIN_TOOLS', () => {
  it('recognises PascalCase built-in tool names', () => {
    expect(isBuiltinTool('ReadFile')).toBe(true)
    expect(isBuiltinTool('CreatePlan')).toBe(true)
    expect(isBuiltinTool('SpawnAgent')).toBe(true)
    expect(isBuiltinTool('RequestUserInput')).toBe(true)
    expect(isBuiltinTool('tool_search')).toBe(true)
    expect(isBuiltinTool('acme_mcp_tool')).toBe(false)
  })

  it('exposes a non-empty BUILTIN_TOOLS set', () => {
    expect(BUILTIN_TOOLS.size).toBeGreaterThan(5)
    expect(BUILTIN_TOOLS.has('WriteFile')).toBe(true)
  })
})

describe('extractPartialJsonStringValue', () => {
  it('returns unterminated string value when delta is mid-stream', () => {
    expect(extractPartialJsonStringValue('{"path":"src/main.rs","content":"hel', 'path'))
      .toBe('src/main.rs')
    expect(extractPartialJsonStringValue('{"path":"src/main.rs","content":"hel', 'content'))
      .toBe('hel')
  })

  it('returns null when key is missing', () => {
    expect(extractPartialJsonStringValue('{"path":"a"}', 'content')).toBeNull()
  })
})
