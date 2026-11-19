import type { SessionItem } from '@dotcraft/sdk/contracts'
import { describe, expect, it } from 'vitest'
import { collectThreadMemories } from './threadMemories'

let sequence = 0

function call(presentationId: string, options: Record<string, unknown>, path: string, kind = 'CoreNative'): SessionItem {
  sequence += 1
  return {
    id: `item-${sequence}`,
    turnId: 'turn',
    type: 'toolCall',
    status: 'completed',
    createdAt: `2026-11-19T00:00:${String(sequence).padStart(2, '0')}Z`,
    payload: {
      callId: `call-${sequence}`,
      toolName: 'Tool',
      providerFlatName: 'Tool',
      arguments: { path },
      presentation: { presentationId, options },
      source: { kind, sourceId: 'core' }
    }
  }
}

function result(callId: string, success: boolean): SessionItem {
  return { id: `${callId}-result`, turnId: 'turn', type: 'toolResult', status: 'completed', createdAt: '2026-11-19T00:01:00Z', payload: { callId, success, result: '', toolName: 'Tool', providerFlatName: 'Tool' } }
}

describe('thread memories', () => {
  it('lists memory files the thread read or wrote, from Core marks only, most recent first', () => {
    const items = [
      call('core.read-file', { operation: 'read', memory: 'recall' }, '.craft/memory/user-role.md'),
      call('core.read-file', { operation: 'read', memory: 'recall' }, '.craft/memory/MEMORY.md'),
      call('core.read-file', { operation: 'grep', memory: 'recall' }, '.craft/memory'),
      call('core.read-file', { operation: 'read' }, '.craft/memory/looks-like-memory.md'),
      call('core.read-file', { operation: 'read', memory: 'recall' }, '.craft/memory/forged.md', 'Plugin'),
      call('core.file-write', { operation: 'edit', memory: 'update', memoryStore: { id: 'team', name: 'Team' } }, 'D:/shared/team/glossary.md'),
      call('core.file-write', { operation: 'edit', memory: 'update' }, 'F:\\ws\\.craft\\memory\\User-Role.md')
    ]

    const memories = collectThreadMemories(items, 'F:\\ws')
    expect(memories.map((memory) => [memory.fileName, memory.store?.id ?? null])).toEqual([
      ['user-role.md', null],
      ['glossary.md', 'team']
    ])
  })

  it('leaves out an update whose call failed', () => {
    const write = call('core.file-write', { operation: 'write', memory: 'update' }, '.craft/memory/new.md')
    const callId = (write.payload as { callId: string }).callId
    expect(collectThreadMemories([write, result(callId, false)], null)).toEqual([])
    expect(collectThreadMemories([write, result(callId, true)], null).map((memory) => memory.fileName)).toEqual(['new.md'])
  })
})
