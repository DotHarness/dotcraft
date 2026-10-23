import { describe, expect, it } from 'vitest'
import { extractPartialJsonStringValue } from '../utils/toolCallDisplay'
import { getStreamingToolDisplay } from '../utils/streamingToolDisplay'
import { coreToolFields } from './testToolPresentation'

const tool = (toolName: string) => ({ toolName, ...coreToolFields(toolName) })

describe('getStreamingToolDisplay', () => {
  it('extracts WriteFile, Exec and CreatePlan previews while arguments stream', () => {
    const display = getStreamingToolDisplay(tool('WriteFile'),
      '{"path":"src/demo.rs","content":"let x',
      'en'
    )
    expect(display.parsedPreview?.path).toBe('src/demo.rs')
    expect(display.parsedPreview?.content).toBe('let x')

    const exec = getStreamingToolDisplay(tool('Exec'), '{"command":"npm run build', 'en')
    expect(exec.label).toBe('Running: npm run build')
    expect(exec.parsedPreview?.command).toBe('npm run build')

    const plan = getStreamingToolDisplay(tool('CreatePlan'), '{"plan":"# Ship feature X\\n\\n## Summary\\n\\nNot yet', 'en')
    expect(plan.parsedPreview?.planDraft?.title).toBe('Ship feature X')
    expect(plan.parsedPreview?.planDraft?.plan).toBe('# Ship feature X\n\n## Summary\n\nNot yet')
  })

  it('preserves complete long labels for tools that rely on layout truncation', () => {
    const command = 'node scripts/verify-desktop.mjs --workspace /workspace/dotcraft-lab --surface conversation --scenario tool-row-width'
    const pattern = 'tool activity title truncation shared disclosure conversation width'
    const task = 'Review every formatter used by the conversation tool activity header'

    expect(getStreamingToolDisplay(tool('Exec'), `{"command":"${command}`, 'en').label).toContain(command)
    expect(getStreamingToolDisplay(tool('GrepFiles'), `{"pattern":"${pattern}`, 'en').label).toContain(pattern)
    expect(getStreamingToolDisplay(tool('SearchTools'), `{"query":"${pattern}`, 'en').label).toContain(pattern)
    expect(getStreamingToolDisplay(tool('SpawnAgent'), `{"agentPrompt":"${task}`, 'en').label).toContain(task)
  })

  it('never shows argument text for a tool without a trusted presentation', () => {
    const display = getStreamingToolDisplay({ toolName: 'Exec' }, '{"command":"rm -rf /tmp/x', 'en')
    expect(display.label).not.toContain('rm -rf')
  })

  it('does not expose SendMessage targets', () => {
    const display = getStreamingToolDisplay(tool('SendMessage'),
      '{"target":"thread_20260503_child"',
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
    expect(extractPartialJsonStringValue('{"path":"a"}', 'content')).toBeNull()
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
})
