import { describe, expect, it } from 'vitest'
import {
  formatInvocationDisplay,
  invocationNeedsCallingPrefix,
  toWebSearchResultRows
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
