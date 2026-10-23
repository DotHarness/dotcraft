import { describe, expect, it } from 'vitest'
import { formatDefaultToolResultForDisplay } from '../utils/toolResultDisplay'

describe('formatDefaultToolResultForDisplay', () => {
  it('formats MCP structuredContent or decoded JSON text instead of the outer envelope', () => {
    const structured = JSON.stringify({
      content: [{ type: 'text', text: JSON.stringify({ ok: false, message: 'text fallback' }) }],
      structuredContent: { ok: true, message: 'structured result' },
      isError: false
    })
    expect(formatDefaultToolResultForDisplay(structured)).toBe(JSON.stringify({
      ok: true,
      message: 'structured result'
    }, null, 2))

    const textOnly = '{"content":[{"type":"text","text":"{\\u0022ok\\u0022:true,\\u0022message\\u0022:\\u0022dotcraft manual test\\u0022}"}],"isError":false}'
    expect(formatDefaultToolResultForDisplay(textOnly)).toBe(JSON.stringify({
      ok: true,
      message: 'dotcraft manual test'
    }, null, 2))
  })

  it('pretty prints ordinary JSON and keeps non-JSON text unchanged', () => {
    expect(formatDefaultToolResultForDisplay('{"items":[{"id":1},{"id":2}]}')).toBe(JSON.stringify({
      items: [{ id: 1 }, { id: 2 }]
    }, null, 2))
    expect(formatDefaultToolResultForDisplay('plain output\nsecond line')).toBe('plain output\nsecond line')
  })
})
