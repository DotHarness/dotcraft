import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { describe, expect, it } from 'vitest'

const rendererRoot = resolve(__dirname, '..')

function readRendererFile(path: string): string {
  return readFileSync(resolve(rendererRoot, path), 'utf8').replace(/\r\n/g, '\n')
}

describe('App active thread subscription source', () => {
  it('waits for thread subscription readiness before restoring parked approvals', () => {
    const appSource = readRendererFile('App.tsx')

    expect(appSource).toContain('threadSubscriptionIntentRef')
    expect(appSource).toContain('threadSubscriptionInFlightRef')
    expect(appSource).toContain('threadSubscriptionReadyRef')
    expect(appSource).toContain('threadSubscriptionOperationsRef')
    expect(appSource).toContain('const restoreGateToken = beginThreadRestoreGate(requestedId)')
    expect(appSource).toContain('const subscriptionReady = ensureThreadSubscribed(requestedId, { replayRecent: true })')
    expect(appSource).toContain("sendRequest('thread/subscribe', requestParams)")
    expect(appSource).toContain('queueThreadUnsubscribe(prev)')
    expect(appSource).toContain('realtimeScopeThreadId: requestedId')

    const gateIndex = appSource.indexOf('const restoreGateToken = beginThreadRestoreGate(requestedId)')
    const subscribeIndex = appSource.indexOf('const subscriptionReady = ensureThreadSubscribed(requestedId, { replayRecent: true })')
    const awaitIndex = appSource.indexOf('if (!await subscriptionReady)')
    const clearIndex = appSource.indexOf('clearThreadRestoreGate(requestedId, restoreGateToken)', awaitIndex)
    const approvalsIndex = appSource.indexOf('consumeParkedApprovals(requestedId)')
    const inputIndex = appSource.indexOf('consumeParkedUserInput(requestedId)')

    expect(gateIndex).toBeGreaterThan(-1)
    expect(subscribeIndex).toBeGreaterThan(-1)
    expect(subscribeIndex).toBeGreaterThan(gateIndex)
    expect(awaitIndex).toBeGreaterThan(subscribeIndex)
    expect(clearIndex).toBeGreaterThan(awaitIndex)
    expect(approvalsIndex).toBeGreaterThan(clearIndex)
    expect(inputIndex).toBeGreaterThan(clearIndex)
  })

  it('parks replayed interactive requests while active thread restore is gated', () => {
    const appSource = readRendererFile('App.tsx')

    expect(appSource).toContain('threadRestoreGateRef')
    expect(appSource).toContain('isThreadRestoreGated(threadId)')
    expect(appSource).toContain('threadId !== activeThreadId || isThreadRestoreGated(threadId)')
    expect(appSource).toContain('parkApproval(threadId')
    expect(appSource).toContain('parkUserInput(threadId')
  })

  it('forces replay when runtime says an active thread is waiting but no composer is pending', () => {
    const appSource = readRendererFile('App.tsx')

    expect(appSource).toContain('shouldReplayInteractiveRequests')
    expect(appSource).toContain('runtimeSnapshot.waitingOnApproval && pendingApprovals.length === 0')
    expect(appSource).toContain('runtimeSnapshot.waitingOnInput && conversation.pendingUserInput == null')
    expect(appSource).toContain("ensureSubscribed(threadId, { replayRecent: true, forceReplay: true })")
  })

  it('reconciles active thread snapshots when local restore state is stale', () => {
    const appSource = readRendererFile('App.tsx')

    expect(appSource).toContain('conversationNeedsFullSnapshotReconcile')
    expect(appSource).toContain('reconcileActiveThreadSnapshotRef')
    expect(appSource).toContain('const reconcileActiveThreadSnapshot = useCallback')
    expect(appSource).toContain("sendRequest('thread/read', {")
    expect(appSource).toContain('includeTurns: true')
    expect(appSource).toContain('conversation.setTurns(rawTurns.map(wireTurnToConversationTurn), {')
    expect(appSource).toContain('realtimeScopeThreadId: requestedId')
    expect(appSource).toContain("reconcileActiveThreadSnapshotRef.current?.('runtimeChanged')")
    expect(appSource).toContain("reconcileActiveThreadSnapshot('metadata-refresh')")
  })

  it('schedules a thread-bound snapshot reconcile after interactive responses are accepted', () => {
    const appSource = readRendererFile('App.tsx')

    expect(appSource).toContain('const scheduleActiveThreadSnapshotReconcile = useCallback')
    expect(appSource).toContain('const scheduledThreadId = useThreadStore.getState().activeThreadId')
    expect(appSource).toContain("reconcileActiveThreadSnapshotRef.current?.('interactive-response')")
    expect(appSource).toContain('onInteractionResponseAccepted={scheduleActiveThreadSnapshotReconcile}')
  })
})
