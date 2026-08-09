import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { OratorioBoard } from '../components/oratorio/OratorioBoard'
import { useToastStore } from '../stores/toastStore'

describe('OratorioBoard', () => {
  beforeEach(() => useToastStore.setState({ toasts: [] }))

  it('uses the host toast system for source sync feedback', async () => {
    render(
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
    render(
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
    render(
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
})
