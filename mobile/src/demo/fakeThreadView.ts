import type { FakeItem, FakeThread } from './fakeComputer'
import { withSummaries } from './fakeHistorySummary'

const CONTEXT_WINDOW = 400_000

export function threadSummary(thread: FakeThread) {
  const active = thread.turns.at(-1)?.status === 'running' ? thread.turns.at(-1) : null
  return {
    id: thread.id, displayName: thread.displayName, createdAt: thread.createdAt, lastActiveAt: thread.lastActiveAt,
    status: thread.archived ? 'archived' : 'active',
    runtime: {
      running: !!active, busy: !!active,
      waitingOnApproval: thread.pending.some((request) => request.kind === 'approval'),
      waitingOnInput: thread.pending.some((request) => request.kind === 'question'),
      waitingOnPlanConfirmation: !active && thread.planned === true && thread.config?.mode === 'plan',
      activeTurnId: active?.id ?? null,
    },
  }
}

export function currentPlan(thread: FakeThread) {
  const call = thread.items.findLast((item) => item.type === 'toolCall' && item.payload.toolName === 'CreatePlan')
  const result = thread.items.find((item) => item.turnId === call?.turnId && item.type === 'toolResult' && item.payload.callId === call?.payload.callId)
  const args = call?.payload.arguments as { plan: string; todos: unknown[] } | undefined
  return result?.payload.success === true && args ? { title: '', overview: '', content: args.plan, todos: args.todos } : null
}

export function contextUsage(tokens: number) {
  return { tokens, contextWindow: CONTEXT_WINDOW, percentLeft: Math.max(0, 1 - tokens / CONTEXT_WINDOW) }
}

export function threadBody(thread: FakeThread, workspacePath: string) {
  return {
    ...threadSummary(thread), workspacePath, effectiveWorkspacePath: thread.cwd ?? workspacePath,
    configuration: { ...thread.config, agentProfileId: thread.profileId }, plan: currentPlan(thread),
    ...(thread.context === undefined ? {} : { contextUsage: contextUsage(thread.context) }),
    metadata: {}, ephemeral: false, source: { kind: thread.source ?? 'user' }, readState: thread.readState ?? null,
    sessionId: thread.id, cwd: thread.cwd ?? workspacePath, historyMode: 'full', originChannel: 'dotcraft-desktop',
    queuedInputs: [], runtimeWorkspaceRoots: [], worktree: null,
  }
}

export function turnDiff(items: FakeItem[], turnId: string): string {
  return items.filter((item) => item.turnId === turnId && item.type === 'toolResult')
    .flatMap((item) => (item.payload.structuredContent as { changes?: { diff?: string }[] } | undefined)?.changes ?? [])
    .map((change) => change.diff ?? '').join('')
}

export function resumeState(thread: FakeThread) {
  const turn = thread.turns.at(-1)
  return {
    turnId: turn?.id, turnStatus: turn?.status,
    itemCount: thread.items.filter((item) => item.turnId === turn?.id).length,
    ...(thread.planned && currentPlan(thread) ? { planTurnId: turn?.id } : {}),
    ...(turn ? { diff: turnDiff(thread.items, turn.id) } : {}),
    pendingRequests: thread.pending.map((request) => ({
      method: request.kind === 'approval' ? 'item/approval/request' : 'item/tool/requestUserInput',
      requestId: request.requestId, itemId: request.requestId, turnId: turn!.id,
      isBlocking: request.kind === 'approval' || request.isBlocking,
    })),
  }
}

export function turnsPage(thread: FakeThread, params: Record<string, unknown>) {
  const ordered = params.sortDirection === 'ascending' ? thread.turns : [...thread.turns].reverse()
  const start = typeof params.cursor === 'string' ? Number(params.cursor.slice(6)) : 0
  const end = start + Number(params.limit ?? 5)
  return {
    data: withSummaries(ordered.slice(start, end), thread.items, params),
    nextCursor: end < ordered.length ? `turns:${end}` : null,
  }
}
