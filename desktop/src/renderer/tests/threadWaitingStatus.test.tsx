import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { useThreadWaitingStatus } from '../components/sidebar/threadWaitingStatus'
import { createThreadConversation, releaseThreadConversation } from '../stores/conversationStore'
import { useUIStore } from '../stores/uiStore'

const threadId = 'fixture-plan'
const runtime = { waitingOnApproval: false, waitingOnInput: false, waitingOnPlanConfirmation: true }

beforeEach(() => useUIStore.setState({ planApprovalDismissed: {} }))
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
})
