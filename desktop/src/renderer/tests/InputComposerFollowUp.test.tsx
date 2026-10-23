import { useComposerContextStore } from '../stores/composerContextStore'
import { usePendingPasteStore } from '../components/conversation/usePastedText'
import './setupPluginRuntime'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { InputComposer } from '../components/conversation/InputComposer'
import { FollowUpBehaviorRow } from '../components/settings/panels/FollowUpBehaviorRow'
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
const settingsSet = vi.fn()

function renderComposer(withSettings = false): void {
  render(
    <LocaleProvider>
      {withSettings && <FollowUpBehaviorRow />}
      <InputComposer threadId="thread-1" workspacePath="X:\\fixtures\\workspace" modelName="test-model" modelOptions={['test-model']} />
    </LocaleProvider>
  )
}

function draft(text = 'follow up'): HTMLElement {
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
    settingsSet.mockReset().mockResolvedValue(undefined)
    installDesktopApiMock({
      settings: { get: async () => ({ locale: 'en' }), set: settingsSet },
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
  })

  it.each(['queue', 'steer'] as const)('preserves draft and attachments after failed %s without fallback', async (mode) => {
    useComposerPreferencesStore.getState().hydrate({ followUpQueueMode: mode })
    const method = mode === 'queue' ? 'turn/enqueue' : 'turn/steer'
    sendRequest.mockImplementation((name: string) => name === method ? Promise.reject(new Error('turn changed')) : Promise.resolve({}))
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
    await waitFor(() => expect(useToastStore.getState().toasts.length).toBe(1))
    expect(textbox).toHaveTextContent('keep attached request')
    expect(screen.getByText('notes.md')).toBeInTheDocument()
    expect(sendRequest).toHaveBeenCalledWith(method, expect.objectContaining({ input: expect.arrayContaining([
      expect.objectContaining({ type: 'fileRef', path: 'X:\\fixtures\\workspace\\notes.md' })
    ]) }))
    expect(sendRequest).not.toHaveBeenCalledWith(mode === 'queue' ? 'turn/steer' : 'turn/enqueue', expect.anything())
    expect(sendRequest).not.toHaveBeenCalledWith('turn/start', expect.anything())
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
  })

  it('echoes an enqueued message and drops the echo when turn/enqueue fails', async () => {
    useComposerPreferencesStore.getState().hydrate({ followUpQueueMode: 'queue' })
    let fail!: (reason: unknown) => void
    sendRequest.mockImplementation((name: string) => name === 'turn/enqueue'
      ? new Promise((_resolve, reject) => { fail = reject })
      : Promise.resolve({}))
    renderComposer()
    fireEvent.keyDown(draft('queued request'), { key: 'Enter', code: 'Enter' })
    await waitFor(() => expect(fail).toBeDefined())
    const echoed = useConversationStore.getState().queuedInputs
    expect(echoed).toHaveLength(1)
    expect(echoed[0]).toMatchObject({ displayText: 'queued request', threadId: 'thread-1', status: 'queued' })
    expect(echoed[0].id).toBe(`local-${echoed[0].clientUserMessageId}`)
    await act(async () => fail(new Error('offline')))
    await waitFor(() => expect(useConversationStore.getState().queuedInputs).toEqual([]))
  })

  it('echoes a steered message only in the queue and drops the echo when turn/steer fails', async () => {
    useConversationStore.setState({ turns: [{
      id: 'turn-123', threadId: 'thread-1', status: 'running', items: [], startedAt: '2025-01-01T00:00:00Z'
    }] })
    let fail!: (reason: unknown) => void
    sendRequest.mockImplementation((name: string) => name === 'turn/steer'
      ? new Promise((_resolve, reject) => { fail = reject })
      : Promise.resolve({}))
    renderComposer()
    fireEvent.keyDown(draft('steered request'), { key: 'Enter', code: 'Enter' })
    await waitFor(() => expect(fail).toBeDefined())
    const state = useConversationStore.getState()
    expect(state.turns[0].items).toEqual([])
    expect(state.queuedInputs).toHaveLength(1)
    expect(state.queuedInputs[0]).toMatchObject({
      displayText: 'steered request',
      threadId: 'thread-1',
      status: 'guidancePending'
    })
    expect(state.queuedInputs[0].id).toBe(`local-${state.queuedInputs[0].clientUserMessageId}`)
    await act(async () => fail(new Error('turn changed')))
    await waitFor(() => expect(useConversationStore.getState().queuedInputs).toEqual([]))
    expect(useConversationStore.getState().turns[0].items).toEqual([])
  })
})
