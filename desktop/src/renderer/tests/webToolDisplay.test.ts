import { describe, expect, it } from 'vitest'
import {
  formatInvocationDisplay,
  invocationNeedsCallingPrefix,
  parseWebSearchResultDisplay
} from '../utils/webToolDisplay'

describe('formatInvocationDisplay', () => {
  it('preserves complete invocation values for layout-level truncation', () => {
    const query = 'compare shared tool activity title geometry across commands todo searches workflows and agents'
    const url = 'https://example.com/reference/tool-activity-disclosure/complete-layout-contract?mode=desktop'

    expect(formatInvocationDisplay('webSearch', { query }, 'en')).toContain(query)
    expect(formatInvocationDisplay('webFetch', { url }, 'en')).toContain(url)
    expect(formatInvocationDisplay('toolSearch', { query }, 'en')).toContain(query)
  })
})

describe('invocationNeedsCallingPrefix', () => {
  it('omits the generic prefix only when standalone web/tool-search args parse', () => {
    expect(invocationNeedsCallingPrefix('webSearch', { query: 'x', maxResults: 5 })).toBe(false)
    expect(invocationNeedsCallingPrefix('webFetch', { url: 'https://a.com' })).toBe(false)
    expect(invocationNeedsCallingPrefix('toolSearch', { query: 'ReadFile' })).toBe(false)
    expect(invocationNeedsCallingPrefix('webSearch', {})).toBe(true)
  })
})

describe('parseWebSearchResultDisplay', () => {
  it('parses structured WebSearch rows into sources', () => {
    const json = JSON.stringify({
      query: 'q',
      provider: 'exa',
      results: [
        { title: 'DotCraft Docs', url: 'https://docs.dotcraft.ai/start', snippet: 'Guide' },
        { url: 'https://www.example.com/a' },
        { title: 'No url' }
      ]
    })

    expect(parseWebSearchResultDisplay(json)).toEqual({
      kind: 'results',
      rows: [
        { title: 'DotCraft Docs', url: 'https://docs.dotcraft.ai/start', domain: 'docs.dotcraft.ai' },
        { title: 'example.com', url: 'https://www.example.com/a', domain: 'example.com' }
      ]
    })
  })

  it('passes through message-only no-result payloads', () => {
    expect(parseWebSearchResultDisplay(JSON.stringify({ query: 'x', message: 'No results found.' }))).toEqual({
      kind: 'empty',
      message: 'No results found.'
    })
  })

  it('double-decodes JSON string wrappers', () => {
    const inner = JSON.stringify({ results: [{ title: 'Hi', url: 'https://z.com' }] })
    const outer = JSON.stringify(inner)
    const parsed = parseWebSearchResultDisplay(outer)

    expect(parsed?.kind).toBe('results')
    if (parsed?.kind === 'results') {
      expect(parsed.rows[0]?.domain).toBe('z.com')
    }
  })

  it('returns null for invalid WebSearch JSON', () => {
    expect(parseWebSearchResultDisplay('not-json')).toBeNull()
  })
})
