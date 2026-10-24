import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { PlanApprovalComposer } from '../components/conversation/PlanApprovalComposer'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { installDesktopApiMock } from './desktopApiMock'
import { useActiveThreadModeSync } from '../hooks/useActiveThreadModeSync'

const appServerSendRequest = vi.fn()

function ModeSync(): null {
  useActiveThreadModeSync('fixture-workspace')
  return null
}

function renderWithLocale(node: JSX.Element): void {
  render(<LocaleProvider><ModeSync />{node}</LocaleProvider>)
}

describe('PlanApprovalComposer', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useConversationStore.getState().reset()
    useConnectionStore.getState().reset()
    useThreadStore.getState().reset()
    useUIStore.setState({ planApprovalDismissed: {} })
    useConversationStore.setState({ threadMode: 'plan', turnStatus: 'idle' })
    useThreadStore.setState({
      activeThreadId: 'thread-1',
      threadList: [{
        id: 'thread-1',
        displayName: null,
        status: 'active',
        originChannel: 'dotcraft',
        createdAt: new Date().toISOString(),
        lastActiveAt: new Date().toISOString()
      }]
    })
    appServerSendRequest.mockImplementation((method: string) => {
      if (method === 'turn/start') {
        return Promise.resolve({ turn: { id: 'turn-server-1' } })
      }
      return Promise.resolve({})
    })
    installDesktopApiMock({
        settings: { get: async () => ({ locale: 'en' }) },
        appServer: { sendRequest: appServerSendRequest },
        file: { readFile: vi.fn().mockResolvedValue('{}') },
        shell: { listOpenTargets: vi.fn().mockResolvedValue([]) },
        workspace: { saveImageToTemp: vi.fn() }
      })
  })

  it('accept path switches to agent and shows the plan acceptance request', async () => {
    renderWithLocale(
      <PlanApprovalComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" turnId="turn-1" />
    )

    fireEvent.click(screen.getByRole('button', { name: /Yes, implement this plan/ }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/mode/set', {
        threadId: 'thread-1',
        mode: 'agent'
      })
    })
    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'turn/start',
        expect.objectContaining({
          threadId: 'thread-1',
          input: [{ type: 'text', text: 'Implement the plan.' }]
        })
      )
    })
    expect(useConversationStore.getState().threadMode).toBe('agent')
    expect(useUIStore.getState().planApprovalDismissed['turn-1']).toBe(true)
    expect(useConversationStore.getState().turns).toEqual([
      expect.objectContaining({
        id: 'turn-server-1',
        items: [expect.objectContaining({ type: 'userMessage', text: 'Implement the plan.' })]
      })
    ])
  })

  it('does not start implementation or dismiss confirmation when switching mode fails', async () => {
    appServerSendRequest.mockRejectedValueOnce(new Error('mode failed'))
    renderWithLocale(
      <PlanApprovalComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" turnId="turn-1" />
    )
    fireEvent.click(screen.getByRole('button', { name: /Yes, implement this plan/ }))
    await waitFor(() => expect(useConversationStore.getState().threadMode).toBe('plan'))
    expect(appServerSendRequest).not.toHaveBeenCalledWith('turn/start', expect.anything())
    expect(useUIStore.getState().planApprovalDismissed['turn-1']).not.toBe(true)
  })

  it('does not start an old plan after the selected thread changes during mode switching', async () => {
    let resolveMode!: (value: unknown) => void
    appServerSendRequest.mockImplementationOnce(() => new Promise((resolve) => { resolveMode = resolve }))
    renderWithLocale(
      <PlanApprovalComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" turnId="turn-1" />
    )
    fireEvent.click(screen.getByRole('button', { name: /Yes, implement this plan/ }))
    act(() => useThreadStore.getState().setActiveThreadId('thread-2'))
    await act(async () => { resolveMode({}) })
    expect(appServerSendRequest).not.toHaveBeenCalledWith('turn/start', expect.anything())
    expect(useUIStore.getState().planApprovalDismissed['turn-1']).not.toBe(true)
  })

  it('does not implement a plan when a newer external mode supersedes acceptance', async () => {
    const activeThread = {
      ...useThreadStore.getState().threadList[0],
      workspacePath: '/fixture', configuration: { mode: 'plan' }, turns: []
    }
    useThreadStore.getState().setActiveThread(activeThread)
    let resolveMode!: (value: unknown) => void
    appServerSendRequest.mockImplementationOnce(() => new Promise((resolve) => { resolveMode = resolve }))
    renderWithLocale(
      <PlanApprovalComposer threadId="thread-1" workspacePath="/fixture" turnId="turn-1" />
    )
    fireEvent.click(screen.getByRole('button', { name: /Yes, implement this plan/ }))
    act(() => {
      useThreadStore.getState().setActiveThread({ ...activeThread, configuration: { mode: 'agent' } })
      useThreadStore.getState().setActiveThread({ ...activeThread, configuration: { mode: 'plan' } })
    })

    await act(async () => { resolveMode({}) })

    expect(useConversationStore.getState().threadMode).toBe('plan')
    expect(appServerSendRequest).not.toHaveBeenCalledWith('turn/start', expect.anything())
    expect(useUIStore.getState().planApprovalDismissed['turn-1']).not.toBe(true)
  })

  it('reject path keeps plan mode and sends typed text', async () => {
    renderWithLocale(
      <PlanApprovalComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" turnId="turn-2" />
    )

    const textbox = screen.getByRole('textbox')
    textbox.textContent = 'Please split this into two milestones.'
    fireEvent.input(textbox)
    fireEvent.click(screen.getByRole('button', { name: 'Submit' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'turn/start',
        expect.objectContaining({
          threadId: 'thread-1',
          input: [{ type: 'text', text: 'Please split this into two milestones.' }]
        })
      )
    })
    expect(useConversationStore.getState().threadMode).toBe('plan')
    expect(useUIStore.getState().planApprovalDismissed['turn-2']).toBe(true)
  })

  it('uses latest thread, workspace, and turn ids after rerender when accepting via keyboard', async () => {
    const initialThreadId = 'thread-1'
    const initialWorkspacePath = 'X:\\fixtures\\workspace'
    const initialTurnId = 'turn-1'
    const nextThreadId = 'thread-2'
    const nextWorkspacePath = 'F:\\another-workspace'
    const nextTurnId = 'turn-2'

    const { rerender } = render(
      <LocaleProvider>
        <PlanApprovalComposer
          threadId={initialThreadId}
          workspacePath={initialWorkspacePath}
          turnId={initialTurnId}
        />
      </LocaleProvider>
    )

    act(() => useThreadStore.getState().setActiveThreadId(nextThreadId))
    rerender(
      <LocaleProvider>
        <PlanApprovalComposer
          threadId={nextThreadId}
          workspacePath={nextWorkspacePath}
          turnId={nextTurnId}
        />
      </LocaleProvider>
    )

    fireEvent.keyDown(window, { key: '1' })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/mode/set', {
        threadId: nextThreadId,
        mode: 'agent'
      })
    })
    await waitFor(() => {
      const turnStartCall = appServerSendRequest.mock.calls.find(([method]) => method === 'turn/start')
      expect(turnStartCall).toBeDefined()
      const payload = turnStartCall?.[1] as { threadId: string }
      expect(payload.threadId).toBe(nextThreadId)
    })
    expect(useUIStore.getState().planApprovalDismissed[nextTurnId]).toBe(true)
    expect(useUIStore.getState().planApprovalDismissed[initialTurnId]).not.toBe(true)
  })
})
