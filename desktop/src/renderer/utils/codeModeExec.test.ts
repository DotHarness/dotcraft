import { describe, expect, it } from 'vitest'
import type { ConversationItem } from '../types/conversation'
import { isCodeModeExecItem } from './codeModeExec'

function item(overrides: Partial<ConversationItem>): ConversationItem {
  return { id: 'item', type: 'toolCall', status: 'completed', toolName: 'CodeMode', ...overrides }
}

describe('isCodeModeExecItem', () => {
  it('matches only the code mode tool, accepting a streaming call without provenance', () => {
    expect(isCodeModeExecItem(item({ source: { kind: 'CoreNative', sourceId: 'code-mode', sourceToolId: 'CodeMode' } }))).toBe(true)
    expect(isCodeModeExecItem(item({ toolName: 'CodeModeWait', source: { kind: 'CoreNative', sourceId: 'code-mode', sourceToolId: 'CodeModeWait' } }))).toBe(true)
    expect(isCodeModeExecItem(item({ toolName: 'Exec', source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'Exec' } }))).toBe(false)
    expect(isCodeModeExecItem(item({ source: { kind: 'Mcp', sourceId: 'docs', sourceToolId: 'CodeMode' } }))).toBe(false)
    expect(isCodeModeExecItem(item({ pluginNamespace: 'plugin' }))).toBe(false)
    expect(isCodeModeExecItem(item({ status: 'streaming' }))).toBe(true)
    expect(isCodeModeExecItem(item({ status: 'completed' }))).toBe(false)
  })
})
