import { useComposerContextStore } from '../stores/composerContextStore'
import { usePendingPasteStore } from '../components/conversation/usePastedText'
import { projectInputParts } from '../utils/inputPresentation'
import './setupPluginRuntime'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { InputComposer } from '../components/conversation/InputComposer'
import { FollowUpBehaviorRow } from '../components/settings/panels/FollowUpBehaviorRow'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useComposerPreferencesStore } from '../stores/composerPreferencesStore'
import { useConversationStore } from '../stores/conversationStore'
import { useConnectionStore } from '../stores/connectionStore'
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
  it('blocks keyboard submission while a pasted file is still being written', async () => {
    renderComposer()
    const textbox = draft('Wait for my attachment')
    act(() => usePendingPasteStore.getState().setPaste({ id: 'pending', threadId: 'thread-1', text: 'x'.repeat(5000), status: 'writing' }))
    fireEvent.keyDown(textbox, { key: 'Enter', code: 'Enter' })
    expect(sendRequest.mock.calls.some(([method]) => method === 'turn/steer')).toBe(false)
    expect(textbox).toHaveTextContent('Wait for my attachment')
    act(() => usePendingPasteStore.getState().remove('pending'))
    fireEvent.keyDown(textbox, { key: 'Enter', code: 'Enter' })
    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('turn/steer', expect.anything()))
  })
  beforeEach(() => {
    localStorage.clear()
    useComposerContextStore.setState({ byThread: {} })
    usePendingPasteStore.setState({ pastes: [] })
    sendRequest.mockReset().mockResolvedValue({})
    settingsSet.mockReset().mockResolvedValue(undefined)
    installDesktopApiMock({
      settings: { get: async () => ({ locale: 'en' }), set: settingsSet },
      appServer: { sendRequest, onNotification: undefined },
      git: { listBranches: async () => ({ current: 'main', detachedHead: null, branches: [{ name: 'main', current: true }] }) },
      voice: undefined
    })
    useConversationStore.getState().reset()
    useConnectionStore.getState().reset()
    useModelCatalogStore.getState().reset()
    useProvidersStore.getState().reset()
    useSubAgentStore.getState().reset()
    useThreadStore.getState().reset()
    useGitStore.getState().reset()
    useComposerPreferencesStore.setState({ followUpQueueMode: 'steer', saving: false })
    useComposerDraftStore.setState({ draftsByThread: {} })
    useToastStore.setState({ toasts: [] })
    useUIStore.setState({ composerPrefill: null, composerFileAttachmentRequest: null, pendingWelcomeTurn: null })
    useVoiceStore.setState({ initialized: false, recording: null, finalizing: null,
      snapshot: { model: { phase: 'missing', bytesDownloaded: 0, bytesTotal: null }, chatGpt: { signedIn: false, enabled: true }, sessions: [], capacity: 2 } })
    useConversationStore.setState({ turnStatus: 'running', activeTurnId: 'turn-123' })
  })

  it.each([
    ['queue', 'Enter'], ['queue', 'button'], ['steer', 'Enter'], ['steer', 'button']
  ] as const)('sends %s with %s and announces the same action', async (mode, trigger) => {
    useComposerPreferencesStore.getState().hydrate({ followUpQueueMode: mode })
    renderComposer()
    const textbox = draft()
    const button = screen.getByRole('button', { name: mode === 'queue' ? 'Queue message' : 'Send to current turn' })
    if (trigger === 'Enter') fireEvent.keyDown(textbox, { key: 'Enter', code: 'Enter' })
    else fireEvent.click(button)
    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith(
      mode === 'queue' ? 'turn/enqueue' : 'turn/steer',
      { threadId: 'thread-1', input: [{ type: 'text', text: 'follow up' }], sender: undefined, clientUserMessageId: expect.any(String),
        ...(mode === 'steer' ? { expectedTurnId: 'turn-123' } : {}) }
    ))
    expect(sendRequest).not.toHaveBeenCalledWith(mode === 'queue' ? 'turn/steer' : 'turn/enqueue', expect.anything())
    await waitFor(() => expect(textbox.textContent).toBe(''))
  })

  it('uses a saved setting on the next submission without changing existing queue entries', async () => {
    useConversationStore.setState({ queuedInputs: [{
      id: 'queued-1', threadId: 'thread-1', displayText: 'existing request', status: 'queued',
      createdAt: '2025-01-01T00:00:00Z'
    }] })
    renderComposer(true)
    const textbox = draft()
    expect(screen.getByRole('button', { name: 'Send to current turn' })).toBeInTheDocument()
    const existing = useConversationStore.getState().queuedInputs[0]
    fireEvent.click(screen.getByRole('button', { name: 'Queue', exact: true }))
    await screen.findByRole('button', { name: 'Queue message' })
    fireEvent.keyDown(textbox, { key: 'Enter' })
    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('turn/enqueue', expect.anything()))
    expect(useConversationStore.getState().queuedInputs[0]).toEqual(existing)
    expect(sendRequest).not.toHaveBeenCalledWith('turn/queue/update', expect.anything())
  })

  it.each(['queue', 'steer'] as const)('preserves structured commands, file references and images through %s', async (mode) => {
    useComposerPreferencesStore.getState().hydrate({ followUpQueueMode: mode })
    useComposerDraftStore.getState().saveDraft('thread-1', {
      text: '/code-review notes.md',
      segments: [
        { type: 'command', command: '/code-review' },
        { type: 'text', value: ' ' },
        { type: 'file', relativePath: 'notes.md' }
      ],
      files: [],
      images: [{ tempPath: 'X:\\fixtures\\attachments\\diagram.png', dataUrl: 'data:image/png;base64,AA==', fileName: 'diagram.png', mimeType: 'image/png' }]
    })
    renderComposer()
    await screen.findByRole('button', { name: mode === 'queue' ? 'Queue message' : 'Send to current turn' })
    fireEvent.keyDown(screen.getByRole('textbox'), { key: 'Enter' })
    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith(mode === 'queue' ? 'turn/enqueue' : 'turn/steer', expect.objectContaining({
      input: [
        { type: 'commandRef', name: 'code-review', rawText: '/code-review' },
        { type: 'text', text: ' ' },
        { type: 'fileRef', path: 'notes.md', displayPath: 'notes.md' },
        { type: 'localImage', path: 'X:\\fixtures\\attachments\\diagram.png', fileName: 'diagram.png', mimeType: 'image/png' }
      ]
    })))
  })

  it.each(['queue', 'steer'] as const)('preserves draft and attachments after failed %s without fallback', async (mode) => {
    useComposerPreferencesStore.getState().hydrate({ followUpQueueMode: mode })
    const method = mode === 'queue' ? 'turn/enqueue' : 'turn/steer'
    sendRequest.mockImplementation((name: string) => name === method ? Promise.reject(new Error('turn changed')) : Promise.resolve({}))
    renderComposer()
    act(() => useUIStore.getState().requestComposerFileAttachment({ path: 'X:\\fixtures\\workspace\\notes.md', fileName: 'notes.md' }))
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

  it('refuses chat references while the thread lacks the Desktop thread tools and keeps the draft', async () => {
    installDesktopApiMock({
      settings: { get: async () => ({ locale: 'en' }), set: settingsSet },
      appServer: { sendRequest, onNotification: undefined, hasDesktopThreadTools: async () => false },
      git: { listBranches: async () => ({ current: 'main', detachedHead: null, branches: [{ name: 'main', current: true }] }) },
      voice: undefined
    })
    useConversationStore.setState({ turnStatus: 'completed', activeTurnId: null })
    useComposerDraftStore.getState().saveDraft('thread-1', {
      text: 'continue [@Fix login](thread://thread_a)',
      segments: [{ type: 'text', value: 'continue ' }, { type: 'thread', threadId: 'thread_a', title: 'Fix login' }],
      files: [],
      images: []
    })
    renderComposer()
    const textbox = screen.getByRole('textbox')
    await waitFor(() => expect(textbox).toHaveTextContent('continue Fix login'))
    fireEvent.keyDown(textbox, { key: 'Enter' })
    await waitFor(() => expect(useToastStore.getState().toasts).toHaveLength(1))
    expect(sendRequest).not.toHaveBeenCalledWith('turn/start', expect.anything())
    expect(textbox).toHaveTextContent('continue Fix login')
  })

  it('queues maintenance follow-ups even when Steer is preferred', async () => {
    useConversationStore.setState({ turnStatus: 'idle', activeTurnId: null, maintenanceKind: 'compacting' })
    renderComposer()
    fireEvent.keyDown(draft(), { key: 'Enter' })
    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('turn/enqueue', expect.anything()))
    expect(sendRequest).not.toHaveBeenCalledWith('turn/steer', expect.anything())
  })
  it.each(['start', 'queue', 'steer'] as const)('preserves typed context through %s without a new wire type', async (mode) => {
    const context = { kind: 'responseAnnotation' as const, id: 'annotation', threadId: 'thread-1', turnId: 'turn-1', itemId: 'item-1', selectedText: 'selected reply', comment: 'correct this' }
    useComposerContextStore.getState().addContext('thread-1', context)
    if (mode === 'start') useConversationStore.setState({ turnStatus: 'completed', activeTurnId: null })
    else useComposerPreferencesStore.getState().hydrate({ followUpQueueMode: mode })
    renderComposer()
    fireEvent.keyDown(screen.getByRole('textbox'), { key: 'Enter', code: 'Enter' })
    const method = mode === 'start' ? 'turn/start' : mode === 'queue' ? 'turn/enqueue' : 'turn/steer'
    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith(method, expect.anything()))
    const args = sendRequest.mock.calls.find(([name]) => name === method)![1]
    expect(projectInputParts(args.input).contexts).toEqual([context])
    await waitFor(() => expect(useComposerContextStore.getState().getContexts('thread-1')).toEqual([]))
  })

  it.each(['start', 'steer'] as const)('clears the composer before %s resolves', async (mode) => {
    const method = mode === 'start' ? 'turn/start' : 'turn/steer'
    let settle!: (value: unknown) => void
    sendRequest.mockImplementation((name: string) => name === method
      ? new Promise((resolve) => { settle = resolve })
      : Promise.resolve({}))
    if (mode === 'start') useConversationStore.setState({ turnStatus: 'idle', activeTurnId: null })
    renderComposer()
    const textbox = draft('ship it')
    fireEvent.keyDown(textbox, { key: 'Enter', code: 'Enter' })
    await waitFor(() => expect(settle).toBeDefined())
    expect(textbox.textContent).toBe('')
    await act(async () => settle({ turn: { id: 'turn-9' } }))
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

  it('keeps feedback added while the accepted message is in flight', async () => {
    let accept!: () => void
    sendRequest.mockImplementation((method) => method === 'turn/enqueue' ? new Promise<void>((resolve) => { accept = resolve }) : Promise.resolve({}))
    useComposerPreferencesStore.getState().hydrate({ followUpQueueMode: 'queue' })
    const first = { kind: 'responseAnnotation' as const, id: 'one', threadId: 'thread-1', turnId: 'turn-1', itemId: 'item-1', selectedText: 'one', comment: 'change' }
    useComposerContextStore.getState().addContext('thread-1', first)
    renderComposer()
    fireEvent.keyDown(screen.getByRole('textbox'), { key: 'Enter', code: 'Enter' })
    await waitFor(() => expect(accept).toBeDefined())
    act(() => useComposerContextStore.getState().addContext('thread-1', { ...first, id: 'two' }))
    await act(async () => accept())
    expect(useComposerContextStore.getState().getContexts('thread-1').map((context) => context.id)).toEqual(['two'])
  })

})
