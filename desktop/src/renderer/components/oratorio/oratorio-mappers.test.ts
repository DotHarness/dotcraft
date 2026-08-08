import { describe, expect, it } from 'vitest'
import { mapItemDetail, mapItemSummary } from './oratorio-mappers'
import type { ItemDetailResponse, ItemSummaryDto } from './oratorio-contracts'

function summary(overrides: Partial<ItemSummaryDto> = {}): ItemSummaryDto {
  return {
    itemId: 'item-1', source: 'github', externalId: '42', kind: 'pullRequest', title: 'Review me',
    repository: 'example-org/sample-project', assignee: null, branch: 'feature/test', labels: ['review'], state: 'awaitingReview',
    currentRound: 2, checkState: 'passing', latestSummary: 'Ready', createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z', taskStatus: 'in_review', ...overrides
  }
}

describe('Oratorio contract mapping', () => {
  it('preserves server lifecycle and does not invent detail artifacts', () => {
    expect(mapItemSummary(summary())).toMatchObject({ id: 'item-1', state: 'awaiting-review', column: 'in-review', artifacts: { reviewDrafts: 0 } })
  })

  it('maps real draft, comment, write, and run counts from detail', () => {
    const detail = {
      item: { ...summary(), itemId: 'item-1', workspaceId: 'workspace', description: 'Problem', currentRunId: 'run-1', lastSourceSyncAt: null },
      rounds: [], timeline: [], decisions: [], comments: [{ commentId: 'comment-1', roundId: null, authorKind: 'operator', authorName: 'Reviewer', body: 'Looks good', visibility: 'internal', createdAt: '2026-01-01T00:00:00Z' }],
      runs: [{ runId: 'run-1', roundId: 'round-1', attempt: 1, status: 'succeeded', runnerKind: 'appServer', threadId: 'thread-1', turnId: 'turn-1', startedAt: null, completedAt: null, summary: 'Done', errorCode: null, errorMessage: null, progressPercent: 100, statusMessage: null, lastHeartbeatAt: null, baseWorkspacePath: 'F:/workspace', worktreePath: 'F:/workspace/.craft/oratorio/worktrees/item-1', worktreeBranch: 'oratorio/run/item-1', baseRef: 'main', baseSha: null, worktreeStatus: 'ready', worktreeErrorCode: null, worktreeErrorMessage: null, retryCount: 0, nextRetryAt: null, targetHeadSha: null, purpose: 'reviewAnalysis', dispatchTrigger: 'manual', deliveryPolicy: 'manualDelivery', implementationTurnCount: 0 }],
      reviewDrafts: [{ draftId: 'draft-1', status: 'draft', comments: [] }], implementationDrafts: [], followUpDrafts: [], sourceWrites: [{ writeId: 'write-1' }]
    } as unknown as ItemDetailResponse
    const task = mapItemDetail(detail)
    expect(task.description).toBe('Problem')
    expect(task.artifacts).toMatchObject({ reviewDrafts: 1, actionableReviewDrafts: 1, comments: 1, writes: 1 })
    expect(task.run).toMatchObject({ threadId: 'thread-1', workspacePath: 'F:/workspace', status: 'succeeded' })
    expect(task.detail?.runs[0].worktreeBranch).toBe('oratorio/run/item-1')
  })

  it('falls back safely for an unknown lifecycle state', () => {
    expect(mapItemSummary(summary({ state: 'futureState' as never })).state).toBe('discovered')
  })
})
