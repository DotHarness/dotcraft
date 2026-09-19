import { act, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import {
  ConfirmDialog,
  ConfirmDialogHost,
  requestConfirmDialog,
  type ConfirmDialogRequest
} from '../components/ui/ConfirmDialog'

describe('ConfirmDialog', () => {
  it('uses primary confirmation and focuses cancel by default', async () => {
    render(<ConfirmDialog title="Save?" message="Continue" onConfirm={vi.fn()} onCancel={vi.fn()} />)
    const cancel = screen.getByRole('button', { name: 'Cancel' })
    expect(screen.getByRole('button', { name: 'Confirm' })).toHaveAttribute('data-variant', 'primary')
    await waitFor(() => expect(cancel).toHaveFocus())
  })

  it('dismisses an imperative request and resolves it as cancelled', async () => {
    render(<ConfirmDialogHost />)
    let request!: ConfirmDialogRequest
    act(() => {
      request = requestConfirmDialog({ title: 'Remove?', message: 'Confirm removal' })
    })
    expect(screen.getByRole('dialog', { name: 'Remove?' })).toBeInTheDocument()

    act(() => request.dismiss())

    expect(screen.queryByRole('dialog')).toBeNull()
    await expect(request.result).resolves.toBe(false)
  })
})
