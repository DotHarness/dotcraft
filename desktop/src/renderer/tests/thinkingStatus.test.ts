import { describe, expect, it } from 'vitest'
import { extractThinkingStatus } from '../components/conversation/thinkingStatus'

describe('extractThinkingStatus', () => {
  it('prefers the latest bold or Markdown heading line', () => {
    for (const [reasoning, expected] of [
      ['**Checking configuration**', 'Checking configuration'],
      ['## Reading the config', 'Reading the config'],
      ['**Earlier heading**\n\nA later plain paragraph\nstill going', 'Earlier heading'],
      ['Earlier\r\n\r\n  **正在检查配置**  \r\n ', '正在检查配置'],
      ['**Inspecting [config](https://example.com) and `settings.json`**', 'Inspecting config and settings.json'],
      ['**`**Nested heading**`**', 'Nested heading'],
      ['**Checking**\n<!-- internal -->\n<!-- another --> <!-- unfinished', 'Checking'],
      ['', undefined],
      [' \r\n ', undefined],
      ['<!-- internal -->', undefined]
    ] as const) {
      expect(extractThinkingStatus(reasoning), JSON.stringify(reasoning)).toBe(expected)
    }
  })

  it('falls back to the first line of the latest paragraph whose first line is written', () => {
    expect(extractThinkingStatus('The user wants release notes')).toBeUndefined()
    expect(extractThinkingStatus('The user wants release notes', true)).toBe('The user wants release notes')
    expect(extractThinkingStatus('The user wants release notes.\nI need the tags.')).toBe('The user wants release notes.')
    expect(extractThinkingStatus('The user wants release notes.\n\nLet me start')).toBe('The user wants release notes.')
    expect(extractThinkingStatus('The user wants release notes.\n\nLet me run `git log`.\n')).toBe('Let me run git log.')
    expect(extractThinkingStatus('**Checking** the lockfile next\n\nmore')).toBe('Checking the lockfile next')
  })
})
