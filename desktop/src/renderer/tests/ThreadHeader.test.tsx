import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ThreadHeader } from '../components/conversation/ThreadHeader'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import type { Thread } from '../types/thread'

const settingsGet = vi.fn()
const appServerSendRequest = vi.fn()

function makeThread(): Thread {
  return {
    id: 'thread-1',
    userId: 'local',
    workspacePath: 'fixtures\\sample-app',
    effectiveWorkspacePath: 'fixtures\\sample-app',
    displayName: 'Thread',
    status: 'active',
    originChannel: 'dotcraft-desktop',
    createdAt: '2026-01-01T00:00:00.000Z',
    lastActiveAt: '2026-01-01T00:00:00.000Z',
    metadata: {},
    turns: []
  }
}

function renderHeader(): void {
  render(
    <LocaleProvider>
      <ThreadHeader
        threadName="Thread"
        threadId="thread-1"
        workspacePath="fixtures\\sample-app"
      />
    </LocaleProvider>
  )
}

describe('ThreadHeader', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useConnectionStore.getState().reset()
    useConversationStore.getState().reset()
    useThreadStore.getState().reset()
    settingsGet.mockResolvedValue({ locale: 'en' })
    appServerSendRequest.mockResolvedValue({})

    const thread = makeThread()
    useThreadStore.setState({
      activeThreadId: thread.id,
      activeThread: thread,
      threadList: [thread]
    })

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet },
        appServer: { sendRequest: appServerSendRequest },
        shell: {
          listEditors: vi.fn().mockResolvedValue([])
        }
      }
    })
  })

  it('opens the Fork submenu from the header menu when fork is available', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: { threadFork: true, gitWorktrees: true }
    })

    renderHeader()

    fireEvent.click(await screen.findByRole('button', { name: 'More chat actions' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Fork' }))

    expect(screen.getByRole('menuitem', { name: 'Fork into local' })).toBeInTheDocument()
    expect(screen.getByRole('menuitem', { name: 'Fork into new worktree' })).toBeInTheDocument()
  })

  it('omits Fork from the header menu when fork is unavailable', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {}
    })

    renderHeader()

    fireEvent.click(await screen.findByRole('button', { name: 'More chat actions' }))

    expect(screen.queryByRole('menuitem', { name: 'Fork' })).not.toBeInTheDocument()
  })
})
