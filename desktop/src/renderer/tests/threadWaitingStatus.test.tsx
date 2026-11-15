import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ThreadTask, ThreadTasksSnapshot } from '../../shared/threadTasks'
import { useThreadWaitingStatus } from '../components/sidebar/threadWaitingStatus'
import { createThreadConversation, releaseThreadConversation } from '../stores/conversationStore'
import { useThreadTasksStore } from '../stores/threadTasksStore'
import { useUIStore } from '../stores/uiStore'
import { installDesktopApiMock } from './desktopApiMock'

const threadId = 'fixture-plan'
const runtime = { waitingOnApproval: false, waitingOnInput: false, waitingOnPlanConfirmation: true }

let publishTasks: (snapshot: ThreadTasksSnapshot) => void = () => {}

function offer(state: ThreadTask['state'], id = threadId): ThreadTasksSnapshot {
  return {
    threadId: id,
    tasks: [{
      id: 'task_00000001',
      threadId: id,
      workspacePath: 'C:/work',
      kind: 'offer',
      state,
      title: 'Write the release notes',
      defaults: {},
      createdAt: '2026-11-15T00:00:00.000Z'
    }]
  }
}

beforeEach(() => {
  useThreadTasksStore.setState({ byThread: {} })
  useUIStore.setState({ planApprovalDismissed: {} })
  installDesktopApiMock({
    threadTasks: {
      list: async (id) => ({ threadId: id, tasks: [] }),
      onChanged: vi.fn((listener: (snapshot: ThreadTasksSnapshot) => void) => {
        publishTasks = listener
        return () => {}
      })
    }
  })
})
afterEach(() => releaseThreadConversation(threadId))

describe('thread waiting classification', () => {
  it('keeps plan confirmation distinct from approval and input, with matching precedence', async () => {
    const { result, rerender } = await act(async () => renderHook(({ flags }) => useThreadWaitingStatus(threadId, flags), {
      initialProps: { flags: { ...runtime, waitingOnApproval: true, waitingOnInput: true } }
    }))
    expect(result.current).toBe('approval')
    rerender({ flags: { ...runtime, waitingOnInput: true } })
    expect(result.current).toBe('input')
    rerender({ flags: runtime })
    expect(result.current).toBe('plan')
    rerender({ flags: { ...runtime, waitingOnPlanConfirmation: false } })
    expect(result.current).toBeNull()
  })

  it('hides only the selected plan presentation and applies dismissal to its known turn, not future plans', async () => {
    const conversation = createThreadConversation(threadId)
    conversation.setState({ restoredPlanTurnId: 'plan-one' })
    const { result, rerender } = await act(async () => renderHook(({ selected }) =>
      useThreadWaitingStatus(threadId, runtime, selected), { initialProps: { selected: true } }))
    expect(result.current).toBeNull()

    rerender({ selected: false })
    expect(result.current).toBe('plan')
    act(() => useUIStore.getState().dismissPlanApproval('plan-one'))
    expect(result.current).toBeNull()

    conversation.setState({ restoredPlanTurnId: 'plan-two' })
    rerender({ selected: false })
    expect(result.current).toBe('plan')
    rerender({ selected: true })
    expect(result.current).toBeNull()
  })

  it('loads existing offers on mount and when the sidebar row changes thread', async () => {
    window.api.threadTasks.list = async (id) => offer('pending', id)
    const idle = { ...runtime, waitingOnPlanConfirmation: false }
    const { result, rerender } = renderHook(({ id }) => useThreadWaitingStatus(id, idle), {
      initialProps: { id: threadId }
    })

    await waitFor(() => expect(result.current).toBe('input'))

    rerender({ id: 'fixture-other' })
    await waitFor(() => expect(result.current).toBe('input'))
  })

  it('reports a chat as needing an answer while its new-task offer waits, without a server flag', async () => {
    const idle = { ...runtime, waitingOnPlanConfirmation: false }
    const { result } = await act(async () => renderHook(() => useThreadWaitingStatus(threadId, idle)))
    expect(result.current).toBeNull()

    act(() => publishTasks(offer('pending')))
    expect(result.current).toBe('input')

    act(() => publishTasks(offer('keptHere')))
    expect(result.current).toBeNull()
  })
})
