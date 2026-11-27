import { beforeEach, describe, expect, it, vi } from 'vitest'
import { startPendingWelcomeTurn } from '../utils/startPendingWelcomeTurn'
import { useComposerDraftStore } from '../stores/composerDraftStore'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useToastStore } from '../stores/toastStore'
import { installDesktopApiMock } from './desktopApiMock'

const sendRequest = vi.fn()

function pending(): Parameters<typeof startPendingWelcomeTurn>[0] {
  return {
    threadId: 'thread-1',
    pending: {
      text: 'List my board items',
      inputParts: [{ type: 'text', text: 'List my board items' }]
    },
    workspacePath: 'X:\\fixtures\\workspace',
    translate: (key: string) => key
  }
}

describe('startPendingWelcomeTurn', () => {
  beforeEach(() => {
    sendRequest.mockReset()
    sendRequest.mockImplementation(async (method: string) => {
      if (method === 'turn/start') return { turn: { id: 'turn-server' } }
      return {}
    })
    installDesktopApiMock({
      appServer: { sendRequest },
      shell: { openExternal: vi.fn() }
    })
    useConversationStore.getState().reset()
    useThreadStore.getState().reset()
    useThreadStore.setState({ activeThreadId: 'thread-1' })
    useComposerDraftStore.setState({ draftsByThread: {} })
    useToastStore.setState({ toasts: [] })
  })

  it('starts the turn directly on the active thread', async () => {
    await startPendingWelcomeTurn(pending())

    expect(sendRequest.mock.calls.map(([method]) => method)).toContain('turn/start')
    expect(useConversationStore.getState().turns.map((turn) => turn.id)).toEqual(['turn-server'])
    expect(useConversationStore.getState().systemLabel).toBeNull()
  })

  it('starts the turn without an on-screen echo when the user is on another thread', async () => {
    useThreadStore.setState({ activeThreadId: 'thread-2' })

    await startPendingWelcomeTurn(pending())

    expect(sendRequest.mock.calls.map(([method]) => method)).toContain('turn/start')
    expect(useConversationStore.getState().turns).toEqual([])
    expect(useConversationStore.getState().systemLabel).toBeNull()
  })
})
