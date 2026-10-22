import { act } from 'react'
import { beforeEach, describe, expect, it } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { AgentResponseBlock } from '../components/conversation/AgentResponseBlock'
import { LocaleProvider } from '../contexts/LocaleContext'
import type { ConversationTurn } from '../types/conversation'
import { installDesktopApiMock } from './desktopApiMock'
import { findToolCallRevealTarget, useToolCallRevealStore } from '../stores/toolCallRevealStore'
import { useViewerTabStore } from '../stores/viewerTabStore'
import { withTestCorePresentation } from './testToolPresentation'

describe('turn activity disclosure lifecycle', () => {
  beforeEach(() => { installDesktopApiMock({ settings: { get: async () => ({ locale: 'en' }) } }) })

  it('collapses activity when final streaming begins, preserves guidance, and restores partial content on stop', () => {
    const turn: ConversationTurn = {
      id: 'turn', threadId: 'thread', status: 'running', startedAt: '2026-01-01T12:00:00Z',
      items: [
        { id: 'progress', type: 'agentMessage', status: 'completed', phase: 'commentary', text: 'Inspecting the example.', createdAt: '2026-01-01T12:00:01Z' },
        { id: 'guidance', type: 'userMessage', status: 'completed', deliveryMode: 'guidance', text: 'Keep existing behavior.', createdAt: '2026-01-01T12:00:02Z' }
      ]
    }
    const ui = (value: ConversationTurn, running: boolean) => <LocaleProvider loadSettings={false}><AgentResponseBlock turn={value} isRunning={running} /></LocaleProvider>
    const { rerender } = render(ui(turn, true))
    expect(screen.getByText('Inspecting the example.')).toBeInTheDocument()
    const answering: ConversationTurn = { ...turn, items: [...turn.items,
      { id: 'final', type: 'agentMessage', phase: 'final', status: 'streaming', text: 'Here is the partial answer.', createdAt: '2026-01-01T12:00:12Z' }
    ] }
    rerender(ui(answering, true))
    expect(screen.getByText('Inspecting the example.').closest('[inert]')).not.toBeNull()
    expect(screen.getByText('Keep existing behavior.')).toBeInTheDocument()
    expect(screen.getByText('Here is the partial answer.')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { expanded: false }))
    expect(screen.getByText('Inspecting the example.').closest('[inert]')).toBeNull()
    rerender(ui({ ...answering, status: 'cancelled', completedAt: '2026-01-01T12:00:20Z' }, false))
    expect(screen.getByText('Inspecting the example.')).toBeInTheDocument()
    expect(screen.getByText('Here is the partial answer.')).toBeInTheDocument()
    expect(screen.queryByRole('button', { expanded: false })).toBeNull()
    expect(screen.getAllByRole('status')).toHaveLength(1)
  })

  it('exposes the command behind a turn started by a background command notice', () => {
    const turn: ConversationTurn = {
      id: 'woken', threadId: 'thread', status: 'completed', startedAt: '2026-01-01T12:00:00Z', completedAt: '2026-01-01T12:00:18Z',
      items: [
        { id: 'notice', type: 'userMessage', status: 'completed', deliveryMode: 'queued', triggerKind: 'terminal', triggerLabel: 'npm run build', text: 'Background command finished (exit code 0): npm run build', createdAt: '2026-01-01T12:00:00Z' },
        { id: 'final', type: 'agentMessage', status: 'completed', phase: 'final', text: 'The build passed.', createdAt: '2026-01-01T12:00:18Z' }
      ]
    }
    render(<LocaleProvider loadSettings={false}><AgentResponseBlock turn={turn} isRunning={false} /></LocaleProvider>)
    expect(screen.getByRole('status', { name: /npm run build/ })).toBeInTheDocument()
    expect(screen.queryByText(/Background command finished/)).toBeNull()
  })

  it('reveals the command that woke a turn by opening the activity of the turn that ran it', () => {
    useToolCallRevealStore.setState({ request: null, target: null })
    const origin: ConversationTurn = {
      id: 'origin', threadId: 'thread', status: 'completed', startedAt: '2026-01-01T11:59:00Z', completedAt: '2026-01-01T11:59:30Z',
      items: [
        { id: 'build', type: 'toolCall', status: 'completed', toolName: 'Exec', toolCallId: 'call-build', terminalSessionId: 'term-build', command: 'npm run build', arguments: { command: 'npm run build' }, executionStatus: 'completed', aggregatedOutput: 'built', success: true, presentation: { presentationId: 'core.shell', options: { operation: 'exec' } }, createdAt: '2026-01-01T11:59:01Z' },
        { id: 'reply', type: 'agentMessage', status: 'completed', phase: 'final', text: 'The build is running.', createdAt: '2026-01-01T11:59:30Z' }
      ]
    }
    const woken: ConversationTurn = {
      id: 'woken', threadId: 'thread', status: 'completed', startedAt: '2026-01-01T12:00:00Z', completedAt: '2026-01-01T12:00:18Z',
      items: [
        { id: 'notice', type: 'userMessage', status: 'completed', deliveryMode: 'queued', triggerKind: 'terminal', triggerLabel: 'npm run build', triggerRefId: 'term-build', text: 'Background command finished (exit code 0): npm run build', createdAt: '2026-01-01T12:00:00Z' },
        { id: 'final', type: 'agentMessage', status: 'completed', phase: 'final', text: 'The build passed.', createdAt: '2026-01-01T12:00:18Z' }
      ]
    }
    render(<LocaleProvider loadSettings={false}>
      <AgentResponseBlock turn={origin} isRunning={false} />
      <AgentResponseBlock turn={woken} isRunning={false} />
    </LocaleProvider>)
    expect(screen.getByRole('button', { name: /Worked for 30s/, expanded: false })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Show the command' }))
    const request = useToolCallRevealStore.getState().request
    expect(request).toMatchObject({ threadId: 'thread', match: { terminalSessionId: 'term-build' } })

    act(() => { useToolCallRevealStore.setState({ target: findToolCallRevealTarget([origin, woken], request!) }) })
    expect(useToolCallRevealStore.getState().target).toMatchObject({ turnId: 'origin', callId: 'call-build' })
    expect(screen.getByRole('button', { name: /Worked for 30s/, expanded: true })).toBeInTheDocument()
  })

  it('reveals the call that started a subagent the panel does not list from the turn its notification woke', () => {
    const [kind, taskId, status, buttonName, match, callId] = ['subagent', '/root/reviewer', 'completed', 'Show the subagent', { agentPath: '/root/reviewer' }, 'call-spawn-reviewer'] as const
    useToolCallRevealStore.setState({ request: null, target: null })
    const spawn = (id: string, agentPath: string): ConversationTurn['items'][number] => ({
      id, type: 'toolCall', status: 'completed', toolName: 'SpawnAgent', toolCallId: `call-${id}`, success: true,
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'SpawnAgent' },
      presentation: { presentationId: 'core.subagent', options: { operation: 'spawn' } },
      arguments: { taskName: agentPath.split('/').pop() }, result: JSON.stringify({ agentPath, status: 'running' }),
      createdAt: '2026-01-01T11:59:01Z'
    })
    const origin: ConversationTurn = {
      id: 'origin', threadId: 'thread', status: 'completed', startedAt: '2026-01-01T11:59:00Z', completedAt: '2026-01-01T11:59:30Z',
      items: [
        spawn('spawn-writer', '/root/writer'),
        spawn('spawn-reviewer', '/root/reviewer'),
        { id: 'workflow', type: 'toolCall', status: 'completed', toolName: 'Workflow', toolCallId: 'call-workflow', success: true, arguments: { name: 'review' }, result: JSON.stringify({ runId: 'run_review', name: 'review', status: 'running' }), createdAt: '2026-01-01T11:59:02Z' }
      ]
    }
    const woken: ConversationTurn = {
      id: 'woken', threadId: 'thread', status: 'completed', startedAt: '2026-01-01T12:00:00Z', completedAt: '2026-01-01T12:00:18Z',
      items: [
        { id: 'notice', type: 'userMessage', status: 'completed', deliveryMode: 'queued', triggerKind: kind, triggerLabel: 'review', triggerRefId: taskId, triggerStatus: status, text: 'Task ended', createdAt: '2026-01-01T12:00:00Z' },
        { id: 'final', type: 'agentMessage', status: 'completed', phase: 'final', text: 'Done.', createdAt: '2026-01-01T12:00:18Z' }
      ]
    }
    render(<LocaleProvider loadSettings={false}><AgentResponseBlock turn={woken} isRunning={false} /></LocaleProvider>)
    expect(screen.queryByText('Task ended')).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: buttonName }))
    const request = useToolCallRevealStore.getState().request
    expect(request).toMatchObject({ threadId: 'thread', match })
    expect(findToolCallRevealTarget([origin, woken], request!)).toMatchObject({ turnId: 'origin', callId })
  })

  it('opens the run of a workflow whose notification woke the turn', () => {
    const woken: ConversationTurn = {
      id: 'woken', threadId: 'thread', status: 'completed', startedAt: '2026-01-01T12:00:00Z', completedAt: '2026-01-01T12:00:18Z',
      items: [
        { id: 'notice', type: 'userMessage', status: 'completed', deliveryMode: 'queued', triggerKind: 'workflow', triggerLabel: 'review', triggerRefId: 'run_review', triggerStatus: 'succeeded', text: 'Task ended', createdAt: '2026-01-01T12:00:00Z' },
        { id: 'final', type: 'agentMessage', status: 'completed', phase: 'final', text: 'Done.', createdAt: '2026-01-01T12:00:18Z' }
      ]
    }
    render(<LocaleProvider loadSettings={false}><AgentResponseBlock turn={woken} isRunning={false} /></LocaleProvider>)

    fireEvent.click(screen.getByRole('button', { name: 'Show the workflow' }))
    expect(useViewerTabStore.getState().getThreadState('thread').tabs).toContainEqual(expect.objectContaining({ kind: 'workflow', runId: 'run_review' }))
  })

  it('keeps one tool row across background task notifications and empty messages, and shows no notification', () => {
    const read = (id: string): ConversationTurn['items'][number] => withTestCorePresentation({
      id, type: 'toolCall', status: 'completed', toolName: 'ReadFile', toolCallId: `call-${id}`, success: true, arguments: { path: `${id}.ts` }, result: 'ok', createdAt: '2026-01-01T12:00:01Z'
    }, 'core.read-file', { operation: 'read' })
    const notice = (id: string, command: string, status: string): ConversationTurn['items'][number] => ({
      id, type: 'userMessage', status: 'completed', deliveryMode: 'guidance', triggerKind: 'terminal', triggerLabel: command, triggerRefId: `term_${id}`, triggerStatus: status, text: 'Task ended', createdAt: '2026-01-01T12:00:02Z'
    })
    const turn: ConversationTurn = {
      id: 'turn', threadId: 'thread', status: 'completed', startedAt: '2026-01-01T12:00:00Z', completedAt: '2026-01-01T12:00:30Z',
      items: [
        read('a'),
        notice('lint', 'npm run lint', 'completed'),
        { id: 'blank', type: 'agentMessage', status: 'completed', phase: 'commentary', text: '', createdAt: '2026-01-01T12:00:03Z' },
        read('b'),
        { id: 'commentary', type: 'agentMessage', status: 'completed', phase: 'commentary', text: 'Both files look fine.', createdAt: '2026-01-01T12:00:04Z' },
        notice('test', 'npm test', 'failed')
      ]
    }
    render(<LocaleProvider loadSettings={false}><AgentResponseBlock turn={turn} isRunning={false} /></LocaleProvider>)

    const rows = screen.getAllByTestId('tool-row').map((row) => row.textContent)
    expect(rows).toEqual(['Explored files'])
    expect(screen.queryByText(/npm test/)).toBeNull()
  })
})
