import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
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
const workspaceRemoveRecent = vi.fn()
const workspaceDisconnectRemote = vi.fn()
const workspaceGetRecent = vi.fn()
const workspaceClearRecent = vi.fn()
const workspaceClearSelection = vi.fn()

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
    welcomeDraft: null,
    welcomeDraftsByWorkspace: {},
    welcomeDraftWorkspacePath: null
  })
}

describe('ThreadList project-first layout', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ locale: 'en' })
    settingsSet.mockResolvedValue({})
    workspaceSwitch.mockResolvedValue(undefined)
    workspacePickFolder.mockResolvedValue(null)
    workspaceRemoveRecent.mockResolvedValue(undefined)
    workspaceDisconnectRemote.mockResolvedValue(undefined)
    workspaceGetRecent.mockResolvedValue([])
    workspaceClearRecent.mockResolvedValue(undefined)
    workspaceClearSelection.mockResolvedValue(undefined)
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet, set: settingsSet },
        appServer: { sendRequest: vi.fn() },
        workspace: {
          switch: workspaceSwitch,
          pickFolder: workspacePickFolder,
          removeRecent: workspaceRemoveRecent,
          disconnectRemote: workspaceDisconnectRemote,
          getRecent: workspaceGetRecent,
          clearRecent: workspaceClearRecent,
          clearSelection: workspaceClearSelection
        },
        shell: { openPath: vi.fn() }
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

  it('renders pinned rows above Projects and does not duplicate pinned threads inside projects', () => {
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
    expect(screen.getByTestId('project-thread-pinned-/workspace/b-pinned-b')).toBeInTheDocument()
    expect(screen.queryByTestId('project-thread-pinned-/workspace/b-normal-b')).not.toBeInTheDocument()
    expect(screen.getByText('Normal A')).toBeInTheDocument()
    expect(screen.getByText('Normal B')).toBeInTheDocument()
  })

  it('clicking a project row collapses it without switching workspace', () => {
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
    expect(screen.queryByText('Thread B')).not.toBeInTheDocument()
  })

  it('keeps cold projects collapsed without rendering an empty chat message', () => {
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
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

    expect(projectRow).toHaveAttribute('aria-expanded', 'false')
    expect(screen.queryByText('No chats')).not.toBeInTheDocument()

    fireEvent.click(projectRow)

    expect(projectRow).toHaveAttribute('aria-expanded', 'false')
    expect(workspaceSwitch).not.toHaveBeenCalled()
    expect(screen.queryByText('No chats')).not.toBeInTheDocument()
  })

  it('does not repeat empty chat copy for loaded empty projects', () => {
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
    expect(screen.queryByText('No chats')).not.toBeInTheDocument()
  })

  it('clicking a background thread promotes its workspace before selecting the thread', async () => {
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
      expect(useThreadStore.getState().activeThreadId).toBe('thread-b')
    })
  })

  it('project actions can start a new chat for that project', async () => {
    useThreadStore.getState().setActiveThreadId('old-thread')
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
    })
  })

  it('Add project picks a folder and switches to it', async () => {
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
    fireEvent.click(screen.getByRole('button', { name: 'Add project' }))

    await waitFor(() => {
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
    ], 'F:/Git/dotcraft')
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: 'F:\\Git\\dotcraft',
      foregroundProjectId: 'f:\\git\\dotcraft',
      secondaryLimit: 8,
      projects: [
        {
          kind: 'local',
          path: 'F:\\Git\\dotcraft',
          identityWorkspacePath: 'F:\\Git\\dotcraft',
          name: 'dotcraft',
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

  it('renders cached foreground pinned rows with a static pinned icon while the global rows belong elsewhere', async () => {
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
    fireEvent.click(screen.getByText('Cached B pinned thread'))

    expect(screen.getByTestId('project-thread-pinned-/workspace/b-thread-b')).toBeInTheDocument()
    expect(screen.queryByTestId('thread-pin-thread-b')).not.toBeInTheDocument()
    expect(screen.queryByText('Thread from A')).not.toBeInTheDocument()
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
})
