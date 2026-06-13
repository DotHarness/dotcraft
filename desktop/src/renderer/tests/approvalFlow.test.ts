import { describe, it, expect, beforeEach } from 'vitest'
import { useConversationStore } from '../stores/conversationStore'
import type { ApprovalDecision, ApprovalState } from '../types/conversation'

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
  threadId: 'thread-1',
  turnId: 'turn-1',
  requestId: 'req-shell-1',
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
    expect(state.pendingApproval?.threadId).toBe('thread-1')
    expect(state.pendingApproval?.turnId).toBe('turn-1')
    expect(state.pendingApproval?.requestId).toBe('req-shell-1')
    expect(state.pendingApproval?.locallySubmittedDecision).toBeNull()
    expect(state.pendingApproval?.approvalType).toBe('shell')
    expect(state.pendingApproval?.operation).toBe('npm test')

    const items = state.turns[0].items
    const approvalItem = items.find((i) => i.type === 'approvalCard')
    expect(approvalItem).toBeDefined()
    expect(approvalItem?.approvalRequestId).toBe('req-shell-1')
    expect(approvalItem?.approvalState).toBe('pending')
    expect(approvalItem?.approvalType).toBe('shell')
    expect(approvalItem?.approvalOperation).toBe('npm test')
    expect(approvalItem?.approvalTarget).toBe('/home/dev/project')
    expect(approvalItem?.approvalReason).toBe('Agent wants to execute a shell command')
  })

  it('records and clears local approval submission without clearing pendingApproval', () => {
    s().onApprovalRequest('bridge-submit', SHELL_PARAMS)

    s().onApprovalSubmitStarted('accept')
    expect(s().pendingApproval?.locallySubmittedDecision).toBe('accept')
    expect(s().pendingApproval?.bridgeId).toBe('bridge-submit')
    expect(s().turnStatus).toBe('waitingApproval')
    let approvalItem = s().turns[0].items.find((i) => i.type === 'approvalCard')
    expect(approvalItem?.approvalState).toBe('pending')

    s().onApprovalSubmitFailed()
    expect(s().pendingApproval?.locallySubmittedDecision).toBeNull()
    expect(s().pendingApproval?.bridgeId).toBe('bridge-submit')
    approvalItem = s().turns[0].items.find((i) => i.type === 'approvalCard')
    expect(approvalItem?.approvalState).toBe('pending')
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

    s().onApprovalResolved({
      threadId: 'thread-1',
      turnId: 'turn-1',
      requestId: 'req-shell-1',
      decision: 'accept'
    })
    expect(s().turnStatus).toBe('running')
    expect(s().pendingApproval).toBeNull()
    const approvalItem = s().turns[0].items.find((i) => i.type === 'approvalCard')
    expect(approvalItem?.approvalState).toBe('accepted')
  })

  it('onApprovalResolved clears locally submitted approvals and applies the resolved decision', () => {
    s().onApprovalRequest('bridge-6a', SHELL_PARAMS)
    s().onApprovalSubmitStarted('accept')
    expect(s().pendingApproval?.locallySubmittedDecision).toBe('accept')

    s().onApprovalResolved({
      threadId: 'thread-1',
      turnId: 'turn-1',
      requestId: 'req-shell-1',
      decision: 'decline'
    })

    expect(s().pendingApproval).toBeNull()
    expect(s().turnStatus).toBe('running')
    const approvalItem = s().turns[0].items.find((i) => i.type === 'approvalCard')
    expect(approvalItem?.approvalState).toBe('declined')
  })

  const resolvedDecisionCases: Array<[ApprovalDecision, ApprovalState]> = [
    ['accept', 'accepted'],
    ['acceptForSession', 'acceptedForSession'],
    ['acceptAlways', 'acceptedAlways'],
    ['decline', 'declined'],
    ['cancel', 'cancelled']
  ]

  for (const [decision, expectedState] of resolvedDecisionCases) {
    it(`onApprovalResolved maps "${decision}" to "${expectedState}"`, () => {
      s().onApprovalRequest(`bridge-resolved-${decision}`, SHELL_PARAMS)

      s().onApprovalResolved({
        threadId: 'thread-1',
        turnId: 'turn-1',
        requestId: 'req-shell-1',
        decision
      })

      const approvalItem = s().turns[0].items.find((i) => i.type === 'approvalCard')
      expect(approvalItem?.approvalState).toBe(expectedState)
      expect(s().pendingApproval).toBeNull()
    })
  }

  it('onApprovalResolved ignores unrelated approval requests', () => {
    s().onApprovalRequest('bridge-6b', SHELL_PARAMS)

    s().onApprovalResolved({
      threadId: 'thread-1',
      turnId: 'turn-1',
      requestId: 'req-other',
      decision: 'accept'
    })

    expect(s().turnStatus).toBe('waitingApproval')
    expect(s().pendingApproval?.requestId).toBe('req-shell-1')
    const approvalItem = s().turns[0].items.find((i) => i.type === 'approvalCard')
    expect(approvalItem?.approvalState).toBe('pending')
  })

  it('onApprovalNoLongerPending clears only the matching pending approval', () => {
    s().onApprovalRequest('bridge-6c', SHELL_PARAMS)

    s().onApprovalNoLongerPending({
      threadId: 'thread-1',
      turnId: 'turn-1',
      requestId: 'req-other',
      nextTurnStatus: 'running'
    })
    expect(s().pendingApproval?.bridgeId).toBe('bridge-6c')
    expect(s().turnStatus).toBe('waitingApproval')

    s().onApprovalNoLongerPending({
      threadId: 'thread-1',
      turnId: 'turn-1',
      requestId: 'req-shell-1',
      nextTurnStatus: 'idle'
    })
    expect(s().pendingApproval).toBeNull()
    expect(s().turnStatus).toBe('idle')
    expect(s().activeTurnId).toBeNull()
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
    const { threadId, turnId, requestId, ...paramsWithoutTurn } = SHELL_PARAMS
    void threadId
    void turnId
    void requestId
    s().onApprovalRequest('bridge-x', paramsWithoutTurn)
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

  it('queues multiple pending approvals and advances composer after each resolution', () => {
    s().onApprovalRequest('bridge-1', {
      ...SHELL_PARAMS,
      itemId: 'approval-item-1'
    })
    s().onApprovalRequest('bridge-2', {
      ...SHELL_PARAMS,
      requestId: 'req-shell-2',
      operation: 'npm run lint',
      itemId: 'approval-item-2'
    })
    s().onApprovalRequest('bridge-3', {
      ...SHELL_PARAMS,
      requestId: 'req-shell-3',
      operation: 'npm run build',
      itemId: 'approval-item-3'
    })

    expect(s().turnStatus).toBe('waitingApproval')
    expect(s().pendingApprovals.map((approval) => approval.requestId)).toEqual([
      'req-shell-1',
      'req-shell-2',
      'req-shell-3'
    ])
    expect(s().pendingApproval?.bridgeId).toBe('bridge-1')

    s().onApprovalResolved({
      threadId: 'thread-1',
      turnId: 'turn-1',
      requestId: 'req-shell-1',
      decision: 'accept'
    })
    expect(s().turnStatus).toBe('waitingApproval')
    expect(s().pendingApproval?.bridgeId).toBe('bridge-2')
    expect(s().pendingApprovals.map((approval) => approval.requestId)).toEqual([
      'req-shell-2',
      'req-shell-3'
    ])

    s().onApprovalResolved({
      threadId: 'thread-1',
      turnId: 'turn-1',
      requestId: 'req-shell-2',
      decision: 'decline'
    })
    expect(s().turnStatus).toBe('waitingApproval')
    expect(s().pendingApproval?.bridgeId).toBe('bridge-3')

    s().onApprovalResolved({
      threadId: 'thread-1',
      turnId: 'turn-1',
      requestId: 'req-shell-3',
      decision: 'accept'
    })
    expect(s().pendingApproval).toBeNull()
    expect(s().pendingApprovals).toHaveLength(0)
    expect(s().turnStatus).toBe('running')
  })

  it('applies local submission state and advances only the targeted queued approval', () => {
    s().onApprovalRequest('bridge-1', {
      ...SHELL_PARAMS,
      itemId: 'approval-item-1'
    })
    s().onApprovalRequest('bridge-2', {
      ...SHELL_PARAMS,
      requestId: 'req-shell-2',
      operation: 'npm run lint',
      itemId: 'approval-item-2'
    })

    const secondTarget = {
      threadId: 'thread-1',
      turnId: 'turn-1',
      requestId: 'req-shell-2',
      itemId: 'approval-item-2',
      bridgeId: 'bridge-2'
    }

    s().onApprovalSubmitStarted('acceptForSession', secondTarget)

    expect(s().pendingApproval?.requestId).toBe('req-shell-1')
    expect(s().pendingApproval?.locallySubmittedDecision).toBeNull()
    expect(
      s().pendingApprovals.find((approval) => approval.requestId === 'req-shell-2')?.locallySubmittedDecision
    ).toBe('acceptForSession')

    s().onApprovalDecision('acceptForSession', secondTarget)

    const approvalItems = s().turns[0].items.filter((item) => item.type === 'approvalCard')
    expect(approvalItems.find((item) => item.id === 'approval-item-1')?.approvalState).toBe('pending')
    expect(approvalItems.find((item) => item.id === 'approval-item-2')?.approvalState).toBe('acceptedForSession')
    expect(s().pendingApproval?.requestId).toBe('req-shell-1')
    expect(s().pendingApprovals.map((approval) => approval.requestId)).toEqual(['req-shell-1'])

    s().onApprovalSubmitFailed(secondTarget)

    expect(s().pendingApproval?.requestId).toBe('req-shell-1')
    expect(s().pendingApprovals.map((approval) => approval.requestId)).toEqual(['req-shell-1'])
    expect(s().pendingApproval?.locallySubmittedDecision).toBeNull()
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
