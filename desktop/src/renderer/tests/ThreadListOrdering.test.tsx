import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createEvent, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { installDesktopApiMock } from './desktopApiMock'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ThreadList } from '../components/sidebar/ThreadList'
import { useSidebarThreadOrderStore } from '../stores/sidebarThreadOrderStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import type { ThreadSummary } from '../types/thread'

const settingsSet = vi.fn()

function makeThread(id: string, minutesAgo: number): ThreadSummary {
  const time = new Date(Date.now() - minutesAgo * 60 * 1000).toISOString()
  return {
    id,
    displayName: `Thread ${id}`,
    status: 'active',
    originChannel: 'dotcraft-desktop',
    createdAt: time,
    lastActiveAt: time
  }
}

function seedWorkspaces(): void {
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
        projectId: '/workspace/b',
        path: '/workspace/b',
        name: 'b',
        state: 'secondary',
        running: true,
        loaded: true,
        threadCount: 1,
        threads: [makeThread('project-b', 5)],
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
      threadCount: 3,
      threads: [makeThread('chat-a', 1), makeThread('chat-b', 10), makeThread('chat-c', 20)],
      pinnedThreadIds: []
    }
  })
}

function renderList(): void {
  render(
    <LocaleProvider>
      <ThreadList workspacePath="/workspace/a" />
    </LocaleProvider>
  )
}

function openRecentsOptions(): void {
  fireEvent.mouseEnter(screen.getByRole('button', { name: 'Toggle Recents section' }))
  fireEvent.click(screen.getByRole('button', { name: 'Recents options' }))
}

/** The test DOM has no DragEvent, so pointer coordinates must be attached by hand. */
function dragOverUpperHalf(target: HTMLElement, transfer: DataTransfer): void {
  const event = createEvent.dragOver(target, { dataTransfer: transfer })
  Object.defineProperty(event, 'clientY', { value: target.getBoundingClientRect().top - 1 })
  fireEvent(target, event)
}

function dataTransfer(): DataTransfer {
  const data = new Map<string, string>()
  return {
    effectAllowed: 'uninitialized',
    dropEffect: 'none',
    types: [],
    setData: (type: string, value: string) => { data.set(type, value) },
    getData: (type: string) => data.get(type) ?? ''
  } as unknown as DataTransfer
}

describe('ThreadList ordering', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsSet.mockResolvedValue({})
    installDesktopApiMock({
      settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }), set: settingsSet },
      appServer: { sendRequest: vi.fn() },
      workspace: { switch: vi.fn(), getRecent: vi.fn().mockResolvedValue([]) }
    })
    useThreadStore.getState().reset()
    useWorkspaceProjectsStore.getState().reset()
    useSidebarThreadOrderStore.getState().hydrate({})
    useUIStore.setState({
      projectsSectionCollapsed: false,
      pinnedSectionCollapsed: false,
      chatsSectionCollapsed: false
    })
    seedWorkspaces()
  })

  it('adds loaded project threads to Recents when Show Projects is checked', () => {
    renderList()
    expect(screen.getAllByText('Thread project-b')).toHaveLength(1)

    openRecentsOptions()
    const showProjects = screen.getByRole('menuitemcheckbox', { name: 'Projects' })
    expect(showProjects).toHaveAttribute('aria-checked', 'false')
    fireEvent.click(showProjects)

    expect(settingsSet).toHaveBeenCalledWith({ recentsShowProjects: true })
    expect(screen.getAllByText('Thread project-b')).toHaveLength(2)
  })

  it('places project threads after the saved Recents order when shown in manual order', () => {
    useSidebarThreadOrderStore.getState().hydrate({
      recentsThreadSort: 'manual',
      recentsThreadOrder: ['chat-c', 'chat-a', 'chat-b']
    })
    renderList()

    openRecentsOptions()
    fireEvent.click(screen.getByRole('menuitemcheckbox', { name: 'Projects' }))

    expect(settingsSet).toHaveBeenCalledWith({
      recentsThreadOrder: ['chat-c', 'chat-a', 'chat-b', 'project-b']
    })
  })

  it('freezes the displayed Recents order when switching to manual order', () => {
    renderList()
    openRecentsOptions()
    fireEvent.mouseEnter(screen.getByRole('menuitem', { name: 'Sort chats by' }))
    fireEvent.click(screen.getByRole('menuitemradio', { name: 'Manual order' }))

    expect(settingsSet).toHaveBeenCalledWith({
      recentsThreadSort: 'manual',
      recentsThreadOrder: ['chat-a', 'chat-b', 'chat-c']
    })
  })

  it('persists a dragged row at its drop position in manual order', async () => {
    useSidebarThreadOrderStore.getState().hydrate({
      recentsThreadSort: 'manual',
      recentsThreadOrder: ['chat-a', 'chat-b', 'chat-c']
    })
    renderList()

    const transfer = dataTransfer()
    fireEvent.dragStart(screen.getByTestId('thread-reorder-recents-chat-c'), { dataTransfer: transfer })
    const target = screen.getByTestId('thread-reorder-recents-chat-a')
    dragOverUpperHalf(target, transfer)
    fireEvent.drop(target, { dataTransfer: transfer })

    await waitFor(() => {
      expect(settingsSet).toHaveBeenCalledWith({ recentsThreadOrder: ['chat-c', 'chat-a', 'chat-b'] })
    })
  })

  it('keeps one pinned order across projects', async () => {
    const pinnedProject = (id: string, threadId: string) => ({
      projectId: id,
      path: id,
      name: id,
      state: 'secondary' as const,
      running: true,
      loaded: true,
      threadCount: 1,
      threads: [makeThread(threadId, 5)],
      pinnedThreadIds: [threadId],
      pinned: false
    })
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/workspace/a',
      foregroundProjectId: '/workspace/a',
      secondaryLimit: 8,
      projects: [pinnedProject('/workspace/b', 'pin-b'), pinnedProject('/workspace/c', 'pin-c')]
    })
    renderList()

    const transfer = dataTransfer()
    fireEvent.dragStart(screen.getByTestId('thread-reorder-pinned-pin-c'), { dataTransfer: transfer })
    const target = screen.getByTestId('thread-reorder-pinned-pin-b')
    dragOverUpperHalf(target, transfer)
    fireEvent.drop(target, { dataTransfer: transfer })

    await waitFor(() => {
      expect(settingsSet).toHaveBeenCalledWith({ pinnedThreadOrder: ['pin-c', 'pin-b'] })
    })
  })

  it('hides section options that cannot change anything', () => {
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
        state: 'secondary',
        running: true,
        loaded: true,
        threadCount: 1,
        threads: [makeThread('only-chat', 1)],
        pinnedThreadIds: []
      }
    })
    renderList()

    expect(screen.queryByRole('button', { name: 'Recents options' })).not.toBeInTheDocument()
  })

  it('ignores drops from another list', () => {
    useSidebarThreadOrderStore.getState().hydrate({
      recentsThreadSort: 'manual',
      projectsThreadSort: 'manual',
      recentsThreadOrder: ['chat-a', 'chat-b', 'chat-c']
    })
    renderList()

    const transfer = dataTransfer()
    fireEvent.dragStart(screen.getByTestId('thread-reorder-project:/workspace/b-project-b'), { dataTransfer: transfer })
    const target = screen.getByTestId('thread-reorder-recents-chat-a')
    dragOverUpperHalf(target, transfer)
    fireEvent.drop(target, { dataTransfer: transfer })

    expect(settingsSet).not.toHaveBeenCalled()
  })
})
