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
})
