import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { InputComposer } from '../components/conversation/InputComposer'
import { ConfirmDialogHost } from '../components/ui/ConfirmDialog'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useToastStore } from '../stores/toastStore'
import type { ThreadGoal } from '../types/thread'
import type { ConversationTurn } from '../types/conversation'

const settingsGet = vi.fn()
const appServerSendRequest = vi.fn()
const pickFiles = vi.fn()
const saveImageToTemp = vi.fn()
const getPathForFile = vi.fn((file: File) => file.name === 'notes.txt' ? 'C:\\temp\\notes.txt' : '')

function renderWithLocale(node: JSX.Element): void {
  render(<LocaleProvider>{node}</LocaleProvider>)
}

function renderWithLocaleAndConfirm(node: JSX.Element): void {
  render(<LocaleProvider><ConfirmDialogHost />{node}</LocaleProvider>)
}

function makeGoal(threadId = 'thread-1', objective = 'Existing goal'): ThreadGoal {
  return {
    threadId,
    goalId: `goal-${threadId}`,
    objective,
    status: 'active',
    tokenBudget: null,
    tokensUsed: {
      inputTokens: 0,
      outputTokens: 0,
      totalTokens: 0
    },
    timeUsedSeconds: 0,
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z'
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
    pickFiles.mockResolvedValue([])
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
      return {}
    })

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet },
        appServer: { sendRequest: appServerSendRequest },
        workspace: { saveImageToTemp, pickFiles, getPathForFile }
      }
    })

    useConversationStore.getState().reset()
    useConnectionStore.getState().reset()
    useThreadStore.getState().reset()
    useToastStore.setState({ toasts: [] })
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
    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

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

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

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

  it('inserts skill via slash and serializes skillRef input', async () => {
    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

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

  it('serializes picked file attachments into fileRef parts on turn/start', async () => {
    pickFiles.mockResolvedValue([
      { path: 'C:\\temp\\notes.txt', fileName: 'notes.txt' }
    ])

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

    const textbox = screen.getByRole('textbox')
    textbox.textContent = 'Review this file'
    fireEvent.input(textbox)
    fireEvent.click(screen.getByRole('button', { name: 'Add attachment' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Reference file' }))

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

  it('shows the Goal system action above commands when thread goals are supported', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        commandManagement: true,
        skillsManagement: true,
        threadGoals: true
      }
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('command/list', {})
    })

    const textbox = screen.getByRole('textbox')
    fireEvent.focus(textbox)
    textbox.textContent = '/'
    setCaretToEnd(textbox)
    fireEvent.input(textbox)

    const systemHeader = await screen.findByText('System')
    const commandHeader = await screen.findByText('Commands')
    expect(systemHeader.compareDocumentPosition(commandHeader) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()

    fireEvent.click(screen.getByRole('option', { name: /goal/i }))

    expect(await screen.findByRole('dialog', { name: 'Goal' })).toBeInTheDocument()
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

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

    const textbox = screen.getByRole('textbox')
    fireEvent.focus(textbox)
    textbox.textContent = '/'
    setCaretToEnd(textbox)
    fireEvent.input(textbox)

    fireEvent.click(await screen.findByRole('option', { name: /目标/ }))

    expect(await screen.findByRole('dialog', { name: '目标' })).toBeInTheDocument()
  })

  it('shows plan mode system action and toggles mode without starting a turn', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {}
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

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

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

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

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

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

  it('preserves compact result contextUsage when post-compact thread/read omits it', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        manualCompaction: true
      }
    })
    useThreadStore.setState({ activeThreadId: 'thread-1' })
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
      if (method === 'thread/read') {
        return {
          thread: {
            id: 'thread-1',
            workspacePath: 'E:\\Git\\dotcraft',
            userId: 'local',
            displayName: null,
            status: 'active',
            originChannel: 'appserver',
            metadata: {},
            createdAt: '2026-05-08T00:00:00Z',
            lastActiveAt: '2026-05-08T00:00:02Z',
            turns: [{
              id: 'turn_001',
              threadId: 'thread-1',
              status: 'completed',
              items: [],
              startedAt: '2026-05-08T00:00:00Z',
              completedAt: '2026-05-08T00:00:01Z'
            }],
            queuedInputs: []
          }
        }
      }
      return {}
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

    const textbox = screen.getByRole('textbox')
    fireEvent.focus(textbox)
    textbox.textContent = '/'
    setCaretToEnd(textbox)
    fireEvent.input(textbox)

    fireEvent.click(await screen.findByRole('option', { name: /Compact/i }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith(
        'thread/read',
        { threadId: 'thread-1', includeTurns: true }
      )
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

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

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

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

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

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

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

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

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

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

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

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

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

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

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

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

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

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

    const textbox = screen.getByRole('textbox')
    textbox.textContent = '/goal Fix tests'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/goal/set', {
        threadId: 'thread-1',
        objective: 'Fix tests',
        mode: 'upsertOrUpdate'
      })
    })
    expect(appServerSendRequest).not.toHaveBeenCalledWith('turn/start', expect.anything())
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

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

    const textbox = screen.getByRole('textbox')
    textbox.textContent = '/goal pause'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/goal/set', {
        threadId: 'thread-1',
        status: 'paused',
        mode: 'updateOnly'
      })
    })

    textbox.textContent = '/goal resume'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/goal/set', {
        threadId: 'thread-1',
        status: 'active',
        mode: 'updateOnly'
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

    renderWithLocaleAndConfirm(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

    const textbox = screen.getByRole('textbox')
    textbox.textContent = '/goal New goal'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })

    fireEvent.click(await screen.findByRole('button', { name: 'Replace goal' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/goal/set', {
        threadId: 'thread-1',
        objective: 'New goal',
        mode: 'replaceExisting'
      })
    })
  })

  it('intercepts /goal when the capability is unavailable', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {}
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

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

  it('opens a compact attachment menu with image and file actions', async () => {
    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('skills/list', { includeUnavailable: true })
    })

    expect(screen.queryByText('Attach file')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Add attachment' }))

    expect(screen.getByRole('menuitem', { name: 'Attach image' })).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: 'Reference file' })).toBeInTheDocument()
  })

  it('accepts mixed dropped images and files', async () => {
    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

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

  it('queues structured pending messages while running so slash commands keep their leading slash', async () => {
    pickFiles.mockResolvedValue([
      { path: 'C:\\temp\\notes.txt', fileName: 'notes.txt' }
    ])
    useConversationStore.setState({
      turnStatus: 'running',
      activeTurnId: 'turn-running'
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

    const textbox = screen.getByRole('textbox')
    textbox.textContent = '/code-review'
    fireEvent.input(textbox)
    fireEvent.click(screen.getByRole('button', { name: 'Add attachment' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Reference file' }))

    expect(await screen.findByText('notes.txt')).toBeInTheDocument()

    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      const enqueueCall = appServerSendRequest.mock.calls.find((call) => call[0] === 'turn/enqueue')
      expect(enqueueCall).toBeDefined()
      expect(enqueueCall?.[1]).toEqual({
        threadId: 'thread-1',
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
    pickFiles.mockResolvedValue([
      { path: 'C:\\temp\\notes.txt', fileName: 'notes.txt' }
    ])
    useConversationStore.setState({
      turnStatus: 'running',
      activeTurnId: 'turn-running'
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

    fireEvent.click(screen.getByRole('button', { name: 'Add attachment' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Reference file' }))

    expect(await screen.findByText('notes.txt')).toBeInTheDocument()

    fireEvent.keyDown(screen.getByRole('textbox'), { key: 'Enter' })

    await waitFor(() => {
      const enqueueCall = appServerSendRequest.mock.calls.find((call) => call[0] === 'turn/enqueue')
      expect(enqueueCall).toBeDefined()
      expect(enqueueCall?.[1]).toEqual({
        threadId: 'thread-1',
        input: [
          { type: 'fileRef', path: 'C:\\temp\\notes.txt', displayPath: 'C:\\temp\\notes.txt' }
        ],
        sender: undefined
      })
    })
    expect(screen.queryByText(/\[\[Attached File:/)).not.toBeInTheDocument()
  })

  it('queues dropped images alongside text and file references while running', async () => {
    useConversationStore.setState({
      turnStatus: 'running',
      activeTurnId: 'turn-running'
    })

    renderWithLocale(<InputComposer threadId="thread-1" workspacePath="E:\\Git\\dotcraft" />)

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
      const enqueueCall = appServerSendRequest.mock.calls.find((call) => call[0] === 'turn/enqueue')
      expect(enqueueCall).toBeDefined()
      expect(enqueueCall?.[1]).toEqual({
        threadId: 'thread-1',
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
