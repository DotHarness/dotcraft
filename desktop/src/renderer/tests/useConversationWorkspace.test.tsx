import { act, cleanup, render, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { UserMessageBlock } from '../components/conversation/UserMessageBlock'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useConversationWorkspace } from '../hooks/useConversationWorkspace'
import { activateThreadConversation, createThreadConversation, useConversationStore } from '../stores/conversationStore'
import { openThreadState, releaseThreadState } from '../stores/threadState'
import type { ConversationTurn } from '../types/conversation'
import { installDesktopApiMock } from './desktopApiMock'

const REMOTE_THREAD = 'workspace-context-remote'
const LOCAL_THREAD = 'workspace-context-local'
const REMOTE_PATH = '/fixtures/remote-workspace'
const LOCAL_PATH = 'C:/fixtures/workspace'

beforeEach(() => {
  activateThreadConversation(null)
  useConversationStore.setState({ workspacePath: '', remoteWorkspaceActive: false })
})

afterEach(() => {
  cleanup()
  releaseThreadState(REMOTE_THREAD)
  releaseThreadState(LOCAL_THREAD)
  activateThreadConversation(null)
  useConversationStore.setState({ workspacePath: '', remoteWorkspaceActive: false })
})

describe('conversation workspace synchronization', () => {
  it('loads a local attachment after returning from remote and reactivating a stale cached conversation', async () => {
    const imagePath = `${LOCAL_PATH}/.craft/attachments/images/workspace-context-regression.png`
    const dataUrl = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII='
    const readImageAsDataUrl = vi.fn().mockResolvedValue({ dataUrl })
    installDesktopApiMock({ initialLocale: 'en', workspace: { readImageAsDataUrl } })
    const turns: ConversationTurn[] = [{
      id: 'workspace-context-turn', threadId: LOCAL_THREAD, status: 'completed', startedAt: '2026-11-08T00:00:00Z',
      items: [{
        id: 'workspace-context-message', type: 'userMessage', status: 'completed', text: '',
        nativeInputParts: [{ type: 'localImage', path: imagePath, fileName: 'image.png' }],
        createdAt: '2026-11-08T00:00:00Z'
      }]
    }]
    openThreadState(REMOTE_THREAD)
    useConversationStore.setState({ workspacePath: REMOTE_PATH, remoteWorkspaceActive: true, turns })
    createThreadConversation(LOCAL_THREAD).setState({ turns })

    function Conversation({ remote }: { remote: boolean }): JSX.Element {
      useConversationWorkspace(remote ? REMOTE_PATH : LOCAL_PATH, remote)
      const message = useConversationStore((state) => state.turns[0]?.items[0])
      return (
        <LocaleProvider loadSettings={false}>
          {message && <UserMessageBlock messageId={message.id} text={message.text ?? ''} nativeInputParts={message.nativeInputParts} />}
        </LocaleProvider>
      )
    }

    const view = render(<Conversation remote />)
    expect(readImageAsDataUrl).not.toHaveBeenCalled()
    expect(view.container.querySelector('img')).toBeNull()

    act(() => openThreadState(null))
    view.rerender(<Conversation remote={false} />)
    expect(readImageAsDataUrl).not.toHaveBeenCalled()

    act(() => openThreadState(LOCAL_THREAD))
    await waitFor(() => expect(view.container.querySelector('img')).toHaveAttribute('src', dataUrl))
    expect(readImageAsDataUrl).toHaveBeenCalledExactlyOnceWith({ path: imagePath })
    expect(useConversationStore.getState()).toMatchObject({ workspacePath: LOCAL_PATH, remoteWorkspaceActive: false })
  })
})
