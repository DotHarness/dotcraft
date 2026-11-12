import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { requestThreadModeChange, useActiveThreadModeSync } from '../hooks/useActiveThreadModeSync'
import { useComposerDraftStore } from '../stores/composerDraftStore'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { openThreadState, releaseThreadState } from '../stores/threadState'
import { useUIStore } from '../stores/uiStore'
import type { Thread } from '../types/thread'
import { installDesktopApiMock } from './desktopApiMock'
import { applyThreadNotification } from '../stores/threadNotifications'

const sendRequest = vi.fn()

function thread(mode?: string, id = 'thread-1'): Thread {
  return {
    id, userId: 'local', workspacePath: 'workspace-1', displayName: 'Thread',
    status: 'active', originChannel: 'dotcraft-desktop', metadata: {},
    createdAt: '2026-10-24T00:00:00Z', lastActiveAt: '2026-10-24T00:00:00Z',
    configuration: mode ? { mode } : {}, turns: []
  }
}

function selectThread(mode?: string, id = 'thread-1'): void {
  openThreadState(id)
  useThreadStore.getState().setActiveThreadId(id)
  useThreadStore.getState().setActiveThread(thread(mode, id))
}

function notifyMode(mode: string, threadId = 'thread-1'): void {
  applyThreadNotification({ method: 'thread/settings/updated', params: { threadId, mode } }, {
    locale: 'en', foreground: true,
    updateWarmThread() {}, updateWarmThreadMode() {}, releaseWarmThread() {},
    reloadThreadList: async () => {}
  })
}

function deferred() {
  let resolve!: (value: unknown) => void
  let reject!: (error: Error) => void
  const promise = new Promise((res, rej) => { resolve = res; reject = rej })
  return { promise, resolve, reject }
}

beforeEach(() => {
  sendRequest.mockReset().mockResolvedValue({})
  installDesktopApiMock({
    settings: { get: async () => ({ locale: 'en' }) },
    appServer: { sendRequest },
    file: { readFile: vi.fn().mockResolvedValue('{}') },
    shell: { listOpenTargets: vi.fn().mockResolvedValue([]) },
    workspace: { saveImageToTemp: vi.fn() }
  })
  useThreadStore.getState().reset()
  useConversationStore.getState().reset()
  releaseThreadState('thread-1')
  releaseThreadState('thread-2')
  useUIStore.setState({ planApprovalDismissed: {}, pendingThreadCreation: null })
  useComposerDraftStore.setState({ draftsByThread: {} })
  selectThread('agent')
})

describe('active thread mode synchronization', () => {
  it('applies confirmed settings without reloading the thread or discarding its plan', () => {
    const plan = { title: 'Plan', overview: '', content: 'Implement it', todos: [] }
    useConversationStore.getState().onPlanUpdated(plan)
    renderHook(() => useActiveThreadModeSync('workspace-1'))

    act(() => notifyMode('plan'))

    expect(useConversationStore.getState().threadMode).toBe('plan')
    expect(useThreadStore.getState().activeThread?.configuration?.mode).toBe('plan')
    expect(useConversationStore.getState().plan).toEqual(plan)
    expect(useThreadStore.getState().activeThreadId).toBe('thread-1')
    expect(sendRequest).not.toHaveBeenCalled()

    act(() => notifyMode('agent'))
    expect(useConversationStore.getState().threadMode).toBe('agent')
  })

  it('does not let a pending request overwrite a same-value authoritative notification', async () => {
    const response = deferred()
    sendRequest.mockReturnValue(response.promise)
    renderHook(() => useActiveThreadModeSync('workspace-1'))
    let request!: Promise<boolean>
    act(() => { request = requestThreadModeChange('thread-1', 'plan') })
    act(() => notifyMode('agent'))

    expect(useConversationStore.getState().threadMode).toBe('agent')
    await act(async () => { response.resolve({}); expect(await request).toBe(false) })
    expect(useConversationStore.getState().threadMode).toBe('agent')
  })

  it('initializes explicit mode and preserves drafts and dismissed confirmations during metadata refresh', () => {
    selectThread('plan')
    useComposerDraftStore.getState().saveDraft('thread-1', {
      text: 'unsent', segments: [], images: [], files: []
    })
    useUIStore.setState({ planApprovalDismissed: { 'plan-turn': true } })
    renderHook(() => useActiveThreadModeSync('workspace-1'))
    expect(useConversationStore.getState().threadMode).toBe('plan')

    act(() => useThreadStore.getState().setActiveThread(thread()))
    expect(useConversationStore.getState().threadMode).toBe('plan')
    act(() => useThreadStore.getState().setActiveThread(thread('agent')))
    expect(useConversationStore.getState().threadMode).toBe('agent')
    expect(useComposerDraftStore.getState().getDraft('thread-1')?.text).toBe('unsent')
    expect(useUIStore.getState().planApprovalDismissed['plan-turn']).toBe(true)
  })

  it('ignores metadata for another thread and does not infer mode from runtime flags', () => {
    renderHook(() => useActiveThreadModeSync('workspace-1'))
    act(() => useThreadStore.getState().setActiveThread(thread('plan', 'thread-2')))
    expect(useConversationStore.getState().threadMode).toBe('agent')
    act(() => useThreadStore.getState().setActiveThread({
      ...thread(),
      runtime: { running: false, waitingOnApproval: false, waitingOnPlanConfirmation: true }
    }))
    expect(useConversationStore.getState().threadMode).toBe('agent')
  })

  it('keeps an optimistic request across same-value metadata refresh and restores confirmed mode on failure', async () => {
    const response = deferred()
    sendRequest.mockReturnValue(response.promise)
    renderHook(() => useActiveThreadModeSync('workspace-1'))
    let request!: Promise<boolean>
    act(() => { request = requestThreadModeChange('thread-1', 'plan') })
    expect(useConversationStore.getState().threadMode).toBe('plan')
    act(() => useThreadStore.getState().setActiveThread({ ...thread('agent'), displayName: 'Renamed' }))
    expect(useConversationStore.getState().threadMode).toBe('plan')
    const failure = expect(request).rejects.toThrow('mode failed')
    await act(async () => { response.reject(new Error('mode failed')); await failure })
    expect(useConversationStore.getState().threadMode).toBe('agent')
  })

  it('reconciles newer external metadata when a pending request succeeds', async () => {
    selectThread('plan')
    const response = deferred()
    sendRequest.mockReturnValue(response.promise)
    renderHook(() => useActiveThreadModeSync('workspace-1'))
    let request!: Promise<boolean>
    act(() => { request = requestThreadModeChange('thread-1', 'agent') })
    act(() => useThreadStore.getState().setActiveThread(thread('agent')))
    act(() => useThreadStore.getState().setActiveThread(thread('plan')))

    await act(async () => { response.resolve({}); expect(await request).toBe(false) })

    expect(useConversationStore.getState().threadMode).toBe('plan')
    act(() => useThreadStore.getState().setActiveThread(thread('plan')))
    expect(useConversationStore.getState().threadMode).toBe('plan')
  })

  it('confirms a successful optimistic change despite unchanged pre-request metadata', async () => {
    selectThread('plan')
    const response = deferred()
    sendRequest.mockReturnValue(response.promise)
    renderHook(() => useActiveThreadModeSync('workspace-1'))
    let request!: Promise<boolean>
    act(() => { request = requestThreadModeChange('thread-1', 'agent') })
    act(() => useThreadStore.getState().setActiveThread(thread('plan')))

    await act(async () => { response.resolve({}); expect(await request).toBe(true) })

    expect(useConversationStore.getState().threadMode).toBe('agent')
  })

  it('recovers the latest confirmed metadata rather than the mode at request start', async () => {
    const response = deferred()
    sendRequest.mockReturnValue(response.promise)
    renderHook(() => useActiveThreadModeSync('workspace-1'))
    let request!: Promise<boolean>
    act(() => { request = requestThreadModeChange('thread-1', 'plan') })
    act(() => useThreadStore.getState().setActiveThread(thread('plan')))
    const failure = expect(request).rejects.toThrow('mode failed')
    await act(async () => { response.reject(new Error('mode failed')); await failure })
    expect(useConversationStore.getState().threadMode).toBe('plan')
  })

  it('does not let a failed old-thread request roll back the next selected thread', async () => {
    const response = deferred()
    sendRequest.mockReturnValue(response.promise)
    renderHook(() => useActiveThreadModeSync('workspace-1'))
    let request!: Promise<boolean>
    act(() => { request = requestThreadModeChange('thread-1', 'plan') })
    act(() => selectThread('plan', 'thread-2'))
    const failure = expect(request).rejects.toThrow('mode failed')
    await act(async () => { response.reject(new Error('mode failed')); await failure })
    expect(useConversationStore.getState().threadMode).toBe('plan')
  })

  it('isolates requests across workspaces even when thread ids coincide', async () => {
    const response = deferred()
    sendRequest.mockReturnValue(response.promise)
    const { rerender } = renderHook(({ workspace }) => useActiveThreadModeSync(workspace), {
      initialProps: { workspace: 'workspace-1' }
    })
    let request!: Promise<boolean>
    act(() => { request = requestThreadModeChange('thread-1', 'plan') })
    rerender({ workspace: 'workspace-2' })
    act(() => useThreadStore.getState().setActiveThread({
      ...thread('plan'), workspacePath: 'workspace-2'
    }))
    const failure = expect(request).rejects.toThrow('mode failed')
    await act(async () => { response.reject(new Error('mode failed')); await failure })
    expect(useConversationStore.getState().threadMode).toBe('plan')
  })

  it('does not confirm an old successful request after leaving and returning to the same thread', async () => {
    const response = deferred()
    sendRequest.mockReturnValue(response.promise)
    renderHook(() => useActiveThreadModeSync('workspace-1'))
    let request!: Promise<boolean>
    act(() => { request = requestThreadModeChange('thread-1', 'plan') })
    act(() => { selectThread('agent', 'thread-2'); selectThread('agent') })
    await act(async () => { response.resolve({}); expect(await request).toBe(false) })
    expect(useConversationStore.getState().threadMode).toBe('agent')
  })
})
