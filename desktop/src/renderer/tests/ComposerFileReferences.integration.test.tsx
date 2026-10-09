import './setupPluginRuntime'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { InputComposer } from '../components/conversation/InputComposer'
import { ConversationWelcome } from '../components/conversation/ConversationWelcome'
import { collectComposerDraftSegments } from '../components/conversation/richInputSerialization'
import { clearDesktopPluginRegistry } from '../plugins/desktopPluginRegistry'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { useComposerContextStore } from '../stores/composerContextStore'
import { useComposerDraftStore } from '../stores/composerDraftStore'
import { useConfigStore } from '../stores/configStore'
import { useGitStore } from '../stores/gitStore'
import { useModelCatalogStore } from '../stores/modelCatalogStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useComposerFileReferenceStore } from '../stores/composerFileReferenceStore'
import { useViewerTabStore } from '../stores/viewerTabStore'
import { useSkillsStore } from '../stores/skillsStore'
import { useToastStore } from '../stores/toastStore'
import { useVoiceStore } from '../voice/voiceStore'
import { welcomeScopeKey } from '../utils/detailPanelScope'
import { savePlainComposerDraft } from '../utils/plainComposerDraft'
import { installDesktopApiMock } from './desktopApiMock'

const workspacePath = 'C:\\workspace'
const filePath = workspacePath + '\\src\\file with spaces.ts'
const appServerSendRequest = vi.fn()

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => { resolve = done })
  return { promise, resolve }
}

function renderThread(threadId: string) {
  return render(
    <LocaleProvider>
      <InputComposer threadId={threadId} workspacePath={workspacePath} />
    </LocaleProvider>
  )
}

function renderWelcome(projectKey = workspacePath) {
  return render(
    <LocaleProvider>
      <ConversationWelcome workspacePath={workspacePath} projectKey={projectKey} />
    </LocaleProvider>
  )
}

function segments() {
  return collectComposerDraftSegments(screen.getByRole('textbox'))
}

function saveThreadDraft(threadId: string, text: string) {
  useComposerDraftStore.getState().saveDraft(threadId, {
    text,
    segments: [{ type: 'text', value: text }],
    files: [],
    images: []
  })
}

describe('file references in real composer draft lifecycles', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    clearDesktopPluginRegistry()
    useConnectionStore.getState().reset()
    useConversationStore.getState().reset()
    useConversationStore.setState({ remoteWorkspaceActive: false })
    useConfigStore.getState().reset()
    useConfigStore.setState({ config: { WelcomeSuggestions: { Enabled: false } } })
    useGitStore.getState().reset()
    useModelCatalogStore.getState().reset()
    useThreadStore.getState().reset()
    useThreadStore.setState({
      threadList: ['thread-1', 'thread-2'].map((id) => ({
        id,
        displayName: null,
        status: 'active' as const,
        originChannel: 'appserver',
        createdAt: '2026-10-09T00:00:00.000Z',
        lastActiveAt: '2026-10-09T00:00:00.000Z'
      }))
    })
    useComposerDraftStore.setState({ draftsByThread: {} })
    useComposerFileReferenceStore.setState({ pendingByScope: new Map() })
    useComposerContextStore.setState({ byThread: {}, restoreRequests: {} })
    useSkillsStore.setState({ skills: [], loading: false, error: null })
    useToastStore.setState({ toasts: [] })
    useVoiceStore.setState({ initialized: true, recording: null, finalizing: null })
    useViewerTabStore.setState({ welcomeScopeId: welcomeScopeKey(workspacePath) })
    useUIStore.setState({
      activeMainView: 'conversation',
      composerPrefill: null,
      composerImageAttachmentRequest: null,
      pendingWelcomeTurn: null,
      pendingThreadCreation: null,
      welcomeDraft: null,
      welcomeDraftsByWorkspace: {}
    })
    useConnectionStore.setState({ status: 'connected', capabilities: {} })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'command/list') return { commands: [] }
      if (method === 'skills/list') return { skills: [] }
      return {}
    })
    installDesktopApiMock({
      settings: { get: async () => ({ locale: 'en' }) },
      appServer: { sendRequest: appServerSendRequest, onNotification: undefined },
      git: {
        listBranches: async () => ({ current: 'main', detachedHead: null, branches: [] })
      },
      workspace: { saveImageToTemp: vi.fn(), getPathForFile: vi.fn() },
      voice: undefined
    })
  })

  it('restores a saved thread draft before inserting queued references on remount', async () => {
    useThreadStore.setState({ activeThreadId: 'thread-1' })
    saveThreadDraft('thread-1', 'Keep this draft')
    act(() => {
      useComposerFileReferenceStore.getState().request(filePath)
      useComposerFileReferenceStore.getState().request(workspacePath + '\\src\\second.ts')
    })

    const view = renderThread('thread-1')
    const expected = [
      { type: 'text', value: 'Keep this draft ' },
      { type: 'file', relativePath: 'src/file with spaces.ts' },
      { type: 'text', value: '\u00a0' },
      { type: 'file', relativePath: 'src/second.ts' },
      { type: 'text', value: '\u00a0' }
    ]
    await waitFor(() => expect(segments()).toEqual(expected))
    expect(useComposerFileReferenceStore.getState().pendingByScope.size).toBe(0)
    view.unmount()
    expect(useComposerDraftStore.getState().getDraft('thread-1')?.segments).toEqual(expected)

    renderThread('thread-1')
    await waitFor(() => expect(segments()).toEqual(expected))
  })

  it('waits for both catalogs before merging a reference into a saved plain welcome draft', async () => {
    const commands = deferred<{ commands: [] }>()
    const skills = deferred<{ skills: [] }>()
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'command/list') return commands.promise
      if (method === 'skills/list') return skills.promise
      return {}
    })
    useConnectionStore.setState({
      capabilities: { commandManagement: true, skillsManagement: true }
    })
    savePlainComposerDraft(welcomeScopeKey(workspacePath), 'Saved welcome text')
    act(() => useComposerFileReferenceStore.getState().request(filePath))

    const view = renderWelcome()
    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('command/list', {})
      expect(appServerSendRequest).toHaveBeenCalledWith('skills/list', {})
    })
    expect(segments()).toEqual([])
    expect(useComposerFileReferenceStore.getState().pendingByScope.has(welcomeScopeKey(workspacePath))).toBe(true)

    await act(async () => { commands.resolve({ commands: [] }) })
    expect(segments()).toEqual([])
    expect(useComposerFileReferenceStore.getState().pendingByScope.has(welcomeScopeKey(workspacePath))).toBe(true)

    await act(async () => { skills.resolve({ skills: [] }) })
    const expected = [
      { type: 'text', value: 'Saved welcome text ' },
      { type: 'file', relativePath: 'src/file with spaces.ts' },
      { type: 'text', value: '\u00a0' }
    ]
    await waitFor(() => expect(segments()).toEqual(expected))
    expect(useComposerFileReferenceStore.getState().pendingByScope.size).toBe(0)
    view.unmount()
    expect(useUIStore.getState().getWelcomeDraftForWorkspace(workspacePath)?.segments).toEqual(expected)
  })

  it('does not let another thread consume a pending reference and preserves both drafts', async () => {
    useThreadStore.setState({ activeThreadId: 'thread-1' })
    saveThreadDraft('thread-1', 'First draft')
    saveThreadDraft('thread-2', 'Second draft')
    act(() => useComposerFileReferenceStore.getState().request(filePath))
    const pending = useComposerFileReferenceStore.getState().pendingByScope
    useThreadStore.setState({ activeThreadId: 'thread-2' })

    const other = renderThread('thread-2')
    await waitFor(() => expect(segments()).toEqual([{ type: 'text', value: 'Second draft' }]))
    expect(useComposerFileReferenceStore.getState().pendingByScope).toEqual(pending)
    other.unmount()
    expect(useComposerDraftStore.getState().getDraft('thread-2')?.segments).toEqual([
      { type: 'text', value: 'Second draft' }
    ])

    useThreadStore.setState({ activeThreadId: 'thread-1' })
    renderThread('thread-1')
    await waitFor(() => expect(segments()).toEqual([
      { type: 'text', value: 'First draft ' },
      { type: 'file', relativePath: 'src/file with spaces.ts' },
      { type: 'text', value: '\u00a0' }
    ]))
    expect(useComposerFileReferenceStore.getState().pendingByScope.size).toBe(0)
  })

  it('inserts queued files for both threads when the second thread is opened first', async () => {
    useThreadStore.setState({ activeThreadId: 'thread-1' })
    saveThreadDraft('thread-1', 'First draft')
    saveThreadDraft('thread-2', 'Second draft')
    useComposerFileReferenceStore.getState().request(filePath)
    useThreadStore.setState({ activeThreadId: 'thread-2' })
    useComposerFileReferenceStore.getState().request(workspacePath + '/src/second.ts')

    const second = renderThread('thread-2')
    await waitFor(() => expect(segments()).toEqual([
      { type: 'text', value: 'Second draft ' },
      { type: 'file', relativePath: 'src/second.ts' },
      { type: 'text', value: '\u00a0' }
    ]))
    expect(useComposerFileReferenceStore.getState().pendingByScope.has('thread-1')).toBe(true)
    second.unmount()

    useThreadStore.setState({ activeThreadId: 'thread-1' })
    renderThread('thread-1')
    await waitFor(() => expect(segments()).toEqual([
      { type: 'text', value: 'First draft ' },
      { type: 'file', relativePath: 'src/file with spaces.ts' },
      { type: 'text', value: '\u00a0' }
    ]))
    expect(useComposerFileReferenceStore.getState().pendingByScope.size).toBe(0)
  })

  it('preserves both welcome queues while each project waits for its composer', async () => {
    const otherProject = 'other-project'
    savePlainComposerDraft(welcomeScopeKey(workspacePath), 'First welcome')
    savePlainComposerDraft(welcomeScopeKey(otherProject), 'Second welcome')
    useComposerFileReferenceStore.getState().request(filePath)
    useViewerTabStore.setState({ welcomeScopeId: welcomeScopeKey(otherProject) })
    useComposerFileReferenceStore.getState().request(workspacePath + '/src/second.ts')

    const second = renderWelcome(otherProject)
    await waitFor(() => expect(segments()).toEqual([
      { type: 'text', value: 'Second welcome ' },
      { type: 'file', relativePath: 'src/second.ts' },
      { type: 'text', value: '\u00a0' }
    ]))
    expect(useComposerFileReferenceStore.getState().pendingByScope.has(welcomeScopeKey(workspacePath))).toBe(true)
    second.unmount()

    useViewerTabStore.setState({ welcomeScopeId: welcomeScopeKey(workspacePath) })
    renderWelcome()
    await waitFor(() => expect(segments()).toEqual([
      { type: 'text', value: 'First welcome ' },
      { type: 'file', relativePath: 'src/file with spaces.ts' },
      { type: 'text', value: '\u00a0' }
    ]))
    expect(useComposerFileReferenceStore.getState().pendingByScope.size).toBe(0)
  })

  it('applies a new global prefill before inserting a simultaneous reference', async () => {
    useThreadStore.setState({ activeThreadId: 'thread-1' })
    saveThreadDraft('thread-1', 'Old draft')
    renderThread('thread-1')
    await waitFor(() => expect(segments()).toEqual([{ type: 'text', value: 'Old draft' }]))

    act(() => {
      useUIStore.getState().setComposerPrefill('Prefilled text')
      useComposerFileReferenceStore.getState().request(filePath)
    })
    await waitFor(() => expect(segments()).toEqual([
      { type: 'text', value: 'Prefilled text ' },
      { type: 'file', relativePath: 'src/file with spaces.ts' },
      { type: 'text', value: '\u00a0' }
    ]))
    expect(useComposerFileReferenceStore.getState().pendingByScope.size).toBe(0)
  })

  it('waits for an embedded prefill request before consuming the file reference', async () => {
    useThreadStore.setState({ activeThreadId: 'thread-1' })
    saveThreadDraft('thread-1', 'Old draft')
    const view = renderThread('thread-1')
    await waitFor(() => expect(segments()).toEqual([{ type: 'text', value: 'Old draft' }]))

    act(() => {
      useComposerFileReferenceStore.getState().request(filePath)
      view.rerender(
        <LocaleProvider>
          <InputComposer
            threadId="thread-1"
            workspacePath={workspacePath}
            prefillRequest={{ id: 1, text: 'Embedded prefill' }}
          />
        </LocaleProvider>
      )
    })
    await waitFor(() => expect(segments()).toEqual([
      { type: 'text', value: 'Embedded prefill ' },
      { type: 'file', relativePath: 'src/file with spaces.ts' },
      { type: 'text', value: '\u00a0' }
    ]))
    expect(useComposerFileReferenceStore.getState().pendingByScope.size).toBe(0)
  })

  it('does not consume a welcome reference in another project scope', async () => {
    act(() => useComposerFileReferenceStore.getState().request(filePath))
    const pending = useComposerFileReferenceStore.getState().pendingByScope
    const otherProject = 'other-project'
    useViewerTabStore.setState({ welcomeScopeId: welcomeScopeKey(otherProject) })
    savePlainComposerDraft(welcomeScopeKey(otherProject), 'Other project draft')

    renderWelcome(otherProject)
    await waitFor(() => expect(segments()).toEqual([
      { type: 'text', value: 'Other project draft' }
    ]))
    expect(useComposerFileReferenceStore.getState().pendingByScope).toEqual(pending)
  })
})
