import { describe, expect, it } from 'vitest'
import type { ConversationItem } from '../types/conversation'
import {
  CORE_TOOL_PRESENTATION_IDS,
  ToolRendererRegistry,
  coreToolRendererRegistry,
  type ToolRendererRegistration
} from '../utils/toolRendererRegistry'

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

describe('ToolRendererRegistry', () => {
  it('resolves a registered Core renderer from presentation and provenance', () => {
    const plan = coreToolRendererRegistry.resolve(item(
      CORE_TOOL_PRESENTATION_IDS.web,
      'CoreNative',
      { operation: 'search' }
    ))

    expect(plan).toMatchObject({
      family: 'web',
      mode: 'collapsible',
      groupCategory: 'web',
      options: { operation: 'search' }
    })
  })

  it.each(['Mcp', 'PluginNative', 'RuntimeDynamic', 'LegacyAppBinding'])(
    'rejects %s provenance even when it claims a Core presentation id',
    (kind) => {
      expect(coreToolRendererRegistry.resolve(item(CORE_TOOL_PRESENTATION_IDS.shell, kind))).toBeNull()
    }
  )

  it('resolves the agent-builder family only for a known profile field', () => {
    expect(coreToolRendererRegistry.resolve(item(
      CORE_TOOL_PRESENTATION_IDS.agentBuilder,
      'CoreNative',
      { field: 'tools.policy' }
    ))).toMatchObject({
      family: 'agentBuilder',
      mode: 'standalone',
      options: { field: 'tools.policy' }
    })
    expect(coreToolRendererRegistry.resolve(item(CORE_TOOL_PRESENTATION_IDS.agentBuilder, 'CoreNative', { field: 'avatar' }))).toBeNull()
    expect(coreToolRendererRegistry.resolve(item(CORE_TOOL_PRESENTATION_IDS.agentBuilder, 'CoreNative'))).toBeNull()
  })

  it('uses the generic fallback when presentation is missing or unknown', () => {
    expect(coreToolRendererRegistry.resolve(item(undefined))).toBeNull()
    expect(coreToolRendererRegistry.resolve(item('core.not-registered'))).toBeNull()
  })

  it('rejects invalid and oversized options', () => {
    const invalidOptions = Object.create(null) as Record<string, unknown>
    invalidOptions.operation = 'search'
    expect(coreToolRendererRegistry.resolve(item(
      CORE_TOOL_PRESENTATION_IDS.web,
      'CoreNative',
      invalidOptions
    ))).toBeNull()

    expect(coreToolRendererRegistry.resolve(item(
      CORE_TOOL_PRESENTATION_IDS.web,
      'CoreNative',
      { value: 'x'.repeat(5000) }
    ))).toBeNull()

    expect(coreToolRendererRegistry.resolve(item(
      CORE_TOOL_PRESENTATION_IDS.web,
      'CoreNative',
      { operation: 'shell' }
    ))).toBeNull()
  })

  it('rejects duplicate presentation ids at construction', () => {
    const registration: ToolRendererRegistration = {
      presentationId: 'core.duplicate',
      resolve: () => null
    }
    expect(() => new ToolRendererRegistry([registration, registration]))
      .toThrow('Duplicate tool renderer presentation id: core.duplicate')
  })

  it('does not infer a renderer from a trusted tool name', () => {
    const namedLikeCore = item(undefined)
    namedLikeCore.toolName = 'CreatePlan'
    expect(coreToolRendererRegistry.resolve(namedLikeCore)).toBeNull()
  })

  it.each(['ReadFile', 'GrepFiles', 'FindFiles'])(
    'resolves %s through the production read presentation',
    (toolName) => {
      const projected = item(CORE_TOOL_PRESENTATION_IDS.readFile)
      projected.toolName = toolName
      projected.source = {
        kind: 'CoreNative',
        sourceId: 'core-native',
        sourceToolId: toolName
      }

      expect(coreToolRendererRegistry.resolve(projected)).toMatchObject({
        family: 'readFile',
        groupCategory: 'explore'
      })
    }
  )

  it('resolves the canonical SearchTools call from its Core projection', () => {
    const projected = item(CORE_TOOL_PRESENTATION_IDS.deferredSearch)
    projected.toolName = 'SearchTools'
    projected.source = {
      kind: 'CoreNative',
      sourceId: 'core-native',
      sourceToolId: 'SearchTools'
    }

    expect(coreToolRendererRegistry.resolve(projected)).toMatchObject({
      family: 'deferredSearch'
    })
  })

  it.each([
    [CORE_TOOL_PRESENTATION_IDS.lsp, 'lsp', 'explore'],
    [CORE_TOOL_PRESENTATION_IDS.commitSuggest, 'commitSuggest', undefined]
  ])('resolves the trusted %s utility presentation', (presentationId, family, groupCategory) => {
    expect(coreToolRendererRegistry.resolve(item(presentationId))).toMatchObject({
      family,
      ...(groupCategory ? { groupCategory } : {})
    })
  })
})
