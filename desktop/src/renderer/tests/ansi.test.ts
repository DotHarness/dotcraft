import { describe, expect, it } from 'vitest'
import { parseAnsi, stripAnsi } from '../utils/ansi'

describe('ansi utils', () => {
  it('strips SGR color sequences', () => {
    expect(stripAnsi('\u001b[31mred\u001b[0m world')).toBe('red world')
  })

  it('parses style spans for bold + 16-color SGR', () => {
    expect(parseAnsi('\u001b[1;32mA\u001b[0mB')).toEqual([
      { text: 'A', bold: true, fg: 'var(--ansi-green)' },
      { text: 'B' }
    ])
  })

  it('supports 24-bit and 256-color SGR', () => {
    expect(parseAnsi('\u001b[38;2;12;34;56mX\u001b[0m')).toEqual([
      { text: 'X', fg: 'rgb(12, 34, 56)' }
    ])

    expect(parseAnsi('\u001b[48;5;196mY\u001b[0m')).toEqual([
      { text: 'Y', bg: 'rgb(255, 0, 0)' }
    ])
  })

  it('strips non-SGR CSI and OSC control sequences', () => {
    const input = `hello\u001b[2K\u001b]8;;https://dotcraft.ai\u0007link\u001b]8;;\u0007\u001b[1A!`
    expect(stripAnsi(input)).toBe('hellolink!')
  })

  it('coalesces carriage-return rewrites to last frame', () => {
    const input = 'progress 10%\rprogress 50%\rprogress 100%\nDone'
    expect(stripAnsi(input)).toBe('progress 100%\nDone')
  })

  it('parses ANSI even when lines end with CRLF (Windows shell output)', () => {
    const input = '\u001b[1m\u001b[46m RUN \u001b[49m\u001b[22m\r\n'
    const spans = parseAnsi(input)
    expect(
      spans.some(
        (span) =>
          span.text.includes('RUN') &&
          span.bold &&
          span.bg === 'var(--ansi-cyan)'
      )
    ).toBe(true)
  })
})
