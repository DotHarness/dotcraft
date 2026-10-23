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

const approvalCard = () => s().turns[0].items.find((i) => i.type === 'approvalCard')

beforeEach(() => {
  s().reset()
  s().onTurnStarted(makeTurn())
})

describe('decision mapping', () => {
  const cases: Array<[ApprovalDecision, ApprovalState]> = [
    ['accept', 'accepted'],
    ['acceptForSession', 'acceptedForSession'],
    ['acceptAlways', 'acceptedAlways'],
    ['decline', 'declined'],
    ['cancel', 'cancelled']
  ]

  it.each(cases)('maps "%s" to "%s" for local decisions and server resolutions', (decision, expectedState) => {
    s().onApprovalRequest('bridge-local', SHELL_PARAMS)
    s().onApprovalDecision(decision)
    expect(approvalCard()?.approvalState).toBe(expectedState)

    s().reset()
    s().onTurnStarted(makeTurn())
    s().onApprovalRequest('bridge-resolved', SHELL_PARAMS)
    s().onApprovalResolved({ threadId: 'thread-1', turnId: 'turn-1', requestId: 'req-shell-1', decision })
    expect(approvalCard()?.approvalState).toBe(expectedState)
    expect(s().pendingApproval).toBeNull()
    expect(s().turnStatus).toBe('running')
  })
})

describe('approval card state machine', () => {
  it('onApprovalRequest creates a pending approvalCard and enters waitingApproval', () => {
    s().onApprovalRequest('bridge-1', SHELL_PARAMS)

    const state = s()
    expect(state.turnStatus).toBe('waitingApproval')
    expect(state.pendingApproval).toMatchObject({
      bridgeId: 'bridge-1',
      threadId: 'thread-1',
      turnId: 'turn-1',
      requestId: 'req-shell-1',
      locallySubmittedDecision: null,
      approvalType: 'shell',
      operation: 'npm test'
    })
    expect(state.pendingApproval?.shell).toBeUndefined()

    expect(approvalCard()).toMatchObject({
      approvalRequestId: 'req-shell-1',
      approvalState: 'pending',
      approvalType: 'shell',
      approvalOperation: 'npm test',
      approvalTarget: '/home/dev/project',
      approvalReason: 'Agent wants to execute a shell command'
    })
    expect(approvalCard()?.approvalShell).toBeUndefined()
  })

  it('keeps type-specific request fields on the pending approval and the item', () => {
    s().onApprovalRequest('bridge-shell-ok', {
      ...SHELL_PARAMS,
      shell: {
        reasons: ['Matched a dangerous command.'],
        rememberedPrefixes: [['npm']],
        remembersExactCommand: false
      }
    })

    expect(s().pendingApproval?.shell?.reasons).toEqual(['Matched a dangerous command.'])
    expect(approvalCard()?.approvalShell?.rememberedPrefixes).toEqual([['npm']])

    s().onApprovalRequest('bridge-cu', {
      threadId: 'thread-1',
      turnId: 'turn-1',
      requestId: 'req-cu-1',
      approvalType: 'computerUse',
      operation: 'use',
      target: 'C:/Windows/notepad.exe',
      targetLabel: 'Notepad',
      reason: 'Agent wants to use Notepad.'
    })
    expect(s().pendingApprovals.find((approval) => approval.requestId === 'req-cu-1')).toMatchObject({
      approvalType: 'computerUse',
      target: 'C:/Windows/notepad.exe',
      targetLabel: 'Notepad'
    })
  })

  it('records and clears local approval submission without clearing pendingApproval', () => {
    s().onApprovalRequest('bridge-submit', SHELL_PARAMS)

    s().onApprovalSubmitStarted('accept')
    expect(s().pendingApproval?.locallySubmittedDecision).toBe('accept')
    expect(s().pendingApproval?.bridgeId).toBe('bridge-submit')
    expect(s().turnStatus).toBe('waitingApproval')
    expect(approvalCard()?.approvalState).toBe('pending')

    s().onApprovalSubmitFailed()
    expect(s().pendingApproval?.locallySubmittedDecision).toBeNull()
    expect(s().pendingApproval?.bridgeId).toBe('bridge-submit')
    expect(approvalCard()?.approvalState).toBe('pending')
  })

  it('onApprovalResolved clears locally submitted approvals and applies the resolved decision', () => {
    s().onApprovalRequest('bridge-6a', SHELL_PARAMS)
    s().onApprovalSubmitStarted('accept')

    s().onApprovalResolved({
      threadId: 'thread-1',
      turnId: 'turn-1',
      requestId: 'req-shell-1',
      decision: 'decline'
    })

    expect(s().pendingApproval).toBeNull()
    expect(s().turnStatus).toBe('running')
    expect(approvalCard()?.approvalState).toBe('declined')
  })

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
    expect(approvalCard()?.approvalState).toBe('pending')
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

  it('onApprovalRequest does nothing when no active turn', () => {
    s().reset()
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
    expect(approvalCard()?.id).toBe('approval-item-1')
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
})

describe('user input request lifecycle', () => {
  const USER_INPUT_PARAMS = {
    threadId: 'thread-1',
    requestId: 'req-1',
    turnId: 'turn-1',
    isBlocking: false,
    questions: [
      {
        id: 'provider_id_handling',
        header: 'Provider ID',
        question: 'Should users handle the provider id directly?',
        isOther: true,
        options: [
          { label: 'Auto-generate (Recommended)', description: 'DotCraft creates a stable id.' },
          { label: 'Required', description: 'Users must type the id explicitly.' }
        ]
      }
    ]
  }

  it('stores a pending request in waitingInput until it is resolved', () => {
    s().onUserInputRequest('bridge-input', USER_INPUT_PARAMS)

    expect(s().turnStatus).toBe('waitingInput')
    expect(s().pendingUserInput?.bridgeId).toBe('bridge-input')
    expect(s().pendingUserInput?.requestId).toBe('req-1')
    expect(s().pendingUserInput?.questions[0].options).toHaveLength(2)

    s().onUserInputResolved()

    expect(s().turnStatus).toBe('running')
    expect(s().pendingUserInput).toBeNull()
  })

  it('onUserInputRequest can arrive before history hydration and still show a pending composer', () => {
    s().reset()

    s().onUserInputRequest('bridge-input', USER_INPUT_PARAMS)

    expect(s().turnStatus).toBe('waitingInput')
    expect(s().activeTurnId).toBe('turn-1')
    expect(s().pendingUserInput?.bridgeId).toBe('bridge-input')
  })
})
