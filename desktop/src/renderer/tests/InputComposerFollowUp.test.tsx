import { useComposerContextStore } from '../stores/composerContextStore'
import { usePendingPasteStore } from '../components/conversation/usePastedText'
import './setupPluginRuntime'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { InputComposer } from '../components/conversation/InputComposer'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useComposerPreferencesStore } from '../stores/composerPreferencesStore'
import { useConversationStore } from '../stores/conversationStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useConfigStore } from '../stores/configStore'
import { useModelCatalogStore } from '../stores/modelCatalogStore'
import { useProvidersStore } from '../stores/providersStore'
import { useSubAgentStore } from '../stores/subAgentStore'
import { useThreadStore } from '../stores/threadStore'
import { useToastStore } from '../stores/toastStore'
import { useUIStore } from '../stores/uiStore'
import { useComposerDraftStore } from '../stores/composerDraftStore'
import { useGitStore } from '../stores/gitStore'
import { useVoiceStore } from '../voice/voiceStore'
import { installDesktopApiMock } from './desktopApiMock'
import { generatePromptSuggestion } from '../utils/promptSuggestion'

vi.mock('../utils/promptSuggestion', () => ({ generatePromptSuggestion: vi.fn(), logPromptSuggestion: vi.fn() }))

const sendRequest = vi.fn()
const onOptimisticEcho = vi.fn()

function renderComposer(): void {
  render(
    <LocaleProvider>
      <InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" modelName="test-model" modelOptions={['test-model']} onOptimisticEcho={onOptimisticEcho} />
    </LocaleProvider>
  )
}

function draft(text: string): HTMLElement {
  const textbox = screen.getByRole('textbox')
  textbox.textContent = text
  fireEvent.input(textbox)
  return textbox
}

describe('InputComposer follow-up routing', () => {
  beforeEach(() => {
    vi.mocked(generatePromptSuggestion).mockReset().mockResolvedValue(null)
    localStorage.clear()
    useComposerContextStore.setState({ byThread: {} })
    usePendingPasteStore.setState({ pastes: [] })
    sendRequest.mockReset().mockResolvedValue({})
    onOptimisticEcho.mockReset()
    installDesktopApiMock({
      settings: { get: async () => ({ locale: 'en' }), set: async () => undefined },
      appServer: { sendRequest, onNotification: undefined },
      git: { listBranches: async () => ({ current: 'main', detachedHead: null, branches: [{ name: 'main', current: true }] }) },
      workspace: { getPathForFile: () => 'X:\\fixtures\\workspace\\notes.md' },
      voice: undefined
    })
    useConversationStore.getState().reset()
    useConnectionStore.getState().reset()
    useConfigStore.getState().reset()
    useModelCatalogStore.getState().reset()
    useProvidersStore.getState().reset()
    useSubAgentStore.getState().reset()
    useThreadStore.getState().reset()
    useGitStore.getState().reset()
    useComposerPreferencesStore.setState({ followUpQueueMode: 'steer', saving: false })
    useComposerDraftStore.setState({ draftsByThread: {} })
    useToastStore.setState({ toasts: [] })
    useUIStore.setState({ composerPrefill: null, pendingWelcomeTurn: null })
    useVoiceStore.setState({ initialized: false, recording: null, finalizing: null,
      snapshot: { model: { phase: 'missing', bytesDownloaded: 0, bytesTotal: null }, chatGpt: { signedIn: false, enabled: true }, sessions: [], capacity: 2 } })
    useConversationStore.setState({ turnStatus: 'running', activeTurnId: 'turn-123' })
  })

  it.each(['enter', 'button'] as const)('starts a turn and notifies when submitted by %s', async (submission) => {
    useConversationStore.setState({ turnStatus: 'idle', activeTurnId: null })
    renderComposer()
    const textbox = draft('new request')
    if (submission === 'enter') fireEvent.keyDown(textbox, { key: 'Enter', code: 'Enter' })
    else fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('turn/start', expect.objectContaining({
      threadId: 'thread-1', input: [{ type: 'text', text: 'new request' }]
    })))
    expect(onOptimisticEcho).toHaveBeenCalledTimes(1)
  })

  it('does not notify when a maintenance submission is only enqueued', async () => {
    useConversationStore.setState({ turnStatus: 'idle', activeTurnId: null, maintenanceKind: 'compacting' })
    renderComposer()
    fireEvent.keyDown(draft('after compaction'), { key: 'Enter', code: 'Enter' })

    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('turn/enqueue', expect.objectContaining({ threadId: 'thread-1' })))
    expect(onOptimisticEcho).not.toHaveBeenCalled()
  })

  it('changes mode without requesting send positioning', async () => {
    useConversationStore.setState({ turnStatus: 'idle', activeTurnId: null })
    useThreadStore.setState({ activeThreadId: 'thread-1' })
    renderComposer()
    await act(async () => {
      fireEvent.keyDown(draft('/plan'), { key: 'Enter', code: 'Enter' })
    })

    expect(sendRequest).toHaveBeenCalledWith('thread/mode/set', { threadId: 'thread-1', mode: 'plan' })
    expect(onOptimisticEcho).not.toHaveBeenCalled()
    expect(sendRequest).not.toHaveBeenCalledWith('turn/start', expect.anything())
  })

  it('notifies for /init only once the expanded prompt has a local turn echo', async () => {
    useConversationStore.setState({ turnStatus: 'idle', activeTurnId: null })
    let expand!: (result: unknown) => void
    sendRequest.mockImplementation((method: string) => method === 'command/execute'
      ? new Promise((resolve) => { expand = resolve })
      : Promise.resolve({}))
    renderComposer()
    fireEvent.keyDown(draft('/init'), { key: 'Enter', code: 'Enter' })

    await waitFor(() => expect(expand).toBeDefined())
    expect(onOptimisticEcho).not.toHaveBeenCalled()
    await act(async () => expand({ handled: true, expandedPrompt: 'Initialize this workspace' }))
    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('turn/start', expect.objectContaining({
      input: [{ type: 'text', text: 'Initialize this workspace' }]
    })))
    expect(onOptimisticEcho).toHaveBeenCalledTimes(1)
  })

  it.each(['idle', 'running'] as const)('notifies for goal input only when it starts a turn from %s', async (status) => {
    useConversationStore.setState({ turnStatus: status, activeTurnId: status === 'running' ? 'turn-123' : null })
    useConnectionStore.setState({ capabilities: { threadGoals: true } })
    let setGoal!: (result: unknown) => void
    sendRequest.mockImplementation((method: string) => {
      if (method === 'thread/goal/get') return Promise.resolve({ goal: null })
      if (method === 'thread/goal/set') return new Promise((resolve) => { setGoal = resolve })
      return Promise.resolve({})
    })
    renderComposer()
    const textbox = screen.getByRole('textbox')
    fireEvent.focus(textbox)
    textbox.textContent = '/'
    const range = document.createRange()
    range.selectNodeContents(textbox)
    range.collapse(false)
    window.getSelection()?.removeAllRanges()
    window.getSelection()?.addRange(range)
    fireEvent.input(textbox)
    fireEvent.click(await screen.findByRole('option', { name: /Goal/ }))
    fireEvent.keyDown(draft('Finish this objective'), { key: 'Enter', code: 'Enter' })

    await waitFor(() => expect(setGoal).toBeDefined())
    expect(onOptimisticEcho).not.toHaveBeenCalled()
    await act(async () => setGoal({}))
    if (status === 'idle') {
      await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('turn/start', expect.objectContaining({ sentAsGoal: true })))
      expect(onOptimisticEcho).toHaveBeenCalledTimes(1)
    } else {
      await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('turn/enqueue', expect.objectContaining({ sentAsGoal: true })))
      expect(onOptimisticEcho).not.toHaveBeenCalled()
    }
  })

  it('returns an edited queued message to its place even when follow-ups steer', async () => {
    const entry = (id: string) => ({
      id, threadId: 'thread-1', displayText: id, status: 'queued' as const, createdAt: '2025-01-01T00:00:00Z',
      nativeInputParts: [{ type: 'text' as const, text: id }]
    })
    useConversationStore.setState({ queuedInputs: [entry('first'), entry('middle'), entry('last')] })
    sendRequest.mockImplementation(async (method: string, params: { orderedQueuedInputIds?: string[] }) => {
      if (method === 'turn/queue/remove') return { queuedInputs: [entry('first'), entry('last')] }
      if (method === 'turn/enqueue') return { queuedInput: entry('edited'), queuedInputs: [entry('first'), entry('last'), entry('edited')] }
      if (method === 'turn/queue/reorder') return { queuedInputs: params.orderedQueuedInputIds!.map(entry) }
      return {}
    })
    renderComposer()
    fireEvent.click(screen.getAllByRole('button', { name: 'Queued message actions' })[1])
    fireEvent.click(screen.getByRole('menuitem', { name: 'Edit queued message' }))
    await screen.findByRole('button', { name: 'Queue message' })
    fireEvent.keyDown(screen.getByRole('textbox'), { key: 'Enter' })
    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('turn/queue/reorder', {
      threadId: 'thread-1', orderedQueuedInputIds: ['first', 'edited', 'last']
    }))
    expect(sendRequest).not.toHaveBeenCalledWith('turn/steer', expect.anything())
    expect(onOptimisticEcho).not.toHaveBeenCalled()
  })

  it.each(['queue', 'steer'] as const)('echoes a %s input and restores its draft and attachments on failure without fallback', async (mode) => {
    useComposerPreferencesStore.getState().hydrate({ followUpQueueMode: mode })
    useConversationStore.setState({ turns: [{
      id: 'turn-123', threadId: 'thread-1', status: 'running', items: [], startedAt: '2025-01-01T00:00:00Z'
    }] })
    const method = mode === 'queue' ? 'turn/enqueue' : 'turn/steer'
    let fail!: (reason: unknown) => void
    sendRequest.mockImplementation((name: string) => name === method
      ? new Promise((_resolve, reject) => { fail = reject })
      : Promise.resolve({}))
    renderComposer()
    fireEvent.drop(screen.getByRole('textbox'), {
      dataTransfer: {
        types: ['Files'],
        files: [new File(['notes'], 'notes.md', { type: 'text/markdown' })],
        items: []
      }
    })
    const textbox = draft('keep attached request')
    fireEvent.keyDown(textbox, { key: 'Enter' })
    await waitFor(() => expect(fail).toBeDefined())
    const echoed = useConversationStore.getState().queuedInputs
    expect(echoed).toMatchObject([{
      threadId: 'thread-1', displayText: expect.stringContaining('keep attached request'),
      status: mode === 'steer' ? 'guidancePending' : 'queued',
      clientUserMessageId: expect.any(String)
    }])
    expect(useConversationStore.getState().turns[0].items).toEqual([])
    expect(onOptimisticEcho).toHaveBeenCalledTimes(mode === 'steer' ? 1 : 0)

    await act(async () => fail(new Error('turn changed')))
    await waitFor(() => expect(useConversationStore.getState().queuedInputs).toEqual([]))
    expect(textbox).toHaveTextContent('keep attached request')
    expect(screen.getByText('notes.md')).toBeInTheDocument()
    expect(sendRequest).toHaveBeenCalledWith(method, expect.objectContaining({
      clientUserMessageId: echoed[0].clientUserMessageId,
      input: expect.arrayContaining([expect.objectContaining({ type: 'fileRef', path: 'X:\\fixtures\\workspace\\notes.md' })])
    }))
    expect(sendRequest).not.toHaveBeenCalledWith(mode === 'queue' ? 'turn/steer' : 'turn/enqueue', expect.anything())
    expect(sendRequest).not.toHaveBeenCalledWith('turn/start', expect.anything())
    expect(onOptimisticEcho).toHaveBeenCalledTimes(mode === 'steer' ? 1 : 0)
  })

  it('restores a failed submission above text typed while it was in flight', async () => {
    let fail!: (reason: unknown) => void
    sendRequest.mockImplementation((name: string) => name === 'turn/start'
      ? new Promise((_resolve, reject) => { fail = reject })
      : Promise.resolve({}))
    useConversationStore.setState({ turnStatus: 'idle', activeTurnId: null })
    renderComposer()
    const textbox = draft('first request')
    fireEvent.keyDown(textbox, { key: 'Enter', code: 'Enter' })
    await waitFor(() => expect(fail).toBeDefined())
    await waitFor(() => expect(textbox.textContent).toBe(''))
    draft('second request')
    await act(async () => fail(new Error('offline')))
    await waitFor(() => expect(useToastStore.getState().toasts.length).toBe(1))
    const restored = screen.getByRole('textbox').textContent ?? ''
    expect(restored).toContain('first request')
    expect(restored.indexOf('second request')).toBeGreaterThan(restored.indexOf('first request'))
    expect(onOptimisticEcho).toHaveBeenCalledTimes(1)
  })
})
