import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { InputComposer } from '../components/conversation/InputComposer'
import { COMMAND_REF_CLASS } from '../components/conversation/richInputConstants'
import { ConfirmDialogHost } from '../components/ui/ConfirmDialog'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useToastStore } from '../stores/toastStore'
import { useComposerDraftStore } from '../stores/composerDraftStore'
import type { ThreadGoal } from '../types/thread'
import type { ConversationTurn } from '../types/conversation'
import { installDesktopApiMock } from './desktopApiMock'

const settingsGet = vi.fn()
const appServerSendRequest = vi.fn()
const saveImageToTemp = vi.fn()
const getPathForFile = vi.fn((file: File) => file.name === 'notes.txt' ? 'C:\\temp\\notes.txt' : '')

class ResizeObserverMock {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
}

Object.defineProperty(globalThis, 'ResizeObserver', {
  configurable: true,
  writable: true,
  value: ResizeObserverMock
})

function renderWithLocale(node: JSX.Element): void {
  render(<LocaleProvider>{node}</LocaleProvider>)
}

function renderWithLocaleAndConfirm(node: JSX.Element): void {
  render(<LocaleProvider><ConfirmDialogHost />{node}</LocaleProvider>)
}

function makeGoal(threadId = 'thread-1', objective = 'Existing goal'): ThreadGoal {
  return {
    threadId,
    objective,
    status: 'active',
    tokenBudget: null,
    tokensUsed: 0,
    timeUsedSeconds: 0,
    createdAt: 1704067200,
    updatedAt: 1704067200
  }
}

function setCaretToEnd(element: HTMLElement): void {
  const selection = window.getSelection()
  if (!selection) return
  const range = document.createRange()
  range.selectNodeContents(element)
  range.collapse(false)
  selection.removeAllRanges()
  selection.addRange(range)
}

describe('InputComposer custom command expansion', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ locale: 'en' })
    saveImageToTemp.mockResolvedValue({ path: 'C:\\temp\\image.png' })
    getPathForFile.mockImplementation((file: File) => file.name === 'notes.txt' ? 'C:\\temp\\notes.txt' : '')
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'command/list') {
        return {
          commands: [
            {
              name: '/code-review',
              aliases: ['/cr'],
              description: 'Review files',
              category: 'custom',
              requiresAdmin: false
            }
          ]
        }
      }
      if (method === 'skills/list') {
        return {
          skills: [
            {
              name: 'memory',
              description: 'Recall project context',
              source: 'builtin',
              available: true,
              enabled: true,
              path: '/skills/memory/SKILL.md'
            }
          ]
        }
      }
      if (method === 'turn/start') {
        return { turn: { id: 'turn-1' } }
      }
      if (method === 'command/execute') {
        return { handled: true, expandedPrompt: 'Generate AGENTS.md' }
      }
      return {}
    })

    installDesktopApiMock({
        settings: { get: settingsGet },
        appServer: { sendRequest: appServerSendRequest },
        workspace: { saveImageToTemp, getPathForFile },
        voice: undefined
      })

    useConversationStore.getState().reset()
    useConversationStore.setState({ remoteWorkspaceActive: false })
    useConnectionStore.getState().reset()
    useThreadStore.getState().reset()
    useToastStore.setState({ toasts: [] })
    useComposerDraftStore.setState({ draftsByThread: {} })
    useUIStore.setState({
      activeMainView: 'conversation',
      automationsTab: 'tasks',
      sidebarCollapsed: false,
      sidebarWidth: 240,
      detailPanelVisible: true,
      detailPanelWidth: 400,
      activeDetailTab: 'changes',
      selectedChangedFile: null,
      autoShowTriggeredForTurn: null,
      autoShowPlanForItem: null,
      composerPrefill: null,
      pendingWelcomeTurn: null,
      _pendingWelcomeTimer: null
    })
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        commandManagement: true,
        skillsManagement: true
      }
    })
    useThreadStore.setState({
      threadList: [
        {
          id: 'thread-1',
          displayName: null,
          status: 'active',
          originChannel: 'appserver',
          createdAt: new Date().toISOString(),
          lastActiveAt: new Date().toISOString()
        }
      ]
    })
  })

  it('sends custom commands as native commandRef parts', async () => {
    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />)

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('command/list', {})
    })

    const textbox = screen.getByRole('textbox')
    textbox.textContent = '/code-review'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      const turnStartCall = appServerSendRequest.mock.calls.find((call) => call[0] === 'turn/start')
      expect(turnStartCall).toBeDefined()
      expect(turnStartCall?.[1]).toEqual(
        expect.objectContaining({
          threadId: 'thread-1',
          input: [{ type: 'text', text: '/code-review' }]
        })
      )
    })
  })

  it('prevents duplicate send while turn/start is in flight', async () => {
    let resolveTurnStart: ((value: unknown) => void) | null = null
    appServerSendRequest.mockImplementation((method: string) => {
      if (method === 'command/list') {
        return Promise.resolve({
          commands: [
            {
              name: '/code-review',
              aliases: ['/cr'],
              description: 'Review files',
              category: 'custom',
              requiresAdmin: false
            }
          ]
        })
      }
      if (method === 'turn/start') {
        return new Promise((resolve) => {
          resolveTurnStart = resolve
        })
      }
      return Promise.resolve({})
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />)

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('command/list', {})
    })

    const textbox = screen.getByRole('textbox')
    textbox.textContent = '/code-review'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })
    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      const turnStartCalls = appServerSendRequest.mock.calls.filter((call) => call[0] === 'turn/start')
      expect(turnStartCalls).toHaveLength(1)
    })

    resolveTurnStart?.({ turn: { id: 'turn-1' } })

    await waitFor(() => {
      const turnStartCalls = appServerSendRequest.mock.calls.filter((call) => call[0] === 'turn/start')
      expect(turnStartCalls).toHaveLength(1)
    })
  })

  it('shows Init only when command/list advertises it', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'command/list') {
        return {
          commands: [{
            name: '/init',
            aliases: [],
            description: 'Create an AGENTS.md file with instructions for DotCraft',
            category: 'builtin',
            requiresAdmin: false
          }]
        }
      }
      if (method === 'skills/list') return { skills: [] }
      return {}
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="/workspace/demo" />)
    await waitFor(() => expect(appServerSendRequest).toHaveBeenCalledWith('command/list', {}))

    const textbox = screen.getByRole('textbox')
    fireEvent.focus(textbox)
    textbox.textContent = '/'
    setCaretToEnd(textbox)
    fireEvent.input(textbox)

    expect(await screen.findByText('Create an AGENTS.md file with instructions for DotCraft')).toBeInTheDocument()
  })

  it('hides Init when command/list omits it', async () => {
    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="/workspace/demo" />)
    await waitFor(() => expect(appServerSendRequest).toHaveBeenCalledWith('command/list', {}))

    const textbox = screen.getByRole('textbox')
    fireEvent.focus(textbox)
    textbox.textContent = '/'
    setCaretToEnd(textbox)
    fireEvent.input(textbox)

    expect(screen.queryByText('Create an AGENTS.md file with instructions for DotCraft')).not.toBeInTheDocument()
  })

  it('executes /init through the server before starting the agent turn', async () => {
    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="/workspace/project" />)

    const textbox = screen.getByRole('textbox')
    textbox.textContent = '/init'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('command/execute', {
        threadId: 'thread-1',
        command: '/init',
        arguments: []
      })
      expect(appServerSendRequest).toHaveBeenCalledWith('turn/start', expect.objectContaining({
        threadId: 'thread-1',
        input: [{ type: 'text', text: 'Generate AGENTS.md' }]
      }))
    })
  })

  it('inserts skill via slash and serializes skillRef input', async () => {
    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />)

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('skills/list', { includeUnavailable: true })
    })

    const textbox = screen.getByRole('textbox')
    fireEvent.focus(textbox)
    textbox.textContent = '/memory'
    setCaretToEnd(textbox)
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })
    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      const turnStartCall = appServerSendRequest.mock.calls.find((call) => call[0] === 'turn/start')
      expect(turnStartCall).toBeDefined()
      expect(turnStartCall?.[1]).toEqual(
        expect.objectContaining({
          threadId: 'thread-1',
          input: [
            { type: 'skillRef', name: 'memory' },
            { type: 'text', text: '\u00a0' }
          ]
        })
      )
    })
  })

  it('serializes dropped file attachments into fileRef parts on turn/start', async () => {
    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />)

    const textbox = screen.getByRole('textbox')
    textbox.textContent = 'Review this file'
    fireEvent.input(textbox)
    const surface = textbox.closest('div[style*="border-radius: 20px"]') as HTMLElement
    const note = new File(['notes'], 'notes.txt', { type: 'text/plain' })
    fireEvent.drop(surface, {
      dataTransfer: {
        files: [note],
        items: [{
          kind: 'file',
          getAsFile: () => note,
          webkitGetAsEntry: () => ({ isDirectory: false })
        }]
      }
    })

    expect(await screen.findByText('notes.txt')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => {
      const turnStartCall = appServerSendRequest.mock.calls.find((call) => call[0] === 'turn/start')
      expect(turnStartCall).toBeDefined()
      expect(turnStartCall?.[1]).toEqual(
        expect.objectContaining({
          threadId: 'thread-1',
          input: [
            { type: 'fileRef', path: 'C:\\temp\\notes.txt', displayPath: 'C:\\temp\\notes.txt' },
            { type: 'text', text: '\n\n' },
            { type: 'text', text: 'Review this file' }
          ]
        })
      )
    })
  })

  it('opens the command picker in remote mode without exposing local attachment actions', async () => {
    renderWithLocale(
      <InputComposer
        threadId="thread-1"
        workspacePath="/workspace"
        remoteWorkspace
      />
    )

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('command/list', {})
      expect(appServerSendRequest).toHaveBeenCalledWith('skills/list', { includeUnavailable: true })
    })

    fireEvent.click(screen.getByRole('button', { name: 'Open commands' }))

    expect(screen.getByRole('listbox')).toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: 'Reference file' })).toBeNull()
    expect(screen.queryByText('notes.txt')).not.toBeInTheDocument()
  })

  it('shows the Goal system action above commands when thread goals are supported', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        commandManagement: true,
        skillsManagement: true,
        threadGoals: true
      }
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />)

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('command/list', {})
    })

    const textbox = screen.getByRole('textbox')
    fireEvent.focus(textbox)
    textbox.textContent = '/'
    setCaretToEnd(textbox)
    fireEvent.input(textbox)

    const goalOption = await screen.findByRole('option', { name: /goal/i })
    const commandHeader = await screen.findByText('Commands')
    expect(screen.queryByText('System')).toBeNull()
    expect(goalOption.compareDocumentPosition(commandHeader) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()

    fireEvent.click(screen.getByRole('option', { name: /goal/i }))

    // With no current goal, selecting Goal enters compose mode (the next message
    // becomes the thread goal) instead of opening a separate dialog.
    expect(await screen.findByRole('button', { name: 'Exit goal mode' })).toBeInTheDocument()
    expect(screen.getAllByRole('textbox').length).toBeGreaterThan(0)
  })

  it('shows the Goal system action in Chinese when thread goals are supported', async () => {
    settingsGet.mockResolvedValue({ locale: 'zh-Hans' })
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        threadGoals: true
      }
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />)

    const textbox = screen.getByRole('textbox')
    fireEvent.focus(textbox)
    textbox.textContent = '/'
    setCaretToEnd(textbox)
    fireEvent.input(textbox)

    fireEvent.click(await screen.findByRole('option', { name: /目标/ }))

    expect(await screen.findByRole('button', { name: '退出目标模式' })).toBeInTheDocument()
  })

  it('shows plan mode system action and toggles mode without starting a turn', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {}
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />)

    const textbox = screen.getByRole('textbox')
    fireEvent.focus(textbox)
    textbox.textContent = '/'
    setCaretToEnd(textbox)
    fireEvent.input(textbox)

    expect(await screen.findByText('Enable plan mode')).toBeInTheDocument()
    fireEvent.click(await screen.findByRole('option', { name: /plan/i }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/mode/set', {
        threadId: 'thread-1',
        mode: 'plan'
      })
    })
    expect(textbox.textContent?.trim()).toBe('')
    expect(appServerSendRequest).not.toHaveBeenCalledWith('turn/start', expect.anything())
  })

  it('handles /agent locally without starting a turn', async () => {
    useConversationStore.setState({ threadMode: 'plan' })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />)

    const textbox = screen.getByRole('textbox')
    textbox.textContent = '/agent'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/mode/set', {
        threadId: 'thread-1',
        mode: 'agent'
      })
    })
    expect(appServerSendRequest).not.toHaveBeenCalledWith('turn/start', expect.anything())
  })

  it('shows compact system action only for idle threads with history and calls manual compaction', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        manualCompaction: true
      }
    })
    useConversationStore.setState({
      turnStatus: 'idle',
      turns: [{
        id: 'turn_001',
        threadId: 'thread-1',
        status: 'completed',
        items: [],
        startedAt: '2026-05-08T00:00:00Z',
        completedAt: '2026-05-08T00:00:01Z'
      }] as ConversationTurn[]
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/compact/start') {
        return {
          outcome: 'partial',
          contextUsage: {
            tokens: 100,
            contextWindow: 1000,
            autoCompactThreshold: 800,
            warningThreshold: 700,
            errorThreshold: 750,
            percentLeft: 0.9
          }
        }
      }
      return {}
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />)

    const textbox = screen.getByRole('textbox')
    fireEvent.focus(textbox)
    textbox.textContent = '/'
    setCaretToEnd(textbox)
    fireEvent.input(textbox)

    expect(await screen.findByText("Compact this session's context")).toBeInTheDocument()
    fireEvent.click(await screen.findByRole('option', { name: /Compact/i }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'thread/compact/start',
        { threadId: 'thread-1' },
        300_000
      )
    })
    expect(useConversationStore.getState().contextUsage?.tokens).toBe(100)
  })

  it('preserves compact usage and replaces history cursors when the post-compact header omits usage', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        manualCompaction: true
      }
    })
    useThreadStore.setState({
      activeThreadId: 'thread-1',
      activeHistoryCursors: {
        threadId: 'thread-1',
        turnCursor: 'old-turn-cursor'
      }
    })
    useConversationStore.setState({
      turnStatus: 'idle',
      turns: [{
        id: 'turn_001',
        threadId: 'thread-1',
        status: 'completed',
        items: [],
        startedAt: '2026-05-08T00:00:00Z',
        completedAt: '2026-05-08T00:00:01Z'
      }] as ConversationTurn[]
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/compact/start') {
        return {
          outcome: 'partial',
          contextUsage: {
            tokens: 100,
            contextWindow: 1000,
            autoCompactThreshold: 800,
            warningThreshold: 700,
            errorThreshold: 750,
            percentLeft: 0.9
          }
        }
      }
      if (method === 'thread/turns/list') {
        return {
          data: [{
            id: 'turn_001',
            threadId: 'thread-1',
            status: 'completed',
            items: [],
            startedAt: '2026-05-08T00:00:00Z',
            completedAt: '2026-05-08T00:00:01Z'
          }],
          nextCursor: 'new-turn-cursor'
        }
      }
      if (method === 'thread/items/list') {
        return { data: [], nextCursor: null }
      }
      if (method === 'thread/read') {
        return {
          thread: {
            id: 'thread-1',
            workspacePath: 'X:\\fixtures\\workspace',
            userId: 'local',
            displayName: null,
            status: 'active',
            originChannel: 'appserver',
            metadata: {},
            createdAt: '2026-05-08T00:00:00Z',
            lastActiveAt: '2026-05-08T00:00:02Z',
            queuedInputs: []
          }
        }
      }
      return {}
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />)

    const textbox = screen.getByRole('textbox')
    fireEvent.focus(textbox)
    textbox.textContent = '/'
    setCaretToEnd(textbox)
    fireEvent.input(textbox)

    fireEvent.click(await screen.findByRole('option', { name: /Compact/i }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'thread/read',
        { threadId: 'thread-1' }
      )
      expect(useThreadStore.getState().activeHistoryCursors).toEqual({
        threadId: 'thread-1',
        turnCursor: 'new-turn-cursor'
      })
    })
    expect(useConversationStore.getState().contextUsage?.tokens).toBe(100)
  })

  it('keeps manual compaction running when the RPC request times out but maintenance is active', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        manualCompaction: true
      }
    })
    useConversationStore.setState({
      turnStatus: 'idle',
      maintenanceKind: null,
      turns: [{
        id: 'turn_001',
        threadId: 'thread-1',
        status: 'completed',
        items: [],
        startedAt: '2026-05-08T00:00:00Z',
        completedAt: '2026-05-08T00:00:01Z'
      }] as ConversationTurn[]
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/compact/start') {
        useConversationStore.setState({ maintenanceKind: 'compacting' })
        throw new Error("Request 'thread/compact/start' timed out after 300000ms")
      }
      return {}
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />)

    const textbox = screen.getByRole('textbox')
    fireEvent.focus(textbox)
    textbox.textContent = '/'
    setCaretToEnd(textbox)
    fireEvent.input(textbox)

    fireEvent.click(await screen.findByRole('option', { name: /Compact/i }))

    await waitFor(() => {
      expect(useToastStore.getState().toasts.some(
        (toast) => toast.message === 'Context compaction is still running'
      )).toBe(true)
    })
    expect(useToastStore.getState().toasts.some(
      (toast) => toast.message.startsWith('Failed to compact context')
    )).toBe(false)
    expect(useConversationStore.getState().maintenanceKind).toBe('compacting')
  })

  it('shows the generic toast when compact skips because there is no older context', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        manualCompaction: true
      }
    })
    useConversationStore.setState({
      turnStatus: 'idle',
      turns: [{
        id: 'turn_001',
        threadId: 'thread-1',
        status: 'completed',
        items: [],
        startedAt: '2026-05-08T00:00:00Z',
        completedAt: '2026-05-08T00:00:01Z'
      }] as ConversationTurn[]
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/compact/start') {
        return {
          outcome: 'skipped',
          message: 'no_summarizable_prefix'
        }
      }
      return {}
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />)

    const textbox = screen.getByRole('textbox')
    fireEvent.focus(textbox)
    textbox.textContent = '/'
    setCaretToEnd(textbox)
    fireEvent.input(textbox)

    fireEvent.click(await screen.findByRole('option', { name: /Compact/i }))

    await waitFor(() => {
      expect(useToastStore.getState().toasts.some(
        (toast) => toast.message === 'Nothing needed compaction'
      )).toBe(true)
    })
  })

  it('shows the generic toast for other compact skipped reasons', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        manualCompaction: true
      }
    })
    useConversationStore.setState({
      turnStatus: 'idle',
      turns: [{
        id: 'turn_001',
        threadId: 'thread-1',
        status: 'completed',
        items: [],
        startedAt: '2026-05-08T00:00:00Z',
        completedAt: '2026-05-08T00:00:01Z'
      }] as ConversationTurn[]
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/compact/start') {
        return {
          outcome: 'skipped',
          message: 'below_threshold'
        }
      }
      return {}
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />)

    const textbox = screen.getByRole('textbox')
    fireEvent.focus(textbox)
    textbox.textContent = '/'
    setCaretToEnd(textbox)
    fireEvent.input(textbox)

    fireEvent.click(await screen.findByRole('option', { name: /Compact/i }))

    await waitFor(() => {
      expect(useToastStore.getState().toasts.some(
        (toast) => toast.message === 'Nothing needed compaction'
      )).toBe(true)
    })
  })

  it('shows consolidate system action in Chinese for /con and calls manual memory consolidation', async () => {
    settingsGet.mockResolvedValue({ locale: 'zh-Hans' })
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        manualMemoryConsolidation: true
      }
    })
    useConversationStore.setState({
      turnStatus: 'idle',
      turns: [{
        id: 'turn_001',
        threadId: 'thread-1',
        status: 'completed',
        items: [],
        startedAt: '2026-05-08T00:00:00Z',
        completedAt: '2026-05-08T00:00:01Z'
      }] as ConversationTurn[]
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/memory/consolidate/start') {
        return {
          outcome: 'succeeded',
          memoryWritten: true,
          historyWritten: true
        }
      }
      return {}
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />)

    const textbox = screen.getByRole('textbox')
    fireEvent.focus(textbox)
    textbox.textContent = '/con'
    setCaretToEnd(textbox)
    fireEvent.input(textbox)

    expect(await screen.findByText('整理长期记忆')).toBeInTheDocument()
    fireEvent.click(await screen.findByRole('option', { name: /整理/i }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'thread/memory/consolidate/start',
        { threadId: 'thread-1' },
        300_000
      )
    })
  })

  it('handles /consolidate locally without starting a turn', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        manualMemoryConsolidation: true
      }
    })
    useConversationStore.setState({
      turnStatus: 'idle',
      turns: [{
        id: 'turn_001',
        threadId: 'thread-1',
        status: 'completed',
        items: [],
        startedAt: '2026-05-08T00:00:00Z',
        completedAt: '2026-05-08T00:00:01Z'
      }] as ConversationTurn[]
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/memory/consolidate/start') {
        return {
          outcome: 'succeeded',
          memoryWritten: true,
          historyWritten: true
        }
      }
      return {}
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />)

    const textbox = screen.getByRole('textbox')
    textbox.textContent = '/consolidate'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'thread/memory/consolidate/start',
        { threadId: 'thread-1' },
        300_000
      )
    })
    expect(appServerSendRequest).not.toHaveBeenCalledWith('turn/start', expect.anything())
  })

  it('shows skipped toast when manual memory consolidation has no changes', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        manualMemoryConsolidation: true
      }
    })
    useConversationStore.setState({
      turnStatus: 'idle',
      turns: [{
        id: 'turn_001',
        threadId: 'thread-1',
        status: 'completed',
        items: [],
        startedAt: '2026-05-08T00:00:00Z',
        completedAt: '2026-05-08T00:00:01Z'
      }] as ConversationTurn[]
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/memory/consolidate/start') {
        return {
          outcome: 'skipped',
          message: 'no_memory_changes'
        }
      }
      return {}
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />)

    const textbox = screen.getByRole('textbox')
    textbox.textContent = '/consolidate'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      expect(useToastStore.getState().toasts.some(
        (toast) => toast.message === 'Nothing needed memory consolidation'
      )).toBe(true)
    })
  })

  it('shows failed toast when manual memory consolidation fails', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        manualMemoryConsolidation: true
      }
    })
    useConversationStore.setState({
      turnStatus: 'idle',
      turns: [{
        id: 'turn_001',
        threadId: 'thread-1',
        status: 'completed',
        items: [],
        startedAt: '2026-05-08T00:00:00Z',
        completedAt: '2026-05-08T00:00:01Z'
      }] as ConversationTurn[]
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/memory/consolidate/start') {
        return {
          outcome: 'failed',
          message: 'provider unavailable'
        }
      }
      return {}
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />)

    const textbox = screen.getByRole('textbox')
    textbox.textContent = '/consolidate'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      expect(useToastStore.getState().toasts.some(
        (toast) => toast.message === 'Failed to consolidate memory: provider unavailable'
      )).toBe(true)
    })
  })

  it('shows unavailable toast for /consolidate without idle history', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        manualMemoryConsolidation: true
      }
    })
    useConversationStore.setState({ turnStatus: 'idle', turns: [] })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />)

    const textbox = screen.getByRole('textbox')
    textbox.textContent = '/consolidate'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      expect(useToastStore.getState().toasts.some(
        (toast) => toast.message === 'Memory consolidation is available only for idle conversations with history.'
      )).toBe(true)
    })
    expect(appServerSendRequest).not.toHaveBeenCalledWith(
      'thread/memory/consolidate/start',
      expect.anything(),
      expect.anything()
    )
  })

  it('handles /goal set locally without starting a turn', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        threadGoals: true
      }
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/goal/get') return { goal: null }
      if (method === 'thread/goal/set') return { goal: makeGoal('thread-1', 'Fix tests') }
      return {}
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />)

    const textbox = screen.getByRole('textbox')
    textbox.textContent = '/goal Fix tests'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/goal/set', {
        threadId: 'thread-1',
        objective: 'Fix tests'
      })
    })
    expect(appServerSendRequest).not.toHaveBeenCalledWith('turn/start', expect.anything())
  })

  it('sets the goal from goal compose mode without sending a legacy mode field', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        threadGoals: true
      }
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/goal/get') return { goal: null }
      if (method === 'thread/goal/set') return { goal: makeGoal('thread-1', 'Match the mockup') }
      if (method === 'turn/start') return { turn: { id: 'turn-1' } }
      return {}
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="C:\\work\\project" />)

    // Goal compose mode is hidden by default and entered from the `/` system menu.
    const textbox = screen.getByRole('textbox')
    fireEvent.focus(textbox)
    textbox.textContent = '/'
    setCaretToEnd(textbox)
    fireEvent.input(textbox)
    fireEvent.click(await screen.findByRole('option', { name: /goal/i }))

    expect(await screen.findByRole('button', { name: 'Exit goal mode' })).toBeInTheDocument()
    textbox.textContent = 'Match the mockup'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/goal/set', {
        threadId: 'thread-1',
        objective: 'Match the mockup'
      })
    })
    // The goal is now active, so the composer switches from the compose pill to the status pill.
    expect(await screen.findByText('Goal: active')).toBeInTheDocument()
  })

  it('handles /goal pause resume and clear with goal RPCs', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        threadGoals: true
      }
    })
    useThreadStore.getState().setThreadGoal(makeGoal('thread-1'))
    appServerSendRequest.mockImplementation(async (method: string, params: Record<string, unknown>) => {
      if (method === 'thread/goal/set') {
        return { goal: makeGoal('thread-1', params.status === 'paused' ? 'Existing goal' : 'Existing goal') }
      }
      if (method === 'thread/goal/clear') return { cleared: true }
      return {}
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />)

    const textbox = screen.getByRole('textbox')
    textbox.textContent = '/goal pause'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/goal/set', {
        threadId: 'thread-1',
        status: 'paused'
      })
    })

    textbox.textContent = '/goal resume'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/goal/set', {
        threadId: 'thread-1',
        status: 'active'
      })
    })

    textbox.textContent = '/goal clear'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/goal/clear', { threadId: 'thread-1' })
    })
    expect(appServerSendRequest).not.toHaveBeenCalledWith('turn/start', expect.anything())
  })

  it('confirms before replacing a different active goal', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        threadGoals: true
      }
    })
    useThreadStore.getState().setThreadGoal(makeGoal('thread-1', 'Old goal'))
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/goal/set') return { goal: makeGoal('thread-1', 'New goal') }
      return {}
    })

    renderWithLocaleAndConfirm(<InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />)

    const textbox = screen.getByRole('textbox')
    textbox.textContent = '/goal New goal'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })

    fireEvent.click(await screen.findByRole('button', { name: 'Replace goal' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/goal/set', {
        threadId: 'thread-1',
        objective: 'New goal'
      })
    })
  })

  it('intercepts /goal when the capability is unavailable', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {}
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />)

    const textbox = screen.getByRole('textbox')
    textbox.textContent = '/goal Fix tests'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      expect(useToastStore.getState().toasts.some((toast) => toast.message.includes('Goals are not available'))).toBe(true)
    })
    expect(appServerSendRequest).not.toHaveBeenCalledWith('turn/start', expect.anything())
    expect(appServerSendRequest.mock.calls.some((call) => String(call[0]).startsWith('thread/goal/'))).toBe(false)
  })

  it('opens and toggles the command picker without changing draft text', async () => {
    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />)

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('skills/list', { includeUnavailable: true })
    })

    const textbox = screen.getByRole('textbox')
    textbox.textContent = 'Review this'
    setCaretToEnd(textbox)
    fireEvent.input(textbox)
    const trigger = screen.getByRole('button', { name: 'Open commands' })

    fireEvent.click(trigger)
    expect(textbox).toHaveTextContent('Review this')
    expect(trigger).toHaveAttribute('aria-expanded', 'true')
    expect(trigger).toHaveAttribute('data-active', 'true')
    expect(screen.getByRole('listbox')).toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: 'Attach image' })).toBeNull()

    fireEvent.click(trigger)
    expect(trigger).toHaveAttribute('aria-expanded', 'false')
    expect(trigger).toHaveAttribute('data-active', 'false')
    expect(screen.queryByRole('listbox')).toBeNull()

    fireEvent.click(trigger)
    expect(textbox).toHaveTextContent('Review this')
    expect(trigger).toHaveAttribute('data-active', 'true')
    expect(screen.getByRole('listbox')).toBeInTheDocument()
  })

  it('uses visible text after the command trigger as the query and replaces only that range', async () => {
    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />)

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('command/list', {})
    })

    const textbox = screen.getByRole('textbox')
    textbox.textContent = 'Review this '
    setCaretToEnd(textbox)
    fireEvent.input(textbox)
    fireEvent.click(screen.getByRole('button', { name: 'Open commands' }))

    textbox.textContent = 'Review this code'
    setCaretToEnd(textbox)
    fireEvent.input(textbox)

    expect(textbox).toHaveTextContent('Review this code')
    expect(await screen.findByRole('option', { name: /code-review/i })).toBeInTheDocument()
    expect(screen.queryByRole('option', { name: /Plan mode/i })).toBeNull()

    textbox.textContent = 'Review this '
    setCaretToEnd(textbox)
    fireEvent.input(textbox)
    expect(screen.getByRole('option', { name: /Plan mode/i })).toBeInTheDocument()

    textbox.textContent = 'Review this code'
    setCaretToEnd(textbox)
    fireEvent.input(textbox)
    fireEvent.click(screen.getByRole('option', { name: /code-review/i }))

    expect(textbox).toHaveTextContent('Review this')
    expect(textbox.querySelector(`.${COMMAND_REF_CLASS}`)).not.toBeNull()
    expect(screen.queryByRole('listbox')).toBeNull()
  })

  it('keeps a visible button query when Escape dismisses the picker', async () => {
    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />)

    const textbox = screen.getByRole('textbox')
    fireEvent.click(screen.getByRole('button', { name: 'Open commands' }))
    textbox.textContent = 'code'
    setCaretToEnd(textbox)
    fireEvent.input(textbox)
    fireEvent.keyDown(window, { key: 'Escape' })

    expect(textbox).toHaveTextContent('code')
    expect(screen.queryByRole('listbox')).toBeNull()
  })

  it('removes only the visible button query before running a system action', async () => {
    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />)

    const textbox = screen.getByRole('textbox')
    textbox.textContent = 'Keep this '
    setCaretToEnd(textbox)
    fireEvent.input(textbox)
    fireEvent.click(screen.getByRole('button', { name: 'Open commands' }))
    await screen.findByRole('option', { name: /Plan mode/i })
    textbox.textContent = 'Keep this pl'
    setCaretToEnd(textbox)
    fireEvent.input(textbox)
    fireEvent.click(await screen.findByRole('option', { name: /Plan mode/i }))

    expect(textbox).toHaveTextContent('Keep this')
    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/mode/set', {
        threadId: 'thread-1',
        mode: 'plan'
      })
    })
  })

  it('keeps the command trigger inactive when slash input opens the picker', async () => {
    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />)

    const textbox = screen.getByRole('textbox')
    textbox.textContent = '/'
    setCaretToEnd(textbox)
    fireEvent.input(textbox)

    const trigger = screen.getByRole('button', { name: 'Open commands' })
    expect(await screen.findByRole('listbox')).toBeInTheDocument()
    expect(trigger).toHaveAttribute('aria-expanded', 'true')
    expect(trigger).toHaveAttribute('data-active', 'false')
  })

  it('accepts mixed dropped images and files', async () => {
    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />)

    const surface = screen
      .getByRole('textbox')
      .closest('div[style*="border-radius: 20px"]') as HTMLElement

    const image = new File(['image-bytes'], 'diagram.png', { type: 'image/png' })
    const note = new File(['notes'], 'notes.txt', { type: 'text/plain' })

    fireEvent.drop(surface, {
      dataTransfer: {
        files: [image, note],
        items: [
          {
            kind: 'file',
            getAsFile: () => image,
            webkitGetAsEntry: () => ({ isDirectory: false })
          },
          {
            kind: 'file',
            getAsFile: () => note,
            webkitGetAsEntry: () => ({ isDirectory: false })
          }
        ]
      }
    })

    await waitFor(() => {
      expect(saveImageToTemp).toHaveBeenCalled()
      expect(screen.getByText('notes.txt')).toBeInTheDocument()
    })
  })

  it('steers structured messages while running so slash commands keep their leading slash', async () => {
    useConversationStore.setState({
      turnStatus: 'running',
      activeTurnId: 'turn-running'
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />)

    const textbox = screen.getByRole('textbox')
    textbox.textContent = '/code-review'
    fireEvent.input(textbox)
    const surface = textbox.closest('div[style*="border-radius: 20px"]') as HTMLElement
    const note = new File(['notes'], 'notes.txt', { type: 'text/plain' })
    fireEvent.drop(surface, {
      dataTransfer: {
        files: [note],
        items: [{
          kind: 'file',
          getAsFile: () => note,
          webkitGetAsEntry: () => ({ isDirectory: false })
        }]
      }
    })

    expect(await screen.findByText('notes.txt')).toBeInTheDocument()

    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      const steerCall = appServerSendRequest.mock.calls.find((call) => call[0] === 'turn/steer')
      expect(steerCall).toBeDefined()
      expect(steerCall?.[1]).toEqual({
        threadId: 'thread-1',
        expectedTurnId: 'turn-running',
        input: [
          { type: 'fileRef', path: 'C:\\temp\\notes.txt', displayPath: 'C:\\temp\\notes.txt' },
          { type: 'text', text: '\n\n' },
          { type: 'text', text: '/code-review' }
        ],
        sender: undefined
      })
    })
  })

  it('shows a file-reference queue label instead of raw markers when queued text is empty', async () => {
    useConversationStore.setState({
      turnStatus: 'running',
      activeTurnId: 'turn-running'
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />)

    const textbox = screen.getByRole('textbox')
    const surface = textbox.closest('div[style*="border-radius: 20px"]') as HTMLElement
    const note = new File(['notes'], 'notes.txt', { type: 'text/plain' })
    fireEvent.drop(surface, {
      dataTransfer: {
        files: [note],
        items: [{
          kind: 'file',
          getAsFile: () => note,
          webkitGetAsEntry: () => ({ isDirectory: false })
        }]
      }
    })

    expect(await screen.findByText('notes.txt')).toBeInTheDocument()

    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      const steerCall = appServerSendRequest.mock.calls.find((call) => call[0] === 'turn/steer')
      expect(steerCall).toBeDefined()
      expect(steerCall?.[1]).toEqual({
        threadId: 'thread-1',
        expectedTurnId: 'turn-running',
        input: [
          { type: 'fileRef', path: 'C:\\temp\\notes.txt', displayPath: 'C:\\temp\\notes.txt' }
        ],
        sender: undefined
      })
    })
    expect(screen.queryByText(/\[\[Attached File:/)).not.toBeInTheDocument()
  })

  it('steers dropped images alongside text and file references while running', async () => {
    useConversationStore.setState({
      turnStatus: 'running',
      activeTurnId: 'turn-running'
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" />)

    const surface = screen
      .getByRole('textbox')
      .closest('div[style*="border-radius: 20px"]') as HTMLElement

    const image = new File(['image-bytes'], 'diagram.png', { type: 'image/png' })
    const note = new File(['notes'], 'notes.txt', { type: 'text/plain' })

    fireEvent.drop(surface, {
      dataTransfer: {
        files: [image, note],
        items: [
          {
            kind: 'file',
            getAsFile: () => image,
            webkitGetAsEntry: () => ({ isDirectory: false })
          },
          {
            kind: 'file',
            getAsFile: () => note,
            webkitGetAsEntry: () => ({ isDirectory: false })
          }
        ]
      }
    })

    await waitFor(() => {
      expect(saveImageToTemp).toHaveBeenCalled()
      expect(screen.getByText('notes.txt')).toBeInTheDocument()
    })

    const textbox = screen.getByRole('textbox')
    textbox.textContent = '/code-review'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      const steerCall = appServerSendRequest.mock.calls.find((call) => call[0] === 'turn/steer')
      expect(steerCall).toBeDefined()
      expect(steerCall?.[1]).toEqual({
        threadId: 'thread-1',
        expectedTurnId: 'turn-running',
        input: [
          { type: 'fileRef', path: 'C:\\temp\\notes.txt', displayPath: 'C:\\temp\\notes.txt' },
          { type: 'text', text: '\n\n' },
          { type: 'text', text: '/code-review' },
          {
            type: 'localImage',
            path: 'C:\\temp\\image.png',
            mimeType: 'image/png',
            fileName: 'diagram.png'
          }
        ],
        sender: undefined
      })
    })
  })
})
