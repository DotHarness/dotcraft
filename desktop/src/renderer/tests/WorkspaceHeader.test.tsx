import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { WorkspaceHeader, WorkspaceOptionsMenu } from '../components/sidebar/WorkspaceHeader'
import { ConfirmDialogHost } from '../components/ui/ConfirmDialog'

const settingsGet = vi.fn()
const workspaceGetRecent = vi.fn()
const workspaceClearRecent = vi.fn()
const workspaceSwitch = vi.fn()
const workspacePickFolder = vi.fn()
const workspaceClearSelection = vi.fn()
const shellOpenPath = vi.fn()

function renderHeader(): void {
  render(
    <LocaleProvider>
      <WorkspaceHeader workspaceName='dotcraft' workspacePath='X:\\fixtures\\workspace' />
    </LocaleProvider>
  )
}

function renderOptionsMenu(): void {
  render(
    <LocaleProvider>
      <ConfirmDialogHost />
      <WorkspaceOptionsMenu workspacePath='X:\\fixtures\\workspace' />
    </LocaleProvider>
  )
}

function openWorkspaceMenu(): void {
  fireEvent.click(screen.getByRole('button', { name: 'Workspace options' }))
}

function openRecentSubmenu(): void {
  const recentLabel = screen.getByText('Recent Workspaces')
  const submenuTrigger = recentLabel.parentElement?.parentElement
  if (!submenuTrigger) {
    throw new Error('Recent submenu trigger not found')
  }
  fireEvent.mouseEnter(submenuTrigger)
}

describe('WorkspaceHeader', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ locale: 'en' })
    workspaceGetRecent.mockResolvedValue([
      { path: 'F:\\workspace-a', name: 'workspace-a', lastOpenedAt: '2026-04-19T00:00:00.000Z' },
      { path: 'F:\\workspace-b', name: 'workspace-b', lastOpenedAt: '2026-04-19T00:01:00.000Z' }
    ])
    workspaceClearRecent.mockResolvedValue(undefined)
    workspaceSwitch.mockResolvedValue(undefined)
    workspacePickFolder.mockResolvedValue(null)
    workspaceClearSelection.mockResolvedValue(undefined)
    shellOpenPath.mockResolvedValue('')
    vi.spyOn(window, 'alert').mockImplementation(() => {})

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: settingsGet
        },
        workspace: {
          getRecent: workspaceGetRecent,
          clearRecent: workspaceClearRecent,
          switch: workspaceSwitch,
          pickFolder: workspacePickFolder,
          clearSelection: workspaceClearSelection
        },
        shell: {
          openPath: shellOpenPath
        }
      }
    })
  })

  it('renders workspace identity without a persistent options button', () => {
    renderHeader()

    expect(screen.getByText('dotcraft')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Workspace options' })).not.toBeInTheDocument()
  })

  it('shows a clear recent action in the recent workspace submenu', async () => {
    renderOptionsMenu()

    openWorkspaceMenu()
    await waitFor(() => {
      expect(workspaceGetRecent).toHaveBeenCalledOnce()
    })

    openRecentSubmenu()

    expect(await screen.findByRole('button', { name: 'Clear Recently Opened...' })).toBeInTheDocument()
  })

  it('returns to the welcome screen from the switch workspace menu item', async () => {
    renderOptionsMenu()

    openWorkspaceMenu()
    await waitFor(() => {
      expect(workspaceGetRecent).toHaveBeenCalledOnce()
    })

    fireEvent.click(screen.getByText('Switch Workspace'))

    await waitFor(() => {
      expect(workspaceClearSelection).toHaveBeenCalledOnce()
    })
    expect(workspacePickFolder).not.toHaveBeenCalled()
    expect(workspaceSwitch).not.toHaveBeenCalled()
  })

  it('keeps recent workspace entries as direct switches', async () => {
    renderOptionsMenu()

    openWorkspaceMenu()
    await waitFor(() => {
      expect(workspaceGetRecent).toHaveBeenCalledOnce()
    })
    openRecentSubmenu()

    fireEvent.click(await screen.findByText('workspace-a'))

    await waitFor(() => {
      expect(workspaceSwitch).toHaveBeenCalledWith('F:\\workspace-a')
    })
    expect(workspaceClearSelection).not.toHaveBeenCalled()
    expect(workspacePickFolder).not.toHaveBeenCalled()
  })

  it('does not clear recents when confirmation is cancelled', async () => {
    renderOptionsMenu()

    openWorkspaceMenu()
    await waitFor(() => {
      expect(workspaceGetRecent).toHaveBeenCalledOnce()
    })
    openRecentSubmenu()

    fireEvent.click(await screen.findByRole('button', { name: 'Clear Recently Opened...' }))

    expect(await screen.findByRole('dialog', { name: 'Clear recently opened workspaces?' })).toBeInTheDocument()
    expect(screen.getByText('This removes all saved workspace history from the recent list.')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))

    expect(workspaceClearRecent).not.toHaveBeenCalled()
  })

  it('clears recents after confirmation and updates the submenu', async () => {
    renderOptionsMenu()

    openWorkspaceMenu()
    await waitFor(() => {
      expect(workspaceGetRecent).toHaveBeenCalledOnce()
    })
    openRecentSubmenu()

    expect(await screen.findByText('workspace-a')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Clear Recently Opened...' }))
    expect(await screen.findByRole('dialog', { name: 'Clear recently opened workspaces?' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Clear' }))

    await waitFor(() => {
      expect(workspaceClearRecent).toHaveBeenCalledOnce()
    })
    expect(screen.queryByText('workspace-a')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Clear Recently Opened...' })).not.toBeInTheDocument()
  })

  it('does not show the clear action when there are no recents', async () => {
    workspaceGetRecent.mockResolvedValue([])
    renderOptionsMenu()

    openWorkspaceMenu()
    await waitFor(() => {
      expect(workspaceGetRecent).toHaveBeenCalledOnce()
    })

    expect(screen.queryByRole('button', { name: 'Clear Recently Opened...' })).not.toBeInTheDocument()
  })
})
