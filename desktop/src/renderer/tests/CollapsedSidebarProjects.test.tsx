import { fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { Sidebar } from '../components/layout/Sidebar'
import { useConnectionStore } from '../stores/connectionStore'
import { usePluginStore } from '../stores/pluginStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import type { WorkspaceProjectSummary } from '../../shared/workspaceProjects'
import type { ThreadSummary } from '../types/thread'

const switchWorkspace = vi.fn().mockResolvedValue(undefined)

function project(overrides: Partial<WorkspaceProjectSummary>): WorkspaceProjectSummary {
  return {
    projectId: overrides.path ?? overrides.projectId ?? 'p',
    kind: 'local',
    path: 'F:\\unset',
    name: 'Unset',
    state: 'secondary',
    running: true,
    loaded: true,
    threadCount: 0,
    threads: [],
    ...overrides
  }
}

function renderCollapsedSidebar(): void {
  render(
    <LocaleProvider>
      <Sidebar workspaceName="alpha" workspacePath="F:\\alpha" />
    </LocaleProvider>
  )
}

describe('CollapsedSidebar projects rail', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
        workspace: {
          getRecent: vi.fn().mockResolvedValue([]),
          switch: switchWorkspace
        },
        shell: { openPath: vi.fn().mockResolvedValue(undefined) }
      }
    })

    useConnectionStore.getState().reset()
    useConnectionStore.getState().setStatus({ status: 'connected', capabilities: {} })
    useThreadStore.getState().reset()
    useWorkspaceProjectsStore.getState().reset()
    usePluginStore.setState({
      plugins: [],
      diagnostics: [],
      loading: false,
      error: null,
      selectedPluginId: null,
      selectedPlugin: null,
      detailLoading: false
    })
    useUIStore.setState({
      activeMainView: 'conversation',
      sidebarCollapsed: true,
      sidebarPreferredCollapsed: true
    })
  })

  it('renders one icon per project, marking the foreground project', () => {
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: 'F:\\alpha',
      foregroundProjectId: 'F:\\alpha',
      secondaryLimit: 8,
      projects: [
        project({ projectId: 'F:\\alpha', path: 'F:\\alpha', name: 'Alpha', state: 'foreground' }),
        project({ projectId: 'F:\\beta', path: 'F:\\beta', name: 'Beta', state: 'cold', running: false })
      ]
    })

    renderCollapsedSidebar()

    const alpha = screen.getByRole('button', { name: 'Alpha' })
    const beta = screen.getByRole('button', { name: 'Beta' })
    expect(alpha).toHaveAttribute('aria-current', 'true')
    expect(beta).not.toHaveAttribute('aria-current')
  })

  it('switches workspace when a background project icon is clicked', async () => {
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: 'F:\\alpha',
      foregroundProjectId: 'F:\\alpha',
      secondaryLimit: 8,
      projects: [
        project({ projectId: 'F:\\alpha', path: 'F:\\alpha', name: 'Alpha', state: 'foreground' }),
        project({ projectId: 'F:\\beta', path: 'F:\\beta', name: 'Beta', state: 'cold', running: false })
      ]
    })

    renderCollapsedSidebar()

    fireEvent.click(screen.getByRole('button', { name: 'Beta' }))
    expect(switchWorkspace).toHaveBeenCalledWith('F:\\beta')
  })

  it('does not switch when the foreground project icon is clicked', () => {
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: 'F:\\alpha',
      foregroundProjectId: 'F:\\alpha',
      secondaryLimit: 8,
      projects: [project({ projectId: 'F:\\alpha', path: 'F:\\alpha', name: 'Alpha', state: 'foreground' })]
    })

    renderCollapsedSidebar()

    fireEvent.click(screen.getByRole('button', { name: 'Alpha' }))
    expect(switchWorkspace).not.toHaveBeenCalled()
  })

  it('orders nav destinations above the projects rail, with Settings pinned last', () => {
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: 'F:\\alpha',
      foregroundProjectId: 'F:\\alpha',
      secondaryLimit: 8,
      projects: [project({ projectId: 'F:\\alpha', path: 'F:\\alpha', name: 'Alpha', state: 'foreground' })]
    })

    renderCollapsedSidebar()

    const channels = screen.getByRole('button', { name: 'Channels' })
    const projectIcon = screen.getByRole('button', { name: 'Alpha' })
    const settings = screen.getByRole('button', { name: 'Open settings' })

    // Channels (a nav destination) sits above the project rail, which sits above Settings.
    expect(channels.compareDocumentPosition(projectIcon) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(projectIcon.compareDocumentPosition(settings) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('falls back to recent-thread dots when no projects are present', () => {
    useThreadStore.setState({
      threadList: [
        { id: 't1', displayName: 'Hello world' } as unknown as ThreadSummary
      ]
    })

    renderCollapsedSidebar()

    // First-letter dot for the recent thread, not a project icon.
    expect(screen.getByRole('button', { name: 'Hello world' })).toHaveTextContent('H')
  })

  it('shows Chats instead of recent-thread dots when only the chat workspace is available', () => {
    useThreadStore.setState({
      threadList: [
        { id: 't1', displayName: 'Hello world' } as unknown as ThreadSummary
      ]
    })
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: 'C:\\Users\\me\\.craft\\workspaces\\chats',
      foregroundProjectId: 'C:\\Users\\me\\.craft\\workspaces\\chats',
      secondaryLimit: 8,
      projects: [],
      chat: project({
        projectId: 'C:\\Users\\me\\.craft\\workspaces\\chats',
        kind: 'chat',
        path: 'C:\\Users\\me\\.craft\\workspaces\\chats',
        name: 'C:\\Users\\me\\.craft\\workspaces\\chats',
        state: 'foreground'
      })
    })

    renderCollapsedSidebar()

    expect(screen.getByRole('button', { name: 'Chats' })).toHaveAttribute('aria-current', 'true')
    expect(screen.queryByRole('button', { name: 'Hello world' })).not.toBeInTheDocument()
  })

  it('switches to the chat workspace from the collapsed Chats icon', () => {
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: 'F:\\alpha',
      foregroundProjectId: 'F:\\alpha',
      secondaryLimit: 8,
      projects: [project({ projectId: 'F:\\alpha', path: 'F:\\alpha', name: 'Alpha', state: 'foreground' })],
      chat: project({
        projectId: 'C:\\Users\\me\\.craft\\workspaces\\chats',
        kind: 'chat',
        path: 'C:\\Users\\me\\.craft\\workspaces\\chats',
        name: 'C:\\Users\\me\\.craft\\workspaces\\chats',
        state: 'secondary'
      })
    })

    renderCollapsedSidebar()

    fireEvent.click(screen.getByRole('button', { name: 'Chats' }))

    expect(switchWorkspace).toHaveBeenCalledWith('C:\\Users\\me\\.craft\\workspaces\\chats')
  })
})
