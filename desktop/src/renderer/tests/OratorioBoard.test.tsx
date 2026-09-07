import './setupPluginRuntime'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { mapItemSummary } from '../../bundled-plugins/oratorio/src/oratorio-mappers'
import type { ItemSummaryDto } from '../../bundled-plugins/oratorio/src/oratorio-contracts'
import { OratorioBoard } from '../../bundled-plugins/oratorio/src/OratorioBoard'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useToastStore } from '../stores/toastStore'
import { installDesktopApiMock } from './desktopApiMock'
import { installOratorioTestHost } from './oratorioPluginTestHost'

function renderBoard(board: ReactNode) {
  return render(<LocaleProvider>{board}</LocaleProvider>)
}

describe('OratorioBoard', () => {
  beforeEach(() => {
    installDesktopApiMock({ settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) } })
    installOratorioTestHost()
    useToastStore.setState({ toasts: [] })
  })

  it('keeps archived and cancelled open-source tasks out of Active while preserving completed work', () => {
    const item: ItemSummaryDto = {
      itemId: 'done', source: 'github', externalId: '42', kind: 'issue', title: 'Completed work',
      repository: 'example/project', assignee: null, branch: null, labels: [], state: 'approved',
      currentRound: 1, checkState: null, latestSummary: null, sourceState: 'open',
      createdAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z', taskStatus: 'done',
    }
    const tasks = [
      mapItemSummary(item),
      mapItemSummary({ ...item, itemId: 'archived', title: 'Archived work', state: 'archived', taskStatus: 'cancelled' }),
      mapItemSummary({ ...item, itemId: 'cancelled', title: 'Cancelled work', state: 'rejected', taskStatus: 'cancelled' }),
    ]
    renderBoard(<OratorioBoard tasks={tasks} onOpenDetail={vi.fn()} onOpenSettings={vi.fn()} onOpenThread={vi.fn()} />)

    expect(screen.getByText('Completed work')).toBeInTheDocument()
    expect(screen.queryByText('Archived work')).not.toBeInTheDocument()
    expect(screen.queryByText('Cancelled work')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Archived', exact: true }))
    expect(screen.getByText('Archived work')).toBeInTheDocument()
    expect(screen.queryByText('Completed work')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'All', exact: true }))
    for (const task of tasks) expect(screen.getByText(task.title)).toBeInTheDocument()
  })

  it('uses the host toast system for source sync feedback', async () => {
    renderBoard(
      <OratorioBoard
        tasks={[]}
        onSync={vi.fn().mockResolvedValue(undefined)}
        onOpenDetail={vi.fn()}
        onOpenSettings={vi.fn()}
        onOpenThread={vi.fn()}
      />
    )

    fireEvent.click(screen.getByRole('button', { name: 'Sync sources' }))

    await waitFor(() => {
      expect(useToastStore.getState().toasts).toEqual(expect.arrayContaining([
        expect.objectContaining({ message: 'Sources updated', type: 'success' })
      ]))
    })
    expect(document.querySelector('.ora-board__notice')).toBeNull()
  })

  it('keeps closed-list headers compact and omits the old subtitle', () => {
    renderBoard(
      <OratorioBoard
        initialMode="cancelled"
        tasks={[]}
        onOpenDetail={vi.fn()}
        onOpenSettings={vi.fn()}
        onOpenThread={vi.fn()}
      />
    )

    const closedList = screen.getByRole('region', { name: 'cancelled tasks' })
    expect(closedList.querySelector('header > strong')).toHaveTextContent('Cancelled')
    expect(screen.getByText('No matching tasks')).toBeInTheDocument()
    expect(screen.queryByText('Closed work is shown as a scan-friendly list.')).not.toBeInTheDocument()
  })

  it('shows source identity and timestamps without redundant quick-view facts', () => {
    renderBoard(
      <OratorioBoard
        tasks={[{
          id: 'issue-42', shortId: 'DEF-42', sourceLabel: '#42', provider: 'github', repository: 'example/repository', kind: 'Issue',
          title: 'Review source metadata', description: 'Keep source identity visible.', assignee: null, labels: ['docs', 'frontend'],
          column: 'todo', state: 'discovered', lifecycle: 'open', synced: '5 min ago', updated: '10 min ago',
          artifacts: { reviewDrafts: 0, implementationDrafts: 0, followUpDrafts: 0, comments: 0, writes: 0 },
          capabilities: { dispatch: true, implement: true, autoTarget: true, reviewOnly: true }
        }]}
        onOpenDetail={vi.fn()}
        onOpenSettings={vi.fn()}
        onOpenThread={vi.fn()}
      />
    )

    expect(screen.getByText('#42')).toBeInTheDocument()
    expect(screen.getByText('synced 5 min ago · updated 10 min ago')).toBeInTheDocument()
    expect(screen.queryByText('2 labels')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: /Review source metadata/ }))
    expect(screen.getByText('Start Agent work')).toBeInTheDocument()
    expect(screen.queryByText('State')).not.toBeInTheDocument()
    expect(screen.queryByText('Assignee')).not.toBeInTheDocument()
    expect(screen.queryByText('Round')).not.toBeInTheDocument()
    expect(screen.queryByText('Updated')).not.toBeInTheDocument()
  })

  it('uses a clock icon for a pending check', () => {
    renderBoard(
      <OratorioBoard
        tasks={[{
          id: 'pr-42', shortId: 'EX-42', sourceLabel: '#42', provider: 'github', repository: 'example/widgets', kind: 'Pull request',
          title: 'Refine built-in agent profiles', description: 'Could not fetch review target.', assignee: null, labels: [],
          column: 'in-progress', state: 'discovered', lifecycle: 'open', check: 'pending', updated: '12 min ago',
          artifacts: { reviewDrafts: 0, implementationDrafts: 0, followUpDrafts: 0, comments: 0, writes: 0 },
          capabilities: {},
        }]}
        onOpenDetail={vi.fn()}
        onOpenSettings={vi.fn()}
        onOpenThread={vi.fn()}
      />
    )

    const pending = screen.getByText('pending').closest('.ora-status')
    expect(pending?.querySelector('svg.lucide-clock-3')).not.toBeNull()
    expect(pending).not.toHaveTextContent('◌')
  })

  it('loads source detail for Quick View and reports decisions through the host toast', async () => {
    const summaryTask = {
      id: 'pr-198', shortId: 'DEF-198', sourceLabel: '#198', provider: 'github' as const, repository: 'DotHarness/dotcraft', kind: 'Pull request' as const,
      title: 'Preserve filters when opening a thread', description: 'Agent-generated list summary', assignee: null, labels: ['frontend'],
      column: 'in-review' as const, state: 'awaiting-review' as const, lifecycle: 'open' as const, updated: '10 min ago',
      artifacts: { reviewDrafts: 0, implementationDrafts: 0, followUpDrafts: 0, comments: 0, writes: 0 }, capabilities: { decide: true },
    }
    const detailTask = {
      ...summaryTask,
      description: '## Summary\nKeep the Board context stable.\n\n## Key details\nInternal detail.',
      artifacts: { ...summaryTask.artifacts, reviewDrafts: 1 },
      detail: {
        item: {}, rounds: [], runs: [{ summary: 'Raw run output' }], comments: [], timeline: [], decisions: [], sourceWrites: [], implementationDrafts: [], followUpDrafts: [], discussionTurns: [],
        reviewDrafts: [{ summaryBody: 'The implementation preserves Board context.', updatedAt: '2026-08-08T08:00:00Z' }],
      },
    } as unknown as typeof summaryTask & { detail: import('../../bundled-plugins/oratorio/src/oratorio-contracts').ItemDetailResponse }
    const onAction = vi.fn().mockResolvedValue(detailTask)

    renderBoard(
      <OratorioBoard
        tasks={[summaryTask]}
        onLoadTaskDetail={vi.fn().mockResolvedValue(detailTask)}
        onTaskAction={onAction}
        onOpenDetail={vi.fn()}
        onOpenSettings={vi.fn()}
        onOpenThread={vi.fn()}
      />
    )

    fireEvent.click(screen.getByRole('button', { name: /Preserve filters when opening a thread/ }))
    expect(await screen.findByText('Keep the Board context stable.')).toBeInTheDocument()
    expect(screen.getByText('The implementation preserves Board context.')).toBeInTheDocument()
    expect(screen.queryByText('Agent-generated list summary')).not.toBeInTheDocument()
    expect(screen.queryByText('Raw run output')).not.toBeInTheDocument()
    expect(screen.queryByText('Drafts')).not.toBeInTheDocument()
    expect(screen.queryByText('Comments')).not.toBeInTheDocument()
    expect(screen.queryByText('Writes')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Approve' }))
    await waitFor(() => expect(useToastStore.getState().toasts).toEqual(expect.arrayContaining([
      expect.objectContaining({ message: 'Task approved', type: 'success' })
    ])))
  })
})
