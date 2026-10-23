import { describe, expect, it } from 'vitest'
import { mapItemDetail, mapItemSummary } from './oratorio-mappers'
import type { ItemDetailResponse, ItemSummaryDto } from './oratorio-contracts'
import { repositoryName, repositoryPath } from './oratorio-model'

function summary(overrides: Partial<ItemSummaryDto> = {}): ItemSummaryDto {
  return {
    itemId: 'item-1', source: 'github', externalId: '42', kind: 'pullRequest', title: 'Review me',
    repository: 'example-org/sample-project', assignee: null, branch: 'feature/test', labels: ['review'], state: 'awaitingReview',
    currentRound: 2, checkState: 'passing', latestSummary: 'Ready', createdAt: '2026-01-01T00:00:00Z',
    updatedAt: '2026-01-01T00:00:00Z', taskStatus: 'in_review', ...overrides
  }
}

describe('Oratorio contract mapping', () => {
  it('maps lifecycle, latest result, and source identity and timestamps', () => {
    expect(mapItemSummary(summary())).toMatchObject({ id: 'item-1', state: 'awaiting-review', column: 'in-review', latestResult: 'none' })
    expect(mapItemSummary(summary({ latestResult: 'findings' })).latestResult).toBe('findings')
    expect(mapItemSummary(summary({ state: 'futureState' as never })).state).toBe('discovered')
    expect(mapItemSummary(summary({
      shortId: 'DEF-42',
      sourceUpdatedAt: '2026-08-09T11:50:00Z',
      lastSourceSyncAt: '2026-08-09T11:55:00Z'
    }))).toMatchObject({ shortId: 'DEF-42', sourceLabel: '#42', syncedAt: '2026-08-09T11:55:00Z', updatedAt: '2026-08-09T11:50:00Z' })
  })

  it('maps source identity without exposing the raw external id', () => {
    for (const { source, kind, externalId, expected } of [
      { source: 'github', kind: 'issue', externalId: 'issue:sample-org/widget-service#6', expected: '#6' },
      { source: 'github', kind: 'pullRequest', externalId: 'pr:sample-org/widget-service#174', expected: '#174' },
      { source: 'gitlab', kind: 'pullRequest', externalId: 'mr:gitlab.example.com/platform/runtime/oratorio!38', expected: '!38' },
      { source: 'local', kind: 'localTask', externalId: 'task:local-1', expected: 'Task' },
      { source: 'github', kind: 'issue', externalId: 'unparseable-source-id', expected: 'Issue' },
    ]) {
      expect(mapItemSummary(summary({ source, kind: kind as ItemSummaryDto['kind'], externalId })).sourceLabel).toBe(expected)
    }
  })

  it('maps the source body and latest run from detail', () => {
    const detail = {
      item: { ...summary(), itemId: 'item-1', workspaceId: 'workspace', description: 'Problem', currentRunId: 'run-1', lastSourceSyncAt: null },
      rounds: [], timeline: [], decisions: [], comments: [{ commentId: 'comment-1', roundId: null, authorKind: 'operator', authorName: 'Reviewer', body: 'Looks good', visibility: 'internal', createdAt: '2026-01-01T00:00:00Z' }],
      runs: [{ runId: 'run-1', roundId: 'round-1', attempt: 1, status: 'succeeded', runnerKind: 'appServer', threadId: 'thread-1', turnId: 'turn-1', startedAt: null, completedAt: null, summary: 'Done', errorCode: null, errorMessage: null, progressPercent: 100, statusMessage: null, lastHeartbeatAt: null, baseWorkspacePath: 'F:/workspace', worktreePath: 'F:/workspace/.craft/oratorio/worktrees/item-1', worktreeBranch: 'oratorio/run/item-1', baseRef: 'main', baseSha: null, worktreeStatus: 'ready', worktreeErrorCode: null, worktreeErrorMessage: null, retryCount: 0, nextRetryAt: null, targetHeadSha: null, purpose: 'reviewAnalysis', dispatchTrigger: 'manual', deliveryPolicy: 'manualDelivery', implementationTurnCount: 0 }],
      reviewDrafts: [{ draftId: 'draft-1', status: 'draft', comments: [] }], implementationDrafts: [], followUpDrafts: [], sourceWrites: [{ writeId: 'write-1' }]
    } as unknown as ItemDetailResponse
    const task = mapItemDetail(detail)
    expect(task).toMatchObject({ description: 'Ready', body: 'Problem' })
    expect(task.run).toMatchObject({ threadId: 'thread-1', workspacePath: 'F:/workspace', status: 'succeeded' })
    expect(task.detail?.runs[0].worktreeBranch).toBe('oratorio/run/item-1')
  })

  it('keeps the brief separate from the full source body and never uses the agent summary as body', () => {
    const detail = {
      item: { ...summary({ latestSummary: null }), itemId: 'item-1', workspaceId: 'workspace', description: '## Summary\nKeep filters stable.\n\n## Steps\n1. Reload', currentRunId: null, lastSourceSyncAt: null },
      rounds: [], runs: [], comments: [], timeline: [], decisions: [], sourceWrites: [], reviewDrafts: [], implementationDrafts: [], followUpDrafts: [], discussionTurns: [],
    } as ItemDetailResponse

    expect(mapItemDetail(detail)).toMatchObject({ description: 'Keep filters stable.', body: '## Summary\nKeep filters stable.\n\n## Steps\n1. Reload' })

    const agentOnly = {
      item: { ...summary({ latestSummary: 'Agent-generated result' }), itemId: 'item-1', workspaceId: 'workspace', description: null, currentRunId: null, lastSourceSyncAt: null },
      rounds: [], runs: [], comments: [], timeline: [], decisions: [], sourceWrites: [], reviewDrafts: [], implementationDrafts: [], followUpDrafts: [], discussionTurns: [],
    } as ItemDetailResponse

    expect(mapItemDetail(agentOnly)).toMatchObject({ description: 'Agent-generated result', body: '' })
  })
})

describe('GitLab sources', () => {
  it('names a nested GitLab project by its last two segments and spells out its path, keeping only a self-hosted instance', () => {
    expect(repositoryName('gitlab:gitlab.example.com/platform/payments/backend/billing-api')).toBe('backend/billing-api')
    expect(repositoryName('gitlab:gitlab.com/group/project')).toBe('group/project')
    expect(repositoryName('example-org/sample-project')).toBe('example-org/sample-project')
    expect(repositoryPath('gitlab:gitlab.com/group/sub/project')).toBe('group/sub/project')
    expect(repositoryPath('gitlab:gitlab.example.com/platform/billing-api')).toBe('gitlab.example.com/platform/billing-api')
    expect(repositoryPath('example-org/sample-project')).toBe('example-org/sample-project')
  })
})
