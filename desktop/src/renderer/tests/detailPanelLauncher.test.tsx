// @vitest-environment jsdom
import { describe, it, expect, beforeEach, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { createElement } from 'react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { DetailPanelLauncher } from '../components/detail/DetailPanelLauncher'
import type { AddTabMenuAction } from '../../shared/addTabMenu'
import { installDesktopApiMock } from './desktopApiMock'

function renderLauncher(props: {
  onAction: (action: AddTabMenuAction) => void
  canOpenWorkspaceTab?: boolean
  systemTabsAvailable?: boolean
}): void {
  render(
    createElement(
      LocaleProvider,
      null,
      createElement(DetailPanelLauncher, {
        onAction: props.onAction,
        canOpenWorkspaceTab: props.canOpenWorkspaceTab ?? true,
        systemTabsAvailable: props.systemTabsAvailable ?? true
      })
    )
  )
}

describe('DetailPanelLauncher', () => {
  beforeEach(() => {
    installDesktopApiMock({
      settings: { get: async () => ({ locale: 'en' }) },
      platform: 'win32'
    })
  })

  it('dispatches the matching add-tab action when a card is clicked', () => {
    const onAction = vi.fn()
    renderLauncher({ onAction })

    fireEvent.click(screen.getByRole('button', { name: 'Files' }))
    expect(onAction).toHaveBeenCalledWith('openFile')

    fireEvent.click(screen.getByRole('button', { name: 'Changes' }))
    expect(onAction).toHaveBeenCalledWith('newChanges')

    fireEvent.click(screen.getByRole('button', { name: 'Checks' }))
    expect(onAction).toHaveBeenCalledWith('newPlan')
  })

  it('disables Files, Browser, and Terminal without an active workspace', () => {
    renderLauncher({ onAction: vi.fn(), canOpenWorkspaceTab: false })
    expect(screen.getByRole('button', { name: 'Browser' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Terminal' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Files' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Changes' })).not.toBeDisabled()
    expect(screen.getByRole('button', { name: 'Checks' })).not.toBeDisabled()
  })

  it('leaves out the thread-only cards when no thread is open', () => {
    const onAction = vi.fn()
    renderLauncher({ onAction, systemTabsAvailable: false })

    fireEvent.click(screen.getByRole('button', { name: 'Browser' }))
    expect(onAction).toHaveBeenCalledWith('newBrowser')
    expect(screen.getByRole('button', { name: 'Files' })).not.toBeDisabled()
    expect(screen.getByRole('button', { name: 'Terminal' })).not.toBeDisabled()
    expect(screen.queryByRole('button', { name: 'Changes' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Checks' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Subagents' })).toBeNull()
  })

})
