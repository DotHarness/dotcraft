import { describe, expect, it } from 'vitest'
import { formatDefaultToolResultForDisplay } from '../utils/toolResultDisplay'

describe('formatDefaultToolResultForDisplay', () => {
  it('formats MCP structuredContent instead of the outer envelope', () => {
    const result = JSON.stringify({
      content: [
        {
          type: 'text',
          text: JSON.stringify({ ok: false, message: 'text fallback' })
        }
      ],
      structuredContent: {
        ok: true,
        message: 'structured result'
      },
      isError: false
    })

    expect(formatDefaultToolResultForDisplay(result)).toBe(JSON.stringify({
      ok: true,
      message: 'structured result'
    }, null, 2))
  })

  it('decodes JSON text content inside MCP envelopes', () => {
    const result = '{"content":[{"type":"text","text":"{\\u0022ok\\u0022:true,\\u0022message\\u0022:\\u0022dotcraft manual test\\u0022}"}],"isError":false}'

    expect(formatDefaultToolResultForDisplay(result)).toBe(JSON.stringify({
      ok: true,
      message: 'dotcraft manual test'
    }, null, 2))
  })

  it('pretty prints ordinary JSON objects and arrays', () => {
    expect(formatDefaultToolResultForDisplay('{"items":[{"id":1},{"id":2}]}')).toBe(JSON.stringify({
      items: [
        { id: 1 },
        { id: 2 }
      ]
    }, null, 2))
  })

  it('keeps non-JSON text unchanged', () => {
    expect(formatDefaultToolResultForDisplay('plain output\nsecond line')).toBe('plain output\nsecond line')
  })
})
