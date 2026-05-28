import { describe, expect, it, vi } from 'vitest'
import {
  filterWorkspaceConfigChangedRegions,
  normalizeWorkspaceConfigChangedPayload,
  resolveWorkspaceConfigChangedPayload
} from '../utils/workspaceConfigChanged'

describe('workspaceConfigChanged utils', () => {
  it('normalizes workspace/configChanged payloads', () => {
    const getNow = vi.fn(() => '2026-04-19T10:15:03.000Z')

    expect(
      normalizeWorkspaceConfigChangedPayload(
        {
          method: 'workspace/configChanged',
          params: {
            source: 'workspace/config/update',
            regions: ['skills', 'mcp', 123, null],
            changedAt: undefined
          }
        },
        getNow
      )
    ).toEqual({
      source: 'workspace/config/update',
      regions: ['skills', 'mcp'],
      changedAt: '2026-04-19T10:15:03.000Z'
    })
  })

  it('returns null for unrelated payloads or empty regions', () => {
    expect(
      normalizeWorkspaceConfigChangedPayload({
        method: 'turn/started',
        params: {}
      })
    ).toBeNull()

    expect(
      normalizeWorkspaceConfigChangedPayload({
        method: 'workspace/configChanged',
        params: { regions: [] }
      })
    ).toBeNull()
  })

  it('deduplicates repeated source and region pairs within the short window', () => {
    const dedupe = new Map<string, number>()

    const first = resolveWorkspaceConfigChangedPayload(
      {
        method: 'workspace/configChanged',
        params: {
          source: 'skills/setEnabled',
          regions: ['skills'],
          changedAt: '2026-04-19T10:15:03.000Z'
        }
      },
      dedupe
    )
    const second = resolveWorkspaceConfigChangedPayload(
      {
        method: 'workspace/configChanged',
        params: {
          source: 'skills/setEnabled',
          regions: ['skills'],
          changedAt: '2026-04-19T10:15:03.500Z'
        }
      },
      dedupe
    )

    expect(first?.regions).toEqual(['skills'])
    expect(second).toBeNull()
  })

  it('keeps non-duplicated regions when only part of the event is deduped', () => {
    const dedupe = new Map<string, number>([['workspace/config/update:skills', Date.parse('2026-04-19T10:15:03.000Z')]])

    const event = filterWorkspaceConfigChangedRegions(
      {
        source: 'workspace/config/update',
        regions: ['skills', 'mcp', 'externalChannel'],
        changedAt: '2026-04-19T10:15:03.500Z'
      },
      dedupe
    )

    expect(event).toEqual({
      source: 'workspace/config/update',
      regions: ['mcp', 'externalChannel'],
      changedAt: '2026-04-19T10:15:03.500Z'
    })
  })
})
