import type { ItemDetailResponse } from './oratorio-contracts'

export interface OratorioQuickViewResult {
  kind: 'review' | 'implementation' | 'follow-up'
  summary: string
  updatedAt: string
}
export function sourceSummary(description: string | null | undefined): string | null {
  const source = description?.trim()
  if (!source) return null

  const lines = source.replace(/\r\n/g, '\n').split('\n')
  const summary: string[] = []
  let readingSummary = false

  for (const line of lines) {
    const heading = line.match(/^#{2,4}\s+(.+?)\s*$/)
    if (heading) {
      const name = heading[1].trim().toLowerCase()
      if (name === 'summary') {
        readingSummary = true
        continue
      }
      if (readingSummary) break
    }
    if (readingSummary) summary.push(line)
  }

  return summary.join('\n').trim() || source
}
export function latestFormalResult(detail: ItemDetailResponse | undefined): OratorioQuickViewResult | null {
  if (!detail) return null

  const results: OratorioQuickViewResult[] = [
    ...(detail.reviewDrafts ?? []).map((draft) => ({ kind: 'review' as const, summary: draft.summaryBody.trim(), updatedAt: draft.updatedAt })),
    ...(detail.implementationDrafts ?? []).map((draft) => ({ kind: 'implementation' as const, summary: draft.summary.trim(), updatedAt: draft.updatedAt })),
    ...(detail.followUpDrafts ?? []).map((draft) => ({ kind: 'follow-up' as const, summary: draft.body.trim(), updatedAt: draft.updatedAt })),
  ].filter((result) => Boolean(result.summary))

  return results.sort((left, right) => Date.parse(right.updatedAt) - Date.parse(left.updatedAt))[0] ?? null
}
