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

  it('extracts the Exec command while arguments stream', () => {
    const display = getStreamingToolDisplay(
      'Exec',
      '{"command":"npm run build',
      'en'
    )
    expect(display.label).toBe('Running: npm run build')
    expect(display.parsedPreview?.command).toBe('npm run build')
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

  it('preserves the complete SpawnAgent task label while streaming', () => {
    const task = 'Review every formatter used by the conversation tool activity header'
    const display = getStreamingToolDisplay(
      'SpawnAgent',
      `{"agentPrompt":"${task}`,
      'en'
    )
    expect(display.label).toContain(task)
  })

  it('preserves complete long labels for tools that rely on layout truncation', () => {
    const command = 'node scripts/verify-desktop.mjs --workspace /workspace/dotcraft-lab --surface conversation --scenario tool-row-width'
    const pattern = 'tool activity title truncation shared disclosure conversation width'

    expect(getStreamingToolDisplay('Exec', `{"command":"${command}`, 'en').label).toContain(command)
    expect(getStreamingToolDisplay('GrepFiles', `{"pattern":"${pattern}`, 'en').label).toContain(pattern)
    expect(getStreamingToolDisplay('SearchTools', `{"query":"${pattern}`, 'en').label).toContain(pattern)
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

  it('decodes Unicode escapes, including surrogate pairs', () => {
    const slash = String.fromCharCode(92)
    const json = `{"content":"${slash}u4fee${slash}u590d ${slash}ud83d${slash}ude80"}`

    expect(extractPartialJsonStringValue(json, 'content')).toBe('修复 🚀')
  })

  it('preserves an incomplete Unicode escape until more input arrives', () => {
    const slash = String.fromCharCode(92)
    const json = `{"content":"${slash}u65`

    expect(extractPartialJsonStringValue(json, 'content')).toBe(`${slash}u65`)
  })

  it('returns null when key is missing', () => {
    expect(extractPartialJsonStringValue('{"path":"a"}', 'content')).toBeNull()
  })
})
