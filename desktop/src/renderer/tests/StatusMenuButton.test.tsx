import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { StatusMenuButton } from '../components/ui/StatusMenuButton'

describe('StatusMenuButton', () => {
  it('exposes menu ARIA, opens from the keyboard, and restores focus on Escape', async () => {
    render(<StatusMenuButton label="Connected" tone="success" items={[{ label: 'Disconnect', danger: true, onClick: vi.fn() }]} />)
    const trigger = screen.getByRole('button', { name: 'Connected' })
    expect(trigger).toHaveAttribute('aria-haspopup', 'menu')
    expect(trigger).toHaveAttribute('aria-expanded', 'false')

    trigger.focus()
    fireEvent.keyDown(trigger, { key: 'Enter' })
    expect(trigger).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByRole('menuitem', { name: 'Disconnect' })).toHaveStyle({ color: 'var(--error)' })

    fireEvent.keyDown(document, { key: 'Escape' })
    await waitFor(() => expect(trigger).toHaveFocus())
    expect(trigger).toHaveAttribute('aria-expanded', 'false')
  })

  it('disables interaction while loading', () => {
    render(<StatusMenuButton label="Connecting…" loading items={[{ label: 'Disconnect', onClick: vi.fn() }]} />)
    expect(screen.getByRole('button', { name: 'Connecting…' })).toBeDisabled()
  })
})
