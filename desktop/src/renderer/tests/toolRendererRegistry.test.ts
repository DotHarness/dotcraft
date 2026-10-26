import { describe, expect, it } from 'vitest'
import type { ConversationItem } from '../types/conversation'
import {
  CORE_TOOL_PRESENTATION_IDS,
  ToolRendererRegistry,
  coreToolRendererRegistry,
  type ToolRendererRegistration
} from '../utils/toolRendererRegistry'
import { resolveMemoryOperation, resolveSkillOperation, resolveToolKind } from '../utils/toolKind'

function item(
  presentationId: string | undefined,
  kind = 'CoreNative',
  options?: Record<string, unknown>
): ConversationItem {
  return {
    id: 'item-1',
    type: 'toolCall',
    status: 'completed',
    toolName: 'RemoteControlledName',
    source: { kind, sourceId: 'core', sourceToolId: 'source-tool' },
    presentation: presentationId ? { presentationId, options } : undefined,
    createdAt: '2026-07-14T00:00:00Z'
  }
}

const resolve = (presentationId: string | undefined, kind = 'CoreNative', options?: Record<string, unknown>) =>
  coreToolRendererRegistry.resolve(item(presentationId, kind, options))

describe('ToolRendererRegistry', () => {
  it('resolves trusted Core presentations to their renderer plan', () => {
    expect(resolve('core.automation')?.family).toBe('automation')
    expect(resolve(CORE_TOOL_PRESENTATION_IDS.channel)).toMatchObject({ family: 'channel', mode: 'standalone' })
    expect(resolve(CORE_TOOL_PRESENTATION_IDS.web, 'CoreNative', { operation: 'search' })).toMatchObject({
      family: 'web',
      mode: 'collapsible',
      groupCategory: 'web',
      options: { operation: 'search' }
    })
    expect(resolve(CORE_TOOL_PRESENTATION_IDS.agentBuilder, 'CoreNative', { field: 'tools.policy' })).toMatchObject({
      family: 'agentBuilder',
      mode: 'standalone',
      options: { field: 'tools.policy' }
    })
    expect(resolve(CORE_TOOL_PRESENTATION_IDS.readFile)).toMatchObject({ family: 'readFile', groupCategory: 'explore' })
    expect(resolve(CORE_TOOL_PRESENTATION_IDS.deferredSearch)).toMatchObject({ family: 'deferredSearch' })
    expect(resolve(CORE_TOOL_PRESENTATION_IDS.lsp)).toMatchObject({ family: 'lsp', groupCategory: 'explore' })
    expect(resolve(CORE_TOOL_PRESENTATION_IDS.commitSuggest)).toMatchObject({ family: 'commitSuggest' })
    for (const operation of ['list', 'connect', 'disconnect', 'transfer']) {
      expect(resolve(CORE_TOOL_PRESENTATION_IDS.remoteToolHost, 'CoreNative', { operation })).toEqual({
        family: 'remoteToolHost',
        mode: 'collapsible',
        options: { operation }
      })
    }
  })

  it.each(['Mcp', 'PluginNative', 'RuntimeDynamic', 'LegacyAppBinding'])(
    'rejects %s provenance even when it claims a Core presentation id',
    (kind) => {
      expect(resolve(CORE_TOOL_PRESENTATION_IDS.shell, kind)).toBeNull()
      expect(resolve(CORE_TOOL_PRESENTATION_IDS.channel, kind)).toBeNull()
      expect(resolve('core.automation', kind)).toBeNull()
    }
  )

  it('falls back to generic rendering for missing, unknown, or name-only presentations', () => {
    expect(resolve(undefined)).toBeNull()
    expect(resolve('core.not-registered')).toBeNull()
    const namedLikeCore = item(undefined)
    namedLikeCore.toolName = 'CreatePlan'
    expect(coreToolRendererRegistry.resolve(namedLikeCore)).toBeNull()
  })

  it('rejects invalid, oversized, or unowned options', () => {
    const invalidOptions = Object.create(null) as Record<string, unknown>
    invalidOptions.operation = 'search'
    expect(resolve(CORE_TOOL_PRESENTATION_IDS.web, 'CoreNative', invalidOptions)).toBeNull()
    expect(resolve(CORE_TOOL_PRESENTATION_IDS.web, 'CoreNative', { value: 'x'.repeat(5000) })).toBeNull()
    expect(resolve(CORE_TOOL_PRESENTATION_IDS.web, 'CoreNative', { operation: 'shell' })).toBeNull()
    expect(resolve(CORE_TOOL_PRESENTATION_IDS.channel, 'CoreNative', { operation: 'send' })).toBeNull()
    expect(resolve(CORE_TOOL_PRESENTATION_IDS.agentBuilder, 'CoreNative', { field: 'avatar' })).toBeNull()
    expect(resolve(CORE_TOOL_PRESENTATION_IDS.agentBuilder, 'CoreNative')).toBeNull()
    expect(resolve(CORE_TOOL_PRESENTATION_IDS.remoteToolHost, 'CoreNative', { operation: 'reroute' })).toBeNull()
  })

  it('rejects duplicate presentation ids at construction', () => {
    const registration: ToolRendererRegistration = {
      presentationId: 'core.duplicate',
      resolve: () => null
    }
    expect(() => new ToolRendererRegistry([registration, registration]))
      .toThrow('Duplicate tool renderer presentation id: core.duplicate')
  })

  it.each([
    [CORE_TOOL_PRESENTATION_IDS.readFile, { operation: 'grep', memory: 'recall' }, 'recall'],
    [CORE_TOOL_PRESENTATION_IDS.fileWrite, { operation: 'edit', memory: 'update' }, 'update'],
    [CORE_TOOL_PRESENTATION_IDS.readFile, { operation: 'read', memory: 'recall', memoryStore: { id: 'team', name: 'Team' } }, 'recall'],
    [CORE_TOOL_PRESENTATION_IDS.fileWrite, { operation: 'write', memory: 'update', memoryStore: { id: 'team', name: 'Team' } }, 'update'],
    [CORE_TOOL_PRESENTATION_IDS.readFile, { operation: 'read' }, null]
  ] as const)('maps the memory mark on %s %o to %s', (presentationId, options, expected) => {
    const marked = item(presentationId, 'CoreNative', { ...options })

    expect(coreToolRendererRegistry.resolve(marked)?.options.operation).toBe(options.operation)
    expect(resolveMemoryOperation(marked)).toBe(expected)
  })

  it('rejects memory marks a presentation does not carry, and ignores them from untrusted sources', () => {
    for (const [presentationId, options] of [
      [CORE_TOOL_PRESENTATION_IDS.readFile, { operation: 'read', memory: 'update' }],
      [CORE_TOOL_PRESENTATION_IDS.fileWrite, { operation: 'write', memory: 'recall' }],
      [CORE_TOOL_PRESENTATION_IDS.fileWrite, { operation: 'write', memory: true }],
      [CORE_TOOL_PRESENTATION_IDS.fileWrite, { operation: 'write', memoryStore: { id: 'team', name: 'Team' } }],
      [CORE_TOOL_PRESENTATION_IDS.readFile, { operation: 'read', memory: 'recall', memoryStore: 'team' }],
      [CORE_TOOL_PRESENTATION_IDS.web, { operation: 'search', memory: 'recall' }]
    ] as const) {
      expect(resolve(presentationId, 'CoreNative', { ...options })).toBeNull()
    }
    expect(resolveMemoryOperation(item(
      CORE_TOOL_PRESENTATION_IDS.fileWrite,
      'Mcp',
      { operation: 'write', memory: 'update' }
    ))).toBeNull()
  })

  it.each([
    [CORE_TOOL_PRESENTATION_IDS.readFile, { operation: 'read' }, 'read', 'readFile', 'read'],
    [CORE_TOOL_PRESENTATION_IDS.readFile, { operation: 'grep' }, 'read', 'readFile', 'grep'],
    [CORE_TOOL_PRESENTATION_IDS.fileWrite, { operation: 'write' }, 'create', 'fileWrite', 'write'],
    [CORE_TOOL_PRESENTATION_IDS.fileWrite, { operation: 'edit' }, 'update', 'fileWrite', 'edit']
  ] as const)('maps the skill mark on %s %o to %s and keeps the file renderer', (presentationId, options, skill, family, kind) => {
    const marked = item(presentationId, 'CoreNative', {
      ...options,
      skill,
      skillName: 'pdf-tools',
      skillFile: 'D:/ws/.craft/skills/pdf-tools/SKILL.md'
    })

    expect(coreToolRendererRegistry.resolve(marked)?.family).toBe(family)
    expect(resolveToolKind(marked)).toBe(kind)
    expect(resolveSkillOperation(marked)).toEqual({
      operation: skill,
      skillName: 'pdf-tools',
      skillFile: 'D:/ws/.craft/skills/pdf-tools/SKILL.md'
    })
    expect(resolveSkillOperation(item(presentationId, 'CoreNative', { ...options }))).toBeNull()
  })

  it('rejects skill marks a presentation does not carry, and ignores them from untrusted sources', () => {
    for (const [presentationId, options] of [
      [CORE_TOOL_PRESENTATION_IDS.readFile, { operation: 'read', skill: 'update', skillName: 'demo' }],
      [CORE_TOOL_PRESENTATION_IDS.fileWrite, { operation: 'write', skill: 'read', skillName: 'demo' }],
      [CORE_TOOL_PRESENTATION_IDS.fileWrite, { operation: 'write', skill: 'create' }],
      [CORE_TOOL_PRESENTATION_IDS.fileWrite, { operation: 'write', skill: 'create', skillName: ' ' }],
      [CORE_TOOL_PRESENTATION_IDS.readFile, { operation: 'read', skillName: 'demo' }],
      [CORE_TOOL_PRESENTATION_IDS.readFile, { operation: 'read', skill: 'read', skillName: 'demo', skillFile: 1 }],
      [CORE_TOOL_PRESENTATION_IDS.readFile, { operation: 'read', skillFile: 'SKILL.md' }],
      [CORE_TOOL_PRESENTATION_IDS.web, { operation: 'fetch', skill: 'read', skillName: 'demo' }]
    ] as const) {
      expect(resolve(presentationId, 'CoreNative', { ...options })).toBeNull()
    }
    expect(resolveSkillOperation(item(
      CORE_TOOL_PRESENTATION_IDS.fileWrite,
      'Mcp',
      { operation: 'write', skill: 'create', skillName: 'demo' }
    ))).toBeNull()
  })
})
