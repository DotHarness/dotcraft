import { describe, expect, it } from 'vitest'
import {
  extractPartialJsonStringValue,
  getStreamingToolDisplay
} from '../utils/toolCallDisplay'

describe('getStreamingToolDisplay', () => {
  it('extracts WriteFile preview fields while arguments stream', () => {
    const display = getStreamingToolDisplay(
      'WriteFile',
      '{"path":"src/demo.rs","content":"let x',
      'en'
    )
    expect(display.parsedPreview?.path).toBe('src/demo.rs')
    expect(display.parsedPreview?.content).toBe('let x')
  })

  it('extracts CreatePlan draft preview while arguments stream', () => {
    const display = getStreamingToolDisplay(
      'CreatePlan',
      '{"plan":"# Ship feature X\\n\\n## Summary\\n\\nNot yet',
      'en'
    )
    expect(display.parsedPreview?.planDraft?.title).toBe('Ship feature X')
    expect(display.parsedPreview?.planDraft?.plan).toBe('# Ship feature X\n\n## Summary\n\nNot yet')
  })

  it('truncates large SpawnAgent task previews while streaming', () => {
    const display = getStreamingToolDisplay(
      'SpawnAgent',
      `{"agentPrompt":"${'x'.repeat(1000)}`,
      'en'
    )
    expect(display.label.length).toBeLessThan(90)
    expect(display.label).not.toContain('x'.repeat(200))
  })

  it('does not expose WaitAgent child thread ids', () => {
    const display = getStreamingToolDisplay(
      'WaitAgent',
      '{"childThreadId":"thread_20260503_child"',
      'en'
    )
    expect(display.label).not.toContain('thread_20260503_child')
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
