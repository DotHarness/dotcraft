import './setupPluginRuntime'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
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

  it('opens a task in the drawer with its source detail and expands it to the full page', async () => {
    const summaryTask = {
      id: 'pr-198', shortId: 'DEF-198', sourceLabel: '#198', provider: 'github' as const, repository: 'example-org/sample-app', kind: 'Pull request' as const,
      title: 'Preserve filters when opening a thread', description: 'Agent-generated list summary', assignee: null, labels: ['frontend'],
      column: 'in-review' as const, state: 'awaiting-review' as const, lifecycle: 'open' as const, updatedAt: '2026-08-08T08:00:00Z',
      latestResult: 'none' as const, capabilities: { decide: true },
    }
    const detailTask = {
      ...summaryTask,
      body: 'Keep the Board context stable.',
      detail: { item: {}, rounds: [], runs: [], comments: [], timeline: [], decisions: [], sourceWrites: [], reviewDrafts: [], implementationDrafts: [], followUpDrafts: [], discussionTurns: [] },
    } as unknown as typeof summaryTask & { detail: import('../../bundled-plugins/oratorio/src/oratorio-contracts').ItemDetailResponse }
    const onOpenDetail = vi.fn()

    renderBoard(
      <OratorioBoard
        tasks={[summaryTask]}
        onLoadTaskDetail={vi.fn().mockResolvedValue(detailTask)}
        onOpenDetail={onOpenDetail}
        onOpenSettings={vi.fn()}
        onOpenThread={vi.fn()}
      />
    )

    fireEvent.click(screen.getByRole('button', { name: /Preserve filters when opening a thread/ }))
    const drawer = within(await screen.findByRole('complementary'))
    expect(await drawer.findByText('Keep the Board context stable.')).toBeInTheDocument()
    expect(drawer.queryByText('Agent-generated list summary')).not.toBeInTheDocument()

    fireEvent.click(drawer.getByRole('button', { name: 'Full details' }))
    expect(onOpenDetail).toHaveBeenCalledWith(expect.objectContaining({ id: 'pr-198' }))
  })
})
