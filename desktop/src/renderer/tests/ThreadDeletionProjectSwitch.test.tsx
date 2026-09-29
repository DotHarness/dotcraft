import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { applyWorkspaceThreadNotificationToCache } from '../../main/workspaceThreadCache'
import type { WorkspaceProjectSummary } from '../../shared/workspaceProjects'
import { ThreadList } from '../components/sidebar/ThreadList'
import { ConfirmDialogHost } from '../components/ui/ConfirmDialog'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import type { ThreadSummary } from '../types/thread'
import { installDesktopApiMock } from './desktopApiMock'

function thread(id: string): ThreadSummary {
  return {
    id,
    displayName: id,
    status: 'active',
    originChannel: 'dotcraft-desktop',
    createdAt: '2026-09-29T00:00:00Z',
    lastActiveAt: '2026-09-29T00:00:00Z'
  }
}

function project(path: string, threads: ThreadSummary[]): WorkspaceProjectSummary {
  return {
    path,
    name: path,
    state: 'secondary',
    running: true,
    loaded: true,
    threadCount: threads.length,
    threads,
    pinned: false,
    pinnedThreadIds: []
  }
}

describe('deleted threads across project switches', () => {
  beforeEach(() => {
    useThreadStore.getState().reset()
    useWorkspaceProjectsStore.getState().reset()
    useUIStore.setState({
      activeMainView: 'conversation',
      pendingProjectThreadOpen: null,
      projectsSectionCollapsed: false,
      pinnedSectionCollapsed: false,
      chatsSectionCollapsed: false
    })
  })

  it.each([
    ['notification first', true],
    ['response first', true],
    ['notification first', false],
    ['response first', false]
  ] as const)('%s, deleting selected=%s', async (order, selected) => {
    const deleted = thread('delete-me')
    const survivor = thread('keep-me')
    const other = thread('other-project-thread')
    const projects = [project('/workspace/a', [deleted, survivor]), project('/workspace/b', [other])]
    let foreground = '/workspace/a'
    let completeDelete!: (value: object) => void
    const deleteResponse = new Promise<object>((resolve) => {
      completeDelete = resolve
    })
    const sendRequest = vi.fn().mockImplementation((method: string) =>
      method === 'thread/delete' ? deleteResponse : Promise.resolve({})
    )

    function publishProjects(): void {
      useWorkspaceProjectsStore.getState().setPayload({
        foregroundWorkspacePath: foreground,
        secondaryLimit: 8,
        projects: projects.map((entry) => ({
          ...entry,
          state: entry.path === foreground ? 'foreground' : 'secondary'
        }))
      })
    }

    const workspaceSwitch = vi.fn().mockImplementation(async (path: string) => {
      foreground = path
      useThreadStore.getState().setThreadList(
        projects.find((entry) => entry.path === path)!.threads as ThreadSummary[],
        path
      )
      publishProjects()
    })
    installDesktopApiMock({
      settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }), set: vi.fn().mockResolvedValue({}) },
      appServer: { sendRequest },
      workspace: { switch: workspaceSwitch }
    })
    useThreadStore.getState().setThreadList([deleted, survivor], foreground)
    useThreadStore.getState().setActiveThreadId(selected ? deleted.id : survivor.id)
    publishProjects()
    render(
      <LocaleProvider>
        <ThreadList />
        <ConfirmDialogHost />
      </LocaleProvider>
    )

    fireEvent.contextMenu(screen.getByText(deleted.id), { clientX: 20, clientY: 20 })
    fireEvent.click(await screen.findByRole('menuitem', { name: 'Delete' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Delete' }))
    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('thread/delete', { threadId: deleted.id }))

    function deliverNotification(): void {
      const result = applyWorkspaceThreadNotificationToCache(
        projects[0].threads, 'thread/deleted', { threadId: deleted.id }
      )
      projects[0] = { ...projects[0], threads: result.threads, threadCount: result.threads.length }
      publishProjects()
      useThreadStore.getState().removeThreadTree(deleted.id)
    }

    if (order === 'notification first') act(deliverNotification)
    await act(async () => {
      completeDelete({})
      await deleteResponse
    })
    if (order === 'response first') act(deliverNotification)
    act(deliverNotification)

    expect(screen.queryByText(deleted.id)).not.toBeInTheDocument()
    expect(useThreadStore.getState().activeThreadId).toBe(selected ? null : survivor.id)
    fireEvent.click(screen.getByText(other.id))
    await waitFor(() => expect(workspaceSwitch).toHaveBeenCalledWith('/workspace/b'))
    expect(screen.queryByText(deleted.id)).not.toBeInTheDocument()
    fireEvent.click(screen.getByText(survivor.id))
    await waitFor(() => expect(workspaceSwitch).toHaveBeenCalledWith('/workspace/a'))
    expect(screen.queryByText(deleted.id)).not.toBeInTheDocument()
    expect(useThreadStore.getState().threadList.map((entry) => entry.id)).toEqual([survivor.id])
  })
})
