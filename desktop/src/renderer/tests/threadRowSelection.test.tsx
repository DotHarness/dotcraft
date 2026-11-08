import type { ReactNode } from 'react'
import { act, fireEvent, render, renderHook, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { installDesktopApiMock } from './desktopApiMock'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ReadonlyThreadRow } from '../components/sidebar/ReadonlyThreadRow'
import { RecentsSection } from '../components/sidebar/RecentsSection'
import { ThreadRowPlacementContext, useThreadRowSelected } from '../components/sidebar/threadRowSelection'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import type { ThreadSummary } from '../types/thread'
import type { WorkspaceProjectSummary } from '../../shared/workspaceProjects'

const WORKSPACE_B = 'X:\\fixtures\\workspace-b'
const PROJECT_B: WorkspaceProjectSummary = {
  projectId: WORKSPACE_B,
  kind: 'local',
  path: WORKSPACE_B,
  identityWorkspacePath: WORKSPACE_B,
  name: 'workspace-b',
  state: 'secondary',
  running: true,
  loaded: true,
  threadCount: 1,
  threads: [],
  pinnedThreadIds: [],
  pinned: false
}
const THREAD_B = {
  id: 'thread-b',
  displayName: 'thread-b',
  status: 'active',
  originChannel: 'dotcraft-desktop',
  createdAt: '2026-01-01T00:00:00.000Z',
  lastActiveAt: '2026-01-01T00:00:00.000Z',
  workspacePath: WORKSPACE_B
} as ThreadSummary

function Placement({ children }: { children: ReactNode }): JSX.Element {
  return (
    <ThreadRowPlacementContext.Provider value={{ listId: `project:${WORKSPACE_B}`, home: true }}>
      {children}
    </ThreadRowPlacementContext.Provider>
  )
}

function openPending(): void {
  useUIStore.getState().setPendingProjectThreadOpen({
    projectKey: WORKSPACE_B,
    workspacePath: WORKSPACE_B,
    threadId: THREAD_B.id
  })
}

describe('thread row selection', () => {
  beforeEach(() => {
    installDesktopApiMock({
      settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
      shell: { listOpenTargets: vi.fn().mockResolvedValue([]) },
      workspace: { switch: vi.fn().mockResolvedValue(undefined) }
    })
    useThreadStore.getState().reset()
    useUIStore.setState({ pendingProjectThreadOpen: null })
  })

  it('promotes local Chats before clearing a remote conversation for Recents New Chat', async () => {
    const path = 'X:/fixtures/chats'
    const chat: WorkspaceProjectSummary = { ...PROJECT_B, projectId: path, path, kind: 'chat', threads: [] }
    let promote!: () => void
    window.api.workspace.switch = vi.fn(() => new Promise<void>(resolve => { promote = resolve }))
    useThreadStore.getState().setActiveThreadId('remote-thread')
    render(<LocaleProvider><RecentsSection
      chat={chat} foreground={false} rows={[]} searchQuery="" opening={false} collapsed={false}
      onToggle={() => {}} sortMode="updated" showProjects={false} reorderEnabled={false} onMove={() => {}}
    /></LocaleProvider>)

    fireEvent.click(screen.getByRole('button', { name: 'New chat' }))
    expect(window.api.workspace.switch).toHaveBeenCalledWith(path)
    expect(useThreadStore.getState().activeThreadId).toBe('remote-thread')
    await act(async () => promote())
    expect(useThreadStore.getState().activeThreadId).toBeNull()
    expect(useUIStore.getState().welcomeDraftWorkspacePath).toBe(path)
  })

  it('moves the selection off the active thread while another project thread is pending', () => {
    useThreadStore.getState().setActiveThreadId('thread-a')
    const active = renderHook(() => useThreadRowSelected('thread-a'), { wrapper: Placement })
    const pending = renderHook(() => useThreadRowSelected(THREAD_B.id), { wrapper: Placement })

    act(() => openPending())

    expect(active.result.current).toBe(false)
    expect(pending.result.current).toBe(true)
  })

  it('stays selected through the workspace reset until the pending thread becomes active', () => {
    const selections: boolean[] = []
    renderHook(() => {
      const selected = useThreadRowSelected(THREAD_B.id)
      selections.push(selected)
      return selected
    }, { wrapper: Placement })

    act(() => openPending())
    selections.length = 0
    act(() => useThreadStore.getState().reset())
    act(() => {
      useUIStore.getState().consumePendingProjectThreadOpen(WORKSPACE_B, [THREAD_B.id])
      useThreadStore.getState().setActiveThreadId(THREAD_B.id)
    })

    expect(selections).not.toContain(false)
  })

  it.each(['local', 'device'] as const)('withdraws a clicked %s project row when promotion fails', async (source) => {
    const project: WorkspaceProjectSummary = source === 'device'
      ? {
          ...PROJECT_B, projectId: 'remote:device:computer-b:project-b', kind: 'remote',
          remote: { source: 'device', hostId: 'computer-b', remoteProjectId: 'project-b' }
        }
      : PROJECT_B
    let rejectSwitch: (error: Error) => void = () => {}
    if (source === 'device') {
      window.api.pairedComputers.openProject = vi.fn(
        () => new Promise<{ ok: true; value: null }>((_resolve, reject) => { rejectSwitch = reject })
      )
    } else {
      window.api.workspace.switch = vi.fn(
        () => new Promise<void>((_resolve, reject) => { rejectSwitch = reject })
      )
    }

    render(
      <LocaleProvider>
        <Placement>
          <ReadonlyThreadRow thread={THREAD_B} project={project} />
        </Placement>
      </LocaleProvider>
    )
    const row = screen.getByRole('generic', { name: THREAD_B.displayName! })

    fireEvent.click(row)
    expect(row).toHaveAttribute('aria-current', 'true')
    expect(useUIStore.getState().pendingProjectThreadOpen?.projectKey).toBe(
      source === 'device' ? 'remote:device:computer-b:project-b' : 'x:/fixtures/workspace-b'
    )
    if (source === 'device') {
      expect(window.api.pairedComputers.openProject).toHaveBeenCalledWith('computer-b', 'project-b')
      expect(window.api.workspace.switch).not.toHaveBeenCalled()
    }

    await act(async () => rejectSwitch(new Error('switch failed')))
    await waitFor(() => expect(row).not.toHaveAttribute('aria-current'))
  })
})
