import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useThreadStore } from '../stores/threadStore'
import { useToastStore } from '../stores/toastStore'
import { useUIStore } from '../stores/uiStore'
import type { ThreadSummary } from '../types/thread'
import { archiveThreadWithUndo } from '../utils/threadArchive'
import { installDesktopApiMock } from './desktopApiMock'
import { requestConfirmDialog } from '../components/ui/ConfirmDialog'
import { archiveWorkspaceThread } from '../utils/archiveWorkspaceThread'

vi.mock('../components/ui/ConfirmDialog', () => ({ requestConfirmDialog: vi.fn() }))

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
  vi.mocked(requestConfirmDialog).mockReset()
  sendRequest = vi.fn().mockResolvedValue({})
  installDesktopApiMock({ appServer: { sendRequest } })
  useToastStore.setState({ toasts: [] })
  useThreadStore.getState().reset()
  useThreadStore.getState().setThreadList([thread('a'), thread('b')])
})

describe('archiveThreadWithUndo', () => {
  function setRunning(): void {
    useThreadStore.getState().setThreadList([{ ...thread('a'), runtime: {
      running: true, activeTurnId: 'turn-a', waitingOnApproval: false, waitingOnPlanConfirmation: false
    } }, thread('b')])
  }

  it('leaves ongoing work untouched when confirmation is cancelled', async () => {
    setRunning()
    vi.mocked(requestConfirmDialog).mockReturnValue({ result: Promise.resolve(false), dismiss: vi.fn() })
    expect(await archiveThreadWithUndo({ threadId: 'a', t })).toBe(false)
    expect(sendRequest).not.toHaveBeenCalled()
    expect(listedIds()).toEqual(['a', 'b'])
  })

  it('waits for confirmation and interrupts before archive without duplicate requests', async () => {
    setRunning()
    let confirm!: (value: boolean) => void
    vi.mocked(requestConfirmDialog).mockReturnValue({ result: new Promise(resolve => { confirm = resolve }), dismiss: vi.fn() })
    sendRequest.mockImplementation(async (method: string) => method === 'thread/read'
      ? { thread: { runtime: { activeTurnId: 'turn-a' } } } : {})
    const pending = archiveThreadWithUndo({ threadId: 'a', t })
    expect(sendRequest).not.toHaveBeenCalled()
    expect(await archiveThreadWithUndo({ threadId: 'a', t })).toBe(false)
    confirm(true)
    expect(await pending).toBe(true)
    expect(requestConfirmDialog).toHaveBeenCalledOnce()
    expect(sendRequest.mock.calls.map(call => call[0])).toEqual(['thread/read', 'turn/interrupt', 'thread/archive'])
    expect(sendRequest).toHaveBeenCalledWith('turn/interrupt', { threadId: 'a', turnId: 'turn-a' })
    useToastStore.getState().toasts[0].action?.onClick()
    await vi.waitFor(() => expect(listedIds()).toContain('a'))
    expect(useThreadStore.getState().threadList.find(thread => thread.id === 'a')?.runtime)
      .toMatchObject({ running: false, activeTurnId: null })
  })

  it('does not archive when interruption fails', async () => {
    setRunning()
    vi.mocked(requestConfirmDialog).mockReturnValue({ result: Promise.resolve(true), dismiss: vi.fn() })
    sendRequest.mockResolvedValueOnce({ thread: { runtime: { activeTurnId: 'turn-a' } } })
      .mockRejectedValueOnce(new Error('offline'))
    expect(await archiveThreadWithUndo({ threadId: 'a', t })).toBe(false)
    expect(sendRequest).not.toHaveBeenCalledWith('thread/archive', expect.anything())
    expect(listedIds()).toEqual(['a', 'b'])
  })

  it('requires confirmation before routing a secondary workspace archive', async () => {
    setRunning()
    const archiveThread = vi.fn().mockResolvedValue(undefined)
    installDesktopApiMock({ workspace: { archiveThread } })
    const target = useThreadStore.getState().threadList[0]
    vi.mocked(requestConfirmDialog).mockReturnValueOnce({ result: Promise.resolve(false), dismiss: vi.fn() })
    expect(await archiveWorkspaceThread('/workspace/secondary', target, t)).toBe(false)
    expect(archiveThread).not.toHaveBeenCalled()
    vi.mocked(requestConfirmDialog).mockReturnValueOnce({ result: Promise.resolve(true), dismiss: vi.fn() })
    expect(await archiveWorkspaceThread('/workspace/secondary', target, t)).toBe(true)
    expect(archiveThread).toHaveBeenCalledWith('/workspace/secondary', 'a')
  })

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
