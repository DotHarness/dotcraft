import { beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { AgentResponseBlock } from '../components/conversation/AgentResponseBlock'
import { useConversationStore } from '../stores/conversationStore'
import { useSubAgentStore } from '../stores/subAgentStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { openThreadState } from '../stores/threadState'
import { coreToolFields } from './testToolPresentation'
import { useTurnActivityDisclosureStore } from '../stores/turnActivityDisclosureStore'
import type { ConversationTurn } from '../types/conversation'
import { installDesktopApiMock } from './desktopApiMock'
import { makeSubAgent, makeSubAgentThread } from './subAgentFixtures'

const asked: ConversationTurn = {
  id: 'turn-asked',
  threadId: 'thread-1',
  status: 'completed',
  startedAt: '2026-06-10T11:00:00.000Z',
  completedAt: '2026-06-10T11:00:10.000Z',
  items: [
    { id: 'prompt', type: 'userMessage', status: 'completed', text: 'clean up', createdAt: '2026-06-10T11:00:00.000Z' },
    { id: 'progress', type: 'agentMessage', status: 'completed', phase: 'commentary', text: 'waiting for the build', createdAt: '2026-06-10T11:00:05.000Z' }
  ]
}

const woken: ConversationTurn = {
  id: 'turn-woken',
  threadId: 'thread-1',
  status: 'completed',
  startedAt: '2026-06-10T11:05:00.000Z',
  completedAt: '2026-06-10T11:05:20.000Z',
  items: [
    { id: 'ended', type: 'userMessage', status: 'completed', deliveryMode: 'queued', triggerKind: 'terminal', triggerLabel: 'npm run build', triggerStatus: 'completed', triggerRefId: 'call-build' },
    { id: 'answer', type: 'agentMessage', status: 'completed', phase: 'final', text: 'the build passed', createdAt: '2026-06-10T11:05:15.000Z' }
  ]
}

const completions = (turnIds: readonly string[]) => <span data-testid={`completion-${turnIds.join('+')}`} />

beforeEach(() => {
  openThreadState(asked.threadId)
  useConversationStore.getState().reset()
  useUIStore.setState({ responseChangesScope: null })
  useThreadStore.setState({ runtimeSnapshots: new Map() })
  useTurnActivityDisclosureStore.setState({ expanded: new Map() })
  installDesktopApiMock({
    settings: { get: async () => ({ locale: 'en' }) },
    appServer: { sendRequest: vi.fn().mockResolvedValue({}) }
  })
})

describe('AgentResponseBlock responses spanning turns', () => {
  it('folds the earlier turns of a response into the one process of its last turn', () => {
    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock turn={woken} leadingTurns={[asked]} />
      </LocaleProvider>
    )

    expect(screen.getByText('the build passed')).toBeInTheDocument()
    expect(screen.queryByText('waiting for the build')).toBeNull()
    const toggles = container.querySelectorAll('button[aria-expanded]')
    expect(toggles).toHaveLength(1)

    fireEvent.click(toggles[0])

    expect(toggles[0]).toHaveAttribute('aria-expanded', 'true')
    expect(screen.getByText('waiting for the build').closest('[data-turn-id]')).toHaveAttribute('data-turn-id', 'turn-asked')
    expect(container.querySelectorAll('button[aria-expanded]')).toHaveLength(1)
  })

  it('holds the answer actions and completion results until background work ends', () => {
    useThreadStore.getState().applyRuntimeSnapshot('thread-1', { hasBackgroundWork: true }, { isDesktopOrigin: false })
    render(
      <LocaleProvider>
        <AgentResponseBlock turn={woken} isLastTurn isLastVisibleTurn renderTurnCompletion={completions} />
      </LocaleProvider>
    )

    expect(screen.queryByTestId('agent-message-footer')).toBeNull()
    expect(screen.queryByTestId('completion-turn-woken')).toBeNull()

    act(() => {
      useThreadStore.getState().applyRuntimeSnapshot('thread-1', { hasBackgroundWork: false }, { isDesktopOrigin: false })
    })

    expect(screen.getByTestId('agent-message-footer')).toBeInTheDocument()
    expect(screen.getByTestId('completion-turn-woken')).toBeInTheDocument()
  })

  it('opens Changes for every turn in the settled response', () => {
    const conversation = useConversationStore.getState()
    conversation.setWorkspacePath('/workspace')
    conversation.onTurnDiffUpdated({ turnId: asked.id, diff: [
      'diff --git a/a.ts b/a.ts', '--- a/a.ts', '+++ b/a.ts', '@@ -1 +1 @@', '-old', '+new', ''
    ].join('\n') })
    conversation.onTurnDiffUpdated({ turnId: woken.id, diff: [
      'diff --git a/b.ts b/b.ts', '--- a/b.ts', '+++ b/b.ts', '@@ -1 +1 @@', '-old', '+new', ''
    ].join('\n') })
    render(<LocaleProvider><AgentResponseBlock turn={woken} leadingTurns={[asked]} /></LocaleProvider>)

    fireEvent.click(screen.getByRole('button', { name: 'Review' }))
    expect(useUIStore.getState().responseChangesScope).toEqual({ threadId: asked.threadId, turnIds: [asked.id, woken.id] })
  })

  it('keeps the checklist through a task wake with no new TodoWrite, then hides it when the response settles', () => {
    useThreadStore.setState({ activeThreadId: asked.threadId })
    useConversationStore.getState().onPlanUpdated({
      title: '', todos: [{ id: 'verify', content: 'Verify the build', status: 'in_progress' }]
    })
    const withChecklist: ConversationTurn = {
      ...asked, items: [...asked.items, { id: 'todo', status: 'completed', type: 'toolCall', toolName: 'TodoWrite',
        ...coreToolFields('TodoWrite'), success: true }]
    }
    useThreadStore.getState().applyRuntimeSnapshot(asked.threadId, { hasBackgroundWork: true }, { isDesktopOrigin: false })

    render(<LocaleProvider><AgentResponseBlock turn={woken} leadingTurns={[withChecklist]} isLastTurn isLastVisibleTurn /></LocaleProvider>)

    expect(screen.getByRole('region', { name: /To-dos/ })).toBeInTheDocument()
    act(() => useThreadStore.getState().applyRuntimeSnapshot(asked.threadId, { hasBackgroundWork: false }, { isDesktopOrigin: false }))
    expect(screen.queryByRole('region', { name: /To-dos/ })).not.toBeInTheDocument()
  })

  it('shows no completion results for an answered earlier turn while the response continues', () => {
    const answered: ConversationTurn = {
      ...asked,
      items: [asked.items[0], { ...asked.items[1], phase: 'final', text: 'the build is running' }]
    }
    const running: ConversationTurn = { ...woken, status: 'running', completedAt: undefined, items: [woken.items[0]] }
    render(
      <LocaleProvider>
        <AgentResponseBlock turn={running} leadingTurns={[answered]} isRunning isLastTurn isLastVisibleTurn renderTurnCompletion={completions} />
      </LocaleProvider>
    )

    expect(screen.getByText('the build is running')).toBeInTheDocument()
    expect(screen.queryByTestId('completion-turn-asked')).toBeNull()
  })

  it('names a subagent that woke a continuing turn with the row its ending shows mid-turn', () => {
    const reviewed: ConversationTurn = {
      ...woken,
      items: [
        { id: 'reviewed', type: 'userMessage', status: 'completed', deliveryMode: 'queued', triggerKind: 'subagent', triggerLabel: 'reviewer', triggerStatus: 'completed', triggerRefId: '/root/reviewer' },
        woken.items[1]
      ]
    }
    const { container } = render(
      <LocaleProvider>
        <AgentResponseBlock turn={reviewed} leadingTurns={[asked]} />
      </LocaleProvider>
    )

    fireEvent.click(container.querySelector('button[aria-expanded]')!)

    expect(screen.getByTestId('subagent-ending-chips')).toHaveTextContent('reviewer')
  })

  it('adds nothing for a subagent message that continues a response', () => {
    const mailed: ConversationTurn = {
      ...woken,
      items: [
        { id: 'mail', type: 'userMessage', status: 'completed', deliveryMode: 'queued', triggerKind: 'subagentMailbox', triggerLabel: 'helper', triggerRefId: '/root/helper' },
        woken.items[1]
      ]
    }
    render(
      <LocaleProvider>
        <AgentResponseBlock turn={mailed} leadingTurns={[asked]} />
      </LocaleProvider>
    )

    expect(screen.queryByRole('button', { name: 'Show the subagent' })).toBeNull()
  })

  it('opens a sender that is not a direct child from the turn its message started', () => {
    const helper = makeSubAgent({
      childThreadId: 'helper-thread', parentThreadId: 'worker-thread', agentPath: '/root/worker/helper', nickname: 'helper',
      threadSummary: makeSubAgentThread('helper-thread', { kind: 'subagent', subAgent: { rootThreadId: 'thread-1', parentThreadId: 'worker-thread' } })
    })
    useThreadStore.setState({ activeThreadId: 'thread-1', activeThread: makeSubAgentThread('thread-1') })
    useSubAgentStore.setState({ childrenByParent: new Map([['worker-thread', [helper]]]), selectedChildByParent: new Map() })
    const mailed: ConversationTurn = {
      ...woken,
      items: [
        { id: 'mail', type: 'userMessage', status: 'completed', deliveryMode: 'queued', triggerKind: 'subagentMailbox', triggerLabel: 'helper', triggerRefId: '/root/worker/helper' },
        woken.items[1]
      ]
    }
    render(
      <LocaleProvider>
        <AgentResponseBlock turn={mailed} />
      </LocaleProvider>
    )

    fireEvent.click(screen.getByRole('button', { name: 'Show the subagent' }))

    expect(useSubAgentStore.getState().selectedChildByParent.get('thread-1')).toBe('helper-thread')
  })
})
