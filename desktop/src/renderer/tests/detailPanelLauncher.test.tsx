// @vitest-environment jsdom
import { describe, it, expect, beforeEach, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { createElement } from 'react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { DetailPanelLauncher } from '../components/detail/DetailPanelLauncher'
import type { AddTabMenuAction } from '../../shared/addTabMenu'

function renderLauncher(props: {
  onAction: (action: AddTabMenuAction) => void
  canOpenWorkspaceTab?: boolean
}): void {
  render(
    createElement(
      LocaleProvider,
      null,
      createElement(DetailPanelLauncher, {
        onAction: props.onAction,
        canOpenWorkspaceTab: props.canOpenWorkspaceTab ?? true
      })
    )
  )
}

describe('DetailPanelLauncher', () => {
  beforeEach(() => {
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: async () => ({ locale: 'en' }) },
        platform: 'win32'
      }
    })
  })

  it('renders the four Codex-style launcher cards', () => {
    renderLauncher({ onAction: vi.fn() })
    expect(screen.getByRole('button', { name: 'Files' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Browser' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Changes' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Terminal' })).toBeInTheDocument()
  })

  it('dispatches the matching add-tab action when a card is clicked', () => {
    const onAction = vi.fn()
    renderLauncher({ onAction })

    fireEvent.click(screen.getByRole('button', { name: 'Files' }))
    expect(onAction).toHaveBeenCalledWith('openFile')

    fireEvent.click(screen.getByRole('button', { name: 'Changes' }))
    expect(onAction).toHaveBeenCalledWith('newChanges')
  })

  it('disables Browser and Terminal without an active workspace', () => {
    renderLauncher({ onAction: vi.fn(), canOpenWorkspaceTab: false })
    expect(screen.getByRole('button', { name: 'Browser' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Terminal' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Files' })).not.toBeDisabled()
    expect(screen.getByRole('button', { name: 'Changes' })).not.toBeDisabled()
  })

  it('shows a keyboard shortcut on every card', () => {
    renderLauncher({ onAction: vi.fn() })
    expect(screen.getByText('Ctrl+P')).toBeInTheDocument()
    expect(screen.getByText('Ctrl+T')).toBeInTheDocument()
    expect(screen.getByText('Ctrl+Shift+G')).toBeInTheDocument()
    expect(screen.getByText('Ctrl+`')).toBeInTheDocument()
  })
})
