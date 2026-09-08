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
  beforeEach(() => {
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
      snapshot: { model: { phase: 'missing', bytesDownloaded: 0, bytesTotal: null }, sessions: [], capacity: 2 } })
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
      { threadId: 'thread-1', input: [{ type: 'text', text: 'follow up' }], sender: undefined,
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
    const queuedInputs = useConversationStore.getState().queuedInputs
    fireEvent.click(screen.getByRole('button', { name: 'Queue', exact: true }))
    await screen.findByRole('button', { name: 'Queue message' })
    fireEvent.keyDown(textbox, { key: 'Enter' })
    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('turn/enqueue', expect.anything()))
    expect(useConversationStore.getState().queuedInputs).toBe(queuedInputs)
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

  it('queues maintenance follow-ups even when Steer is preferred', async () => {
    useConversationStore.setState({ turnStatus: 'idle', activeTurnId: null, maintenanceKind: 'consolidating' })
    renderComposer()
    fireEvent.keyDown(draft(), { key: 'Enter' })
    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('turn/enqueue', expect.anything()))
    expect(sendRequest).not.toHaveBeenCalledWith('turn/steer', expect.anything())
  })
})
