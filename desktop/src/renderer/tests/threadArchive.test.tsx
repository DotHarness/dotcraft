import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useThreadStore } from '../stores/threadStore'
import { useToastStore } from '../stores/toastStore'
import { useUIStore } from '../stores/uiStore'
import type { ThreadSummary } from '../types/thread'
import { archiveThreadWithUndo } from '../utils/threadArchive'
import { installDesktopApiMock } from './desktopApiMock'

const t = (key: string, vars?: Record<string, string | number>): string =>
  vars ? `${key}:${JSON.stringify(vars)}` : key

function thread(id: string): ThreadSummary {
  return {
    id,
    displayName: id,
    status: 'active',
    originChannel: 'desktop',
    createdAt: '2026-09-01T00:00:00Z',
    lastActiveAt: '2026-09-01T00:00:00Z'
  }
}

function listedIds(): string[] {
  return useThreadStore.getState().threadList.map((entry) => entry.id)
}

let sendRequest: ReturnType<typeof vi.fn>

beforeEach(() => {
  sendRequest = vi.fn().mockResolvedValue({})
  installDesktopApiMock({ appServer: { sendRequest } })
  useToastStore.setState({ toasts: [] })
  useThreadStore.getState().reset()
  useThreadStore.getState().setThreadList([thread('a'), thread('b')])
})

describe('archiveThreadWithUndo', () => {
  it('removes the thread, offers Undo, and restores it through thread/unarchive', async () => {
    useThreadStore.getState().setActiveThreadId('a')
    useUIStore.getState().setActiveMainView('settings')

    await expect(archiveThreadWithUndo({ threadId: 'a', t })).resolves.toBe(true)

    expect(sendRequest).toHaveBeenCalledWith('thread/archive', { threadId: 'a' })
    expect(listedIds()).toEqual(['b'])
    expect(useThreadStore.getState().activeThreadId).toBeNull()

    const [toast] = useToastStore.getState().toasts
    expect(toast.message).toBe('threadArchive.toast.archived')
    expect(toast.action?.label).toBe('common.undo')

    toast.action?.onClick()
    await vi.waitFor(() => expect(listedIds()).toContain('a'))

    expect(sendRequest).toHaveBeenCalledWith('thread/unarchive', { threadId: 'a' })
    expect(useThreadStore.getState().activeThreadId).toBe('a')
    expect(useUIStore.getState().activeMainView).toBe('conversation')
  })

  it('keeps the row and reports the error when the archive request fails', async () => {
    sendRequest.mockRejectedValueOnce(new Error('offline'))

    await expect(archiveThreadWithUndo({ threadId: 'a', t })).resolves.toBe(false)

    expect(listedIds()).toEqual(['a', 'b'])
    const [toast] = useToastStore.getState().toasts
    expect(toast.type).toBe('error')
    expect(toast.message).toBe('threadArchive.toast.archiveFailed:{"error":"offline"}')
  })

  it('reports a failed restore and leaves the thread archived', async () => {
    await archiveThreadWithUndo({ threadId: 'a', t })
    sendRequest.mockRejectedValueOnce(new Error('gone'))

    useToastStore.getState().toasts[0].action?.onClick()
    await vi.waitFor(() =>
      expect(useToastStore.getState().toasts.some((toast) => toast.type === 'error')).toBe(true)
    )

    expect(listedIds()).toEqual(['b'])
  })
})
