import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ThreadList } from '../components/sidebar/ThreadList'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import type { ThreadSummary } from '../types/thread'

const settingsGet = vi.fn()
const settingsSet = vi.fn()
const workspaceSwitch = vi.fn()
const workspacePickFolder = vi.fn()
const workspaceSaveLocalProject = vi.fn()
const workspaceCreateLocalProject = vi.fn()
const workspaceRemoveRecent = vi.fn()
const workspaceDisconnectRemote = vi.fn()
const workspaceGetRecent = vi.fn()
const workspaceClearRecent = vi.fn()
const workspaceClearSelection = vi.fn()
const workspaceStop = vi.fn()
const workspaceArchiveThread = vi.fn()
const shellOpenPath = vi.fn()

function makeThread(id: string, displayName: string, minutesAgo = 0): ThreadSummary {
  const time = new Date(Date.now() - minutesAgo * 60 * 1000).toISOString()
  return {
    id,
    displayName,
    status: 'active',
    originChannel: 'dotcraft-desktop',
    createdAt: time,
    lastActiveAt: time
  }
}

function renderList(props: {
  workspacePath?: string
  localWorkspacePath?: string
  localActionsDisabled?: boolean
  foregroundOpening?: boolean
  openingWorkspacePath?: string
} = {}): void {
  render(
    <LocaleProvider>
      <ThreadList {...props} />
    </LocaleProvider>
  )
}

function resetStores(): void {
  useThreadStore.getState().reset()
  useWorkspaceProjectsStore.getState().reset()
  useUIStore.setState({
    activeMainView: 'conversation',
    pendingProjectThreadOpen: null,
    welcomeDraft: null,
    welcomeDraftsByWorkspace: {},
    welcomeDraftWorkspacePath: null,
    projectsSectionCollapsed: false,
    pinnedSectionCollapsed: false,
    chatsSectionCollapsed: false
  })
}

describe('ThreadList project-first layout', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ locale: 'en' })
    settingsSet.mockResolvedValue({})
    workspaceSwitch.mockResolvedValue(undefined)
    workspacePickFolder.mockResolvedValue(null)
    workspaceSaveLocalProject.mockImplementation(async ({ primaryFolder }: { primaryFolder: string }) => ({ path: primaryFolder }))
    workspaceCreateLocalProject.mockResolvedValue({ path: '/workspace/new', gitInitialized: true })
    workspaceRemoveRecent.mockResolvedValue(undefined)
    workspaceDisconnectRemote.mockResolvedValue(undefined)
    workspaceGetRecent.mockResolvedValue([])
    workspaceClearRecent.mockResolvedValue(undefined)
    workspaceClearSelection.mockResolvedValue(undefined)
    workspaceStop.mockResolvedValue(undefined)
    workspaceArchiveThread.mockResolvedValue(undefined)
    shellOpenPath.mockResolvedValue(undefined)
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet, set: settingsSet },
        appServer: { sendRequest: vi.fn() },
        workspace: {
          switch: workspaceSwitch,
          pickFolder: workspacePickFolder,
          saveLocalProject: workspaceSaveLocalProject,
          createLocalProject: workspaceCreateLocalProject,
          removeRecent: workspaceRemoveRecent,
          disconnectRemote: workspaceDisconnectRemote,
          getRecent: workspaceGetRecent,
          clearRecent: workspaceClearRecent,
          clearSelection: workspaceClearSelection,
          stop: workspaceStop,
          archiveThread: workspaceArchiveThread
        },
        shell: { openPath: shellOpenPath }
      }
    })
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText: vi.fn().mockResolvedValue(undefined) }
    })
    resetStores()
  })

  it('renders a flat single-workspace list without time headings', () => {
    useThreadStore.getState().setThreadList([
      makeThread('thread-1', 'Analyze browser callback', 10),
      makeThread('thread-2', 'Patch project rail', 60 * 28)
    ])

    renderList()

    expect(screen.queryByText('Today')).not.toBeInTheDocument()
    expect(screen.queryByText('Yesterday')).not.toBeInTheDocument()
    expect(screen.getByText('Analyze browser callback')).toBeInTheDocument()
    expect(screen.getByText('Patch project rail')).toBeInTheDocument()
  })

  it('renders pinned rows above Projects and does not duplicate pinned threads inside projects', async () => {
    const pinnedA = makeThread('pinned-a', 'Pinned A', 3)
    const normalA = makeThread('normal-a', 'Normal A', 8)
    const pinnedB = makeThread('pinned-b', 'Pinned B', 5)
    const normalB = makeThread('normal-b', 'Normal B', 10)
    useThreadStore.getState().setThreadList([pinnedA, normalA], '/workspace/a')
    useThreadStore.getState().hydratePinnedThreadIds('/workspace/a', ['pinned-a'])
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      secondaryLimit: 8,
      projects: [
        {
          path: '/workspace/a',
          name: 'a',
          state: 'foreground',
          running: true,
          loaded: true,
          threadCount: 2,
          threads: [],
          pinnedThreadIds: ['pinned-a']
        },
        {
          path: '/workspace/b',
          name: 'b',
          state: 'secondary',
          running: true,
          loaded: true,
          threadCount: 2,
          threads: [pinnedB, normalB],
          pinnedThreadIds: ['pinned-b']
        }
      ]
    })

    renderList()

    const pinnedHeading = screen.getByText('Pinned')
    const projectsHeading = screen.getByText('Projects')
    expect(pinnedHeading.compareDocumentPosition(projectsHeading) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(screen.queryByText('Today')).not.toBeInTheDocument()
    expect(screen.getAllByText('Pinned A')).toHaveLength(1)
    expect(screen.getAllByText('Pinned B')).toHaveLength(1)
    expect(screen.getByTestId('thread-pin-pinned-a')).toHaveAttribute('aria-pressed', 'true')
    // Secondary (local) rows now expose an interactive pin toggle instead of the
    // static read-only marker.
    expect(screen.getByTestId('project-thread-pin-/workspace/b-pinned-b')).toHaveAttribute(
      'aria-pressed',
      'true'
    )
    expect(screen.getByTestId('project-thread-pin-/workspace/b-normal-b')).toHaveAttribute(
      'aria-pressed',
      'false'
    )
    expect(screen.queryByTestId('project-thread-pinned-/workspace/b-pinned-b')).not.toBeInTheDocument()
    expect(screen.getByText('Normal A')).toBeInTheDocument()
    expect(screen.getByText('Normal B')).toBeInTheDocument()

    fireEvent.click(screen.getByText('Pinned B'))
    await waitFor(() => {
      expect(workspaceSwitch).toHaveBeenCalledWith('/workspace/b')
    })
    expect(useThreadStore.getState().activeThreadId).toBeNull()
    expect(useUIStore.getState().pendingProjectThreadOpen).toEqual({
      projectKey: '/workspace/b',
      workspacePath: '/workspace/b',
      threadId: 'pinned-b'
    })
  })

  it('does not render archived threads from cached secondary project rows', () => {
    const archivedB: ThreadSummary = {
      ...makeThread('archived-b', 'Archived B', 4),
      status: 'archived'
    }
    const activeB = makeThread('active-b', 'Active B', 8)
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      secondaryLimit: 8,
      projects: [
        {
          path: '/workspace/b',
          name: 'b',
          state: 'secondary',
          running: true,
          loaded: true,
          threadCount: 2,
          threads: [archivedB, activeB],
          pinnedThreadIds: []
        }
      ]
    })

    renderList()

    expect(screen.getByText('Active B')).toBeInTheDocument()
    expect(screen.queryByText('Archived B')).not.toBeInTheDocument()
  })

  it('does not render cached archived threads with legacy status casing', () => {
    const archivedB: ThreadSummary = {
      ...makeThread('archived-b', 'Archived B', 4),
      status: 'Archived' as ThreadSummary['status']
    }
    const activeB = makeThread('active-b', 'Active B', 8)
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      secondaryLimit: 8,
      projects: [
        {
          path: '/workspace/b',
          name: 'b',
          state: 'secondary',
          running: true,
          loaded: true,
          threadCount: 2,
          threads: [archivedB, activeB],
          pinnedThreadIds: []
        }
      ]
    })

    renderList()

    expect(screen.getByText('Active B')).toBeInTheDocument()
    expect(screen.queryByText('Archived B')).not.toBeInTheDocument()
  })

  it('does not render archived pinned threads from cached project rows', () => {
    const archivedPinnedB: ThreadSummary = {
      ...makeThread('archived-pinned-b', 'Archived Pinned B', 4),
      status: 'archived'
    }
    const activeB = makeThread('active-b', 'Active B', 8)
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      secondaryLimit: 8,
      projects: [
        {
          path: '/workspace/b',
          name: 'b',
          state: 'secondary',
          running: true,
          loaded: true,
          threadCount: 2,
          threads: [archivedPinnedB, activeB],
          pinnedThreadIds: ['archived-pinned-b']
        }
      ]
    })

    renderList()

    expect(screen.getByText('Active B')).toBeInTheDocument()
    expect(screen.queryByText('Archived Pinned B')).not.toBeInTheDocument()
    expect(screen.queryByText('Pinned')).not.toBeInTheDocument()
  })

  it('keeps project errors accessible while showing hover actions', () => {
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      secondaryLimit: 8,
      projects: [
        {
          path: '/workspace/b',
          name: 'b',
          state: 'error',
          running: false,
          loaded: false,
          threadCount: 0,
          threads: [],
          pinnedThreadIds: [],
          errorMessage: 'Connection refused'
        }
      ]
    })

    renderList()

    const row = screen.getByRole('button', { name: 'b' })
    expect(screen.getByLabelText('Connection refused')).toBeInTheDocument()

    fireEvent.mouseEnter(row)

    const errorIndicator = screen.getByLabelText('Connection refused')
    const newChatButton = screen.getByRole('button', { name: 'New chat in project' })
    const projectActionsButton = screen.getByRole('button', { name: 'Project actions' })
    expect(errorIndicator).toBeInTheDocument()
    expect(newChatButton).toBeInTheDocument()
    expect(projectActionsButton).toBeInTheDocument()
    expect(newChatButton.compareDocumentPosition(errorIndicator) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(projectActionsButton.compareDocumentPosition(errorIndicator) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('marks only the foreground (current) workspace header with aria-current', () => {
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      foregroundProjectId: '/workspace/a',
      secondaryLimit: 8,
      projects: [
        {
          path: '/workspace/a',
          name: 'a',
          state: 'foreground',
          running: true,
          loaded: true,
          threadCount: 0,
          threads: [],
          pinnedThreadIds: []
        },
        {
          path: '/workspace/b',
          name: 'b',
          state: 'secondary',
          running: true,
          loaded: true,
          threadCount: 0,
          threads: [],
          pinnedThreadIds: []
        }
      ]
    })

    renderList()

    // The current workspace is marked regardless of whether a thread is open.
    expect(screen.getByRole('button', { name: 'a' })).toHaveAttribute('aria-current', 'true')
    expect(screen.getByRole('button', { name: 'b' })).not.toHaveAttribute('aria-current')
  })

  it('clicking a project row collapses it without switching workspace', async () => {
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      secondaryLimit: 8,
      projects: [
        {
          path: '/workspace/b',
          name: 'b',
          state: 'secondary',
          running: true,
          loaded: true,
          threadCount: 1,
          threads: [makeThread('thread-b', 'Thread B')],
          pinnedThreadIds: []
        }
      ]
    })

    renderList()

    fireEvent.click(screen.getByRole('button', { name: 'b' }))

    expect(workspaceSwitch).not.toHaveBeenCalled()
    // The collapse animation keeps the rows mounted through the height
    // transition, then unmounts them.
    await waitFor(() => expect(screen.queryByText('Thread B')).not.toBeInTheDocument())
  })

  it('keeps cold projects collapsed and starts them only on double click', () => {
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '',
      secondaryLimit: 8,
      projects: [
        {
          path: '/workspace/cold',
          name: 'cold',
          state: 'cold',
          running: false,
          loaded: false,
          threadCount: 0,
          threads: [],
          pinnedThreadIds: []
        }
      ]
    })

    renderList()
    const projectRow = screen.getByRole('button', { name: 'cold' })

    expect(projectRow).not.toHaveAttribute('aria-expanded')
    expect(screen.queryByText('No chats')).not.toBeInTheDocument()

    fireEvent.click(projectRow)

    expect(workspaceSwitch).not.toHaveBeenCalled()
    expect(screen.queryByText('No chats')).not.toBeInTheDocument()

    fireEvent.doubleClick(projectRow)

    expect(workspaceSwitch).toHaveBeenCalledWith('/workspace/cold')
  })

  it('shows a thread-aligned empty state for each loaded empty project', () => {
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      secondaryLimit: 8,
      projects: [
        {
          path: '/workspace/a',
          name: 'a',
          state: 'foreground',
          running: true,
          loaded: true,
          threadCount: 0,
          threads: [],
          pinnedThreadIds: []
        },
        {
          path: '/workspace/b',
          name: 'b',
          state: 'secondary',
          running: true,
          loaded: true,
          threadCount: 0,
          threads: [],
          pinnedThreadIds: []
        }
      ]
    })

    renderList()

    expect(screen.getByRole('button', { name: 'a' })).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByRole('button', { name: 'b' })).toHaveAttribute('aria-expanded', 'true')
    const projectEmptyStates = screen.getAllByText('No chats')
    expect(projectEmptyStates).toHaveLength(2)
    for (const emptyState of projectEmptyStates) {
      expect(emptyState).toHaveStyle({ padding: '4px 16px 8px 32px' })
    }
  })

  it('shows search feedback instead of the ordinary project empty state', () => {
    useThreadStore.getState().setSearchQuery('missing')
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      secondaryLimit: 8,
      projects: [
        {
          path: '/workspace/a',
          name: 'a',
          state: 'foreground',
          running: true,
          loaded: true,
          threadCount: 1,
          threads: [makeThread('thread-a', 'Existing thread')],
          pinnedThreadIds: []
        }
      ]
    })

    renderList()

    expect(screen.getByText('No threads match your search.')).toBeInTheDocument()
    expect(screen.queryByText('No chats')).not.toBeInTheDocument()
  })

  it('renders background running rows with the shared leading and status slots', () => {
    const runningThread: ThreadSummary = {
      ...makeThread('thread-b', 'Thread B'),
      runtime: {
        running: true,
        busy: true,
        waitingOnApproval: false,
        waitingOnInput: false,
        waitingOnPlanConfirmation: false,
        maintenanceKind: null
      }
    }
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      secondaryLimit: 8,
      projects: [
        {
          path: '/workspace/b',
          name: 'b',
          state: 'secondary',
          running: true,
          loaded: true,
          threadCount: 1,
          threads: [runningThread],
          pinnedThreadIds: []
        }
      ]
    })

    renderList()

    const row = screen.getByTestId('project-thread-entry-/workspace/b-thread-b')
    const leading = screen.getByTestId('project-thread-leading-/workspace/b-thread-b')
    const layout = screen.getByTestId('project-thread-layout-/workspace/b-thread-b')
    const status = screen.getByTestId('project-thread-status-/workspace/b-thread-b')
    const spinner = screen.getByTestId('project-thread-running-indicator-/workspace/b-thread-b')

    expect(leading.parentElement).toBe(row)
    expect(layout.parentElement).toBe(row)
    expect(status.parentElement).toBe(layout)
    expect(spinner.parentElement?.parentElement?.parentElement).toBe(status)
    expect(screen.queryByTestId('project-thread-pinned-/workspace/b-thread-b')).not.toBeInTheDocument()
  })

  it('clicking a background thread queues it before promoting its workspace', async () => {
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      secondaryLimit: 8,
      projects: [
        {
          path: '/workspace/b',
          name: 'b',
          state: 'secondary',
          running: true,
          loaded: true,
          threadCount: 1,
          threads: [makeThread('thread-b', 'Thread B')],
          pinnedThreadIds: []
        }
      ]
    })

    renderList()
    fireEvent.click(screen.getByText('Thread B'))

    await waitFor(() => {
      expect(workspaceSwitch).toHaveBeenCalledWith('/workspace/b')
    })
    expect(useThreadStore.getState().activeThreadId).toBeNull()
    expect(useUIStore.getState().pendingProjectThreadOpen).toEqual({
      projectKey: '/workspace/b',
      workspacePath: '/workspace/b',
      threadId: 'thread-b'
    })
  })

  it('project actions can start a new chat for that project', async () => {
    useThreadStore.getState().setActiveThreadId('old-thread')
    useUIStore.getState().setWelcomeDraft({
      text: 'Draft for workspace B',
      images: [],
      files: [],
      mode: 'plan',
      model: 'gpt-test'
    }, '/workspace/b')
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      secondaryLimit: 8,
      projects: [
        {
          path: '/workspace/b',
          name: 'b',
          state: 'secondary',
          running: true,
          loaded: true,
          threadCount: 0,
          threads: [],
          pinnedThreadIds: []
        }
      ]
    })

    renderList()
    const projectRow = screen.getByRole('button', { name: 'b' })
    fireEvent.mouseEnter(projectRow)
    fireEvent.click(screen.getByRole('button', { name: 'New chat in project' }))

    await waitFor(() => {
      expect(workspaceSwitch).toHaveBeenCalledWith('/workspace/b')
      expect(useThreadStore.getState().activeThreadId).toBeNull()
      expect(useUIStore.getState().activeMainView).toBe('conversation')
      expect(useUIStore.getState().welcomeDraft).toMatchObject({
        text: 'Draft for workspace B',
        mode: 'plan',
        model: 'gpt-test'
      })
      expect(useUIStore.getState().getWelcomeDraftForWorkspace('/workspace/b')).toMatchObject({
        text: 'Draft for workspace B',
        mode: 'plan',
        model: 'gpt-test'
      })
    })
  })

  it('Add project → attaching an existing folder creates the project and switches to it', async () => {
    workspacePickFolder.mockResolvedValue('/workspace/new')
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      secondaryLimit: 8,
      projects: [
        {
          path: '/workspace/a',
          name: 'a',
          state: 'foreground',
          running: true,
          loaded: true,
          threadCount: 0,
          threads: [],
          pinnedThreadIds: []
        }
      ]
    })

    renderList()
    fireEvent.mouseEnter(screen.getByText('Projects').parentElement as HTMLElement)
    // The "+" opens the unified Create dialog directly (no sub-menu).
    fireEvent.click(screen.getByRole('button', { name: 'Add project' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Add folders DotCraft can read and edit' }))
    await screen.findByText('/workspace/new')
    fireEvent.click(screen.getByRole('button', { name: 'Create project' }))

    await waitFor(() => {
      expect(workspacePickFolder).toHaveBeenCalled()
      expect(workspaceSaveLocalProject).toHaveBeenCalledWith(
        expect.objectContaining({ primaryFolder: '/workspace/new', secondaryFolders: [] })
      )
      expect(workspaceSwitch).toHaveBeenCalledWith('/workspace/new')
    })
  })

  it('moves workspace options into the Projects header action area', async () => {
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      secondaryLimit: 8,
      projects: [
        {
          path: '/workspace/a',
          name: 'a',
          state: 'foreground',
          running: true,
          loaded: true,
          threadCount: 0,
          threads: [],
          pinnedThreadIds: []
        }
      ]
    })

    renderList({ workspacePath: '/workspace/a' })
    fireEvent.mouseEnter(screen.getByText('Projects').parentElement as HTMLElement)
    fireEvent.click(screen.getByRole('button', { name: 'Workspace options' }))

    await waitFor(() => {
      expect(workspaceGetRecent).toHaveBeenCalledOnce()
    })
    expect(screen.getByText('/workspace/a')).toBeInTheDocument()
    expect(screen.getByText('Open in Explorer')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Add project' })).toBeInTheDocument()
  })

  it('renders foreground thread-store rows when local project identity uses a path variant', () => {
    useThreadStore.getState().setThreadList([
      makeThread('foreground-thread', 'Foreground local thread')
    ], 'F:/fixtures/workspace')
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: 'F:\\fixtures\\workspace',
      foregroundProjectId: 'f:\\fixtures\\workspace',
      secondaryLimit: 8,
      projects: [
        {
          kind: 'local',
          path: 'F:\\fixtures\\workspace',
          identityWorkspacePath: 'F:\\fixtures\\workspace',
          name: 'workspace',
          state: 'foreground',
          running: true,
          loaded: true,
          threadCount: 1,
          threads: [],
          pinnedThreadIds: []
        }
      ]
    })

    renderList()

    expect(screen.getByText('Foreground local thread')).toBeInTheDocument()
    expect(screen.queryByText('No chats')).not.toBeInTheDocument()
  })

  it('does not render stale foreground thread-store rows under a different project', () => {
    useThreadStore.getState().setThreadList([
      makeThread('thread-a', 'Thread from A')
    ], '/workspace/a')
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/b',
      foregroundProjectId: '/workspace/b',
      secondaryLimit: 8,
      projects: [
        {
          kind: 'local',
          path: '/workspace/b',
          identityWorkspacePath: '/workspace/b',
          name: 'b',
          state: 'foreground',
          running: true,
          loaded: true,
          threadCount: 0,
          threads: [],
          pinnedThreadIds: []
        }
      ]
    })

    renderList()

    expect(screen.getByRole('button', { name: 'b' })).toBeInTheDocument()
    expect(screen.queryByText('Thread from A')).not.toBeInTheDocument()
  })

  it('renders skeleton rows inside the foreground project while it is opening', () => {
    useThreadStore.getState().setThreadList([
      makeThread('thread-a', 'Thread from A')
    ], '/workspace/a')
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/b',
      foregroundProjectId: '/workspace/b',
      secondaryLimit: 8,
      projects: [
        {
          kind: 'local',
          path: '/workspace/b',
          identityWorkspacePath: '/workspace/b',
          name: 'b',
          state: 'foreground',
          running: true,
          loaded: false,
          threadCount: 0,
          threads: [],
          pinnedThreadIds: []
        }
      ]
    })

    renderList({ foregroundOpening: true })

    expect(screen.getByRole('button', { name: 'b' })).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getAllByTestId('project-thread-skeleton-row')).toHaveLength(4)
    expect(screen.queryByLabelText('Connecting')).not.toBeInTheDocument()
    expect(screen.queryByText('Thread from A')).not.toBeInTheDocument()
  })

  it('keeps the project order stable while the opening project loads', () => {
    useThreadStore.getState().setThreadList([
      makeThread('thread-a', 'Thread from A')
    ], '/workspace/a')
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      foregroundProjectId: '/workspace/a',
      secondaryLimit: 8,
      projects: [
        {
          kind: 'local',
          path: '/workspace/a',
          identityWorkspacePath: '/workspace/a',
          name: 'a',
          state: 'foreground',
          running: true,
          loaded: true,
          threadCount: 1,
          threads: [makeThread('thread-a', 'Thread from A')],
          pinnedThreadIds: []
        },
        {
          kind: 'local',
          path: '/workspace/b',
          identityWorkspacePath: '/workspace/b',
          name: 'b',
          state: 'secondary',
          running: true,
          loaded: false,
          threadCount: 0,
          threads: [],
          pinnedThreadIds: []
        }
      ]
    })

    renderList({
      workspacePath: '/workspace/b',
      foregroundOpening: true,
      openingWorkspacePath: '/workspace/b'
    })

    const bRow = screen.getByRole('button', { name: 'b' })
    const aRow = screen.getByRole('button', { name: 'a' })
    const firstSkeleton = screen.getAllByTestId('project-thread-skeleton-row')[0]

    // Order stays as provided (a, then b); the opening project is not hoisted.
    expect(aRow.compareDocumentPosition(bRow) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    // The opening project (b) still shows its loading skeleton in place.
    expect(bRow.compareDocumentPosition(firstSkeleton) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(screen.queryByLabelText('Connecting')).not.toBeInTheDocument()
  })

  it('renders cached foreground project rows while the global thread-store rows belong elsewhere', () => {
    useThreadStore.getState().setThreadList([
      makeThread('thread-a', 'Thread from A')
    ], '/workspace/a')
    const cachedB = makeThread('thread-b', 'Cached B thread')
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/b',
      foregroundProjectId: '/workspace/b',
      secondaryLimit: 8,
      projects: [
        {
          kind: 'local',
          path: '/workspace/b',
          identityWorkspacePath: '/workspace/b',
          name: 'b',
          state: 'foreground',
          running: true,
          loaded: true,
          threadCount: 1,
          threads: [cachedB],
          pinnedThreadIds: []
        }
      ]
    })

    renderList()
    fireEvent.click(screen.getByText('Cached B thread'))

    expect(screen.getByText('Cached B thread')).toBeInTheDocument()
    expect(screen.queryByText('Thread from A')).not.toBeInTheDocument()
    expect(useThreadStore.getState().activeThreadId).toBe('thread-b')
  })

  it('renders cached foreground pinned rows with an interactive pin toggle while the global rows belong elsewhere', async () => {
    useThreadStore.getState().setThreadList([
      makeThread('thread-a', 'Thread from A')
    ], '/workspace/a')
    const cachedB = makeThread('thread-b', 'Cached B pinned thread')
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/b',
      foregroundProjectId: '/workspace/b',
      secondaryLimit: 8,
      projects: [
        {
          kind: 'local',
          path: '/workspace/b',
          identityWorkspacePath: '/workspace/b',
          name: 'b',
          state: 'foreground',
          running: true,
          loaded: true,
          threadCount: 1,
          threads: [cachedB],
          pinnedThreadIds: ['thread-b']
        }
      ]
    })

    renderList()

    // Local cached rows now expose an interactive pin toggle (not the static marker)
    // so cross-workspace pin works even before the workspace becomes foreground.
    expect(screen.getByTestId('project-thread-pin-/workspace/b-thread-b')).toHaveAttribute(
      'aria-pressed',
      'true'
    )
    expect(screen.queryByTestId('project-thread-pinned-/workspace/b-thread-b')).not.toBeInTheDocument()
    expect(screen.queryByText('Thread from A')).not.toBeInTheDocument()

    fireEvent.click(screen.getByText('Cached B pinned thread'))
    await waitFor(() => {
      expect(useThreadStore.getState().activeThreadId).toBe('thread-b')
    })
  })

  it('keeps active remote threads in a separate project bucket from the previous local workspace', () => {
    useThreadStore.getState().setThreadList([
      makeThread('remote-thread', 'Remote thread', 1)
    ], 'remote:servers:host-1:stack-1')
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      foregroundProjectId: 'remote:servers:host-1:stack-1',
      secondaryLimit: 8,
      projects: [
        {
          projectId: 'remote:servers:host-1:stack-1',
          kind: 'remote',
          path: '/srv/app',
          identityWorkspacePath: '/srv/app',
          name: 'prod-stack',
          state: 'foreground',
          running: true,
          loaded: true,
          threadCount: 1,
          threads: [],
          pinnedThreadIds: [],
          remote: {
            source: 'servers',
            displayPath: '/srv/app',
            endpoint: 'ws://127.0.0.1:9123/ws',
            hostId: 'host-1',
            stackId: 'stack-1'
          }
        },
        {
          projectId: '/workspace/a',
          kind: 'local',
          path: '/workspace/a',
          identityWorkspacePath: '/workspace/a',
          name: 'a',
          state: 'secondary',
          running: true,
          loaded: true,
          threadCount: 1,
          threads: [makeThread('local-thread', 'Local thread', 2)],
          pinnedThreadIds: []
        }
      ]
    })

    renderList()

    const remoteThread = screen.getByText('Remote thread')
    const localProject = screen.getByRole('button', { name: 'a' })
    expect(screen.getByRole('button', { name: 'prod-stack' })).toBeInTheDocument()
    expect(screen.getByText('Local thread')).toBeInTheDocument()
    expect(remoteThread.compareDocumentPosition(localProject) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })

  it('remote project menu hides local filesystem actions and can disconnect the remote project', async () => {
    useThreadStore.getState().setThreadList([
      makeThread('remote-thread', 'Remote thread')
    ], 'remote:manual:ws://example.test:9100/ws')
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      foregroundProjectId: 'remote:manual:ws://example.test:9100/ws',
      secondaryLimit: 8,
      projects: [
        {
          projectId: 'remote:manual:ws://example.test:9100/ws',
          kind: 'remote',
          path: 'ws://example.test:9100/ws',
          identityWorkspacePath: '/remote/workspace',
          name: 'example.test:9100',
          state: 'foreground',
          running: true,
          loaded: true,
          threadCount: 1,
          threads: [],
          pinnedThreadIds: [],
          remote: {
            source: 'manual',
            displayPath: 'ws://example.test:9100/ws',
            endpoint: 'ws://example.test:9100/ws'
          }
        }
      ]
    })

    renderList()
    const remoteRow = screen.getByRole('button', { name: 'example.test:9100' })
    fireEvent.mouseEnter(remoteRow)
    fireEvent.click(screen.getByRole('button', { name: 'Project actions' }))

    expect(screen.queryByRole('menuitem', { name: 'Open in Explorer' })).not.toBeInTheDocument()
    fireEvent.click(screen.getByRole('menuitem', { name: 'Disconnect remote' }))

    await waitFor(() => {
      expect(workspaceDisconnectRemote).toHaveBeenCalled()
    })
  })

  it('shows section-aligned empty states when there are no configured projects or recent chats', () => {
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/chats',
      foregroundProjectId: '/chats',
      secondaryLimit: 8,
      projects: [],
      chat: {
        projectId: '/chats',
        kind: 'chat',
        path: '/chats',
        name: '/chats',
        state: 'foreground',
        running: true,
        loaded: true,
        threadCount: 0,
        threads: [],
        pinnedThreadIds: []
      }
    })

    renderList({ workspacePath: '/chats' })

    expect(screen.getByText('Projects')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Toggle Projects section' }))
      .toHaveStyle({ padding: '8px 16px 2px' })
    expect(screen.getByText('No projects')).toHaveStyle({ padding: '4px 16px 8px' })
    expect(screen.getByText('Recents')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Toggle Recents section' }))
      .toHaveStyle({ padding: '8px 16px 2px' })
    expect(screen.getByText('No chats')).toHaveStyle({ padding: '4px 16px 8px' })
  })

  it('does not report No projects when every configured project is pinned', () => {
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      foregroundProjectId: '/workspace/a',
      secondaryLimit: 8,
      projects: [
        {
          path: '/workspace/a',
          name: 'Pinned project',
          state: 'foreground',
          running: true,
          loaded: true,
          pinned: true,
          threadCount: 0,
          threads: [],
          pinnedThreadIds: []
        }
      ]
    })

    renderList()

    expect(screen.getByText('Pinned')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Pinned project' })).toBeInTheDocument()
    expect(screen.getByText('Projects')).toBeInTheDocument()
    expect(screen.queryByText('No projects')).not.toBeInTheDocument()
  })

  it('renders a Recents group with default chat workspace threads after Projects', () => {
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      foregroundProjectId: '/workspace/a',
      secondaryLimit: 8,
      projects: [
        {
          path: '/workspace/a',
          name: 'a',
          state: 'foreground',
          running: true,
          loaded: true,
          threadCount: 0,
          threads: [],
          pinnedThreadIds: []
        }
      ],
      chat: {
        projectId: '/chats',
        kind: 'chat',
        path: '/chats',
        identityWorkspacePath: '/chats',
        name: '/chats',
        state: 'secondary',
        running: true,
        loaded: true,
        threadCount: 1,
        threads: [makeThread('chat-1', 'General chat thread')],
        pinnedThreadIds: []
      }
    })

    renderList()

    const projectsHeading = screen.getByText('Projects')
    const recentsHeading = screen.getByText('Recents')
    expect(projectsHeading.parentElement).toHaveStyle({ padding: '8px 16px 2px' })
    expect(recentsHeading.parentElement).toHaveStyle({ padding: '8px 16px 2px' })
    // Recents renders as its own group, after Projects.
    expect(projectsHeading.compareDocumentPosition(recentsHeading) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(screen.getByText('General chat thread')).toBeInTheDocument()
    // The Recents group is not a project: no folder row carrying its physical path.
    expect(screen.queryByRole('button', { name: '/chats' })).not.toBeInTheDocument()
  })

  it('shows No chats when the default chat workspace has no threads', () => {
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      foregroundProjectId: '/workspace/a',
      secondaryLimit: 8,
      projects: [
        {
          path: '/workspace/a',
          name: 'a',
          state: 'foreground',
          running: true,
          loaded: true,
          threadCount: 0,
          threads: [],
          pinnedThreadIds: []
        }
      ],
      chat: {
        projectId: '/chats',
        kind: 'chat',
        path: '/chats',
        name: '/chats',
        state: 'secondary',
        running: true,
        loaded: true,
        threadCount: 0,
        threads: [],
        pinnedThreadIds: []
      }
    })

    renderList()

    const recentsHeading = screen.getByText('Recents')
    const recentsGroup = recentsHeading.parentElement?.parentElement
    expect(recentsGroup).not.toBeNull()
    expect(within(recentsGroup as HTMLElement).getByText('No chats')).toHaveStyle({
      padding: '4px 16px 8px'
    })
  })

  it('shows mutually exclusive waiting and running counts in project details', async () => {
    const waitingAndBusy = {
      ...makeThread('waiting', 'Waiting thread'),
      runtime: { running: true, waitingOnApproval: true }
    }
    const running = {
      ...makeThread('running', 'Running thread'),
      runtime: { running: true }
    }
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      secondaryLimit: 8,
      projects: [{
        path: '/workspace/b',
        name: 'project-b',
        state: 'secondary',
        running: true,
        loaded: true,
        threadCount: 2,
        threads: [waitingAndBusy, running],
        pinnedThreadIds: []
      }]
    })

    renderList()
    fireEvent.focus(screen.getByRole('button', { name: 'project-b' }))

    const details = await screen.findByRole('dialog', { name: 'project-b' })
    expect(details).toHaveTextContent('2 threads · 1 waiting · 1 running')
    expect(details).toHaveTextContent('/workspace/b')
    const pinButton = screen.getByRole('button', { name: 'Pin project' })
    expect(pinButton).toHaveAttribute('aria-pressed', 'false')

    fireEvent.focus(pinButton)
    fireEvent.keyDown(pinButton, { key: 'Escape' })
    await waitFor(() => {
      expect(screen.queryByRole('dialog', { name: 'project-b' })).not.toBeInTheDocument()
    })
    expect(screen.getByRole('button', { name: 'project-b' })).toHaveFocus()
  })

  it('opens every local project folder from details and exposes the edit shortcut', async () => {
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      secondaryLimit: 8,
      projects: [{
        path: '/workspace/a',
        secondaryFolders: ['/workspace/shared', '/workspace/docs'],
        name: 'project-a',
        state: 'foreground',
        running: true,
        loaded: true,
        threadCount: 0,
        threads: [],
        pinnedThreadIds: []
      }]
    })

    renderList({ workspacePath: '/workspace/a' })
    fireEvent.focus(screen.getByRole('button', { name: 'project-a' }))

    const details = await screen.findByRole('dialog', { name: 'project-a' })
    const primaryFolder = within(details).getByRole('button', {
      name: 'Open in Explorer: /workspace/a'
    })
    const sharedFolder = within(details).getByRole('button', {
      name: 'Open in Explorer: /workspace/shared'
    })
    const docsFolder = within(details).getByRole('button', {
      name: 'Open in Explorer: /workspace/docs'
    })

    fireEvent.click(primaryFolder)
    fireEvent.click(sharedFolder)
    fireEvent.click(docsFolder)

    expect(shellOpenPath).toHaveBeenNthCalledWith(1, '/workspace/a')
    expect(shellOpenPath).toHaveBeenNthCalledWith(2, '/workspace/shared')
    expect(shellOpenPath).toHaveBeenNthCalledWith(3, '/workspace/docs')

    fireEvent.click(within(details).getByRole('button', { name: 'Edit project' }))

    const editDialog = await screen.findByRole('dialog', { name: 'Edit project' })
    expect(editDialog).toHaveTextContent('/workspace/a')
    expect(editDialog).toHaveTextContent('/workspace/shared')
    expect(editDialog).toHaveTextContent('/workspace/docs')
  })

  it('reports a cold project as not loaded instead of zero threads', async () => {
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      secondaryLimit: 8,
      projects: [{
        path: '/workspace/cold',
        name: 'cold-project',
        state: 'cold',
        running: false,
        loaded: false,
        threadCount: 0,
        threads: [],
        pinnedThreadIds: []
      }]
    })

    renderList()
    fireEvent.focus(screen.getByRole('button', { name: 'cold-project' }))

    expect(await screen.findByRole('dialog', { name: 'cold-project' })).toHaveTextContent('Not loaded')
    expect(screen.getByRole('dialog', { name: 'cold-project' })).not.toHaveTextContent('0 threads')
  })

  it('places pinned project subtrees after flat pinned threads without duplicates', () => {
    const pinned = makeThread('pinned-a', 'Pinned A', 3)
    const normal = makeThread('normal-a', 'Normal A', 8)
    useThreadStore.getState().setThreadList([pinned, normal], '/workspace/a')
    useThreadStore.getState().hydratePinnedThreadIds('/workspace/a', ['pinned-a'])
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      secondaryLimit: 8,
      projects: [{
        path: '/workspace/a',
        name: 'project-a',
        state: 'foreground',
        running: true,
        loaded: true,
        pinned: true,
        threadCount: 2,
        threads: [],
        pinnedThreadIds: ['pinned-a']
      }, {
        path: '/workspace/b',
        name: 'project-b',
        state: 'cold',
        running: false,
        loaded: false,
        threadCount: 0,
        threads: [],
        pinnedThreadIds: []
      }]
    })

    renderList({ workspacePath: '/workspace/a' })

    const pinnedThread = screen.getByText('Pinned A')
    const pinnedProject = screen.getByRole('button', { name: 'project-a' })
    const projectsHeading = screen.getByText('Projects')
    expect(pinnedThread.compareDocumentPosition(pinnedProject) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(pinnedProject.compareDocumentPosition(projectsHeading) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(screen.getAllByText('Pinned A')).toHaveLength(1)
    expect(screen.getAllByText('Normal A')).toHaveLength(1)
  })

  it('persists project pin changes from the details card', async () => {
    settingsGet.mockResolvedValue({ locale: 'en', pinnedProjectIds: ['/workspace/other'] })
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      secondaryLimit: 8,
      projects: [{
        path: '/workspace/a',
        name: 'project-a',
        state: 'foreground',
        running: true,
        loaded: true,
        threadCount: 0,
        threads: [],
        pinnedThreadIds: []
      }]
    })

    renderList({ workspacePath: '/workspace/a' })
    fireEvent.focus(screen.getByRole('button', { name: 'project-a' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Pin project' }))

    await waitFor(() => {
      expect(settingsSet).toHaveBeenCalledWith({
        pinnedProjectIds: ['/workspace/other', '/workspace/a']
      })
    })
  })

  it('also exposes project pinning in the existing project actions menu', async () => {
    settingsGet.mockResolvedValue({ locale: 'en', pinnedProjectIds: [] })
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      secondaryLimit: 8,
      projects: [{
        path: '/workspace/a',
        name: 'project-a',
        state: 'foreground',
        running: true,
        loaded: true,
        threadCount: 0,
        threads: [],
        pinnedThreadIds: []
      }]
    })

    renderList({ workspacePath: '/workspace/a' })
    const projectRow = screen.getByRole('button', { name: 'project-a' })
    fireEvent.mouseEnter(projectRow)
    fireEvent.click(screen.getByRole('button', { name: 'Project actions' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Pin project' }))

    await waitFor(() => {
      expect(settingsSet).toHaveBeenCalledWith({ pinnedProjectIds: ['/workspace/a'] })
    })
  })

  it('keeps the Projects header available when Recents is foreground with no projects', async () => {
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/chats',
      foregroundProjectId: '/chats',
      secondaryLimit: 8,
      projects: [],
      chat: {
        projectId: '/chats',
        kind: 'chat',
        path: '/chats',
        identityWorkspacePath: '/chats',
        name: '/chats',
        state: 'foreground',
        running: true,
        loaded: true,
        threadCount: 0,
        threads: [],
        pinnedThreadIds: []
      }
    })

    renderList({ workspacePath: '/chats' })

    expect(screen.getByText('Projects')).toBeInTheDocument()
    expect(screen.getByText('Recents')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Workspace options' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '/chats' })).not.toBeInTheDocument()

    fireEvent.mouseEnter(screen.getByText('Projects').parentElement as HTMLElement)
    fireEvent.click(screen.getByRole('button', { name: 'Add project' }))

    expect(await screen.findByRole('dialog', { name: 'Create project' })).toBeInTheDocument()
  })

  it('New chat in the Recents group switches to the chat workspace and opens a new chat', async () => {
    useThreadStore.getState().setActiveThreadId('old-thread')
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      foregroundProjectId: '/workspace/a',
      secondaryLimit: 8,
      projects: [
        {
          path: '/workspace/a',
          name: 'a',
          state: 'foreground',
          running: true,
          loaded: true,
          threadCount: 0,
          threads: [],
          pinnedThreadIds: []
        }
      ],
      chat: {
        projectId: '/chats',
        kind: 'chat',
        path: '/chats',
        name: '/chats',
        state: 'secondary',
        running: true,
        loaded: true,
        threadCount: 0,
        threads: [],
        pinnedThreadIds: []
      }
    })

    renderList()
    fireEvent.mouseEnter(screen.getByText('Recents').parentElement as HTMLElement)
    fireEvent.click(screen.getByRole('button', { name: 'New chat' }))

    await waitFor(() => {
      expect(workspaceSwitch).toHaveBeenCalledWith('/chats')
      expect(useThreadStore.getState().activeThreadId).toBeNull()
      expect(useUIStore.getState().activeMainView).toBe('conversation')
    })
  })

  it('renders interactive chat rows without a project folder row when the chat workspace is foreground', () => {
    useThreadStore.getState().setThreadList([
      makeThread('chat-live', 'Live chat thread')
    ], '/chats')
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/chats',
      foregroundProjectId: '/chats',
      secondaryLimit: 8,
      projects: [
        {
          path: '/workspace/a',
          name: 'a',
          state: 'secondary',
          running: true,
          loaded: true,
          threadCount: 0,
          threads: [],
          pinnedThreadIds: []
        }
      ],
      chat: {
        projectId: '/chats',
        kind: 'chat',
        path: '/chats',
        name: '/chats',
        state: 'foreground',
        running: true,
        loaded: true,
        threadCount: 1,
        threads: [],
        pinnedThreadIds: []
      }
    })

    renderList({ workspacePath: '/chats' })

    // The live foreground thread appears in the Recents group as an interactive row.
    expect(screen.getByText('Live chat thread')).toBeInTheDocument()
    // The foreground chat workspace is never synthesized as a Project row.
    expect(screen.queryByRole('button', { name: '/chats' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'a' })).toBeInTheDocument()
  })

  it('collapses the Projects section and persists the preference when its header is clicked', async () => {
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      foregroundProjectId: '/workspace/a',
      secondaryLimit: 8,
      projects: [
        {
          path: '/workspace/a',
          name: 'alpha',
          state: 'foreground',
          running: true,
          loaded: true,
          threadCount: 1,
          threads: [makeThread('a-1', 'Alpha thread')],
          pinnedThreadIds: []
        }
      ]
    })

    renderList()

    const header = screen.getByRole('button', { name: 'Toggle Projects section' })
    expect(header).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByRole('button', { name: 'alpha' })).toBeInTheDocument()

    fireEvent.click(header)

    expect(header).toHaveAttribute('aria-expanded', 'false')
    expect(useUIStore.getState().projectsSectionCollapsed).toBe(true)
    await waitFor(() => {
      expect(settingsSet).toHaveBeenCalledWith({ projectsSectionCollapsed: true })
    })
  })

  it('collapses the Projects section from the keyboard and unmounts rows after the collapse timeout', async () => {
    vi.useFakeTimers()
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      foregroundProjectId: '/workspace/a',
      secondaryLimit: 8,
      projects: [
        {
          path: '/workspace/a',
          name: 'alpha',
          state: 'foreground',
          running: true,
          loaded: true,
          threadCount: 1,
          threads: [makeThread('a-1', 'Alpha thread')],
          pinnedThreadIds: []
        }
      ]
    })

    renderList()

    const header = screen.getByRole('button', { name: 'Toggle Projects section' })
    fireEvent.keyDown(header, { key: 'Enter' })

    expect(header).toHaveAttribute('aria-expanded', 'false')
    expect(settingsSet).toHaveBeenCalledWith({ projectsSectionCollapsed: true })

    await act(async () => {
      vi.advanceTimersByTime(360)
    })

    expect(screen.queryByRole('button', { name: 'alpha' })).not.toBeInTheDocument()
    expect(screen.queryByText('Alpha thread')).not.toBeInTheDocument()
  })

  it('collapses the complete mixed Pinned section and persists the preference', async () => {
    vi.useFakeTimers()
    const pinnedThread = makeThread('pinned-a', 'Pinned thread', 2)
    const projectThread = makeThread('project-a-thread', 'Pinned project thread', 4)
    useThreadStore.getState().setThreadList([pinnedThread], '/workspace/a')
    useThreadStore.getState().hydratePinnedThreadIds('/workspace/a', ['pinned-a'])
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      foregroundProjectId: '/workspace/a',
      secondaryLimit: 8,
      projects: [{
        path: '/workspace/a',
        name: 'alpha',
        state: 'foreground',
        running: true,
        loaded: true,
        pinned: false,
        threadCount: 1,
        threads: [],
        pinnedThreadIds: ['pinned-a']
      }, {
        path: '/workspace/b',
        name: 'pinned-project',
        state: 'secondary',
        running: true,
        loaded: true,
        pinned: true,
        threadCount: 1,
        threads: [projectThread],
        pinnedThreadIds: []
      }]
    })

    renderList()

    const header = screen.getByRole('button', { name: 'Toggle Pinned section' })
    expect(header).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByText('Pinned thread')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'pinned-project' })).toBeInTheDocument()

    fireEvent.keyDown(header, { key: ' ' })

    expect(header).toHaveAttribute('aria-expanded', 'false')
    expect(useUIStore.getState().pinnedSectionCollapsed).toBe(true)
    expect(settingsSet).toHaveBeenCalledWith({ pinnedSectionCollapsed: true })

    await act(async () => {
      vi.advanceTimersByTime(360)
    })

    expect(screen.queryByText('Pinned thread')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'pinned-project' })).not.toBeInTheDocument()
  })

  it('collapses the Chats section and persists the preference when its header is clicked', async () => {
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      foregroundProjectId: '/workspace/a',
      secondaryLimit: 8,
      projects: [
        {
          path: '/workspace/a',
          name: 'alpha',
          state: 'foreground',
          running: true,
          loaded: true,
          threadCount: 0,
          threads: [],
          pinnedThreadIds: []
        }
      ],
      chat: {
        projectId: '/chats',
        kind: 'chat',
        path: '/chats',
        name: '/chats',
        state: 'secondary',
        running: true,
        loaded: true,
        threadCount: 1,
        threads: [makeThread('chat-1', 'General chat thread')],
        pinnedThreadIds: []
      }
    })

    renderList()

    const header = screen.getByRole('button', { name: 'Toggle Recents section' })
    expect(header).toHaveAttribute('aria-expanded', 'true')

    fireEvent.click(header)

    expect(header).toHaveAttribute('aria-expanded', 'false')
    expect(useUIStore.getState().chatsSectionCollapsed).toBe(true)
    await waitFor(() => {
      expect(settingsSet).toHaveBeenCalledWith({ chatsSectionCollapsed: true })
    })
  })

  it('collapses the Chats section from the keyboard and unmounts rows after the collapse timeout', async () => {
    vi.useFakeTimers()
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      foregroundProjectId: '/workspace/a',
      secondaryLimit: 8,
      projects: [
        {
          path: '/workspace/a',
          name: 'alpha',
          state: 'foreground',
          running: true,
          loaded: true,
          threadCount: 0,
          threads: [],
          pinnedThreadIds: []
        }
      ],
      chat: {
        projectId: '/chats',
        kind: 'chat',
        path: '/chats',
        name: '/chats',
        state: 'secondary',
        running: true,
        loaded: true,
        threadCount: 1,
        threads: [makeThread('chat-1', 'General chat thread')],
        pinnedThreadIds: []
      }
    })

    renderList()

    const header = screen.getByRole('button', { name: 'Toggle Recents section' })
    expect(screen.getByText('General chat thread')).toBeInTheDocument()

    fireEvent.keyDown(header, { key: ' ' })

    expect(header).toHaveAttribute('aria-expanded', 'false')
    expect(settingsSet).toHaveBeenCalledWith({ chatsSectionCollapsed: true })

    await act(async () => {
      vi.advanceTimersByTime(360)
    })

    expect(screen.queryByText('General chat thread')).not.toBeInTheDocument()
  })

  it('honors the persisted collapsed Projects preference on mount', () => {
    useUIStore.setState({ projectsSectionCollapsed: true })
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      foregroundProjectId: '/workspace/a',
      secondaryLimit: 8,
      projects: [
        {
          path: '/workspace/a',
          name: 'alpha',
          state: 'foreground',
          running: true,
          loaded: true,
          threadCount: 1,
          threads: [makeThread('a-1', 'Alpha thread')],
          pinnedThreadIds: []
        }
      ]
    })

    renderList()

    expect(screen.getByRole('button', { name: 'Toggle Projects section' }))
      .toHaveAttribute('aria-expanded', 'false')
  })

  it('honors the persisted collapsed Chats preference on mount', () => {
    useUIStore.setState({ chatsSectionCollapsed: true })
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      foregroundProjectId: '/workspace/a',
      secondaryLimit: 8,
      projects: [
        {
          path: '/workspace/a',
          name: 'alpha',
          state: 'foreground',
          running: true,
          loaded: true,
          threadCount: 0,
          threads: [],
          pinnedThreadIds: []
        }
      ],
      chat: {
        projectId: '/chats',
        kind: 'chat',
        path: '/chats',
        name: '/chats',
        state: 'secondary',
        running: true,
        loaded: true,
        threadCount: 1,
        threads: [makeThread('chat-1', 'General chat thread')],
        pinnedThreadIds: []
      }
    })

    renderList()

    expect(screen.getByRole('button', { name: 'Toggle Recents section' }))
      .toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByText('General chat thread')).not.toBeInTheDocument()
  })

  it('does not toggle the section when the Add project button or its dialog is clicked', async () => {
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      foregroundProjectId: '/workspace/a',
      secondaryLimit: 8,
      projects: [
        {
          path: '/workspace/a',
          name: 'alpha',
          state: 'foreground',
          running: true,
          loaded: true,
          threadCount: 0,
          threads: [],
          pinnedThreadIds: []
        }
      ]
    })

    renderList({ workspacePath: '/workspace/a' })

    fireEvent.mouseEnter(screen.getByText('Projects').parentElement as HTMLElement)
    fireEvent.click(screen.getByRole('button', { name: 'Add project' }))

    // Opening the Create dialog must not collapse the section.
    expect(screen.getByRole('button', { name: 'Toggle Projects section' }))
      .toHaveAttribute('aria-expanded', 'true')
    expect(useUIStore.getState().projectsSectionCollapsed).toBe(false)

    // The dialog is portaled, but React events bubble through the component tree.
    // Clicking inside it must NOT reach the section header's toggle onClick
    // (regression: the dialog was rendered inside the toggle element's subtree).
    const dialog = await screen.findByRole('dialog', { name: 'Create project' })
    fireEvent.click(within(dialog).getByRole('textbox'))
    expect(screen.getByRole('button', { name: 'Toggle Projects section' }))
      .toHaveAttribute('aria-expanded', 'true')
    expect(useUIStore.getState().projectsSectionCollapsed).toBe(false)
  })

  it('toggles pin for a secondary-workspace thread by persisting per-workspace settings', async () => {
    useThreadStore.getState().setThreadList([makeThread('thread-a', 'Thread from A')], '/workspace/a')
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      foregroundProjectId: '/workspace/a',
      secondaryLimit: 8,
      projects: [
        {
          kind: 'local',
          path: '/workspace/a',
          name: 'a',
          state: 'foreground',
          running: true,
          loaded: true,
          threadCount: 1,
          threads: [],
          pinnedThreadIds: []
        },
        {
          kind: 'local',
          path: '/workspace/b',
          name: 'b',
          state: 'secondary',
          running: true,
          loaded: true,
          threadCount: 1,
          threads: [makeThread('thread-b', 'Thread B')],
          pinnedThreadIds: []
        }
      ]
    })

    renderList()

    fireEvent.click(screen.getByTestId('project-thread-pin-/workspace/b-thread-b'))

    await waitFor(() => {
      expect(settingsSet).toHaveBeenCalledWith({
        pinnedThreadIdsByWorkspace: { '/workspace/b': ['thread-b'] }
      })
    })
  })

  it('archives a secondary-workspace thread through the workspace archive IPC', async () => {
    useThreadStore.getState().setThreadList([makeThread('thread-a', 'Thread from A')], '/workspace/a')
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      foregroundProjectId: '/workspace/a',
      secondaryLimit: 8,
      projects: [
        {
          kind: 'local',
          path: '/workspace/a',
          name: 'a',
          state: 'foreground',
          running: true,
          loaded: true,
          threadCount: 1,
          threads: [],
          pinnedThreadIds: []
        },
        {
          kind: 'local',
          path: '/workspace/b',
          name: 'b',
          state: 'secondary',
          running: true,
          loaded: true,
          threadCount: 1,
          threads: [makeThread('thread-b', 'Thread B')],
          pinnedThreadIds: []
        }
      ]
    })

    renderList()

    fireEvent.click(screen.getByTestId('project-thread-archive-/workspace/b-thread-b'))

    await waitFor(() => {
      expect(workspaceArchiveThread).toHaveBeenCalledWith('/workspace/b', 'thread-b')
    })
  })

  it('auto-switches to the MRU running workspace when stopping the foreground project', async () => {
    useThreadStore.getState().setThreadList([], '/workspace/a')
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      foregroundProjectId: '/workspace/a',
      secondaryLimit: 8,
      projects: [
        {
          kind: 'local',
          path: '/workspace/a',
          name: 'a',
          state: 'foreground',
          running: true,
          loaded: true,
          threadCount: 0,
          threads: [],
          pinnedThreadIds: [],
          lastOpenedAt: '2026-07-01T00:00:00.000Z'
        },
        {
          kind: 'local',
          path: '/workspace/b',
          name: 'b',
          state: 'secondary',
          running: true,
          loaded: true,
          threadCount: 0,
          threads: [],
          pinnedThreadIds: [],
          lastOpenedAt: '2026-07-05T00:00:00.000Z'
        },
        {
          kind: 'local',
          path: '/workspace/c',
          name: 'c',
          state: 'secondary',
          running: true,
          loaded: true,
          threadCount: 0,
          threads: [],
          pinnedThreadIds: [],
          lastOpenedAt: '2026-07-03T00:00:00.000Z'
        }
      ]
    })

    renderList()

    // Open the foreground project's action menu and stop it.
    fireEvent.mouseEnter(screen.getByRole('button', { name: 'a' }))
    fireEvent.click(screen.getByRole('button', { name: 'Project actions' }))
    fireEvent.click(await screen.findByText('Stop Workspace'))

    await waitFor(() => {
      expect(workspaceStop).toHaveBeenCalledWith('/workspace/a')
      // /workspace/b has the most recent lastOpenedAt among running others.
      expect(workspaceSwitch).toHaveBeenCalledWith('/workspace/b')
    })
  })
})
