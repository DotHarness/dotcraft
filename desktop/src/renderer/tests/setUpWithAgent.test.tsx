import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { ProjectEnvironmentsResult } from '../../shared/projectActions'
import { selectEnvironmentWhenAgentFinishes } from '../components/projectActions/setUpWithAgent'
import { useSideChatStore, type SideChat } from '../stores/sideChatStore'
import { useThreadStore } from '../stores/threadStore'
import type { ConversationTurn } from '../types/conversation'
import { installDesktopApiMock } from './desktopApiMock'

const WORKSPACE = 'C:\\repo'
const PATH = 'C:\\repo\\.craft\\environments\\environment.json'
const ID = '.craft/environments/environment.json'
const KEY = 'side-key'

const none: ProjectEnvironmentsResult = { environments: [], selectedEnvironmentId: null }
const created: ProjectEnvironmentsResult = {
  environments: [{ id: ID, name: 'repo', path: PATH, isDefault: true, actions: [], revision: 'r1' }],
  selectedEnvironmentId: ID
}

let turnCount = 0

function setTurn(status: ConversationTurn['status'], next = false): void {
  if (next) turnCount += 1
  const chat: SideChat = {
    key: KEY,
    parentThreadId: 'thread-1',
    threadId: 'side-1',
    tabId: 'tab-1',
    ordinal: 1,
    status: 'ready',
    turns: [{ id: `turn-${turnCount}`, threadId: 'side-1', status, items: [] } as ConversationTurn],
    live: null,
    unread: false,
    interruptingTurnId: null,
    pendingApprovals: [],
    pendingUserInput: null
  }
  useSideChatStore.setState({ chats: new Map([[KEY, chat]]) })
}

async function flush(): Promise<void> {
  for (let index = 0; index < 5; index++) await Promise.resolve()
}

beforeEach(() => {
  turnCount = 0
  useThreadStore.setState({ activeThread: { id: 'thread-1' } as never, threadList: [] })
})

describe('selectEnvironmentWhenAgentFinishes', () => {
  it('selects the new default environment after the first turn that leaves one, and only once', async () => {
    const list = vi.fn().mockResolvedValueOnce(none).mockResolvedValue(created)
    const select = vi.fn().mockResolvedValue(undefined)
    const selectWorktreeEnvironment = vi.fn().mockResolvedValue(undefined)
    installDesktopApiMock({ projectActions: { list, select, selectWorktreeEnvironment } })
    setTurn('running', true)

    selectEnvironmentWhenAgentFinishes(KEY, { workspacePath: WORKSPACE, threadId: 'thread-1' })
    setTurn('completed')
    await flush()
    expect(list).toHaveBeenCalledTimes(1)
    expect(select).not.toHaveBeenCalled()

    setTurn('running', true)
    setTurn('completed')
    await vi.waitFor(() => expect(select).toHaveBeenCalledWith({ workspacePath: WORKSPACE, environmentId: ID }))
    expect(selectWorktreeEnvironment).toHaveBeenCalledWith({ workspacePath: WORKSPACE, environmentId: ID })

    setTurn('running', true)
    setTurn('completed')
    await flush()
    expect(list).toHaveBeenCalledTimes(2)
    expect(select).toHaveBeenCalledTimes(1)
    expect(selectWorktreeEnvironment).toHaveBeenCalledTimes(1)
  })

  it('stops watching when the side chat closes', async () => {
    const list = vi.fn().mockResolvedValue(created)
    installDesktopApiMock({ projectActions: { list } })
    setTurn('running', true)

    selectEnvironmentWhenAgentFinishes(KEY, { workspacePath: WORKSPACE, threadId: 'thread-1' })
    useSideChatStore.setState({ chats: new Map() })
    setTurn('completed')
    await flush()

    expect(list).not.toHaveBeenCalled()
  })
})
