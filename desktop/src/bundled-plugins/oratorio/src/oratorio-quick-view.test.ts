import { describe, expect, it } from 'vitest'
import type { ItemDetailResponse } from './oratorio-contracts'
import { latestFormalResult, sourceSummary } from './oratorio-quick-view'

describe('Oratorio Quick View content', () => {
  it('prefers a structured source summary and otherwise preserves the source description', () => {
    expect(sourceSummary('## Summary\nKeep filters stable.\n\n## Key details\nDo not show this.')).toBe('Keep filters stable.')
    expect(sourceSummary('Plain source description.')).toBe('Plain source description.')
    expect(sourceSummary('')).toBeNull()
  })

  it('selects the newest formal artifact without reading run summaries', () => {
    const detail = {
      runs: [{ summary: 'Raw agent run output' }],
      reviewDrafts: [{ summaryBody: 'Review result', updatedAt: '2026-08-08T08:00:00Z' }],
      implementationDrafts: [{ summary: 'Implementation result', updatedAt: '2026-08-08T09:00:00Z' }],
      followUpDrafts: [{ body: 'Follow-up result', updatedAt: '2026-08-08T07:00:00Z' }],
    } as ItemDetailResponse

    expect(latestFormalResult(detail)).toMatchObject({ kind: 'implementation', summary: 'Implementation result' })
  })
})
