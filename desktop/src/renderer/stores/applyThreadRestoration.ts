import type { ClientRequestMethods } from '@dotcraft/sdk/contracts'
import { getThreadConversation } from './conversationStore'
import { restartThreadHistory } from './threadHistoryStore'
import { useThreadStore } from './threadStore'
import type { Thread } from '../types/thread'
import { wireTurnToConversationTurn } from '../types/conversation'
import { runtimeSnapshotFromThread } from '../utils/threadRuntime'
import { normalizeRemoteFileTransferProgress } from '../utils/remoteToolHostDisplay'
import { mergeRestoredTurns } from './turnItemsHydration'

export type ThreadResumeResult = ClientRequestMethods['thread/resume']['result']

export function applyThreadRestoration(result: ThreadResumeResult): void {
  const thread = result.thread as unknown as Thread
  const threadId = thread.id
  const store = getThreadConversation(threadId)
  if (!store) return
  const current = result.currentState
  const snapshotTurns = [...(result.initialTurnsPage?.data ?? [])].reverse()
    .map(turn => wireTurnToConversationTurn(turn as unknown as Record<string, unknown>))
  const turns = mergeRestoredTurns(store.getState().turns, snapshotTurns)
  const pending = new Set(current.pendingRequests.map(request => request.requestId))
  const threads = useThreadStore.getState()
  const approvals = threads.consumeParkedApprovals(threadId).filter(request =>
    pending.has(String(request.rawParams.requestId)))
  const input = threads.consumeParkedUserInput(threadId)
  for (const request of approvals) threads.parkApproval(threadId, request)
  if (input && pending.has(String(input.rawParams.requestId))) threads.parkUserInput(threadId, input)

  restartThreadHistory(threadId, turns, result.initialTurnsPage?.nextCursor ?? null)
  store.getState().setTurns(turns)
  const runtime = runtimeSnapshotFromThread(thread)
  const activeTurn = turns.find(turn => turn.id === current.turnId)
  const active = activeTurn?.status === 'running' || activeTurn?.status === 'waitingApproval' || activeTurn?.status === 'waitingInput'
  const liveItem = active ? snapshotTurns.find(turn => turn.id === current.turnId)?.items.findLast(item =>
    item.status !== 'completed' && (item.type === 'agentMessage' || item.type === 'reasoningContent')
    && activeTurn?.items.find(candidate => candidate.id === item.id)?.status !== 'completed') : undefined
  const old = store.getState()
  const pendingApprovals = old.pendingApprovals.filter(request => pending.has(request.requestId))
  store.setState({
    threadMode: thread.configuration?.mode === 'plan' ? 'plan' : 'agent',
    restoredPlanTurnId: current.planTurnId ?? null,
    plan: thread.plan ?? null,
    queuedInputs: thread.queuedInputs ?? [],
    turnStatus: runtime.waitingOnApproval ? 'waitingApproval'
      : runtime.waitingOnInput ? 'waitingInput'
        : runtime.running ? 'running' : 'idle',
    activeTurnId: runtime.running || runtime.waitingOnApproval || runtime.waitingOnInput
      ? current.turnId ?? null : null,
    turnStartedAt: activeTurn ? Date.parse(activeTurn.startedAt) : null,
    pendingApprovals,
    pendingApproval: pendingApprovals[0] ?? null,
    pendingUserInput: old.pendingUserInput && pending.has(old.pendingUserInput.requestId) ? old.pendingUserInput : null,
    activeItemId: liveItem?.id ?? null,
    streamingMessage: liveItem?.type === 'agentMessage' ? liveItem.text ?? '' : '',
    streamingReasoning: liveItem?.type === 'reasoningContent' ? liveItem.reasoning ?? '' : '',
    turns: store.getState().turns.map(turn => ({
      ...turn,
      items: turn.items.map(item => {
        const argumentsPreview = current.toolArguments?.[item.id]
        const progress = current.toolProgress?.[item.id]
        return {
          ...item,
          ...(item.id === liveItem?.id ? { status: 'streaming' as const } : {}),
          ...(argumentsPreview != null ? { argumentsPreview } : {}),
          ...(progress ? { transferProgress: normalizeRemoteFileTransferProgress(progress.progress) ?? undefined } : {})
        }
      })
    }))
  })
  store.getState().setContextUsage(thread.contextUsage ?? null)
  store.getState().setMaintenanceKind(runtime.maintenanceKind)
  if (current.turnId && current.diff != null) {
    store.getState().onTurnDiffUpdated({ turnId: current.turnId, diff: current.diff })
  }
  threads.upsertThreads([thread])
  threads.setActiveThread({ ...thread, turns: [] })
  threads.applyRuntimeSnapshot(threadId, runtime, {
    isDesktopOrigin: thread.originChannel?.toLowerCase() === 'dotcraft-desktop'
  })
}
