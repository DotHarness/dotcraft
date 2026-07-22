import { describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ViewerActionsMenu } from '../components/detail/ViewerActionsMenu'

describe('ViewerActionsMenu', () => {
  it('uses the shared menu and restores trigger focus after Escape', async () => {
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: vi.fn(async () => ({ locale: 'en' })) },
        workspace: { viewer: { readText: vi.fn() } }
      }
    })

    render(
      <LocaleProvider>
        <ViewerActionsMenu
          absolutePath="fixtures/sample.ts"
          isText
          wordWrap={false}
          onToggleWordWrap={vi.fn()}
        />
      </LocaleProvider>
    )

    const trigger = screen.getByRole('button', { name: 'More actions' })
    fireEvent.click(trigger)
    expect(screen.getByRole('menu')).toBeInTheDocument()
    expect(trigger).toHaveAttribute('aria-expanded', 'true')

    fireEvent.keyDown(document, { key: 'Escape' })

    await waitFor(() => {
      expect(screen.queryByRole('menu')).not.toBeInTheDocument()
      expect(trigger).toHaveFocus()
    })
  })
})
