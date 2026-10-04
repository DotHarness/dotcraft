import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { WorkspaceHeader, WorkspaceOptionsMenu } from '../components/sidebar/WorkspaceHeader'
import { ConfirmDialogHost } from '../components/ui/ConfirmDialog'
import { installDesktopApiMock } from './desktopApiMock'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import type { WorkspaceProjectSummary } from '../../shared/workspaceProjects'

const settingsGet = vi.fn()
const workspaceClearProjects = vi.fn()
const workspaceSwitch = vi.fn()
const workspacePickFolder = vi.fn()
const workspaceClearSelection = vi.fn()
const shellOpenPath = vi.fn()

function localProject(path: string, name: string, lastOpenedAt: string): WorkspaceProjectSummary {
  return { kind: 'local', path, name, lastOpenedAt, state: 'cold', running: false, loaded: false, threadCount: 0, threads: [], pinned: false }
}

function setLocalProjects(projects: WorkspaceProjectSummary[]): void {
  useWorkspaceProjectsStore.getState().setPayload({ foregroundWorkspacePath: '', secondaryLimit: 8, projects })
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

async function openRecentSubmenu(): Promise<void> {
  const trigger = screen.getByRole('menuitem', { name: 'Recent Workspaces' })
  await waitFor(() => expect(trigger).toBeEnabled())
  fireEvent.mouseEnter(trigger)
}

describe('WorkspaceHeader', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ locale: 'en' })
    setLocalProjects([
      localProject('F:\\workspace-a', 'workspace-a', '2026-04-19T00:00:00.000Z'),
      localProject('F:\\workspace-b', 'workspace-b', '2026-04-19T00:01:00.000Z')
    ])
    workspaceClearProjects.mockResolvedValue(undefined)
    workspaceSwitch.mockResolvedValue(undefined)
    workspacePickFolder.mockResolvedValue(null)
    workspaceClearSelection.mockResolvedValue(undefined)
    shellOpenPath.mockResolvedValue('')
    vi.spyOn(window, 'alert').mockImplementation(() => {})

    installDesktopApiMock({
      settings: {
        get: settingsGet
      },
      workspace: {
        clearProjects: workspaceClearProjects,
        switch: workspaceSwitch,
        pickFolder: workspacePickFolder,
        clearSelection: workspaceClearSelection
      },
      shell: {
        openPath: shellOpenPath
      }
    })
  })

  it('shows a clear recent action in the recent workspace submenu', async () => {
    renderOptionsMenu()

    openWorkspaceMenu()

    await openRecentSubmenu()

    expect(await screen.findByRole('menuitem', { name: 'Clear Recently Opened...' })).toBeInTheDocument()
  })

  it('returns to the welcome screen from the switch workspace menu item', async () => {
    renderOptionsMenu()

    openWorkspaceMenu()

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
    await openRecentSubmenu()

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
    await openRecentSubmenu()

    fireEvent.click(await screen.findByRole('menuitem', { name: 'Clear Recently Opened...' }))

    expect(await screen.findByRole('dialog', { name: 'Clear recently opened workspaces?' })).toBeInTheDocument()
    expect(screen.getByText('This removes all saved workspace history from the recent list.')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))

    expect(workspaceClearProjects).not.toHaveBeenCalled()
  })

  it('clears projects after confirmation', async () => {
    renderOptionsMenu()

    openWorkspaceMenu()
    await openRecentSubmenu()

    expect(await screen.findByText('workspace-a')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('menuitem', { name: 'Clear Recently Opened...' }))
    expect(await screen.findByRole('dialog', { name: 'Clear recently opened workspaces?' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Clear' }))

    await waitFor(() => {
      expect(workspaceClearProjects).toHaveBeenCalledOnce()
    })
  })

  it('does not show the clear action when there are no recents', async () => {
    setLocalProjects([])
    renderOptionsMenu()

    openWorkspaceMenu()

    expect(screen.queryByRole('menuitem', { name: 'Clear Recently Opened...' })).not.toBeInTheDocument()
  })
})
