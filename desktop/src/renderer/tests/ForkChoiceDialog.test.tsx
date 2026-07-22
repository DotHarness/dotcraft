import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ForkChoiceDialog } from '../components/conversation/ForkChoiceDialog'

describe('ForkChoiceDialog', () => {
  beforeEach(() => {
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: { settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) } }
    })
  })

  it('omits a visible cancel action while retaining Escape dismissal', () => {
    const onCancel = vi.fn()
    render(
      <LocaleProvider>
        <ForkChoiceDialog onChoose={vi.fn()} onCancel={onCancel} />
      </LocaleProvider>
    )

    expect(screen.getByRole('button', { name: /Fork into local/ })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /Fork into new worktree/ })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Cancel' })).not.toBeInTheDocument()

    fireEvent.keyDown(document, { key: 'Escape' })
    expect(onCancel).toHaveBeenCalledTimes(1)
  })
})
