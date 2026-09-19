import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ActionTooltip } from '../components/ui/ActionTooltip'
import { IconButton } from '../components/ui/IconButton'
import { ACTION_SHORTCUTS, formatShortcutParts } from '../components/ui/shortcutKeys'
import { installDesktopApiMock } from './desktopApiMock'

beforeEach(() => {
  installDesktopApiMock({
    settings: {
      get: vi.fn().mockResolvedValue({ locale: 'en' })
    }
  })
})

describe('shortcut formatting', () => {
  it('formats Mod as Ctrl on Windows and Linux', () => {
    expect(formatShortcutParts(ACTION_SHORTCUTS.toggleDetailPanel, 'Win32')).toEqual([
      'Ctrl',
      'Shift',
      'B'
    ])
    expect(formatShortcutParts(ACTION_SHORTCUTS.settings, 'Linux x86_64')).toEqual(['Ctrl', ','])
  })

  it('formats Mod as Cmd on macOS', () => {
    expect(formatShortcutParts(ACTION_SHORTCUTS.quickOpen, 'MacIntel')).toEqual(['Cmd', 'P'])
  })
})

describe('ActionTooltip', () => {
  it('opens on keyboard focus and connects the trigger to the tooltip', async () => {
    render(
      <ActionTooltip label="Explain action" multiline>
        <button type="button">Explain</button>
      </ActionTooltip>
    )

    const button = screen.getByRole('button', { name: 'Explain' })
    fireEvent.focus(button)

    const tooltip = await screen.findByRole('tooltip')
    expect(button).toHaveAttribute('aria-describedby', tooltip.id)
    expect(tooltip).toHaveAttribute('data-multiline', 'true')
  })

})

describe('IconButton', () => {
  it('keeps the legacy label prop as the accessible name', () => {
    render(<IconButton icon={<span aria-hidden>R</span>} label="Refresh" />)

    expect(screen.getByRole('button', { name: 'Refresh' })).toBeInTheDocument()
  })
})
