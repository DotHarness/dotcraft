import { beforeEach, describe, expect, it, vi } from 'vitest'
import { startPendingWelcomeTurn } from '../utils/startPendingWelcomeTurn'
import { useAppBindingStore } from '../stores/appBindingStore'
import { useComposerDraftStore } from '../stores/composerDraftStore'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useToastStore } from '../stores/toastStore'
import { installDesktopApiMock } from './desktopApiMock'

const sendRequest = vi.fn()
const openAppHandoff = vi.fn().mockResolvedValue(undefined)

const APP = {
  appId: 'com.example.workflow',
  displayName: 'Workflow App',
  developerName: 'Example Labs',
  description: 'Board tools',
  pluginId: 'workflow',
  installed: true,
  enabled: true,
  catalogVisible: true,
  connectionState: 'connected',
  handoffModes: []
}

const ACTIVE_BINDING = {
  bindingRequestId: 'request-1',
  bindingId: 'binding-1',
  threadId: 'thread-1',
  appId: 'com.example.workflow',
  state: 'active'
}

const FAILED_BINDING = { ...ACTIVE_BINDING, state: 'failed', failureReason: 'mcpStartupFailed' }

function pending(appIds: string[]): Parameters<typeof startPendingWelcomeTurn>[0] {
  return {
    threadId: 'thread-1',
    pending: {
      text: 'List my board items',
      inputParts: [{ type: 'text', text: 'List my board items' }],
      appIds
    },
    workspacePath: 'X:\\fixtures\\workspace',
    translate: (key: string) => key
  }
}

function mockAppServer(bindings: Array<Record<string, unknown>>): void {
  sendRequest.mockImplementation(async (method: string) => {
    if (method === 'app/list') return { apps: [APP] }
    if (method === 'thread/appBindings/enable') {
      return {
        bindingRequestId: 'request-1',
        bindingId: 'binding-1',
        state: 'connecting',
        expiresAt: '2026-05-16T00:01:00Z',
        handoff: { mode: 'customProtocol', uri: 'workflow://dotcraft/bind?request=request-1' }
      }
    }
    if (method === 'thread/appBindings/list') return { bindings }
    if (method === 'turn/start') return { turn: { id: 'turn-server' } }
    return {}
  })
}

describe('startPendingWelcomeTurn', () => {
  beforeEach(() => {
    sendRequest.mockReset()
    openAppHandoff.mockClear()
    installDesktopApiMock({
      appServer: { sendRequest },
      shell: { openAppHandoff, openExternal: vi.fn() }
    })
    useAppBindingStore.getState().reset()
    useConversationStore.getState().reset()
    useThreadStore.getState().reset()
    useThreadStore.setState({ activeThreadId: 'thread-1' })
    useComposerDraftStore.setState({ draftsByThread: {} })
    useToastStore.setState({ toasts: [] })
  })

  it('starts the turn only after every staged app binding is active', async () => {
    mockAppServer([ACTIVE_BINDING])

    await startPendingWelcomeTurn(pending(['com.example.workflow']))

    const methods = sendRequest.mock.calls.map(([method]) => method)
    expect(methods).toContain('thread/appBindings/enable')
    expect(methods.indexOf('turn/start')).toBeGreaterThan(methods.indexOf('thread/appBindings/enable'))
    expect(openAppHandoff).toHaveBeenCalledWith('workflow://dotcraft/bind?request=request-1')
    expect(useConversationStore.getState().turns.map((turn) => turn.id)).toEqual(['turn-server'])
    expect(useConversationStore.getState().systemLabel).toBeNull()
  })

  it('returns the submission to the thread composer when a staged app fails to activate', async () => {
    mockAppServer([FAILED_BINDING])

    await startPendingWelcomeTurn(pending(['com.example.workflow']))

    const methods = sendRequest.mock.calls.map(([method]) => method)
    expect(methods).toContain('thread/appBindings/revoke')
    expect(methods).not.toContain('turn/start')
    expect(methods).not.toContain('thread/delete')
    expect(useConversationStore.getState().turns).toEqual([])
    expect(useConversationStore.getState().systemLabel).toBeNull()
    expect(useComposerDraftStore.getState().getDraft('thread-1')?.text).toBe('List my board items')
    expect(useToastStore.getState().toasts.filter((toast) => toast.type === 'error')).toHaveLength(1)
  })

  it('abandons the wait without starting a turn when the user leaves the thread', async () => {
    mockAppServer([{ ...ACTIVE_BINDING, state: 'connecting' }])
    const controller = new AbortController()

    const started = startPendingWelcomeTurn({ ...pending(['com.example.workflow']), signal: controller.signal })
    await vi.waitFor(() => expect(useConversationStore.getState().systemLabel).toBe('systemStatus.connectingApps'))
    useThreadStore.setState({ activeThreadId: 'thread-2' })
    controller.abort()
    await started

    expect(sendRequest.mock.calls.map(([method]) => method)).not.toContain('turn/start')
    expect(useComposerDraftStore.getState().getDraft('thread-1')?.text).toBe('List my board items')
    expect(useToastStore.getState().toasts.filter((toast) => toast.type === 'error')).toEqual([])
  })

  it('starts the turn without an on-screen echo when the user is on another thread', async () => {
    mockAppServer([])
    useThreadStore.setState({ activeThreadId: 'thread-2' })

    await startPendingWelcomeTurn(pending([]))

    expect(sendRequest.mock.calls.map(([method]) => method)).toContain('turn/start')
    expect(useConversationStore.getState().turns).toEqual([])
    expect(useConversationStore.getState().systemLabel).toBeNull()
  })

  it('starts the turn directly when no app is staged', async () => {
    mockAppServer([])

    await startPendingWelcomeTurn(pending([]))

    const methods = sendRequest.mock.calls.map(([method]) => method)
    expect(methods).toContain('turn/start')
    expect(methods).not.toContain('thread/appBindings/enable')
  })
})
