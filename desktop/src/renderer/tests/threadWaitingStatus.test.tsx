import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ThreadTask, ThreadTasksSnapshot } from '../../shared/threadTasks'
import { useThreadWaitingStatus } from '../components/sidebar/threadWaitingStatus'
import { createThreadConversation, releaseThreadConversation } from '../stores/conversationStore'
import { useUIStore } from '../stores/uiStore'
import { installDesktopApiMock } from './desktopApiMock'

const threadId = 'fixture-plan'
const runtime = { waitingOnApproval: false, waitingOnInput: false, waitingOnPlanConfirmation: true }

let publishTasks: (snapshot: ThreadTasksSnapshot) => void = () => {}

beforeEach(() => {
  useUIStore.setState({ planApprovalDismissed: {} })
  installDesktopApiMock({
    threadTasks: {
      onChanged: vi.fn((listener: (snapshot: ThreadTasksSnapshot) => void) => {
        publishTasks = listener
        return () => {}
      })
    }
  })
})
afterEach(() => releaseThreadConversation(threadId))

describe('thread waiting classification', () => {
  it('keeps plan confirmation distinct from approval and input, with matching precedence', () => {
    const { result, rerender } = renderHook(({ flags }) => useThreadWaitingStatus(threadId, flags), {
      initialProps: { flags: { ...runtime, waitingOnApproval: true, waitingOnInput: true } }
    })
    expect(result.current).toBe('approval')
    rerender({ flags: { ...runtime, waitingOnInput: true } })
    expect(result.current).toBe('input')
    rerender({ flags: runtime })
    expect(result.current).toBe('plan')
    rerender({ flags: { ...runtime, waitingOnPlanConfirmation: false } })
    expect(result.current).toBeNull()
  })

  it('hides only the selected plan presentation and applies dismissal to its known turn, not future plans', () => {
    const conversation = createThreadConversation(threadId)
    conversation.setState({ restoredPlanTurnId: 'plan-one' })
    const { result, rerender } = renderHook(({ selected }) =>
      useThreadWaitingStatus(threadId, runtime, selected), { initialProps: { selected: true } })
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

  it('reports a chat as needing an answer while its new-task offer waits, without a server flag', () => {
    const offer = (state: ThreadTask['state']): ThreadTasksSnapshot => ({
      threadId,
      tasks: [{
        id: 'task_00000001',
        threadId,
        workspacePath: 'C:/work',
        kind: 'offer',
        state,
        title: 'Write the release notes',
        defaults: {},
        createdAt: '2026-11-15T00:00:00.000Z'
      }]
    })
    const idle = { ...runtime, waitingOnPlanConfirmation: false }
    const { result } = renderHook(() => useThreadWaitingStatus(threadId, idle))
    expect(result.current).toBeNull()

    act(() => publishTasks(offer('pending')))
    expect(result.current).toBe('input')

    act(() => publishTasks(offer('keptHere')))
    expect(result.current).toBeNull()
  })
})
