import { describe, expect, it, vi } from 'vitest'
import {
  filterConfigChangedRegions,
  normalizeConfigChangedPayload,
  resolveConfigChangedPayload
} from '../utils/configChanged'

describe('configChanged utils', () => {
  it('normalizes config/changed payloads', () => {
    const getNow = vi.fn(() => '2026-04-19T10:15:03.000Z')

    expect(
      normalizeConfigChangedPayload(
        {
          method: 'config/changed',
          params: {
            source: 'config/batchWrite',
            regions: ['skills', 'mcp', 123, null],
            changedAt: undefined
          }
        },
        getNow
      )
    ).toEqual({
      source: 'config/batchWrite',
      regions: ['skills', 'mcp'],
      changedAt: '2026-04-19T10:15:03.000Z'
    })
  })

  it('returns null for unrelated payloads or empty regions', () => {
    expect(
      normalizeConfigChangedPayload({
        method: 'turn/started',
        params: {}
      })
    ).toBeNull()

    expect(
      normalizeConfigChangedPayload({
        method: 'config/changed',
        params: { regions: [] }
      })
    ).toBeNull()
  })

  it('deduplicates repeated source and region pairs within the short window', () => {
    const dedupe = new Map<string, number>()

    const first = resolveConfigChangedPayload(
      {
        method: 'config/changed',
        params: {
          source: 'skills/setEnabled',
          regions: ['skills'],
          changedAt: '2026-04-19T10:15:03.000Z'
        }
      },
      dedupe
    )
    const second = resolveConfigChangedPayload(
      {
        method: 'config/changed',
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
    const dedupe = new Map<string, number>([['config/batchWrite:skills', Date.parse('2026-04-19T10:15:03.000Z')]])

    const event = filterConfigChangedRegions(
      {
        source: 'config/batchWrite',
        regions: ['skills', 'mcp', 'externalChannel'],
        changedAt: '2026-04-19T10:15:03.500Z'
      },
      dedupe
    )

    expect(event).toEqual({
      source: 'config/batchWrite',
      regions: ['mcp', 'externalChannel'],
      changedAt: '2026-04-19T10:15:03.500Z'
    })
  })
})
