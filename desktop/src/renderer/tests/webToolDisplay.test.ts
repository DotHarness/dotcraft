import { describe, it, expect } from 'vitest'
import {
  formatInvocationDisplay,
  formatResultSummary,
  formatToolSearchCompletedLabel,
  invocationNeedsCallingPrefix,
  parseWebSearchResultDisplay,
  truncate
} from '../utils/webToolDisplay'

const en = 'en' as const

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

describe('formatInvocationDisplay', () => {
  it('formats WebSearch with query and maxResults', () => {
    expect(
      formatInvocationDisplay('WebSearch', { query: 'rust async', maxResults: 5 }, en)
    ).toBe('Searched "rust async"')
  })

  it('truncates long WebSearch query', () => {
    const q = 'a'.repeat(100)
    const out = formatInvocationDisplay('WebSearch', { query: q }, en)
    expect(out?.startsWith('Searched "')).toBe(true)
    expect(out?.endsWith('…"')).toBe(true)
  })

  it('formats WebFetch with url', () => {
    expect(
      formatInvocationDisplay('WebFetch', { url: 'https://example.com/path' }, en)
    ).toBe('Fetched https://example.com/path')
  })

  it('formats SearchTools', () => {
    expect(formatInvocationDisplay('SearchTools', { query: 'ReadFile' }, en)).toBe(
      'Searched tools: "ReadFile"'
    )
    expect(formatInvocationDisplay('tool_search', { query: 'board task' }, en)).toBe(
      'Searched tools: "board task"'
    )
    expect(formatInvocationDisplay('tool_search', { q: 'board task' }, en)).toBe(
      'Searched tools: "board task"'
    )
  })

  it('returns null for WebSearch without string query', () => {
    expect(formatInvocationDisplay('WebSearch', {}, en)).toBeNull()
  })
})

describe('invocationNeedsCallingPrefix', () => {
  it('is false for standalone web tools with valid args', () => {
    expect(
      invocationNeedsCallingPrefix('WebSearch', { query: 'x', maxResults: 5 })
    ).toBe(false)
    expect(
      invocationNeedsCallingPrefix('WebFetch', { url: 'https://a.com' })
    ).toBe(false)
    expect(invocationNeedsCallingPrefix('SearchTools', { query: 'ReadFile' })).toBe(false)
    expect(invocationNeedsCallingPrefix('tool_search', { query: 'ReadFile' })).toBe(false)
  })

  it('is true when standalone parse fails or non-web tool', () => {
    expect(invocationNeedsCallingPrefix('WebSearch', {})).toBe(true)
    expect(invocationNeedsCallingPrefix('ReadFile', { path: 'src/main.rs' })).toBe(true)
  })
})

describe('formatResultSummary', () => {
  it('parses WebSearch results and domains', () => {
    const json = JSON.stringify({
      query: 'q',
      provider: 'exa',
      results: [
        { title: 'T', url: 'https://exa.ai/docs' },
        { title: 'U', url: 'http://b.com/x' }
      ]
    })
    const lines = formatResultSummary('WebSearch', json)
    expect(lines).not.toBeNull()
    expect(lines!.length).toBe(3)
    expect(lines![0]).toBe('2 results:')
    expect(lines![1]).toContain('exa.ai')
    expect(lines![2]).toContain('b.com')
  })

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

  it('parses WebSearch error', () => {
    const lines = formatResultSummary('WebSearch', JSON.stringify({ error: 'rate limited' }))
    expect(lines).toEqual(['Error: rate limited'])
  })

  it('handles empty WebSearch results array', () => {
    const lines = formatResultSummary('WebSearch', JSON.stringify({ query: 'x', results: [] }))
    expect(lines).toEqual(['No results found.'])
  })

  it('handles message-only WebSearch no-result payloads', () => {
    const parsed = parseWebSearchResultDisplay(JSON.stringify({ query: 'x', message: 'No results found.' }))
    expect(parsed).toEqual({ kind: 'empty', message: 'No results found.' })
    const lines = formatResultSummary('WebSearch', JSON.stringify({ query: 'x', message: 'No results found.' }))
    expect(lines).toEqual(['No results found.'])
  })

  it('double-decodes JSON string wrapper', () => {
    const inner = JSON.stringify({ results: [{ title: 'Hi', url: 'https://z.com' }] })
    const outer = JSON.stringify(inner)
    const lines = formatResultSummary('WebSearch', outer)
    expect(lines).not.toBeNull()
    expect(lines!.length).toBe(2)
    expect(lines![0]).toBe('1 result:')

    const parsed = parseWebSearchResultDisplay(outer)
    expect(parsed?.kind).toBe('results')
    if (parsed?.kind === 'results') {
      expect(parsed.rows[0]?.domain).toBe('z.com')
    }
  })

  it('returns null for invalid WebSearch JSON', () => {
    expect(parseWebSearchResultDisplay('not-json')).toBeNull()
    expect(formatResultSummary('WebSearch', 'not-json')).toBeNull()
  })

  it('formats WebFetch summary', () => {
    const json = JSON.stringify({
      status: 200,
      length: 50000,
      extractor: 'readability',
      truncated: true
    })
    const lines = formatResultSummary('WebFetch', json)
    expect(lines).not.toBeNull()
    expect(lines!.length).toBe(1)
    expect(lines![0]).toContain('200')
    expect(lines![0]).toContain('50,000')
    expect(lines![0]).toContain('readability')
    expect(lines![0]).toContain('truncated')
  })

  it('formats SearchTools tool list', () => {
    const text = 'Found 2 matching tool(s):\n- ReadFile: Read a file\n- WriteFile: Write a file'
    const lines = formatResultSummary('SearchTools', text)
    expect(lines).toEqual([
      'Found 2 matching tool(s):',
      '- ReadFile: Read a file',
      '- WriteFile: Write a file'
    ])
  })

  it('formats native tool_search namespace result and completed label', () => {
    const json = JSON.stringify({
      tools: [
        {
          type: 'namespace',
          name: 'oratorio',
          tools: [
            {
              type: 'function',
              name: 'CreateBoardTask',
              description: 'Create an Oratorio board task.'
            }
          ]
        }
      ]
    })
    const lines = formatResultSummary('tool_search', json)
    expect(lines).toEqual([
      'Found 1 matching tool(s):',
      '- oratorio.CreateBoardTask: Create an Oratorio board task.'
    ])
    expect(formatToolSearchCompletedLabel('tool_search', json, en)).toBe('Searched 1 tool')
  })

  it('counts native tool_search display text for completed labels', () => {
    const text = 'Found 2 matching tool(s):\n- ReadFile\n- WriteFile'
    expect(formatToolSearchCompletedLabel('tool_search', text, en)).toBe('Searched 2 tools')
  })

  it('returns null for unknown tool', () => {
    expect(formatResultSummary('ReadFile', '{}')).toBeNull()
  })
})
