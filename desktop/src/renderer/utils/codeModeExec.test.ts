import { describe, expect, it } from 'vitest'
import type { ConversationItem } from '../types/conversation'
import { isCodeModeExecItem } from './codeModeExec'

function item(overrides: Partial<ConversationItem>): ConversationItem {
  return { id: 'item', type: 'toolCall', status: 'completed', toolName: 'CodeMode', ...overrides }
}

describe('isCodeModeExecItem', () => {
  it('matches only the code mode tool', () => {
    expect(isCodeModeExecItem(item({ source: { kind: 'CoreNative', sourceId: 'code-mode', sourceToolId: 'CodeMode' } }))).toBe(true)
    expect(isCodeModeExecItem(item({ toolName: 'Exec', source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'Exec' } }))).toBe(false)
    expect(isCodeModeExecItem(item({ source: { kind: 'Mcp', sourceId: 'docs', sourceToolId: 'CodeMode' } }))).toBe(false)
    expect(isCodeModeExecItem(item({ pluginNamespace: 'plugin' }))).toBe(false)
  })

  it('treats a streaming CodeMode call without provenance as code mode', () => {
    expect(isCodeModeExecItem(item({ status: 'streaming' }))).toBe(true)
    expect(isCodeModeExecItem(item({ status: 'completed' }))).toBe(false)
  })
})
