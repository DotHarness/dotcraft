import { render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { ConfirmDialog } from '../components/ui/ConfirmDialog'

describe('ConfirmDialog', () => {
  it('uses primary confirmation and focuses cancel by default', async () => {
    render(<ConfirmDialog title="Save?" message="Continue" onConfirm={vi.fn()} onCancel={vi.fn()} />)
    const cancel = screen.getByRole('button', { name: 'Cancel' })
    expect(screen.getByRole('button', { name: 'Confirm' })).toHaveAttribute('data-variant', 'primary')
    await waitFor(() => expect(cancel).toHaveFocus())
  })

  it('uses the danger variant for destructive confirmation', () => {
    render(<ConfirmDialog title="Delete?" message="Cannot be undone" danger onConfirm={vi.fn()} onCancel={vi.fn()} />)
    expect(screen.getByRole('button', { name: 'Confirm' })).toHaveAttribute('data-variant', 'danger')
  })
})
