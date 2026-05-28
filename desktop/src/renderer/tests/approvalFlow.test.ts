import { describe, it, expect, beforeEach } from 'vitest'
import { useConversationStore } from '../stores/conversationStore'
import type { ApprovalDecision } from '../types/conversation'

const s = () => useConversationStore.getState()

function makeTurn(id = 'turn-1') {
  return {
    id,
    threadId: 'thread-1',
    status: 'running',
    items: [],
    startedAt: new Date().toISOString()
  }
}

const SHELL_PARAMS = {
  approvalType: 'shell',
  operation: 'npm test',
  target: '/home/dev/project',
  reason: 'Agent wants to execute a shell command'
}

beforeEach(() => {
  s().reset()
  s().onTurnStarted(makeTurn())
})

// ---------------------------------------------------------------------------
// Decision mapping: each decision produces the correct approvalState
// ---------------------------------------------------------------------------

describe('decision mapping', () => {
  const cases: Array<[ApprovalDecision, string]> = [
    ['accept', 'accepted'],
    ['acceptForSession', 'acceptedForSession'],
    ['acceptAlways', 'acceptedAlways'],
    ['decline', 'declined'],
    ['cancel', 'cancelled']
  ]

  for (const [decision, expectedState] of cases) {
    it(`decision "${decision}" → approvalState "${expectedState}"`, () => {
      s().onApprovalRequest('bridge-1', SHELL_PARAMS)
      s().onApprovalDecision(decision)

      const items = s().turns.find((t) => t.id === 'turn-1')?.items ?? []
      const approvalItem = items.find((i) => i.type === 'approvalCard')
      expect(approvalItem?.approvalState).toBe(expectedState)
    })
  }
})

// ---------------------------------------------------------------------------
// Card state machine
// ---------------------------------------------------------------------------

describe('approval card state machine', () => {
  it('pending → onApprovalRequest creates approvalCard item with pending state', () => {
    s().onApprovalRequest('bridge-1', SHELL_PARAMS)

    const state = s()
    expect(state.turnStatus).toBe('waitingApproval')
    expect(state.pendingApproval).not.toBeNull()
    expect(state.pendingApproval?.bridgeId).toBe('bridge-1')
    expect(state.pendingApproval?.approvalType).toBe('shell')
    expect(state.pendingApproval?.operation).toBe('npm test')

    const items = state.turns[0].items
    const approvalItem = items.find((i) => i.type === 'approvalCard')
    expect(approvalItem).toBeDefined()
    expect(approvalItem?.approvalState).toBe('pending')
    expect(approvalItem?.approvalType).toBe('shell')
    expect(approvalItem?.approvalOperation).toBe('npm test')
    expect(approvalItem?.approvalTarget).toBe('/home/dev/project')
    expect(approvalItem?.approvalReason).toBe('Agent wants to execute a shell command')
  })

  it('pending → accepted after onApprovalDecision("accept")', () => {
    s().onApprovalRequest('bridge-2', SHELL_PARAMS)
    s().onApprovalDecision('accept')

    const items = s().turns[0].items
    const approvalItem = items.find((i) => i.type === 'approvalCard')
    expect(approvalItem?.approvalState).toBe('accepted')
  })

  it('pending → declined after onApprovalDecision("decline")', () => {
    s().onApprovalRequest('bridge-3', SHELL_PARAMS)
    s().onApprovalDecision('decline')

    const approvalItem = s().turns[0].items.find((i) => i.type === 'approvalCard')
    expect(approvalItem?.approvalState).toBe('declined')
  })

  it('pending → cancelled after onApprovalDecision("cancel")', () => {
    s().onApprovalRequest('bridge-4', SHELL_PARAMS)
    s().onApprovalDecision('cancel')

    const approvalItem = s().turns[0].items.find((i) => i.type === 'approvalCard')
    expect(approvalItem?.approvalState).toBe('cancelled')
  })

  it('pending → timedOut after onApprovalTimeout()', () => {
    s().onApprovalRequest('bridge-5', SHELL_PARAMS)
    s().onApprovalTimeout()

    const approvalItem = s().turns[0].items.find((i) => i.type === 'approvalCard')
    expect(approvalItem?.approvalState).toBe('timedOut')
    // pendingApproval is cleared on timeout
    expect(s().pendingApproval).toBeNull()
  })

  it('onApprovalResolved clears pendingApproval and restores running status', () => {
    s().onApprovalRequest('bridge-6', SHELL_PARAMS)
    expect(s().turnStatus).toBe('waitingApproval')

    s().onApprovalResolved()
    expect(s().turnStatus).toBe('running')
    expect(s().pendingApproval).toBeNull()
  })

  it('file approval type renders with correct fields', () => {
    const fileParams = {
      approvalType: 'file',
      operation: 'write src/main.ts',
      target: 'src/main.ts',
      reason: 'Agent wants to write a file'
    }
    s().onApprovalRequest('bridge-7', fileParams)

    const approvalItem = s().turns[0].items.find((i) => i.type === 'approvalCard')
    expect(approvalItem?.approvalType).toBe('file')
    expect(approvalItem?.approvalOperation).toBe('write src/main.ts')
  })

  it('onApprovalRequest does nothing when no active turn', () => {
    s().reset()
    // No turn started — activeTurnId is null
    s().onApprovalRequest('bridge-x', SHELL_PARAMS)
    expect(s().pendingApproval).toBeNull()
    expect(s().turns).toHaveLength(0)
  })

  it('onApprovalRequest restores from params.turnId when replay arrives for a hydrated waiting turn', () => {
    s().reset()
    s().setTurns([{ ...makeTurn('turn-replay'), status: 'waitingApproval' }])

    s().onApprovalRequest('bridge-replay', {
      ...SHELL_PARAMS,
      turnId: 'turn-replay',
      itemId: 'approval-item-1'
    })

    expect(s().activeTurnId).toBe('turn-replay')
    expect(s().turnStatus).toBe('waitingApproval')
    expect(s().pendingApproval?.bridgeId).toBe('bridge-replay')
    const approvalItem = s().turns[0].items.find((i) => i.type === 'approvalCard')
    expect(approvalItem?.id).toBe('approval-item-1')
  })

  it('onApprovalDecision does nothing when no pendingApproval', () => {
    // No approval request has been issued
    expect(() => s().onApprovalDecision('accept')).not.toThrow()
  })
})

// ---------------------------------------------------------------------------
// Integration: full approval lifecycle
// ---------------------------------------------------------------------------

describe('approval lifecycle integration', () => {
  it('complete flow: request → decision → resolved restores idle-capable state', () => {
    // 1. Approval request arrives
    s().onApprovalRequest('bridge-8', SHELL_PARAMS)
    expect(s().turnStatus).toBe('waitingApproval')

    // 2. User accepts
    s().onApprovalDecision('accept')
    const approvalItem = s().turns[0].items.find((i) => i.type === 'approvalCard')
    expect(approvalItem?.approvalState).toBe('accepted')

    // 3. Server sends item/approval/resolved
    s().onApprovalResolved()
    expect(s().turnStatus).toBe('running')
    expect(s().pendingApproval).toBeNull()

    // 4. Turn completes normally
    const completedTurn = { ...makeTurn(), status: 'completed', completedAt: new Date().toISOString() }
    s().onTurnCompleted(completedTurn)
    expect(s().turnStatus).toBe('idle')
  })

  it('timeout flow: request → timeout → turn failed', () => {
    s().onApprovalRequest('bridge-9', SHELL_PARAMS)
    expect(s().turnStatus).toBe('waitingApproval')

    // Approval times out
    s().onApprovalTimeout()
    expect(s().pendingApproval).toBeNull()

    const approvalItem = s().turns[0].items.find((i) => i.type === 'approvalCard')
    expect(approvalItem?.approvalState).toBe('timedOut')

    // Turn then fails
    s().onTurnFailed(makeTurn(), 'Approval timed out')
    expect(s().turnStatus).toBe('idle')
  })
})

describe('user input request lifecycle', () => {
  const USER_INPUT_PARAMS = {
    requestId: 'req-1',
    turnId: 'turn-1',
    questions: [
      {
        id: 'provider_id_handling',
        header: 'Provider ID',
        question: 'Should users handle the provider id directly?',
        isOther: true,
        options: [
          {
            label: 'Auto-generate (Recommended)',
            description: 'DotCraft creates a stable id.'
          },
          {
            label: 'Required',
            description: 'Users must type the id explicitly.'
          }
        ]
      }
    ]
  }

  it('onUserInputRequest stores pending request and enters waitingInput', () => {
    s().onUserInputRequest('bridge-input', USER_INPUT_PARAMS)

    expect(s().turnStatus).toBe('waitingInput')
    expect(s().pendingUserInput?.bridgeId).toBe('bridge-input')
    expect(s().pendingUserInput?.requestId).toBe('req-1')
    expect(s().pendingUserInput?.questions[0].options).toHaveLength(2)
  })

  it('onUserInputRequest can arrive before history hydration and still show a pending composer', () => {
    s().reset()

    s().onUserInputRequest('bridge-input', USER_INPUT_PARAMS)

    expect(s().turnStatus).toBe('waitingInput')
    expect(s().activeTurnId).toBe('turn-1')
    expect(s().pendingUserInput?.bridgeId).toBe('bridge-input')
  })

  it('onUserInputResolved clears pending request and restores running status', () => {
    s().onUserInputRequest('bridge-input', USER_INPUT_PARAMS)
    expect(s().turnStatus).toBe('waitingInput')

    s().onUserInputResolved()

    expect(s().turnStatus).toBe('running')
    expect(s().pendingUserInput).toBeNull()
  })
})
