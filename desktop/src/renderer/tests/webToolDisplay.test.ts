import { describe, expect, it } from 'vitest'
import { invocationNeedsCallingPrefix, toWebSearchResultRows } from '../utils/webToolDisplay'

describe('invocationNeedsCallingPrefix', () => {
  it('omits the generic prefix only when standalone web/tool-search args parse', () => {
    expect(invocationNeedsCallingPrefix('webSearch', { query: 'x', maxResults: 5 })).toBe(false)
    expect(invocationNeedsCallingPrefix('webFetch', { url: 'https://a.com' })).toBe(false)
    expect(invocationNeedsCallingPrefix('toolSearch', { query: 'ReadFile' })).toBe(false)
    expect(invocationNeedsCallingPrefix('webSearch', {})).toBe(true)
  })
})

describe('toWebSearchResultRows', () => {
  it('keeps results with a URL and falls back to the domain for missing titles', () => {
    expect(toWebSearchResultRows([
      { title: 'DotCraft Docs', url: 'https://docs.dotcraft.ai/start', snippet: 'Guide' },
      { url: 'https://www.example.com/a' },
      { title: 'No url' }
    ])).toEqual([
      { title: 'DotCraft Docs', url: 'https://docs.dotcraft.ai/start', domain: 'docs.dotcraft.ai' },
      { title: 'example.com', url: 'https://www.example.com/a', domain: 'example.com' }
    ])
  })
})
