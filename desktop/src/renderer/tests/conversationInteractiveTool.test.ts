import { describe, it, expect } from 'vitest'
import { normalizeToolUiDescriptor, wireItemToConversationItem } from '../types/conversation'

describe('normalizeToolUiDescriptor', () => {
  it('accepts a ui:// descriptor and parses csp/visibility', () => {
    const descriptor = normalizeToolUiDescriptor({
      resourceUri: 'ui://workflow/board',
      visibility: ['model', 'app'],
      prefersBorder: false,
      csp: { connectDomains: ['https://api.workflow.example'] }
    })
    expect(descriptor?.resourceUri).toBe('ui://workflow/board')
    expect(descriptor?.visibility).toEqual(['model', 'app'])
    expect(descriptor?.prefersBorder).toBe(false)
    expect(descriptor?.csp?.connectDomains).toEqual(['https://api.workflow.example'])
  })

  it('rejects a non-ui:// or malformed descriptor', () => {
    expect(normalizeToolUiDescriptor({ resourceUri: 'https://evil.example' })).toBeUndefined()
    expect(normalizeToolUiDescriptor({ resourceUri: '' })).toBeUndefined()
    expect(normalizeToolUiDescriptor(null)).toBeUndefined()
    expect(normalizeToolUiDescriptor('ui://x')).toBeUndefined()
  })
})

describe('wireItemToConversationItem — interactive tool UI', () => {
  it('carries the ui descriptor and result _meta for a dynamicToolCall', () => {
    const item = wireItemToConversationItem({
      id: 'i1',
      type: 'dynamicToolCall',
      payload: {
        toolName: 'CreateCard',
        namespace: 'workflow',
        structuredResult: { cardId: 'c1' },
        _meta: { highlight: true },
        ui: { resourceUri: 'ui://workflow/board', visibility: ['model', 'app'] }
      }
    })
    expect(item.toolUi?.resourceUri).toBe('ui://workflow/board')
    expect(item.meta).toEqual({ highlight: true })
    expect(item.structuredResult).toEqual({ cardId: 'c1' })
  })

  it('ignores ui metadata on non-invocation items', () => {
    const item = wireItemToConversationItem({
      id: 'i2',
      type: 'agentMessage',
      payload: { ui: { resourceUri: 'ui://workflow/board' } }
    })
    expect(item.toolUi).toBeUndefined()
    expect(item.meta).toBeUndefined()
  })
})
