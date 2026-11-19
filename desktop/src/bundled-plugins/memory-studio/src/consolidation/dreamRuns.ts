import type { DreamsRunState } from '@dotcraft/sdk/contracts'

export function isPendingReview(run: DreamsRunState): boolean {
  return run.status === 'succeeded' && run.reviewStatus === 'pending'
}

export function orderRuns(runs: readonly DreamsRunState[]): DreamsRunState[] {
  return [...runs].sort((a, b) => {
    const pending = Number(isPendingReview(b)) - Number(isPendingReview(a))
    return pending || (b.startedAt ?? '').localeCompare(a.startedAt ?? '')
  })
}

export function changeCount(run: DreamsRunState): number {
  const changes = run.changes
  return (changes?.added?.length ?? 0) + (changes?.updated?.length ?? 0) + (changes?.deleted?.length ?? 0)
}

export type RunStatusFilter = 'all' | 'pending' | 'applied' | 'discarded' | 'failed'

export function matchesRunStatus(run: DreamsRunState, filter: RunStatusFilter): boolean {
  switch (filter) {
    case 'all': return true
    case 'pending': return isPendingReview(run)
    case 'applied': return run.reviewStatus === 'applied'
    case 'discarded': return run.reviewStatus === 'discarded'
    case 'failed': return run.status === 'failed'
  }
}

export function matchesRunSearch(run: DreamsRunState, storeName: string, query: string): boolean {
  const needle = query.trim().toLowerCase()
  if (!needle) return true
  return [storeName, run.summary, run.message].some((text) => text?.toLowerCase().includes(needle))
}
