import { describe, expect, it } from 'vitest'
import { extractThinkingStatus } from '../components/conversation/thinkingStatus'

describe('extractThinkingStatus', () => {
  it.each([
    ['**Checking configuration**', 'Checking configuration'],
    ['Checking configuration', 'Checking configuration'],
    ['**Earlier heading**\nA later plain line', 'A later plain line'],
    ['Earlier\r\n\r\n  **正在检查配置**  \r\n ', '正在检查配置'],
    ['Inspecting [config](https://example.com) and `settings.json`', 'Inspecting config and settings.json'],
    ['**`**Nested heading**`**', 'Nested heading'],
    ['Checking\n<!-- internal -->\n<!-- another --> <!-- unfinished', 'Checking'],
    ['Checking\n<', 'Checking'],
    ['Checking\n<!', 'Checking'],
    ['Checking\n<!-', 'Checking'],
    ['Checking\n<!--', 'Checking'],
    ['', undefined],
    [' \r\n ', undefined],
    ['<!-- internal -->', undefined],
    ['<!-- unfinished', undefined],
    ['---', undefined]
  ])('extracts a status from %j', (reasoning, expected) => {
    expect(extractThinkingStatus(reasoning)).toBe(expected)
  })

  it('updates from the latest streamed line without waiting for a complete heading', () => {
    const reasoning = '**Earlier heading**\n\n'
    expect(extractThinkingStatus(reasoning)).toBe('Earlier heading')
    expect(extractThinkingStatus(`${reasoning}Checking`)).toBe('Checking')
    expect(extractThinkingStatus(`${reasoning}Checking configuration`)).toBe('Checking configuration')
  })
})
