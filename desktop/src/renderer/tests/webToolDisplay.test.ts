import { describe, expect, it } from 'vitest'
import {
  invocationNeedsCallingPrefix,
  parseWebSearchResultDisplay,
  truncate
} from '../utils/webToolDisplay'

describe('truncate', () => {
  it('leaves short strings unchanged', () => {
    expect(truncate('abc', 80)).toBe('abc')
  })

  it('truncates long strings with ellipsis', () => {
    const s = 'a'.repeat(100)
    expect(truncate(s, 80).length).toBe(81)
    expect(truncate(s, 80).endsWith('…')).toBe(true)
  })
})

describe('invocationNeedsCallingPrefix', () => {
  it('omits the generic prefix only when standalone web/tool-search args parse', () => {
    expect(invocationNeedsCallingPrefix('WebSearch', { query: 'x', maxResults: 5 })).toBe(false)
    expect(invocationNeedsCallingPrefix('WebFetch', { url: 'https://a.com' })).toBe(false)
    expect(invocationNeedsCallingPrefix('SearchTools', { query: 'ReadFile' })).toBe(false)
    expect(invocationNeedsCallingPrefix('tool_search', { query: 'ReadFile' })).toBe(false)
    expect(invocationNeedsCallingPrefix('WebSearch', {})).toBe(true)
    expect(invocationNeedsCallingPrefix('ReadFile', { path: 'src/main.rs' })).toBe(true)
  })
})

describe('parseWebSearchResultDisplay', () => {
  it('parses structured WebSearch rows for table rendering', () => {
    const json = JSON.stringify({
      query: 'q',
      provider: 'exa',
      results: [
        {
          title: 'DotCraft Docs',
          url: 'https://docs.dotcraft.ai/start',
          snippet: 'Guide',
          author: 'DotCraft',
          publishedDate: '2026-04-01'
        }
      ]
    })

    const parsed = parseWebSearchResultDisplay(json)

    expect(parsed?.kind).toBe('results')
    if (parsed?.kind === 'results') {
      expect(parsed.query).toBe('q')
      expect(parsed.provider).toBe('exa')
      expect(parsed.rows).toHaveLength(1)
      expect(parsed.rows[0]).toMatchObject({
        title: 'DotCraft Docs',
        url: 'https://docs.dotcraft.ai/start',
        snippet: 'Guide',
        author: 'DotCraft',
        publishedDate: '2026-04-01',
        domain: 'docs.dotcraft.ai',
        linkLabel: 'docs.dotcraft.ai'
      })
    }
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
