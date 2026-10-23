import { describe, it, expect } from 'vitest'
import { normalizeToolUiDescriptor, wireItemToConversationItem } from '../types/conversation'

describe('interactive tool UI', () => {
  it('accepts only well-formed ui:// descriptors', () => {
    expect(normalizeToolUiDescriptor({
      resourceUri: 'ui://workflow/board',
      visibility: ['model', 'app'],
      prefersBorder: false,
      csp: { connectDomains: ['https://api.workflow.example'] }
    })).toMatchObject({
      resourceUri: 'ui://workflow/board',
      visibility: ['model', 'app'],
      prefersBorder: false,
      csp: { connectDomains: ['https://api.workflow.example'] }
    })

    expect(normalizeToolUiDescriptor({ resourceUri: 'https://evil.example' })).toBeUndefined()
    expect(normalizeToolUiDescriptor({ resourceUri: '' })).toBeUndefined()
    expect(normalizeToolUiDescriptor(null)).toBeUndefined()
    expect(normalizeToolUiDescriptor('ui://x')).toBeUndefined()
  })

  it('carries the ui descriptor and result _meta only for invocation items', () => {
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

    const message = wireItemToConversationItem({
      id: 'i2',
      type: 'agentMessage',
      payload: { ui: { resourceUri: 'ui://workflow/board' } }
    })
    expect(message.toolUi).toBeUndefined()
    expect(message.meta).toBeUndefined()
  })
})
