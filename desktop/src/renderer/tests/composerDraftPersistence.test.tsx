import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { InputComposer } from '../components/conversation/InputComposer'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useToastStore } from '../stores/toastStore'
import { useComposerDraftStore } from '../stores/composerDraftStore'

const WS = 'C:\\workspace'
const settingsGet = vi.fn()
const appServerSendRequest = vi.fn()

function renderComposer(threadId: string): { unmount: () => void } {
  return render(
    <LocaleProvider>
      <InputComposer threadId={threadId} workspacePath={WS} />
    </LocaleProvider>
  )
}

function editor(): HTMLElement {
  return screen.getByRole('textbox')
}

function typeInto(text: string): void {
  const textbox = editor()
  textbox.textContent = text
  fireEvent.input(textbox)
}

describe('composer draft persistence across navigation', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ locale: 'en' })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'turn/start') return { turn: { id: 'turn-1' } }
      return {}
    })

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet },
        appServer: { sendRequest: appServerSendRequest },
        workspace: { saveImageToTemp: vi.fn(), getPathForFile: vi.fn() }
      }
    })

    useConversationStore.getState().reset()
    useConversationStore.setState({ remoteWorkspaceActive: false })
    useConnectionStore.getState().reset()
    useThreadStore.getState().reset()
    useToastStore.setState({ toasts: [] })
    useComposerDraftStore.setState({ draftsByThread: {} })
    useUIStore.setState({ activeMainView: 'conversation', composerPrefill: null })
    useConnectionStore.setState({ status: 'connected', capabilities: {} })
    useThreadStore.setState({
      threadList: [
        { id: 'thread-1', displayName: null, status: 'active', originChannel: 'appserver', createdAt: new Date().toISOString(), lastActiveAt: new Date().toISOString() },
        { id: 'thread-2', displayName: null, status: 'active', originChannel: 'appserver', createdAt: new Date().toISOString(), lastActiveAt: new Date().toISOString() }
      ]
    })
  })

  it('saves the draft on unmount and restores it on remount', async () => {
    const view = renderComposer('thread-1')
    typeInto('half-written message')

    view.unmount()
    expect(useComposerDraftStore.getState().getDraft('thread-1')?.text).toBe('half-written message')

    renderComposer('thread-1')
    await waitFor(() => expect(editor().textContent).toContain('half-written message'))
  })

  it('does not bleed a draft from one thread into another', async () => {
    const first = renderComposer('thread-1')
    typeInto('thread one draft')
    first.unmount()

    const second = renderComposer('thread-2')
    // thread-2 has no saved draft — its composer must start empty.
    expect(editor().textContent).toBe('')
    second.unmount()

    renderComposer('thread-1')
    await waitFor(() => expect(editor().textContent).toContain('thread one draft'))
  })

  it('clears the saved draft after sending', async () => {
    useComposerDraftStore.getState().saveDraft('thread-1', {
      text: 'queued draft',
      segments: [],
      images: [],
      files: []
    })

    renderComposer('thread-1')
    await waitFor(() => expect(editor().textContent).toContain('queued draft'))

    typeInto('final message')
    fireEvent.keyDown(editor(), { key: 'Enter' })

    await waitFor(() => {
      expect(appServerSendRequest.mock.calls.some((call) => call[0] === 'turn/start')).toBe(true)
    })
    expect(useComposerDraftStore.getState().getDraft('thread-1')).toBeNull()
  })
})
