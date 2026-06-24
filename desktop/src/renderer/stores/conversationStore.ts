import { create } from 'zustand'
import type {
  ConversationTurn,
  ConversationItem,
  TurnStatus,
  ThreadMode,
  ApprovalDecision,
  ApprovalState,
  ApprovalType,
  PendingComposerMessage,
  QueuedTurnInput
} from '../types/conversation'
import {
  derivePluginFunctionResultText,
  isToolLikeItemType,
  normalizePluginFunctionContentItems,
  normalizeToolUiDescriptor,
  wireItemToConversationItem,
  wireTurnToConversationTurn
} from '../types/conversation'
import { isShellToolName } from '../utils/shellTools'
import type { FileDiff, SubAgentEntry } from '../types/toolCall'
import {
  mergeFileDiffIncrement,
  computeCumulativeFileDiff,
  computeIncrementalPerItemDiff,
  parseResultPath,
  toAbsoluteWorkspacePath
} from '../utils/diffExtractor'
import {
  computeStreamingFileDiff,
  extractStreamingFilePath
} from '../utils/streamingDiff'
import { parsePlanMarkdown } from '../utils/planMarkdown'

// ---------------------------------------------------------------------------
// Plan types
// ---------------------------------------------------------------------------

export type PlanTodoStatus = 'pending' | 'in_progress' | 'completed' | 'cancelled'

export type MaintenanceKind = 'compacting' | 'consolidating'
export type BackgroundMemoryStatus = 'consolidating'

export interface PlanTodoItem {
  id: string
  content: string
  status: PlanTodoStatus
}

export interface AgentPlan {
  title: string
  overview: string
  content: string
  todos: PlanTodoItem[]
}

export interface StreamRetrySignal {
  id: string
  turnId: string
  rawMessage: string
  attempt: number | null
  max: number | null
  createdAt: string
}

// ---------------------------------------------------------------------------
// Context usage (token ring)
// ---------------------------------------------------------------------------

/** Threshold classification used by the token ring for color coding. */
export type ContextUsageSeverity = 'normal' | 'warning' | 'error'

/**
 * Mirror of the backend `ContextUsageSnapshot` wire shape, plus a derived
 * severity bucket for UI color coding. Null when the thread has no persisted
 * context usage state yet.
 */
export interface ContextUsage {
  tokens: number
  contextWindow: number
  autoCompactThreshold: number
  warningThreshold: number
  errorThreshold: number
  percentLeft: number
  severity: ContextUsageSeverity
}

interface ContextUsageSnapshotInput {
  tokens: number
  contextWindow: number
  autoCompactThreshold: number
  warningThreshold: number
  errorThreshold: number
  percentLeft: number
  source?: string | null
  isEstimate?: boolean
}

interface PendingToolCompletionEntry {
  turnId: string
  callId: string
  toolExecution?: ConversationItem
  toolResult?: ConversationItem
}

interface SetTurnsOptions {
  preserveExistingRealtime?: boolean
  realtimeScopeThreadId?: string | null
}

// ---------------------------------------------------------------------------
// State interface
// ---------------------------------------------------------------------------

/** One selectable decision in the approval composer (resolved labels). */
export interface ApprovalOptionSpec {
  value: string
  label: string
  description: string
}

/** One label/value row in the approval composer's detail panel. */
export interface ApprovalDetailRowSpec {
  label: string
  value: string
  mono?: boolean
}

export interface PendingApproval {
  /** Bridge ID needed to respond via IPC */
  bridgeId: string
  threadId: string | null
  turnId: string | null
  requestId: string
  locallySubmittedDecision: ApprovalDecision | null
  /** Item ID in the current turn's items list */
  itemId: string
  approvalType: ApprovalType
  operation: string
  target: string
  reason: string
  /**
   * Approval source (informational; routing is driven by `submit`). `'tool'` (default) responds to
   * AppServer; `'browserUse'` and `'uiTool'` are turn-less approvals shown via the generic-approval
   * slot — the browser-use IPC channel and a decoupled `ui/tool/call`, respectively.
   */
  source?: 'tool' | 'browserUse' | 'uiTool'
  /** Custom decision options; when omitted the composer uses the default tool options. */
  options?: ApprovalOptionSpec[]
  /** Custom detail rows; when omitted the composer derives them from type/operation/target/reason. */
  detailRows?: ApprovalDetailRowSpec[]
  /** Custom prompt; when omitted the composer derives it from `approvalType`. */
  question?: string
  /**
   * Custom decision handler. When set (non-tool sources), the composer calls this with the chosen
   * option value instead of the AppServer `sendServerResponse` path. Transient UI state only.
   */
  submit?: (value: string) => Promise<void>
  /** Option value used for Esc / the footer reject button (default `'decline'`). */
  declineValue?: string
}

interface ApprovalRequestMatch {
  threadId?: string | null
  turnId?: string | null
  requestId?: string | null
  itemId?: string | null
  bridgeId?: string | null
}

interface ApprovalResolvedParams extends ApprovalRequestMatch {
  decision?: ApprovalDecision | null
}

interface ApprovalNoLongerPendingParams extends ApprovalRequestMatch {
  nextTurnStatus: 'running' | 'idle'
}

export interface UserInputQuestionOption {
  label: string
  description: string
}

export interface UserInputQuestion {
  id: string
  header: string
  question: string
  isOther: boolean
  isSecret?: boolean
  options: UserInputQuestionOption[]
}

export interface PendingUserInputRequest {
  bridgeId: string
  requestId: string
  turnId: string | null
  questions: UserInputQuestion[]
}

interface StreamingFileBaseline {
  path: string
  originalContent: string
}

const SUB_AGENT_STREAMING_TOOLS = new Set([
  'SpawnAgent',
  'SendMessage',
  'FollowupTask',
  'WaitAgent',
  'ListAgents',
  'CloseAgent'
])

const SUB_AGENT_ARGUMENT_BUFFER_MAX_CHARS = 12000
const SUB_AGENT_ARGUMENT_FIELD_MAX_CHARS = 800
const subAgentStreamingArgumentBuffers = new Map<string, string>()

const approvalDecisionToState: Record<ApprovalDecision, ApprovalState> = {
  accept: 'accepted',
  acceptForSession: 'acceptedForSession',
  acceptAlways: 'acceptedAlways',
  decline: 'declined',
  cancel: 'cancelled'
}

function normalizeApprovalDecision(value: unknown): ApprovalDecision | null {
  return typeof value === 'string' && value in approvalDecisionToState
    ? value as ApprovalDecision
    : null
}

function matchesPendingApproval(
  pending: PendingApproval,
  params: ApprovalRequestMatch | undefined
): boolean {
  if (!params) return true
  if (params.threadId && pending.threadId && params.threadId !== pending.threadId) return false
  if (params.turnId && pending.turnId && params.turnId !== pending.turnId) return false
  if (params.requestId) return pending.requestId === params.requestId
  if (params.itemId) return pending.itemId === params.itemId
  if (params.bridgeId) return pending.bridgeId === params.bridgeId
  return true
}

function samePendingApproval(a: PendingApproval, b: PendingApproval): boolean {
  if (a.requestId && b.requestId) return a.requestId === b.requestId
  if (a.itemId && b.itemId) return a.itemId === b.itemId
  return a.bridgeId === b.bridgeId
}

function activePendingApproval(queue: PendingApproval[]): PendingApproval | null {
  return queue[0] ?? null
}

function queueWithPendingApproval(
  queue: PendingApproval[],
  fallback: PendingApproval | null
): PendingApproval[] {
  return queue.length > 0 ? queue : fallback ? [fallback] : []
}

function upsertPendingApproval(
  queue: PendingApproval[],
  approval: PendingApproval
): PendingApproval[] {
  const existingIndex = queue.findIndex((candidate) => samePendingApproval(candidate, approval))
  if (existingIndex < 0) return [...queue, approval]

  return queue.map((candidate, index) =>
    index === existingIndex
      ? {
          ...candidate,
          ...approval,
          locallySubmittedDecision: approval.locallySubmittedDecision ?? candidate.locallySubmittedDecision
        }
      : candidate
  )
}

function updatePendingApprovalInQueue(
  queue: PendingApproval[],
  pending: PendingApproval,
  update: (approval: PendingApproval) => PendingApproval
): PendingApproval[] {
  return queue.map((candidate) => (
    samePendingApproval(candidate, pending) ? update(candidate) : candidate
  ))
}

function approvalCardMatchesResolution(
  item: ConversationItem,
  itemId: string | null,
  requestId: string | null | undefined
): boolean {
  if (item.type !== 'approvalCard') return false
  if (itemId && item.id === itemId) return true
  return Boolean(requestId && item.approvalRequestId === requestId)
}

interface PendingTerminalEntry {
  event: string
  terminal: Record<string, unknown>
}

interface ConversationState {
  turns: ConversationTurn[]
  turnStatus: 'idle' | 'running' | 'waitingApproval' | 'waitingInput'
  /** Current active turn id (when running or waiting for user action) */
  activeTurnId: string | null
  /** Non-null when turnStatus === 'waitingApproval' */
  pendingApproval: PendingApproval | null
  /** All unresolved turn-bound approvals for the active turn, in composer order. */
  pendingApprovals: PendingApproval[]
  /** A turn-less approval (e.g. browser-use) shown in the same composer; independent of turnStatus. */
  genericApproval: PendingApproval | null
  /** Non-null when turnStatus === 'waitingInput' */
  pendingUserInput: PendingUserInputRequest | null
  /** Currently streaming agent message text */
  streamingMessage: string
  /** Wall-clock ms when the latest agent message delta arrived. UI-only. */
  streamingMessageLastDeltaAt: number | null
  /** Currently streaming reasoning text */
  streamingReasoning: string
  /** Wall-clock ms when streaming reasoning started (for elapsed display) */
  streamingReasoningStartedAt: number | null
  /** ID of the item currently being streamed */
  activeItemId: string | null
  /** Wall-clock ms when the current turn started */
  turnStartedAt: number | null
  inputTokens: number
  outputTokens: number
  /** Transient system status translation key, e.g. "systemStatus.compacting". */
  systemLabel: string | null
  /** Thread-scoped background memory work that should not block input. */
  backgroundMemoryStatus: BackgroundMemoryStatus | null
  /** Transient provider stream retry rows for the active turn; never persisted. */
  streamRetrySignals: StreamRetrySignal[]
  /** Thread-level maintenance that should keep input in queue mode. */
  maintenanceKind: MaintenanceKind | null
  /** Queued follow-up message (sent when current turn completes) */
  pendingMessage: PendingComposerMessage | null
  /** Server-persisted FIFO inputs queued behind the active turn. */
  queuedInputs: QueuedTurnInput[]
  /** Current agent operating mode */
  threadMode: ThreadMode
  /** Workspace root path (for cumulative diff disk reads) */
  workspacePath: string
  /** True when the active AppServer represents a remote workspace. */
  remoteWorkspaceActive: boolean
  /** File diffs accumulated for the active thread (cross-turn), keyed by filePath */
  changedFiles: Map<string, FileDiff>
  /** Per tool-call item incremental diff (Detail Panel uses cumulative changedFiles) */
  itemDiffs: Map<string, FileDiff>
  /** Live per-item file diff shown while WriteFile/EditFile arguments stream in */
  streamingItemDiffs: Map<string, FileDiff>
  /** Baseline file contents captured once per streaming file tool call */
  streamingBaselines: Map<string, StreamingFileBaseline>
  /** Foreground terminal events that arrived before their Exec toolCall item. */
  pendingTerminalByCallId: Map<string, PendingTerminalEntry>
  /** Tool completion items that arrived before their matching toolCall item. */
  pendingToolCompletionsByCallKey: Map<string, PendingToolCompletionEntry>
  /** Live SubAgent progress entries — replaced wholesale on each notification */
  subAgentEntries: SubAgentEntry[]
  /** Current agent plan from plan/updated events — replaced wholesale */
  plan: AgentPlan | null
  /**
   * Approximate context usage snapshot for the active thread. Seeded from
   * thread/read and updated by item/usage/delta and system/event compacted
   * notifications. Null when no token tracker data is available yet.
   */
  contextUsage: ContextUsage | null
}

// ---------------------------------------------------------------------------
// Actions interface
// ---------------------------------------------------------------------------

interface ConversationActions {
  /** Load full turn history from thread/read */
  setTurns(turns: ConversationTurn[] | Array<Record<string, unknown>>, options?: SetTurnsOptions): void
  /** turn/started notification */
  onTurnStarted(rawTurn: Record<string, unknown>): void
  /** turn/completed notification */
  onTurnCompleted(rawTurn: Record<string, unknown>): void
  /** turn/failed notification */
  onTurnFailed(rawTurn: Record<string, unknown>, error: string): void
  /** turn/cancelled notification */
  onTurnCancelled(rawTurn: Record<string, unknown>, reason: string): void
  /** item/started notification */
  onItemStarted(params: Record<string, unknown>): void
  /** item/agentMessage/delta notification */
  onAgentMessageDelta(delta: string): void
  /** item/reasoning/delta notification */
  onReasoningDelta(delta: string): void
  /** item/commandExecution/outputDelta notification */
  onCommandExecutionDelta(params: { threadId?: string; turnId?: string; itemId?: string; delta?: string }): void
  /** terminal/* notification */
  onTerminalEvent(params: { event: string; terminal?: Record<string, unknown>; delta?: string }): void
  /** item/toolCall/argumentsDelta notification */
  onToolCallArgumentsDelta(params: {
    threadId?: string
    turnId?: string
    itemId?: string
    delta?: string
    toolName?: string
    callId?: string
  }): void
  /** item/completed notification */
  onItemCompleted(params: Record<string, unknown>): void
  /**
   * item/usage/delta notification.
   *
   * `totalInputTokens` (when provided) is the current persisted
   * context-occupancy snapshot for the thread and drives the token ring
   * directly. It is not billing/cumulative turn usage.
   */
  onUsageDelta(
    inputTokens: number,
    outputTokens: number,
    totalInputTokens?: number | null,
    totalOutputTokens?: number | null,
    contextUsage?: ContextUsageSnapshotInput | null
  ): void
  /**
   * system/event notification. Accepts the full params from the wire so we can
   * forward `tokenCount` / `percentLeft` into the context-usage slice.
   */
  onSystemEvent(
    kind: string,
    params?: {
      turnId?: string | null
      message?: string | null
      tokenCount?: number | null
      percentLeft?: number | null
      contextUsage?: ContextUsageSnapshotInput | null
    }
  ): void
  /** Replace contextUsage from thread/read / thread/started / thread/resumed. */
  setContextUsage(snapshot: {
    tokens: number
    contextWindow: number
    autoCompactThreshold: number
    warningThreshold: number
    errorThreshold: number
    percentLeft: number
  } | null): void
  setMaintenanceKind(kind: MaintenanceKind | string | null | undefined): void
  setPendingMessage(msg: PendingComposerMessage | null): void
  setQueuedInputs(inputs: QueuedTurnInput[]): void
  setThreadMode(mode: ThreadMode): void
  /** Add an optimistic (locally-created) turn before server confirms */
  addOptimisticTurn(turn: ConversationTurn): void
  /** Remove an optimistic turn on RPC failure */
  removeOptimisticTurn(turnId: string): void
  /**
   * Replace the optimistic client-only turn ID with the real server turn ID.
   * Called as soon as turn/start returns its response (before turn/started arrives).
   */
  promoteOptimisticTurn(localId: string, serverId: string): void
  /** Replace subagent entries snapshot from subagent/progress notification */
  onSubagentProgress(entries: SubAgentEntry[]): void
  /** Add or update a file diff entry in changedFiles */
  upsertChangedFile(diff: FileDiff): void
  /** Store incremental diff for one toolCall item (keyed by ConversationItem.id) */
  upsertItemDiff(itemId: string, diff: FileDiff): void
  /** Mark all files in a turn as reverted in client state. */
  revertFilesForTurn(turnId: string): void
  /** Mark a single file as reverted (state only; caller must write disk via IPC) */
  revertFile(filePath: string): void
  /** Mark a single file as written/re-applied (state only; caller must write disk via IPC) */
  reapplyFile(filePath: string): void
  /** Replace entire plan state from plan/updated notification */
  onPlanUpdated(plan: Partial<AgentPlan>): void
  /**
   * Called when AppServer sends item/approval/request.
   * Adds an approvalCard item to the current turn and sets waitingApproval state.
   */
  onApprovalRequest(bridgeId: string, params: Record<string, unknown>): void
  /** Shows or clears a turn-less approval (e.g. browser-use) in the shared approval composer. */
  setGenericApproval(approval: PendingApproval | null): void
  /**
   * Marks the active approval as already submitted by this Desktop connection.
   * Prevents runtime snapshots from sending a synthetic fallback response.
   */
  onApprovalSubmitStarted(decision: ApprovalDecision, target?: ApprovalRequestMatch): void
  /** Clears the local submission marker after the IPC response fails. */
  onApprovalSubmitFailed(target?: ApprovalRequestMatch): void
  /**
   * Called when the user makes a decision.
   * Updates the approval item state locally; IPC response is sent by the caller.
   */
  onApprovalDecision(decision: ApprovalDecision, target?: ApprovalRequestMatch): void
  /**
   * Called when item/approval/resolved notification arrives.
   * Clears pendingApproval and restores turnStatus to 'running'.
   */
  onApprovalResolved(params?: ApprovalResolvedParams): void
  /**
   * Clears a pending approval after runtime snapshots show the request is no longer pending.
   * Used when another AppServer connection resolved the same approval first.
   */
  onApprovalNoLongerPending(params: ApprovalNoLongerPendingParams): void
  /**
   * Called when approval timeout error (-32020) is received.
   * Updates the approval item to 'timedOut' state.
   */
  onApprovalTimeout(): void
  onUserInputRequest(bridgeId: string, params: Record<string, unknown>): void
  onUserInputResolved(): void
  /** Set workspace path for file read IPC (call from App when path is known) */
  setWorkspacePath(path: string): void
  /** Set whether local file IPC must be disabled for the active conversation. */
  setRemoteWorkspaceActive(active: boolean): void
  reset(): void
}

export interface ConversationStore extends ConversationState, ConversationActions {}

// ---------------------------------------------------------------------------
// Initial state
// ---------------------------------------------------------------------------

const initialState: ConversationState = {
  turns: [],
  turnStatus: 'idle',
  activeTurnId: null,
  genericApproval: null,
  pendingApproval: null,
  pendingApprovals: [],
  pendingUserInput: null,
  streamingMessage: '',
  streamingMessageLastDeltaAt: null,
  streamingReasoning: '',
  streamingReasoningStartedAt: null,
  activeItemId: null,
  turnStartedAt: null,
  inputTokens: 0,
  outputTokens: 0,
  systemLabel: null,
  backgroundMemoryStatus: null,
  streamRetrySignals: [],
  maintenanceKind: null,
  pendingMessage: null,
  queuedInputs: [],
  threadMode: 'agent',
  workspacePath: '',
  remoteWorkspaceActive: false,
  changedFiles: new Map<string, FileDiff>(),
  itemDiffs: new Map<string, FileDiff>(),
  streamingItemDiffs: new Map<string, FileDiff>(),
  streamingBaselines: new Map<string, StreamingFileBaseline>(),
  pendingTerminalByCallId: new Map<string, PendingTerminalEntry>(),
  pendingToolCompletionsByCallKey: new Map<string, PendingToolCompletionEntry>(),
  subAgentEntries: [],
  plan: null,
  contextUsage: null
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

/** Stable chronological order for turn items (Wire Protocol may interleave events). */
function sortItemsByCreatedAt(items: ConversationItem[]): ConversationItem[] {
  return [...items].sort(
    (a, b) => new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime()
  )
}

function isTerminalExecutionStatus(status: ConversationItem['executionStatus'] | undefined): boolean {
  return status === 'completed' || status === 'failed' || status === 'cancelled'
}

function mergeCommandExecutionIntoToolCall(
  item: ConversationItem,
  commandExecution: Partial<ConversationItem>
): ConversationItem {
  if (item.type !== 'toolCall') return item
  if (!isShellToolName(item.toolName)) return item
  if (!commandExecution.toolCallId || item.toolCallId !== commandExecution.toolCallId) return item
  const commandOutput = commandExecution.aggregatedOutput
  const currentOutput = item.aggregatedOutput
  const staleInProgressCommand =
    commandExecution.executionStatus === 'inProgress'
    && isTerminalExecutionStatus(item.executionStatus)
  const terminalPreviewOutput =
    staleInProgressCommand && !commandOutput
      ? (item.aggregatedOutput && item.aggregatedOutput.length > 0
          ? item.aggregatedOutput
          : item.resultPreview ?? item.result)
      : undefined
  const aggregatedOutput =
    terminalPreviewOutput
      ?? (commandOutput != null
      && commandExecution.executionStatus === 'inProgress'
      && currentOutput != null
      && currentOutput.length > commandOutput.length
      && currentOutput.startsWith(commandOutput)
      ? currentOutput
      : commandOutput ?? currentOutput)

  return {
    ...item,
    command: commandExecution.command ?? item.command,
    workingDirectory: commandExecution.workingDirectory ?? item.workingDirectory,
    aggregatedOutput,
    executionStatus: staleInProgressCommand
      ? item.executionStatus
      : commandExecution.executionStatus ?? item.executionStatus,
    exitCode: commandExecution.exitCode ?? item.exitCode,
    commandSource: commandExecution.commandSource ?? item.commandSource,
    duration: commandExecution.duration ?? item.duration
  }
}

function mergeCommandExecutionAcrossItems(
  items: ConversationItem[],
  commandExecution: Partial<ConversationItem>
): ConversationItem[] {
  return items.map((i) => mergeCommandExecutionIntoToolCall(i, commandExecution))
}

function terminalStatusToExecutionStatus(status: string | undefined): ConversationItem['executionStatus'] | undefined {
  switch (status) {
    case 'running':
      return 'inProgress'
    case 'completed':
      return 'completed'
    case 'killed':
    case 'timedOut':
      return 'cancelled'
    case 'failed':
    case 'lost':
      return 'failed'
    default:
      return undefined
  }
}

function isRunInBackgroundTerminal(terminal: Record<string, unknown>): boolean {
  return terminal.backgroundReason === 'runInBackground'
}

function shouldUseTerminalSnapshotOutput(event: string, output: string | undefined): output is string {
  return typeof output === 'string'
    && output.length > 0
    && !(event === 'terminal/started' && output === '(no output)')
}

function appendTerminalDelta(output: string | undefined, delta: string): string {
  const base = output && output !== '(no output)' ? output : ''
  return `${base}${delta}`
}

function mergePendingTerminalEntry(
  previous: PendingTerminalEntry | undefined,
  terminal: Record<string, unknown>,
  event: string,
  delta: string
): PendingTerminalEntry {
  const previousTerminal = previous?.terminal ?? {}
  const nextTerminal: Record<string, unknown> = {
    ...previousTerminal,
    ...terminal
  }
  const snapshotOutput = terminal.output as string | undefined
  if (shouldUseTerminalSnapshotOutput(event, snapshotOutput)) {
    nextTerminal.output = snapshotOutput
  } else if (delta) {
    nextTerminal.output = appendTerminalDelta(previousTerminal.output as string | undefined, delta)
  } else if (previousTerminal.output != null && nextTerminal.output == null) {
    nextTerminal.output = previousTerminal.output
  }

  return { event, terminal: nextTerminal }
}

function terminalMatchesTurn(entry: PendingTerminalEntry, turn: ConversationTurn): boolean {
  const threadId = entry.terminal.threadId as string | undefined
  const turnId = entry.terminal.turnId as string | undefined
  return (!threadId || threadId === turn.threadId) && (!turnId || turnId === turn.id)
}

function turnHasShellToolCall(turn: ConversationTurn, callId: string): boolean {
  return turn.items.some(
    (item) => item.type === 'toolCall' && item.toolCallId === callId && isShellToolName(item.toolName)
  )
}

function removePendingTerminalEntries(
  pending: Map<string, PendingTerminalEntry>,
  callIds: Set<string>
): Map<string, PendingTerminalEntry> {
  if (callIds.size === 0) return pending
  const next = new Map(pending)
  for (const callId of callIds) {
    next.delete(callId)
  }
  return next
}

function applyPendingTerminalsToTurn(
  turn: ConversationTurn,
  pending: Map<string, PendingTerminalEntry>
): { turn: ConversationTurn; appliedCallIds: Set<string> } {
  let items = turn.items
  const appliedCallIds = new Set<string>()
  for (const [callId, entry] of pending) {
    if (!terminalMatchesTurn(entry, turn) || !turnHasShellToolCall({ ...turn, items }, callId)) {
      continue
    }
    items = mergeTerminalAcrossItems(items, entry.terminal, entry.event, '')
    appliedCallIds.add(callId)
  }
  return appliedCallIds.size > 0
    ? { turn: { ...turn, items: sortItemsByCreatedAt(items) }, appliedCallIds }
    : { turn, appliedCallIds }
}

function applyPendingTerminalsToTurns(
  turns: ConversationTurn[],
  pending: Map<string, PendingTerminalEntry>
): { turns: ConversationTurn[]; pendingTerminalByCallId: Map<string, PendingTerminalEntry> } {
  let applied = new Set<string>()
  const nextTurns = turns.map((turn) => {
    const result = applyPendingTerminalsToTurn(turn, pending)
    if (result.appliedCallIds.size > 0) {
      applied = new Set([...applied, ...result.appliedCallIds])
    }
    return result.turn
  })
  return {
    turns: nextTurns,
    pendingTerminalByCallId: removePendingTerminalEntries(pending, applied)
  }
}

function mergeTerminalIntoExecToolCall(
  item: ConversationItem,
  terminal: Record<string, unknown>,
  event: string,
  delta: string
): ConversationItem {
  if (item.type !== 'toolCall') return item
  if (!isShellToolName(item.toolName)) return item
  const callId = terminal.callId as string | undefined
  if (!callId || item.toolCallId !== callId) return item

  const status = terminalStatusToExecutionStatus(terminal.status as string | undefined)
  const output = terminal.output as string | undefined
  const aggregatedOutput = shouldUseTerminalSnapshotOutput(event, output)
    ? output
    : delta
      ? appendTerminalDelta(item.aggregatedOutput, delta)
      : item.aggregatedOutput

  return {
    ...item,
    command: (terminal.command as string | undefined) ?? item.command,
    workingDirectory: (terminal.workingDirectory as string | undefined) ?? item.workingDirectory,
    commandSource: (terminal.source as 'host' | 'sandbox' | undefined) ?? item.commandSource,
    aggregatedOutput,
    executionStatus: status ?? item.executionStatus,
    exitCode: (terminal.exitCode as number | null | undefined) ?? item.exitCode,
    duration: (terminal.wallTimeMs as number | undefined) ?? item.duration
  }
}

function mergeTerminalAcrossItems(
  items: ConversationItem[],
  terminal: Record<string, unknown>,
  event: string,
  delta: string
): ConversationItem[] {
  return items.map((item) => mergeTerminalIntoExecToolCall(item, terminal, event, delta))
}

function mergeToolExecutionIntoToolCall(
  item: ConversationItem,
  toolExecution: Partial<ConversationItem>
): ConversationItem {
  if (item.type !== 'toolCall') return item
  if (!toolExecution.toolCallId || item.toolCallId !== toolExecution.toolCallId) return item
  const resultPreview = toolExecution.executionStatus === 'failed'
    ? toolExecution.errorMessage ?? toolExecution.resultPreview ?? item.resultPreview
    : toolExecution.resultPreview ?? toolExecution.errorMessage ?? item.resultPreview

  return {
    ...item,
    status: 'completed',
    success: toolExecution.success ?? item.success,
    duration: toolExecution.duration ?? item.duration,
    resultPreview,
    result: item.result ?? resultPreview,
    errorMessage: toolExecution.errorMessage ?? item.errorMessage,
    executionStatus: toolExecution.executionStatus ?? item.executionStatus,
    completedAt: toolExecution.completedAt ?? item.completedAt
  }
}

function toolCompletionKey(turnId: string | undefined, callId: string | undefined): string | null {
  if (!turnId || !callId) return null
  return `${turnId}\u0000${callId}`
}

function turnHasToolCall(turn: ConversationTurn, callId: string): boolean {
  return turn.items.some((item) => item.type === 'toolCall' && item.toolCallId === callId)
}

function mergeToolResultIntoToolCall(
  item: ConversationItem,
  toolResult: Partial<ConversationItem>
): ConversationItem {
  if (item.type !== 'toolCall') return item
  if (!toolResult.toolCallId || item.toolCallId !== toolResult.toolCallId) return item

  const startMs = item.createdAt ? Date.parse(item.createdAt) : Date.now()
  const completedAt = toolResult.completedAt ?? item.completedAt ?? new Date().toISOString()
  const endMs = Date.parse(completedAt)
  const duration =
    Number.isFinite(startMs) && Number.isFinite(endMs)
      ? Math.max(0, endMs - startMs)
      : item.duration

  return {
    ...item,
    status: 'completed',
    result: toolResult.result ?? item.result ?? '',
    success: toolResult.success ?? item.success ?? true,
    duration,
    completedAt
  }
}

function mergeToolCompletionEntryIntoItems(
  items: ConversationItem[],
  entry: PendingToolCompletionEntry
): ConversationItem[] {
  return items.map((item) => {
    if (item.type !== 'toolCall' || item.toolCallId !== entry.callId) return item
    const withExecution = entry.toolExecution
      ? mergeToolExecutionIntoToolCall(item, entry.toolExecution)
      : item
    return entry.toolResult
      ? mergeToolResultIntoToolCall(withExecution, entry.toolResult)
      : withExecution
  })
}

function removePendingToolCompletionEntries(
  pending: Map<string, PendingToolCompletionEntry>,
  keys: Set<string>
): Map<string, PendingToolCompletionEntry> {
  if (keys.size === 0) return pending
  const next = new Map(pending)
  for (const key of keys) {
    next.delete(key)
  }
  return next
}

function applyPendingToolCompletionsToTurn(
  turn: ConversationTurn,
  pending: Map<string, PendingToolCompletionEntry>
): { turn: ConversationTurn; appliedKeys: Set<string> } {
  let items = turn.items
  const appliedKeys = new Set<string>()
  for (const [key, entry] of pending) {
    if (entry.turnId !== turn.id || !turnHasToolCall({ ...turn, items }, entry.callId)) {
      continue
    }
    items = mergeToolCompletionEntryIntoItems(items, entry)
    appliedKeys.add(key)
  }
  return appliedKeys.size > 0
    ? { turn: { ...turn, items: sortItemsByCreatedAt(items) }, appliedKeys }
    : { turn, appliedKeys }
}

function applyPendingToolCompletionsToTurns(
  turns: ConversationTurn[],
  pending: Map<string, PendingToolCompletionEntry>
): {
  turns: ConversationTurn[]
  pendingToolCompletionsByCallKey: Map<string, PendingToolCompletionEntry>
} {
  let applied = new Set<string>()
  const nextTurns = turns.map((turn) => {
    const result = applyPendingToolCompletionsToTurn(turn, pending)
    if (result.appliedKeys.size > 0) {
      applied = new Set([...applied, ...result.appliedKeys])
    }
    return result.turn
  })
  return {
    turns: nextTurns,
    pendingToolCompletionsByCallKey: removePendingToolCompletionEntries(pending, applied)
  }
}

function mergePendingToolCompletionEntry(
  previous: PendingToolCompletionEntry | undefined,
  entry: PendingToolCompletionEntry
): PendingToolCompletionEntry {
  return {
    turnId: entry.turnId,
    callId: entry.callId,
    toolExecution: entry.toolExecution ?? previous?.toolExecution,
    toolResult: entry.toolResult ?? previous?.toolResult
  }
}

function isTerminalTurnStatus(status: ConversationTurn['status']): boolean {
  return status === 'completed' || status === 'failed' || status === 'cancelled'
}

function isResolvedApprovalCard(item: ConversationItem): boolean {
  return item.type === 'approvalCard' &&
    item.approvalState != null &&
    item.approvalState !== 'pending'
}

function hasToolCompletionEvidence(item: ConversationItem): boolean {
  return item.type === 'toolCall' && (
    item.success !== undefined ||
    item.result !== undefined ||
    item.resultPreview !== undefined ||
    item.executionStatus != null ||
    item.completedAt != null
  )
}

function itemRealtimeKeys(item: ConversationItem): string[] {
  const keys = [`id:${item.id}`]
  if (item.toolCallId) {
    keys.push(`call:${item.toolCallId}`)
  }
  if (item.type === 'approvalCard' && item.approvalRequestId) {
    keys.push(`approval:${item.approvalRequestId}`)
  }
  return keys
}

function indexItemsByRealtimeKey(items: ConversationItem[]): Map<string, ConversationItem> {
  const index = new Map<string, ConversationItem>()
  for (const item of items) {
    for (const key of itemRealtimeKeys(item)) {
      if (!index.has(key)) {
        index.set(key, item)
      }
    }
  }
  return index
}

function findMatchingRealtimeItem(
  item: ConversationItem,
  existingByKey: Map<string, ConversationItem>
): ConversationItem | undefined {
  for (const key of itemRealtimeKeys(item)) {
    const existing = existingByKey.get(key)
    if (existing) return existing
  }
  return undefined
}

const OPTIMISTIC_TURN_CLOCK_SKEW_MS = 1000
const OPTIMISTIC_TURN_MATCH_WINDOW_MS = 5 * 60 * 1000

function isOptimisticTurn(turn: ConversationTurn): boolean {
  return turn.id.startsWith('local-turn-')
}

function isOrdinaryUserMessage(item: ConversationItem): boolean {
  return item.type === 'userMessage' &&
    item.deliveryMode !== 'guidance' &&
    item.deliveryMode !== 'subagentMailbox' &&
    item.triggerKind == null
}

function isOptimisticUserMessage(item: ConversationItem): boolean {
  return isOrdinaryUserMessage(item) && item.id.startsWith('local-')
}

function firstOrdinaryUserMessage(turn: ConversationTurn): ConversationItem | undefined {
  return turn.items.find(isOrdinaryUserMessage)
}

function itemPayloadRecord(item: ConversationItem): Record<string, unknown> {
  const payload = (item as unknown as { payload?: unknown }).payload
  return payload != null && typeof payload === 'object' && !Array.isArray(payload)
    ? payload as Record<string, unknown>
    : {}
}

function userMessageImageCount(item: ConversationItem): number {
  const payloadImages = itemPayloadRecord(item).images
  return Math.max(
    item.images?.length ?? 0,
    item.imageDataUrls?.length ?? 0,
    Array.isArray(payloadImages) ? payloadImages.length : 0
  )
}

function userMessageNativeInputParts(item: ConversationItem): unknown {
  return item.nativeInputParts ?? itemPayloadRecord(item).nativeInputParts
}

function inputPartSignature(parts: unknown): string | null {
  if (!Array.isArray(parts) || parts.length === 0) return null
  const normalized = parts
    .flatMap((rawPart) => {
      if (rawPart == null || typeof rawPart !== 'object') return []
      const part = rawPart as Record<string, unknown>
      const type = typeof part.type === 'string' ? part.type : ''
      switch (type) {
        case 'text':
          return [{ type, text: typeof part.text === 'string' ? part.text : '' }]
        case 'commandRef':
          return [{
            type,
            name: typeof part.name === 'string' ? part.name : '',
            argsText: typeof part.argsText === 'string' ? part.argsText : undefined,
            rawText: typeof part.rawText === 'string' ? part.rawText : undefined
          }]
        case 'skillRef':
          return [{ type, name: typeof part.name === 'string' ? part.name : '' }]
        case 'fileRef':
          return [{
            type,
            path: typeof part.path === 'string' ? part.path : '',
            displayPath: typeof part.displayPath === 'string' ? part.displayPath : undefined
          }]
        case 'image':
        case 'localImage':
          return []
        default:
          return [{ type }]
      }
    })
  return JSON.stringify(normalized)
}

function normalizedUserMessageText(item: ConversationItem): string {
  const payload = itemPayloadRecord(item)
  const text =
    item.text
    ?? (typeof payload.text === 'string' ? payload.text : undefined)
    ?? (typeof payload.message === 'string' ? payload.message : undefined)
  return (text ?? '').trim()
}

function userMessagesRepresentSameRequest(a: ConversationItem, b: ConversationItem): boolean {
  if (!isOrdinaryUserMessage(a) || !isOrdinaryUserMessage(b)) return false

  const imageCountA = userMessageImageCount(a)
  const imageCountB = userMessageImageCount(b)
  if ((imageCountA > 0 || imageCountB > 0) && imageCountA !== imageCountB) return false

  const partsA = inputPartSignature(userMessageNativeInputParts(a))
  const partsB = inputPartSignature(userMessageNativeInputParts(b))
  if (partsA != null && partsB != null) return partsA === partsB

  const textA = normalizedUserMessageText(a)
  const textB = normalizedUserMessageText(b)
  if (textA.length > 0 || textB.length > 0) return textA === textB

  return imageCountA > 0 && imageCountA === imageCountB
}

function optimisticUserMessageRepresentedByIncoming(
  existingItem: ConversationItem,
  incomingItems: ConversationItem[]
): boolean {
  return isOptimisticUserMessage(existingItem) &&
    incomingItems.some((incomingItem) =>
      !isOptimisticUserMessage(incomingItem) &&
      userMessagesRepresentSameRequest(existingItem, incomingItem)
    )
}

function turnStartedAtMs(turn: ConversationTurn): number | null {
  const value = Date.parse(turn.startedAt)
  return Number.isFinite(value) ? value : null
}

function turnCompletedAtMs(turn: ConversationTurn): number | null {
  const value = turn.completedAt ? Date.parse(turn.completedAt) : Number.NaN
  return Number.isFinite(value) ? value : null
}

function turnRepresentsOptimisticTurn(
  incoming: ConversationTurn,
  optimistic: ConversationTurn
): boolean {
  if (!isOptimisticTurn(optimistic)) return false
  if (incoming.threadId && optimistic.threadId && incoming.threadId !== optimistic.threadId) return false

  const incomingStartedAt = turnStartedAtMs(incoming)
  const optimisticStartedAt = turnStartedAtMs(optimistic)
  if (incomingStartedAt != null && optimisticStartedAt != null) {
    if (incomingStartedAt < optimisticStartedAt - OPTIMISTIC_TURN_CLOCK_SKEW_MS) return false
    if (incomingStartedAt < optimisticStartedAt && isTerminalTurnStatus(incoming.status)) {
      const incomingCompletedAt = turnCompletedAtMs(incoming)
      if (incomingCompletedAt == null || incomingCompletedAt < optimisticStartedAt) return false
    }
    if (incomingStartedAt - optimisticStartedAt > OPTIMISTIC_TURN_MATCH_WINDOW_MS) return false
  }

  const incomingUser = firstOrdinaryUserMessage(incoming)
  const optimisticUser = firstOrdinaryUserMessage(optimistic)
  return incomingUser != null &&
    optimisticUser != null &&
    userMessagesRepresentSameRequest(incomingUser, optimisticUser)
}

function findRepresentedOptimisticTurn(
  incoming: ConversationTurn,
  existingTurns: ConversationTurn[],
  representedExistingIds: Set<string>
): ConversationTurn | undefined {
  return existingTurns.find((existing) =>
    !representedExistingIds.has(existing.id) &&
    turnRepresentsOptimisticTurn(incoming, existing)
  )
}

function mergeRealtimeToolCall(
  incoming: ConversationItem,
  existing: ConversationItem
): ConversationItem {
  if (incoming.type !== 'toolCall' || existing.type !== 'toolCall') return incoming
  if (!hasToolCompletionEvidence(existing)) return incoming

  return {
    ...incoming,
    status: existing.status === 'completed' ? 'completed' : incoming.status,
    result: existing.result ?? incoming.result,
    resultPreview: existing.resultPreview ?? incoming.resultPreview,
    success: existing.success ?? incoming.success,
    duration: existing.duration ?? incoming.duration,
    executionStatus: existing.executionStatus ?? incoming.executionStatus,
    completedAt: existing.completedAt ?? incoming.completedAt
  }
}

function mergeRealtimeApprovalCard(
  incoming: ConversationItem,
  existing: ConversationItem
): ConversationItem {
  if (incoming.type !== 'approvalCard' || existing.type !== 'approvalCard') return incoming
  if (!isResolvedApprovalCard(existing)) return incoming

  return {
    ...incoming,
    approvalState: existing.approvalState,
    completedAt: existing.completedAt ?? incoming.completedAt
  }
}

function mergeRealtimeMatchedItem(
  incoming: ConversationItem,
  existing: ConversationItem | undefined
): ConversationItem {
  if (!existing) return incoming
  if (incoming.type === 'toolCall') {
    return mergeRealtimeToolCall(incoming, existing)
  }
  if (incoming.type === 'approvalCard') {
    return mergeRealtimeApprovalCard(incoming, existing)
  }
  if (
    existing.status === 'completed' &&
    incoming.status !== 'completed' &&
    existing.type === incoming.type
  ) {
    return {
      ...incoming,
      status: 'completed',
      completedAt: existing.completedAt ?? incoming.completedAt
    }
  }
  return incoming
}

function mergeExistingRealtimeTurn(
  incoming: ConversationTurn,
  existing: ConversationTurn | undefined
): ConversationTurn {
  if (!existing) return incoming

  const existingByKey = indexItemsByRealtimeKey(existing.items)
  const representedKeys = new Set<string>()
  const mergedItems = incoming.items.map((item) => {
    const existingItem = findMatchingRealtimeItem(item, existingByKey)
    if (existingItem) {
      for (const key of itemRealtimeKeys(existingItem)) {
        representedKeys.add(key)
      }
    }
    for (const key of itemRealtimeKeys(item)) {
      representedKeys.add(key)
    }
    return mergeRealtimeMatchedItem(item, existingItem)
  })

  for (const existingItem of existing.items) {
    const isRepresented = itemRealtimeKeys(existingItem).some((key) => representedKeys.has(key))
    if (isRepresented) continue
    if (optimisticUserMessageRepresentedByIncoming(existingItem, incoming.items)) continue
    mergedItems.push(existingItem)
  }

  const keepExistingTerminalStatus =
    isTerminalTurnStatus(existing.status) && !isTerminalTurnStatus(incoming.status)

  return {
    ...incoming,
    status: keepExistingTerminalStatus ? existing.status : incoming.status,
    completedAt: keepExistingTerminalStatus ? existing.completedAt : incoming.completedAt,
    tokenUsage: keepExistingTerminalStatus ? existing.tokenUsage : incoming.tokenUsage,
    error: keepExistingTerminalStatus ? existing.error : incoming.error,
    cancelReason: keepExistingTerminalStatus ? existing.cancelReason : incoming.cancelReason,
    items: sortItemsByCreatedAt(mergedItems)
  }
}

function turnThreadIds(turns: ConversationTurn[]): Set<string> {
  const ids = new Set<string>()
  for (const turn of turns) {
    if (turn.threadId) {
      ids.add(turn.threadId)
    }
  }
  return ids
}

function hasSharedRealtimeScope(
  incoming: ConversationTurn[],
  existingTurns: ConversationTurn[],
  realtimeScopeThreadId?: string | null
): boolean {
  if (existingTurns.length === 0) return true
  if (incoming.length === 0) {
    return liveRealtimeTurnsForScope(existingTurns, realtimeScopeThreadId).length > 0
  }

  const incomingThreadIds = turnThreadIds(incoming)
  const existingThreadIds = turnThreadIds(existingTurns)
  if (incomingThreadIds.size > 0 && existingThreadIds.size > 0) {
    for (const threadId of incomingThreadIds) {
      if (existingThreadIds.has(threadId)) {
        return true
      }
    }
    return false
  }

  const incomingTurnIds = new Set(incoming.map((turn) => turn.id))
  return existingTurns.some((turn) => incomingTurnIds.has(turn.id))
}

function mergeExistingRealtimeTurns(
  incoming: ConversationTurn[],
  existingTurns: ConversationTurn[],
  realtimeScopeThreadId?: string | null
): ConversationTurn[] {
  if (existingTurns.length === 0) return incoming
  if (incoming.length === 0) {
    return liveRealtimeTurnsForScope(existingTurns, realtimeScopeThreadId)
  }
  const existingById = new Map(existingTurns.map((turn) => [turn.id, turn]))
  const incomingIds = new Set(incoming.map((turn) => turn.id))
  const incomingThreadIds = turnThreadIds(incoming)
  const representedExistingIds = new Set<string>()
  const merged = incoming.map((turn) => {
    const existing = existingById.get(turn.id)
      ?? findRepresentedOptimisticTurn(turn, existingTurns, representedExistingIds)
    if (existing) representedExistingIds.add(existing.id)
    return mergeExistingRealtimeTurn(turn, existing)
  })
  for (const existing of existingTurns) {
    if (
      !incomingIds.has(existing.id) &&
      !representedExistingIds.has(existing.id) &&
      incomingThreadIds.size > 0 &&
      incomingThreadIds.has(existing.threadId)
    ) {
      merged.push(existing)
    }
  }
  return merged.sort(
    (a, b) => new Date(a.startedAt).getTime() - new Date(b.startedAt).getTime()
  )
}

function liveRealtimeTurnsForScope(
  existingTurns: ConversationTurn[],
  realtimeScopeThreadId?: string | null
): ConversationTurn[] {
  if (!realtimeScopeThreadId) return []
  return existingTurns.filter((turn) =>
    turn.threadId === realtimeScopeThreadId &&
    !isTerminalTurnStatus(turn.status)
  )
}

function findMatchingCommandExecution(
  items: ConversationItem[],
  toolCallId: string | undefined
): ConversationItem | undefined {
  if (!toolCallId) return undefined
  return items.find(
    (item) => item.type === 'commandExecution' && item.toolCallId === toolCallId
  )
}

function mergeExistingCommandExecutionIntoToolCall(
  item: ConversationItem,
  items: ConversationItem[]
): ConversationItem {
  if (item.type !== 'toolCall') return item
  const commandExecution = findMatchingCommandExecution(items, item.toolCallId)
  return commandExecution ? mergeCommandExecutionIntoToolCall(item, commandExecution) : item
}

function buildToolLikeItem(
  item: Record<string, unknown>,
  type: 'toolCall' | 'pluginFunctionCall' | 'dynamicToolCall',
  status: ConversationItem['status']
): ConversationItem {
  const payload = (item.payload ?? {}) as Record<string, unknown>
  const hasStructuredInvocationResult = type !== 'toolCall'
  const contentItems = hasStructuredInvocationResult
    ? normalizePluginFunctionContentItems(item.contentItems ?? payload.contentItems)
    : undefined
  const structuredResult = hasStructuredInvocationResult
    ? ((item.structuredResult as unknown) ?? (payload.structuredResult as unknown))
    : undefined
  const errorMessage = hasStructuredInvocationResult
    ? ((item.errorMessage as string | undefined) ?? (payload.errorMessage as string | undefined))
    : undefined
  const invocationResult = hasStructuredInvocationResult
    ? derivePluginFunctionResultText(contentItems, structuredResult, errorMessage)
    : undefined
  const meta = hasStructuredInvocationResult
    ? ((item._meta as Record<string, unknown> | undefined)
      ?? (payload._meta as Record<string, unknown> | undefined))
    : undefined
  const toolUi = hasStructuredInvocationResult
    ? normalizeToolUiDescriptor((item.ui as unknown) ?? (payload.ui as unknown))
    : undefined

  return {
    id: (item.id as string) ?? '',
    type,
    status,
    toolName:
      (item.toolName as string | undefined)
      ?? (payload.toolName as string | undefined)
      ?? (item.functionName as string | undefined)
      ?? (payload.functionName as string | undefined)
      ?? (item.name as string | undefined)
      ?? 'tool',
    toolCallId:
      (item.toolCallId as string | undefined)
      ?? (payload.callId as string | undefined)
      ?? (item.callId as string | undefined)
      ?? (item.id as string | undefined)
      ?? '',
    arguments:
      (item.arguments as Record<string, unknown> | undefined)
      ?? (payload.arguments as Record<string, unknown> | undefined),
    pluginId: (item.pluginId as string | undefined)
      ?? (payload.pluginId as string | undefined),
    pluginNamespace: (item.namespace as string | undefined)
      ?? (payload.namespace as string | undefined),
    functionName: (item.functionName as string | undefined)
      ?? (payload.functionName as string | undefined),
    contentItems,
    structuredResult,
    meta,
    toolUi,
    errorCode: (item.errorCode as string | undefined)
      ?? (payload.errorCode as string | undefined),
    errorMessage,
    result: (item.result as string | undefined)
      ?? (payload.result as string | undefined)
      ?? invocationResult,
    success: (item.success as boolean | undefined)
      ?? (payload.success as boolean | undefined),
    createdAt: (item.createdAt as string) ?? new Date().toISOString(),
    completedAt: (item.completedAt as string | undefined)
  }
}

function upsertItemById(items: ConversationItem[], item: ConversationItem): ConversationItem[] {
  const existingIndex = items.findIndex((i) => i.id === item.id)
  if (existingIndex < 0) {
    return sortItemsByCreatedAt([...items, item])
  }

  const next = [...items]
  next[existingIndex] = { ...next[existingIndex], ...item }
  return sortItemsByCreatedAt(next)
}

function isGuidanceUserMessage(item: ConversationItem): boolean {
  return item.type === 'userMessage' && item.deliveryMode === 'guidance'
}

const SYSTEM_LABELS: Record<string, string | null> = {
  compacting: 'systemStatus.compacting',
  consolidating: 'systemStatus.consolidating',
  compacted: null,
  compactFailed: null,
  compactSkipped: null,
  compactCancelled: null,
  consolidated: null,
  consolidationSkipped: null,
  consolidationFailed: null,
  consolidationCancelled: null
}

const CONSOLIDATION_TERMINAL_EVENTS = new Set([
  'consolidated',
  'consolidationSkipped',
  'consolidationFailed',
  'consolidationCancelled'
])

const MAINTENANCE_SYSTEM_LABELS = new Set([
  'systemStatus.compacting.manual',
  'systemStatus.consolidating'
])

function systemLabelForEvent(
  kind: string,
  params?: { turnId?: string | null }
): string | null | undefined {
  if (kind === 'consolidating' && params?.turnId) {
    return undefined
  }
  if (kind === 'compacting' && !params?.turnId) {
    return 'systemStatus.compacting.manual'
  }
  return SYSTEM_LABELS[kind]
}

function maintenanceKindForSystemEvent(
  kind: string,
  params?: { turnId?: string | null }
): MaintenanceKind | null | undefined {
  if (kind === 'consolidating') return params?.turnId ? undefined : 'consolidating'
  if (kind === 'compacting' && !params?.turnId) return 'compacting'
  if (
    kind === 'compacted'
    || kind === 'compactSkipped'
    || kind === 'compactFailed'
    || kind === 'compactCancelled'
    || kind === 'consolidated'
    || kind === 'consolidationSkipped'
    || kind === 'consolidationFailed'
    || kind === 'consolidationCancelled'
  ) {
    return null
  }
  return undefined
}

function backgroundMemoryStatusForSystemEvent(
  kind: string,
  params?: { turnId?: string | null }
): BackgroundMemoryStatus | null | undefined {
  if (kind === 'consolidating' && params?.turnId) return 'consolidating'
  if (CONSOLIDATION_TERMINAL_EVENTS.has(kind)) return null
  return undefined
}

function normalizeMaintenanceKind(kind: MaintenanceKind | string | null | undefined): MaintenanceKind | null {
  if (kind === 'compacting' || kind === 'consolidating') return kind
  return null
}

function systemLabelForMaintenanceKind(kind: MaintenanceKind): string {
  return kind === 'compacting'
    ? 'systemStatus.compacting.manual'
    : 'systemStatus.consolidating'
}

function computeSeverity(tokens: number, snapshot: {
  warningThreshold: number
  errorThreshold: number
}): ContextUsageSeverity {
  if (snapshot.errorThreshold > 0 && tokens >= snapshot.errorThreshold) return 'error'
  if (snapshot.warningThreshold > 0 && tokens >= snapshot.warningThreshold) return 'warning'
  return 'normal'
}

function clampPercent(value: number): number {
  if (!Number.isFinite(value)) return 0
  if (value < 0) return 0
  if (value > 1) return 1
  return value
}

function toContextUsage(snapshot: ContextUsageSnapshotInput): ContextUsage {
  const tokens = Math.max(0, Math.trunc(snapshot.tokens ?? 0))
  return {
    tokens,
    contextWindow: Math.max(0, Math.trunc(snapshot.contextWindow ?? 0)),
    autoCompactThreshold: Math.max(0, Math.trunc(snapshot.autoCompactThreshold ?? 0)),
    warningThreshold: Math.max(0, Math.trunc(snapshot.warningThreshold ?? 0)),
    errorThreshold: Math.max(0, Math.trunc(snapshot.errorThreshold ?? 0)),
    percentLeft: clampPercent(snapshot.percentLeft ?? 0),
    severity: computeSeverity(tokens, snapshot)
  }
}

/**
 * Override the current usage with a new absolute `tokens` snapshot. Recomputes
 * severity + percentLeft when `percentLeftOverride` is not supplied. Returns
 * the original slice unchanged when no usage has been seeded yet (usage/delta
 * before thread/read won't conjure thresholds out of thin air).
 */
function applyTokensToContextUsage(
  current: ContextUsage | null,
  tokens: number | null,
  percentLeftOverride: number | null = null
): ContextUsage | null {
  if (!current || tokens === null) return current
  const nextTokens = Math.max(0, Math.trunc(tokens))
  const percentLeft = percentLeftOverride !== null
    ? clampPercent(percentLeftOverride)
    : current.contextWindow > 0
      ? clampPercent(1 - nextTokens / current.contextWindow)
      : current.percentLeft
  return {
    ...current,
    tokens: nextTokens,
    percentLeft,
    severity: computeSeverity(nextTokens, current)
  }
}

function applyCompactedNoticeToContextUsage(
  current: ContextUsage | null,
  notice: {
    kind?: string
    tokensAfter?: number
    percentLeftAfter?: number
  }
): ContextUsage | null {
  if (notice.kind !== 'compacted') return current
  const tokens = typeof notice.tokensAfter === 'number' ? notice.tokensAfter : null
  return applyTokensToContextUsage(
    current,
    tokens,
    typeof notice.percentLeftAfter === 'number' ? notice.percentLeftAfter : null
  )
}

function latestCompactedNotice(turns: ConversationTurn[]): {
  kind: 'compacted'
  tokensAfter?: number
  percentLeftAfter?: number
} | null {
  for (let turnIndex = turns.length - 1; turnIndex >= 0; turnIndex -= 1) {
    const items = turns[turnIndex].items
    for (let itemIndex = items.length - 1; itemIndex >= 0; itemIndex -= 1) {
      const notice = items[itemIndex].systemNotice
      if (!notice || notice.kind !== 'compacted') continue
      return {
        kind: 'compacted',
        tokensAfter: notice.tokensAfter,
        percentLeftAfter: notice.percentLeftAfter
      }
    }
  }
  return null
}

function extractPartialJsonStringValue(json: string, key: string): string | null {
  const keyPattern = `"${key}"`
  const keyIndex = json.indexOf(keyPattern)
  if (keyIndex < 0) return null
  const colonIndex = json.indexOf(':', keyIndex + keyPattern.length)
  if (colonIndex < 0) return null
  const quoteIndex = json.indexOf('"', colonIndex + 1)
  if (quoteIndex < 0) return null

  let escaped = false
  let out = ''
  for (let i = quoteIndex + 1; i < json.length; i += 1) {
    const ch = json[i]
    if (escaped) {
      switch (ch) {
        case 'n':
          out += '\n'
          break
        case 'r':
          out += '\r'
          break
        case 't':
          out += '\t'
          break
        case 'b':
          out += '\b'
          break
        case 'f':
          out += '\f'
          break
        case '\\':
          out += '\\'
          break
        case '"':
          out += '"'
          break
        case '/':
          out += '/'
          break
        default:
          // Keep unknown escapes as-is so partial streams remain readable.
          out += '\\' + ch
          break
      }
      escaped = false
      continue
    }
    if (ch === '\\') {
      escaped = true
      continue
    }
    if (ch === '"') {
      return out
    }
    out += ch
  }

  return out
}

function appendStreamingArgumentsPreview(
  bufferKey: string,
  toolName: string,
  currentPreview: string | undefined,
  delta: string
): string {
  if (!SUB_AGENT_STREAMING_TOOLS.has(toolName)) {
    return (currentPreview ?? '') + delta
  }

  const existing = subAgentStreamingArgumentBuffers.get(bufferKey) ?? ''
  let buffer = existing + delta
  if (buffer.length > SUB_AGENT_ARGUMENT_BUFFER_MAX_CHARS) {
    buffer = buffer.slice(0, SUB_AGENT_ARGUMENT_BUFFER_MAX_CHARS)
  }
  subAgentStreamingArgumentBuffers.set(bufferKey, buffer)
  return buildSubAgentArgumentsPreview(buffer)
}

function buildSubAgentArgumentsPreview(rawArgs: string): string {
  const preview: Record<string, string> = {}
  for (const key of [
    'agentNickname',
    'agentPrompt',
    'agentRole',
    'profile',
    'workingDirectory',
    'childThreadId',
    'message'
  ]) {
    const value = extractPartialJsonStringValue(rawArgs, key)
    if (value != null) {
      preview[key] = truncatePreviewField(value)
    }
  }

  if (Object.keys(preview).length === 0) {
    return rawArgs.slice(0, Math.min(rawArgs.length, SUB_AGENT_ARGUMENT_FIELD_MAX_CHARS))
  }

  return JSON.stringify(preview)
}

function truncatePreviewField(value: string): string {
  const chars = Array.from(value)
  if (chars.length <= SUB_AGENT_ARGUMENT_FIELD_MAX_CHARS) return value
  return `${chars.slice(0, SUB_AGENT_ARGUMENT_FIELD_MAX_CHARS - 1).join('')}…`
}

function parseStreamRetryAttempt(message: string): Pick<StreamRetrySignal, 'attempt' | 'max'> {
  const match = message.match(/(\d+)\s*\/\s*(\d+)/)
  if (!match) {
    return { attempt: null, max: null }
  }

  const attempt = Number.parseInt(match[1], 10)
  const max = Number.parseInt(match[2], 10)
  return {
    attempt: Number.isFinite(attempt) ? attempt : null,
    max: Number.isFinite(max) ? max : null
  }
}

// ---------------------------------------------------------------------------
// Store
// ---------------------------------------------------------------------------

export const useConversationStore = create<ConversationStore>((set, get) => ({
  ...initialState,

  setTurns(turns, options = {}) {
    const converted = (turns as Array<Record<string, unknown>>).map((t) => {
      const maybeTurn = t as unknown as ConversationTurn
      if (typeof maybeTurn.items !== 'undefined' && !Array.isArray(maybeTurn.items)) {
        return wireTurnToConversationTurn(t)
      }
      // Already a ConversationTurn or has ConversationItem[] items
      if (Array.isArray(maybeTurn.items) && maybeTurn.id) {
        return maybeTurn
      }
      return wireTurnToConversationTurn(t)
    })

    // Rehydrate changedFiles from historical turns.
    // When a thread is loaded from history, the live wire events (onItemCompleted for
    // toolResult) never fire, so changedFiles is never populated. We reconstruct it
    // here by matching ToolResult items to their paired ToolCall items via callId,
    // merging result/success/duration back into the ToolCall, and extracting diffs
    // for WriteFile/EditFile calls.
    const rehydratedChangedFiles = new Map<string, FileDiff>()
    const rehydratedItemDiffs = new Map<string, FileDiff>()
    const rehydrateLocalDiffs = !get().remoteWorkspaceActive
    const rehydratedTurns = converted.map((turn) => {
      // Build a callId -> toolResult lookup for this turn
      const resultByCallId = new Map<string, ConversationItem>()
      const commandExecutionByCallId = new Map<string, ConversationItem>()
      const toolExecutionByCallId = new Map<string, ConversationItem>()
      for (const item of turn.items) {
        if (item.type === 'toolResult' && item.toolCallId) {
          resultByCallId.set(item.toolCallId, item)
        }
        if (item.type === 'commandExecution' && item.toolCallId) {
          commandExecutionByCallId.set(item.toolCallId, item)
        }
        if (item.type === 'toolExecution' && item.toolCallId) {
          toolExecutionByCallId.set(item.toolCallId, item)
        }
      }
      if (resultByCallId.size === 0 && commandExecutionByCallId.size === 0 && toolExecutionByCallId.size === 0) return turn

      // Merge result data into toolCall items and extract diffs
      const mergedItems = turn.items.map((item) => {
        if (item.type !== 'toolCall') return item
        const resultItem = resultByCallId.get(item.toolCallId ?? '')
        const commandExecution = commandExecutionByCallId.get(item.toolCallId ?? '')
        const toolExecution = toolExecutionByCallId.get(item.toolCallId ?? '')
        if (!resultItem) {
          const mergedWithToolExecution = toolExecution
            ? mergeToolExecutionIntoToolCall(item, toolExecution)
            : item
          return commandExecution
            ? mergeCommandExecutionIntoToolCall(mergedWithToolExecution, commandExecution)
            : mergedWithToolExecution
        }

        const resultText = resultItem.result ?? ''
        const success = resultItem.success !== false
        const startMs = item.createdAt ? new Date(item.createdAt).getTime() : 0
        const endMs = resultItem.completedAt ? new Date(resultItem.completedAt).getTime() : startMs

        const merged: ConversationItem = {
          ...item,
          result: resultText,
          success,
          duration: endMs - startMs,
          completedAt: resultItem.completedAt
        }
        const mergedWithToolExecution = toolExecution
          ? mergeToolExecutionIntoToolCall(merged, toolExecution)
          : merged
        const mergedWithCommandExecution = commandExecution
          ? mergeCommandExecutionIntoToolCall(mergedWithToolExecution, commandExecution)
          : mergedWithToolExecution

        // Accumulate diffs for file-writing tools (same path may appear multiple times)
        if (rehydrateLocalDiffs && item.arguments && (item.toolName === 'WriteFile' || item.toolName === 'EditFile')) {
          const fp =
            (item.arguments.path as string | undefined) ?? parseResultPath(resultText) ?? ''
          if (fp) {
            const existingBefore = rehydratedChangedFiles.get(fp)
            const perItem = computeIncrementalPerItemDiff(
              item.toolName as 'WriteFile' | 'EditFile',
              item.arguments,
              resultText,
              turn.id,
              existingBefore
            )
            if (perItem) {
              rehydratedItemDiffs.set(item.id, perItem)
            }
            const mergedDiff = mergeFileDiffIncrement(
              rehydratedChangedFiles.get(fp),
              item.toolName as 'WriteFile' | 'EditFile',
              item.arguments,
              resultText,
              turn.id
            )
            if (mergedDiff) {
              rehydratedChangedFiles.set(mergedDiff.filePath, mergedDiff)
            }
          }
        }

        return mergedWithCommandExecution
      })

      return { ...turn, items: mergedItems.filter((item) => item.type !== 'toolExecution') }
    })

    // If the loaded history contains a non-terminal turn (e.g. switching back to a thread
    // that had an active turn or is waiting on a user decision), restore active state.
    const currentTurns = get().turns
    const preserveExistingRealtime =
      options.preserveExistingRealtime === true &&
      hasSharedRealtimeScope(rehydratedTurns, currentTurns, options.realtimeScopeThreadId)
    const turnsForState = preserveExistingRealtime
      ? mergeExistingRealtimeTurns(rehydratedTurns, currentTurns, options.realtimeScopeThreadId)
      : rehydratedTurns
    const preserveEmptyRealtimeSnapshot =
      preserveExistingRealtime &&
      rehydratedTurns.length === 0 &&
      turnsForState.length > 0

    const activeTurn = [...turnsForState]
      .reverse()
      .find((t) => t.status === 'running' || t.status === 'waitingApproval' || t.status === 'waitingInput')
    const activeTurnStatus =
      activeTurn?.status === 'waitingApproval' || activeTurn?.status === 'waitingInput'
        ? activeTurn.status
        : activeTurn
          ? 'running'
          : 'idle'
    const compactedNotice = latestCompactedNotice(rehydratedTurns)
    set((state) => {
      const terminalApplied = applyPendingTerminalsToTurns(
        turnsForState,
        state.pendingTerminalByCallId
      )
      const toolCompletionApplied = applyPendingToolCompletionsToTurns(
        terminalApplied.turns,
        state.pendingToolCompletionsByCallKey
      )
      return {
        turns: toolCompletionApplied.turns,
        turnStatus: activeTurnStatus,
        activeTurnId: activeTurn ? activeTurn.id : null,
        streamingMessage: preserveEmptyRealtimeSnapshot ? state.streamingMessage : '',
        streamingMessageLastDeltaAt: preserveEmptyRealtimeSnapshot ? state.streamingMessageLastDeltaAt : null,
        streamingReasoning: preserveEmptyRealtimeSnapshot ? state.streamingReasoning : '',
        streamingReasoningStartedAt: preserveEmptyRealtimeSnapshot ? state.streamingReasoningStartedAt : null,
        activeItemId: preserveEmptyRealtimeSnapshot ? state.activeItemId : null,
        turnStartedAt: activeTurn
          ? (activeTurn.startedAt ? new Date(activeTurn.startedAt).getTime() : Date.now())
          : null,
        maintenanceKind: null,
        backgroundMemoryStatus: null,
        streamRetrySignals: [],
        changedFiles: preserveExistingRealtime
          ? new Map([...rehydratedChangedFiles, ...state.changedFiles])
          : rehydratedChangedFiles,
        itemDiffs: preserveExistingRealtime
          ? new Map([...rehydratedItemDiffs, ...state.itemDiffs])
          : rehydratedItemDiffs,
        streamingItemDiffs: new Map<string, FileDiff>(),
        streamingBaselines: new Map<string, StreamingFileBaseline>(),
        pendingTerminalByCallId: terminalApplied.pendingTerminalByCallId,
        pendingToolCompletionsByCallKey: toolCompletionApplied.pendingToolCompletionsByCallKey,
        contextUsage: compactedNotice
          ? applyCompactedNoticeToContextUsage(state.contextUsage, compactedNotice)
          : state.contextUsage
      }
    })
  },

  onTurnStarted(rawTurn) {
    const turn = wireTurnToConversationTurn(rawTurn)
    set((state) => {
      // Guard: if this turn ID already exists (was promoted via promoteOptimisticTurn before
      // the notification arrived), update in-place to avoid creating a duplicate key.
      const alreadyExists = state.turns.find((t) => t.id === turn.id)
      if (alreadyExists) {
        return {
          turns: state.turns.map((t) =>
            t.id === turn.id
              ? { ...mergeExistingRealtimeTurn(turn, t), status: 'running', startedAt: turn.startedAt }
              : t
          ),
          turnStatus: 'running',
          activeTurnId: turn.id,
          pendingApproval: null,
          pendingApprovals: [],
          pendingUserInput: null,
          streamingMessage: '',
          streamingMessageLastDeltaAt: null,
          streamingReasoning: '',
          streamingReasoningStartedAt: null,
          activeItemId: null,
          turnStartedAt: Date.now(),
          inputTokens: 0,
          outputTokens: 0,
          systemLabel: null,
          streamRetrySignals: [],
          maintenanceKind: null,
          subAgentEntries: [],
          streamingItemDiffs: new Map<string, FileDiff>(),
          streamingBaselines: new Map<string, StreamingFileBaseline>()
        }
      }

      // Replace the most recent optimistic turn (local-turn-*) with the real turn,
      // preserving the user message items that were added optimistically.
      const lastOptimistic = [...state.turns].reverse().find((t) => t.id.startsWith('local-turn-'))
      let nextTurns: ConversationTurn[]
      if (lastOptimistic) {
        nextTurns = state.turns.map((t) =>
          t.id === lastOptimistic.id ? mergeExistingRealtimeTurn(turn, lastOptimistic) : t
        )
      } else {
        nextTurns = [...state.turns, turn]
      }
      return {
        turns: nextTurns,
        turnStatus: 'running',
        activeTurnId: turn.id,
        pendingApproval: null,
        pendingApprovals: [],
        pendingUserInput: null,
        streamingMessage: '',
        streamingMessageLastDeltaAt: null,
        streamingReasoning: '',
        streamingReasoningStartedAt: null,
        activeItemId: null,
        turnStartedAt: Date.now(),
        inputTokens: 0,
        outputTokens: 0,
        systemLabel: null,
        streamRetrySignals: [],
        maintenanceKind: null,
        subAgentEntries: [],
        streamingItemDiffs: new Map<string, FileDiff>(),
        streamingBaselines: new Map<string, StreamingFileBaseline>()
      }
    })
  },

  onTurnCompleted(rawTurn) {
    const turn = wireTurnToConversationTurn(rawTurn)
    set((state) => {
      return {
        turns: state.turns.map((t) =>
          t.id === turn.id
            ? {
                ...t,
                status: 'completed' as TurnStatus,
                completedAt: turn.completedAt,
                tokenUsage: turn.tokenUsage,
                subAgentEntries: state.subAgentEntries
              }
            : t
        ),
        turnStatus: 'idle',
        activeTurnId: null,
        pendingApproval: null,
        pendingApprovals: [],
        pendingUserInput: null,
        streamingMessage: '',
        streamingMessageLastDeltaAt: null,
        streamingReasoning: '',
        streamingReasoningStartedAt: null,
        activeItemId: null,
        turnStartedAt: null,
        systemLabel: null,
        streamRetrySignals: state.streamRetrySignals.filter((signal) => signal.turnId !== turn.id),
        // Auto-send pending message after clearing it
        pendingMessage: null
      }
    })
    // pendingMessage was already cleared in the set() above.
    // App.tsx reads conv.pendingMessage BEFORE calling onTurnCompleted,
    // so pending message auto-send is handled there, not here.
  },

  onTurnFailed(rawTurn, error) {
    const turn = wireTurnToConversationTurn(rawTurn)
    set((state) => ({
      turns: state.turns.map((t) =>
        t.id === turn.id
          ? { ...t, status: 'failed' as TurnStatus, error, completedAt: turn.completedAt }
          : t
      ),
      turnStatus: 'idle',
      activeTurnId: null,
      pendingApproval: null,
      pendingApprovals: [],
      pendingUserInput: null,
      streamingMessage: '',
      streamingMessageLastDeltaAt: null,
      streamingReasoning: '',
      streamingReasoningStartedAt: null,
      activeItemId: null,
      turnStartedAt: null,
      systemLabel: null,
      streamRetrySignals: state.streamRetrySignals.filter((signal) => signal.turnId !== turn.id)
    }))
  },

  onTurnCancelled(rawTurn, reason) {
    const turn = wireTurnToConversationTurn(rawTurn)
    set((state) => ({
      turns: state.turns.map((t) =>
        t.id === turn.id
          ? {
              ...t,
              status: 'cancelled' as TurnStatus,
              cancelReason: reason,
              completedAt: turn.completedAt,
              items: t.items.map((item) =>
                item.status === 'completed' ? item : { ...item, status: 'completed' }
              )
            }
          : t
      ),
      turnStatus: 'idle',
      activeTurnId: null,
      pendingApproval: null,
      pendingApprovals: [],
      pendingUserInput: null,
      streamingMessage: '',
      streamingMessageLastDeltaAt: null,
      streamingReasoning: '',
      streamingReasoningStartedAt: null,
      activeItemId: null,
      turnStartedAt: null,
      systemLabel: null,
      streamRetrySignals: state.streamRetrySignals.filter((signal) => signal.turnId !== turn.id)
    }))
  },

  onItemStarted(params) {
    const item = params.item as Record<string, unknown>
    const type = item?.type as string
    const itemId = item?.id as string
    const turnId = params.turnId as string

    if (type === 'agentMessage') {
      const newItem: ConversationItem = {
        id: itemId ?? '',
        type: 'agentMessage',
        status: 'streaming',
        text: '',
        createdAt: (item?.createdAt as string) ?? new Date().toISOString()
      }
      set((state) => ({
        streamingMessage: '',
        streamingMessageLastDeltaAt: null,
        activeItemId: itemId,
        turns: state.turns.map((t) =>
          t.id === turnId && !t.items.some((existing) => existing.id === newItem.id)
            ? { ...t, items: sortItemsByCreatedAt([...t.items, newItem]) }
            : t
        )
      }))
    } else if (type === 'userMessage') {
      const newItem = wireItemToConversationItem(item)
      if (!isGuidanceUserMessage(newItem)) return
      set((state) => ({
        turns: state.turns.map((t) =>
          t.id === turnId ? { ...t, items: upsertItemById(t.items, newItem) } : t
        )
      }))
    } else if (type === 'reasoningContent') {
      const newItem: ConversationItem = {
        id: itemId ?? '',
        type: 'reasoningContent',
        status: 'streaming',
        reasoning: '',
        createdAt: (item?.createdAt as string) ?? new Date().toISOString()
      }
      set((state) => ({
        streamingReasoning: '',
        streamingReasoningStartedAt: Date.now(),
        activeItemId: itemId,
        turns: state.turns.map((t) =>
          t.id === turnId && !t.items.some((existing) => existing.id === newItem.id)
            ? { ...t, items: sortItemsByCreatedAt([...t.items, newItem]) }
            : t
        )
      }))
    } else if (isToolLikeItemType(type)) {
      const baseItem = buildToolLikeItem(item, type, 'started')
      set((state) => {
        let nextPending = state.pendingTerminalByCallId
        let nextPendingToolCompletions = state.pendingToolCompletionsByCallKey
        const turns = state.turns.map((t) => {
          if (t.id !== turnId || t.items.some((existing) => existing.id === baseItem.id)) return t
          const nextItem = type === 'toolCall'
            ? mergeExistingCommandExecutionIntoToolCall(baseItem, t.items)
            : baseItem
          const nextTurn = {
            ...t,
            items: sortItemsByCreatedAt([...t.items, nextItem])
          }
          if (type !== 'toolCall') return nextTurn
          const applied = applyPendingTerminalsToTurn(nextTurn, state.pendingTerminalByCallId)
          nextPending = removePendingTerminalEntries(nextPending, applied.appliedCallIds)
          const completionApplied = applyPendingToolCompletionsToTurn(
            applied.turn,
            state.pendingToolCompletionsByCallKey
          )
          nextPendingToolCompletions = removePendingToolCompletionEntries(
            nextPendingToolCompletions,
            completionApplied.appliedKeys
          )
          return completionApplied.turn
        })
        return {
          turns,
          pendingTerminalByCallId: nextPending,
          pendingToolCompletionsByCallKey: nextPendingToolCompletions
        }
      })
    } else if (type === 'commandExecution') {
      const itemPayload = (item?.payload ?? {}) as Record<string, unknown>
      const newItem: ConversationItem = {
        id: itemId ?? '',
        type: 'commandExecution',
        status: 'started',
        command: (item?.command as string | undefined) ?? (itemPayload.command as string | undefined) ?? '',
        workingDirectory: (item?.workingDirectory as string | undefined)
          ?? (itemPayload.workingDirectory as string | undefined),
        commandSource: (item?.source as 'host' | 'sandbox' | undefined)
          ?? (itemPayload.source as 'host' | 'sandbox' | undefined),
        aggregatedOutput: (item?.aggregatedOutput as string | undefined)
          ?? (itemPayload.aggregatedOutput as string | undefined)
          ?? '',
        exitCode: (item?.exitCode as number | null | undefined)
          ?? (itemPayload.exitCode as number | null | undefined),
        // Wire item.status is item lifecycle (started/completed), not shell execution state.
        // Prefer payload.status (inProgress/completed/failed/cancelled).
        executionStatus: (itemPayload.status as ConversationItem['executionStatus'] | undefined)
          ?? 'inProgress',
        toolCallId: (item?.callId as string | undefined)
          ?? (itemPayload.callId as string | undefined),
        createdAt: (item?.createdAt as string) ?? new Date().toISOString()
      }
      set((state) => ({
        turns: state.turns.map((t) =>
          t.id !== turnId
            ? t
            : t.items.some((existing) => existing.id === newItem.id)
              ? t
            : {
                ...t,
                items: sortItemsByCreatedAt(
                  mergeCommandExecutionAcrossItems([...t.items, newItem], newItem)
                )
              }
        )
      }))
    }
  },

  onAgentMessageDelta(delta) {
    const receivedAt = Date.now()
    set((state) => ({
      streamingMessage: state.streamingMessage + delta,
      streamingMessageLastDeltaAt: receivedAt
    }))
  },

  onReasoningDelta(delta) {
    set((state) => ({ streamingReasoning: state.streamingReasoning + delta }))
  },

  onCommandExecutionDelta(params) {
    const turnId = params.turnId ?? ''
    const itemId = params.itemId ?? ''
    const delta = params.delta ?? ''
    if (!turnId || !itemId || !delta) return

    set((state) => ({
      turns: state.turns.map((t) =>
        t.id !== turnId
          ? t
          : {
              ...t,
              items: sortItemsByCreatedAt((() => {
                const updatedItems = t.items.map((i) =>
                  i.id === itemId && i.type === 'commandExecution'
                    ? {
                        ...i,
                        aggregatedOutput: (i.aggregatedOutput ?? '') + delta
                      }
                    : i
                )
                const commandExecution = updatedItems.find((i) => i.id === itemId && i.type === 'commandExecution')
                return commandExecution
                  ? mergeCommandExecutionAcrossItems(updatedItems, commandExecution)
                  : updatedItems
              })())
            }
      )
    }))
  },

  onTerminalEvent(params) {
    const terminal = params.terminal ?? {}
    if (isRunInBackgroundTerminal(terminal)) return

    const callId = terminal.callId as string | undefined
    if (!callId) return

    const turnId = (terminal.turnId as string | undefined) ?? ''
    const delta = params.delta ?? ''
    set((state) => {
      const pendingEntry = mergePendingTerminalEntry(
        state.pendingTerminalByCallId.get(callId),
        terminal,
        params.event,
        delta
      )
      let applied = false
      const turns = state.turns.map((t) => {
        if (turnId && t.id !== turnId) return t
        if (!turnHasShellToolCall(t, callId)) {
          return t
        }
        applied = true
        return {
          ...t,
          items: sortItemsByCreatedAt(
            mergeTerminalAcrossItems(t.items, pendingEntry.terminal, pendingEntry.event, '')
          )
        }
      })
      const pendingTerminalByCallId = new Map(state.pendingTerminalByCallId)
      if (applied) {
        pendingTerminalByCallId.delete(callId)
      } else {
        pendingTerminalByCallId.set(callId, pendingEntry)
      }
      return { turns, pendingTerminalByCallId }
    })
  },

  onToolCallArgumentsDelta(params) {
    const turnId = params.turnId ?? ''
    const itemId = params.itemId ?? ''
    const delta = params.delta ?? ''
    if (!turnId || !itemId || !delta) return
    const streamingBufferKey = `${turnId}:${itemId}`
    let shouldLoadBaseline = false
    let baselinePath = ''
    let nextArgumentsPreviewForLoad = ''
    let nextToolNameForLoad = ''

    set((state) => {
      let capturedToolName = ''
      let capturedPreview = ''
      let nextPendingToolCompletions = state.pendingToolCompletionsByCallKey
      const nextTurns = state.turns.map((t) => {
        if (t.id !== turnId) return t
        const existing = t.items.find((i) => i.id === itemId)
        const nextItems = existing
          ? t.items.map((i) => {
              if (i.id !== itemId) return i
              const nextToolName = params.toolName ?? i.toolName ?? 'tool'
              const nextArgumentsPreview = appendStreamingArgumentsPreview(
                streamingBufferKey,
                nextToolName,
                i.argumentsPreview,
                delta
              )
              capturedToolName = nextToolName
              capturedPreview = nextArgumentsPreview
              return {
                ...i,
                type: 'toolCall' as const,
                status: 'streaming' as const,
                toolName: nextToolName,
                toolCallId: params.callId ?? i.toolCallId ?? itemId,
                argumentsPreview: nextArgumentsPreview,
                streamingFileContent: extractPartialJsonStringValue(nextArgumentsPreview, 'content')
                  ?? extractPartialJsonStringValue(nextArgumentsPreview, 'newText')
                  ?? i.streamingFileContent
              }
            })
          : [
              ...t.items,
              (() => {
                const toolName = params.toolName ?? 'tool'
                const argumentsPreview = appendStreamingArgumentsPreview(
                  streamingBufferKey,
                  toolName,
                  undefined,
                  delta
                )
                return {
                  id: itemId,
                  type: 'toolCall' as const,
                  status: 'streaming' as const,
                  toolName,
                  toolCallId: params.callId ?? itemId,
                  createdAt: new Date().toISOString(),
                  argumentsPreview,
                  streamingFileContent: extractPartialJsonStringValue(argumentsPreview, 'content')
                    ?? extractPartialJsonStringValue(argumentsPreview, 'newText')
                    ?? undefined
                }
              })()
            ]
        if (!capturedToolName) {
          const created = nextItems.find((i) => i.id === itemId)
          capturedToolName = created?.toolName ?? params.toolName ?? ''
          capturedPreview = created?.argumentsPreview ?? delta
        }
        const nextTurn = { ...t, items: sortItemsByCreatedAt(nextItems) }
        const completionApplied = applyPendingToolCompletionsToTurn(
          nextTurn,
          state.pendingToolCompletionsByCallKey
        )
        nextPendingToolCompletions = removePendingToolCompletionEntries(
          nextPendingToolCompletions,
          completionApplied.appliedKeys
        )
        return completionApplied.turn
      })

      const nextStreamingItemDiffs = new Map(state.streamingItemDiffs)
      const nextStreamingBaselines = new Map(state.streamingBaselines)
      const isFileTool = capturedToolName === 'WriteFile' || capturedToolName === 'EditFile'
      if (isFileTool) {
        const previewPath = extractStreamingFilePath(capturedPreview)
        const baseline = nextStreamingBaselines.get(itemId)
        if (previewPath && baseline?.path !== previewPath) {
          nextStreamingBaselines.set(itemId, { path: previewPath, originalContent: baseline?.originalContent ?? '' })
        }
        const baselineContent = baseline?.originalContent ?? (previewPath ? nextStreamingBaselines.get(itemId)?.originalContent : undefined)
        const streamingDiff = computeStreamingFileDiff({
          toolName: capturedToolName as 'WriteFile' | 'EditFile',
          turnId,
          argumentsPreview: capturedPreview,
          filePath: previewPath ?? baseline?.path ?? null,
          baselineContent
        })
        if (streamingDiff) {
          nextStreamingItemDiffs.set(itemId, streamingDiff)
        } else {
          nextStreamingItemDiffs.delete(itemId)
        }
        if (previewPath && !baseline && state.workspacePath && !state.remoteWorkspaceActive) {
          shouldLoadBaseline = true
          baselinePath = previewPath
          nextArgumentsPreviewForLoad = capturedPreview
          nextToolNameForLoad = capturedToolName
        }
      } else {
        nextStreamingItemDiffs.delete(itemId)
      }

      return {
        turns: nextTurns,
        streamingItemDiffs: nextStreamingItemDiffs,
        streamingBaselines: nextStreamingBaselines,
        pendingToolCompletionsByCallKey: nextPendingToolCompletions
      }
    })

    if (!shouldLoadBaseline || !baselinePath) return
    const state = get()
    if (state.remoteWorkspaceActive) return
    const workspacePath = state.workspacePath
    if (!workspacePath || typeof window === 'undefined' || !window.api?.file?.readFile) return
    const absPath = toAbsoluteWorkspacePath(workspacePath, baselinePath)
    void window.api.file.readFile(absPath)
      .then((content) => content ?? '')
      .catch(() => '')
      .then((originalContent) => {
        set((state) => {
          const turn = state.turns.find((t) => t.id === turnId)
          const item = turn?.items.find((i) => i.id === itemId)
          if (!item) return {}

          const toolName = (item.toolName ?? nextToolNameForLoad) as 'WriteFile' | 'EditFile' | string
          if (toolName !== 'WriteFile' && toolName !== 'EditFile') return {}
          const preview = item.argumentsPreview ?? nextArgumentsPreviewForLoad
          if (!preview) return {}
          const resolvedPath = extractStreamingFilePath(preview) ?? baselinePath

          const nextStreamingBaselines = new Map(state.streamingBaselines)
          nextStreamingBaselines.set(itemId, {
            path: resolvedPath,
            originalContent
          })

          const streamingDiff = computeStreamingFileDiff({
            toolName,
            turnId,
            argumentsPreview: preview,
            filePath: resolvedPath,
            baselineContent: originalContent
          })
          const nextStreamingItemDiffs = new Map(state.streamingItemDiffs)
          if (streamingDiff) {
            nextStreamingItemDiffs.set(itemId, streamingDiff)
          } else {
            nextStreamingItemDiffs.delete(itemId)
          }

          return {
            streamingBaselines: nextStreamingBaselines,
            streamingItemDiffs: nextStreamingItemDiffs
          }
        })
      })
  },

  onItemCompleted(params) {
    const item = params.item as Record<string, unknown>
    const type = item?.type as string
    const turnId = params.turnId as string
    const state = get()

    if (type === 'agentMessage') {
      const itemId = (item?.id as string) ?? ''
      // Deduplicate: skip if this item was already completed (streaming placeholder shares the same id)
      const alreadyCommitted = state.turns
        .find((t) => t.id === turnId)
        ?.items.some((i) => i.id === itemId && i.type === 'agentMessage' && i.status === 'completed')
      if (alreadyCommitted) {
        // Still clear streaming state even if we skip the item
        set({ streamingMessage: '', streamingMessageLastDeltaAt: null, activeItemId: null })
        return
      }
      const finalText = state.streamingMessage || ((item?.text as string) ?? (item?.content as string) ?? '')
      set((s) => {
        const turn = s.turns.find((t) => t.id === turnId)
        if (!turn) {
          return { streamingMessage: '', streamingMessageLastDeltaAt: null, activeItemId: null }
        }
        const hasPlaceholder = turn.items.some((i) => i.id === itemId && i.type === 'agentMessage')
        const completedAt = (item?.completedAt as string) ?? new Date().toISOString()
        const nextItems = hasPlaceholder
          ? turn.items.map((i) =>
              i.id === itemId && i.type === 'agentMessage'
                ? {
                    ...i,
                    status: 'completed' as const,
                    text: finalText,
                    completedAt
                  }
                : i
            )
          : sortItemsByCreatedAt([
              ...turn.items,
              {
                id: itemId,
                type: 'agentMessage' as const,
                status: 'completed' as const,
                text: finalText,
                createdAt: (item?.createdAt as string) ?? new Date().toISOString(),
                completedAt
              }
            ])
        return {
          turns: s.turns.map((t) =>
            t.id === turnId ? { ...t, items: sortItemsByCreatedAt(nextItems) } : t
          ),
          streamingMessage: '',
          streamingMessageLastDeltaAt: null,
          activeItemId: null
        }
      })
    } else if (type === 'reasoningContent') {
      const itemId = (item?.id as string) ?? ''
      const alreadyCommitted = state.turns
        .find((t) => t.id === turnId)
        ?.items.some((i) => i.id === itemId && i.type === 'reasoningContent' && i.status === 'completed')
      if (alreadyCommitted) {
        set({ streamingReasoning: '', streamingReasoningStartedAt: null, activeItemId: null })
        return
      }
      const finalText = state.streamingReasoning || ((item?.text as string) ?? (item?.content as string) ?? '')
      const startedAt = state.streamingReasoningStartedAt
      const elapsed = startedAt ? Math.round((Date.now() - startedAt) / 1000) : 0
      const completedAt = (item?.completedAt as string) ?? new Date().toISOString()
      set((s) => {
        const turn = s.turns.find((t) => t.id === turnId)
        if (!turn) {
          return { streamingReasoning: '', streamingReasoningStartedAt: null, activeItemId: null }
        }
        const hasPlaceholder = turn.items.some((i) => i.id === itemId && i.type === 'reasoningContent')
        const nextItems = hasPlaceholder
          ? turn.items.map((i) =>
              i.id === itemId && i.type === 'reasoningContent'
                ? {
                    ...i,
                    status: 'completed' as const,
                    reasoning: finalText,
                    elapsedSeconds: elapsed,
                    completedAt
                  }
                : i
            )
          : sortItemsByCreatedAt([
              ...turn.items,
              {
                id: itemId,
                type: 'reasoningContent' as const,
                status: 'completed' as const,
                reasoning: finalText,
                elapsedSeconds: elapsed,
                createdAt: (item?.createdAt as string) ?? new Date().toISOString(),
                completedAt
              }
            ])
        return {
          turns: s.turns.map((t) =>
            t.id === turnId ? { ...t, items: sortItemsByCreatedAt(nextItems) } : t
          ),
          streamingReasoning: '',
          streamingReasoningStartedAt: null,
          activeItemId: null
        }
      })
    } else if (type === 'userMessage') {
      const newItem = wireItemToConversationItem(item)
      if (!isGuidanceUserMessage(newItem)) return
      set((s) => ({
        turns: s.turns.map((t) =>
          t.id === turnId ? { ...t, items: upsertItemById(t.items, newItem) } : t
        )
      }))
    } else if (type === 'error') {
      const newItem: ConversationItem = {
        id: (item?.id as string) ?? '',
        type: 'error',
        status: 'completed',
        text: (item?.message as string) ?? (item?.text as string) ?? 'Unknown error',
        createdAt: (item?.createdAt as string) ?? new Date().toISOString(),
        completedAt: (item?.completedAt as string) ?? new Date().toISOString()
      }
      set((s) => ({
        turns: s.turns.map((t) =>
          t.id === turnId ? { ...t, items: sortItemsByCreatedAt([...t.items, newItem]) } : t
        )
      }))
    } else if (type === 'systemNotice') {
      const itemId = (item?.id as string) ?? ''
      const itemPayload = (item?.payload ?? {}) as Record<string, unknown>
      const notice = {
        kind: (itemPayload.kind as string | undefined) ?? 'compacted',
        trigger: itemPayload.trigger as string | undefined,
        mode: itemPayload.mode as string | undefined,
        tokensBefore: itemPayload.tokensBefore as number | undefined,
        tokensAfter: itemPayload.tokensAfter as number | undefined,
        percentLeftAfter: itemPayload.percentLeftAfter as number | undefined,
        clearedToolResults: itemPayload.clearedToolResults as number | undefined,
        sourceThreadId: itemPayload.sourceThreadId as string | undefined
      }
      const newItem: ConversationItem = {
        id: itemId,
        type: 'systemNotice',
        status: 'completed',
        createdAt: (item?.createdAt as string) ?? new Date().toISOString(),
        completedAt: (item?.completedAt as string) ?? new Date().toISOString(),
        systemNotice: notice
      }
      set((s) => ({
        turns: s.turns.map((t) => {
          if (t.id !== turnId) return t
          if (t.items.some((i) => i.id === itemId)) return t
          return { ...t, items: sortItemsByCreatedAt([...t.items, newItem]) }
        }),
        contextUsage: applyCompactedNoticeToContextUsage(s.contextUsage, notice)
      }))
    } else if (type === 'toolCall') {
      // Mark the tool call item itself as completed and merge finalized payload fields.
      const itemPayload = (item?.payload ?? {}) as Record<string, unknown>
      const itemId = (item?.id as string) ?? ''
      if (itemId) subAgentStreamingArgumentBuffers.delete(`${turnId}:${itemId}`)
      const completedArgs = (item?.arguments as Record<string, unknown> | undefined)
        ?? (itemPayload.arguments as Record<string, unknown> | undefined)
      const completedToolName = (item?.toolName as string | undefined)
        ?? (itemPayload.toolName as string | undefined)
      const completedCallId = (item?.toolCallId as string | undefined)
        ?? (itemPayload.callId as string | undefined)
      set((s) => {
        let nextPending = s.pendingTerminalByCallId
        let nextPendingToolCompletions = s.pendingToolCompletionsByCallKey
        const turns = s.turns.map((t) => {
          if (t.id !== turnId) return t
          const nextTurn = {
            ...t,
            items: sortItemsByCreatedAt(
              t.items.map((i) =>
                i.id === itemId
                  ? mergeExistingCommandExecutionIntoToolCall({
                      ...i,
                      status: 'completed' as const,
                      completedAt: (item?.completedAt as string),
                      arguments: completedArgs ?? i.arguments,
                      toolName: completedToolName ?? i.toolName,
                      toolCallId: completedCallId ?? i.toolCallId
                    }, t.items)
                  : i
              )
            )
          }
          const applied = applyPendingTerminalsToTurn(nextTurn, s.pendingTerminalByCallId)
          nextPending = removePendingTerminalEntries(nextPending, applied.appliedCallIds)
          const completionApplied = applyPendingToolCompletionsToTurn(
            applied.turn,
            s.pendingToolCompletionsByCallKey
          )
          nextPendingToolCompletions = removePendingToolCompletionEntries(
            nextPendingToolCompletions,
            completionApplied.appliedKeys
          )
          return completionApplied.turn
        })
        return {
          turns,
          pendingTerminalByCallId: nextPending,
          pendingToolCompletionsByCallKey: nextPendingToolCompletions
        }
      })
    } else if (type === 'commandExecution') {
      const itemPayload = (item?.payload ?? {}) as Record<string, unknown>
      set((s) => ({
        turns: s.turns.map((t) =>
          t.id !== turnId
            ? t
            : {
                ...t,
                items: sortItemsByCreatedAt((() => {
                  const updatedItems = t.items.map((i) => {
                    if (i.id !== (item?.id as string) || i.type !== 'commandExecution') return i
                    const startMs = i.createdAt ? new Date(i.createdAt).getTime() : Date.now()
                    const endMs = (item?.completedAt as string)
                      ? new Date(item.completedAt as string).getTime()
                      : Date.now()
                    return {
                      ...i,
                      status: 'completed' as const,
                      command: (item?.command as string | undefined)
                        ?? (itemPayload.command as string | undefined)
                        ?? i.command,
                      workingDirectory: (item?.workingDirectory as string | undefined)
                        ?? (itemPayload.workingDirectory as string | undefined)
                        ?? i.workingDirectory,
                      commandSource: (item?.source as 'host' | 'sandbox' | undefined)
                        ?? (itemPayload.source as 'host' | 'sandbox' | undefined)
                        ?? i.commandSource,
                      aggregatedOutput: (item?.aggregatedOutput as string | undefined)
                        ?? (itemPayload.aggregatedOutput as string | undefined)
                        ?? i.aggregatedOutput
                        ?? '',
                      exitCode: (item?.exitCode as number | null | undefined)
                        ?? (itemPayload.exitCode as number | null | undefined)
                        ?? i.exitCode,
                      executionStatus: (itemPayload.status as ConversationItem['executionStatus'] | undefined)
                        ?? i.executionStatus
                        ?? 'completed',
                      toolCallId: (item?.callId as string | undefined)
                        ?? (itemPayload.callId as string | undefined)
                        ?? i.toolCallId,
                      duration: (itemPayload.durationMs as number | undefined) ?? (endMs - startMs),
                      completedAt: (item?.completedAt as string) ?? new Date().toISOString()
                    }
                  })
                  const commandExecution = updatedItems.find(
                    (i) => i.id === (item?.id as string) && i.type === 'commandExecution'
                  )
                  return commandExecution
                    ? mergeCommandExecutionAcrossItems(updatedItems, commandExecution)
                    : updatedItems
                })())
            }
        )
      }))
    } else if (type === 'toolExecution') {
      const toolExecution = wireItemToConversationItem(item)
      const callId = toolExecution.toolCallId
      const key = toolCompletionKey(turnId, callId)
      if (!callId || !key) return

      set((s) => {
        let matched = false
        const pending = new Map(s.pendingToolCompletionsByCallKey)
        const entry = mergePendingToolCompletionEntry(pending.get(key), {
          turnId,
          callId,
          toolExecution
        })
        const turns = s.turns.map((t) => {
          if (t.id !== turnId || !turnHasToolCall(t, callId)) return t
          matched = true
          return {
            ...t,
            items: sortItemsByCreatedAt(
              mergeToolCompletionEntryIntoItems(t.items, entry)
            )
          }
        })

        if (matched) {
          pending.delete(key)
        } else {
          pending.set(key, entry)
        }
        return { turns, pendingToolCompletionsByCallKey: pending }
      })
    } else if (type === 'pluginFunctionCall' || type === 'dynamicToolCall') {
      const completedItem = buildToolLikeItem(
        item,
        type,
        'completed'
      )
      set((s) => ({
        turns: s.turns.map((t) =>
          t.id !== turnId
            ? t
            : {
                ...t,
                items: sortItemsByCreatedAt(
                  t.items.map((i) => {
                    if (i.id !== (item?.id as string)) return i
                    const startMs = i.createdAt ? new Date(i.createdAt).getTime() : Date.now()
                    const endMs = (item?.completedAt as string)
                      ? new Date(item.completedAt as string).getTime()
                      : Date.now()
                    return {
                      ...i,
                      status: 'completed' as const,
                      toolName: completedItem.toolName ?? i.toolName,
                      toolCallId: completedItem.toolCallId ?? i.toolCallId,
                      arguments: completedItem.arguments ?? i.arguments,
                      result: completedItem.result ?? i.result,
                      success: completedItem.success ?? true,
                      pluginId: completedItem.pluginId ?? i.pluginId,
                      pluginNamespace: completedItem.pluginNamespace ?? i.pluginNamespace,
                      functionName: completedItem.functionName ?? i.functionName,
                      contentItems: completedItem.contentItems ?? i.contentItems,
                      structuredResult: completedItem.structuredResult ?? i.structuredResult,
                      meta: completedItem.meta ?? i.meta,
                      toolUi: completedItem.toolUi ?? i.toolUi,
                      errorCode: completedItem.errorCode ?? i.errorCode,
                      errorMessage: completedItem.errorMessage ?? i.errorMessage,
                      duration: endMs - startMs,
                      completedAt: (item?.completedAt as string) ?? new Date().toISOString()
                    }
                  })
                )
              }
        )
      }))
    } else if (type === 'toolResult') {
      // Extract nested payload for toolResult items (wire protocol: item.payload.{callId,result,success})
      const itemPayload = (item?.payload ?? {}) as Record<string, unknown>
      // Find the matching toolCall item by callId to update with result data
      const callId = (item?.callId as string | undefined)
        ?? (itemPayload.callId as string | undefined)
        ?? (item?.toolCallId as string | undefined)
      const key = toolCompletionKey(turnId, callId)
      if (!callId || !key) return
      const resultText = (item?.result as string | undefined)
        ?? (itemPayload.result as string | undefined)
        ?? (item?.text as string | undefined)
        ?? ''
      const success = (item?.success as boolean | undefined)
        ?? (itemPayload.success as boolean | undefined)
        ?? true
      const toolResult = wireItemToConversationItem(item)

      set((s) => {
        let matched = false
        const pending = new Map(s.pendingToolCompletionsByCallKey)
        const entry = mergePendingToolCompletionEntry(pending.get(key), {
          turnId,
          callId,
          toolResult: {
            ...toolResult,
            toolCallId: callId,
            result: resultText,
            success
          }
        })
        const nextTurns = s.turns.map((t) => {
          if (t.id !== turnId || !turnHasToolCall(t, callId)) return t
          matched = true
          return {
            ...t,
            items: sortItemsByCreatedAt(
              mergeToolCompletionEntryIntoItems(t.items, entry)
            )
          }
        })

        if (matched) {
          pending.delete(key)
        } else {
          pending.set(key, entry)
        }
        return {
          turns: nextTurns,
          pendingToolCompletionsByCallKey: pending
        }
      })

      // Cumulative diff (async — may read disk); requires workspace path for IPC
      const matchedCallItem = get()
        .turns.find((t) => t.id === turnId)
        ?.items.find((i) => i.type === 'toolCall' && i.toolCallId === callId)
      const toolName = matchedCallItem?.toolName ?? ''
      const args = matchedCallItem?.arguments
      const afterToolState = get()
      const wsPath = afterToolState.remoteWorkspaceActive ? '' : afterToolState.workspacePath
      if (args && (toolName === 'WriteFile' || toolName === 'EditFile')) {
        const fp = (args.path as string | undefined) ?? parseResultPath(resultText) ?? ''
        if (fp && matchedCallItem?.id) {
          const existingBeforeCumulative = get().changedFiles.get(fp)
          const incremental = computeIncrementalPerItemDiff(
            toolName as 'WriteFile' | 'EditFile',
            args,
            resultText,
            turnId,
            existingBeforeCumulative
          )
          if (incremental) {
            useConversationStore.getState().upsertItemDiff(matchedCallItem.id, incremental)
          }
          if (wsPath) {
            void computeCumulativeFileDiff({
              filePath: fp,
              toolName,
              args,
              resultText,
              turnId,
              existing: existingBeforeCumulative,
              workspacePath: wsPath
            }).then((diff) => {
              if (diff) {
                useConversationStore.getState().upsertChangedFile(diff)
              }
            })
          }
        }
      }
      if (matchedCallItem?.id) {
        set((s) => {
          const nextStreamingItemDiffs = new Map(s.streamingItemDiffs)
          const nextStreamingBaselines = new Map(s.streamingBaselines)
          nextStreamingItemDiffs.delete(matchedCallItem.id)
          nextStreamingBaselines.delete(matchedCallItem.id)
          return {
            streamingItemDiffs: nextStreamingItemDiffs,
            streamingBaselines: nextStreamingBaselines
          }
        })
      }
    }
  },

  onUsageDelta(inputTokens, outputTokens, totalInputTokens, totalOutputTokens, contextUsageSnapshot) {
    set((state) => {
      const nextInput = state.inputTokens + inputTokens
      const nextOutput = state.outputTokens + outputTokens
      const nextContextUsage = contextUsageSnapshot
        ? toContextUsage(contextUsageSnapshot)
        : applyTokensToContextUsage(
          state.contextUsage,
          typeof totalInputTokens === 'number' ? totalInputTokens : null
        )
      void totalOutputTokens
      return {
        inputTokens: nextInput,
        outputTokens: nextOutput,
        contextUsage: nextContextUsage
      }
    })
  },

  onSystemEvent(kind, params) {
    const label = systemLabelForEvent(kind, params)
    const maintenanceKind = maintenanceKindForSystemEvent(kind, params)
    const backgroundMemoryStatus = backgroundMemoryStatusForSystemEvent(kind, params)
    if (label !== undefined) {
      set({ systemLabel: label })
    }
    if (maintenanceKind !== undefined) {
      set({ maintenanceKind })
    }
    if (backgroundMemoryStatus !== undefined) {
      set({ backgroundMemoryStatus })
    }

    if (kind === 'streamError') {
      const rawMessage = params?.message?.trim()
      const turnId = params?.turnId ?? get().activeTurnId
      if (rawMessage && turnId) {
        set((state) => {
          const lastSignal = state.streamRetrySignals[state.streamRetrySignals.length - 1]
          if (lastSignal?.turnId === turnId && lastSignal.rawMessage === rawMessage) {
            return {}
          }

          const createdAtMs = Date.now()
          const createdAt = new Date(createdAtMs).toISOString()
          const attempt = parseStreamRetryAttempt(rawMessage)
          return {
            streamRetrySignals: [
              ...state.streamRetrySignals,
              {
                id: `stream-retry-${turnId}-${createdAtMs}-${state.streamRetrySignals.length}`,
                turnId,
                rawMessage,
                attempt: attempt.attempt,
                max: attempt.max,
                createdAt
              }
            ]
          }
        })
      }
    }

    if (kind === 'compacting') {
      return
    }

    if (kind === 'compacted' || kind === 'compactSkipped' || kind === 'compactFailed' || kind === 'compactCancelled') {
      if (params?.contextUsage) {
        set({ contextUsage: toContextUsage(params.contextUsage) })
        return
      }

      const tokens = typeof params?.tokenCount === 'number' ? params.tokenCount : null
      set((state) => ({
        contextUsage: applyTokensToContextUsage(
          state.contextUsage,
          tokens,
          typeof params?.percentLeft === 'number' ? params.percentLeft : null
        )
      }))
    }
    // compactWarning / compactError only drive transient warning state; usage
    // deltas already carry the live context snapshot.
  },

  setContextUsage(snapshot) {
    if (!snapshot) {
      set({ contextUsage: null })
      return
    }
    set({ contextUsage: toContextUsage(snapshot) })
  },

  setMaintenanceKind(kind) {
    const maintenanceKind = normalizeMaintenanceKind(kind)
    set((state) => {
      if (maintenanceKind) {
        return {
          maintenanceKind,
          systemLabel: systemLabelForMaintenanceKind(maintenanceKind)
        }
      }

      return {
        maintenanceKind: null,
        systemLabel: state.systemLabel != null && MAINTENANCE_SYSTEM_LABELS.has(state.systemLabel)
          ? null
          : state.systemLabel
      }
    })
  },

  setPendingMessage(msg) {
    set({ pendingMessage: msg })
  },

  setQueuedInputs(inputs) {
    set({ queuedInputs: inputs })
  },

  setThreadMode(mode) {
    set({ threadMode: mode })
  },

  addOptimisticTurn(turn) {
    set((state) => ({
      turns: [...state.turns, turn],
      turnStatus: 'running',
      activeTurnId: turn.id,
      streamingMessage: '',
      streamingMessageLastDeltaAt: null,
      streamingReasoning: '',
      streamingReasoningStartedAt: null,
      activeItemId: null,
      turnStartedAt: Date.now(),
      inputTokens: 0,
      outputTokens: 0,
      systemLabel: null,
      streamRetrySignals: [],
      maintenanceKind: null
    }))
  },

  removeOptimisticTurn(turnId) {
    set((state) => ({
      turns: state.turns.filter((t) => t.id !== turnId),
      turnStatus: 'idle',
      activeTurnId: null,
      streamingMessage: '',
      streamingMessageLastDeltaAt: null,
      streamingReasoning: '',
      streamingReasoningStartedAt: null,
      activeItemId: null,
      turnStartedAt: null,
      streamRetrySignals: state.streamRetrySignals.filter((signal) => signal.turnId !== turnId)
    }))
  },

  promoteOptimisticTurn(localId, serverId) {
    set((state) => {
      const localTurn = state.turns.find((turn) => turn.id === localId)
      const serverTurn = state.turns.find((turn) => turn.id === serverId)
      const turns = localTurn && serverTurn
        ? state.turns
          .flatMap((turn) => {
            if (turn.id === serverId) return [mergeExistingRealtimeTurn(serverTurn, localTurn)]
            if (turn.id === localId) return []
            return [turn]
          })
          .sort((a, b) => new Date(a.startedAt).getTime() - new Date(b.startedAt).getTime())
        : state.turns.map((turn) => (turn.id === localId ? { ...turn, id: serverId } : turn))

      return {
        turns,
        activeTurnId: state.activeTurnId === localId ? serverId : state.activeTurnId,
        streamRetrySignals: state.streamRetrySignals.map((signal) =>
          signal.turnId === localId ? { ...signal, turnId: serverId } : signal
        )
      }
    })
  },

  onSubagentProgress(entries) {
    set((state) => {
      const allCompleted = entries.length > 0 && entries.every((entry) => entry.isCompleted)
      if (!allCompleted || !state.activeTurnId) {
        return { subAgentEntries: entries }
      }

      return {
        subAgentEntries: entries,
        turns: state.turns.map((turn) =>
          turn.id === state.activeTurnId
            ? { ...turn, subAgentEntries: entries }
            : turn
        )
      }
    })
  },

  upsertChangedFile(diff) {
    set((state) => {
      const next = new Map(state.changedFiles)
      next.set(diff.filePath, diff)
      return { changedFiles: next }
    })
  },

  upsertItemDiff(itemId, diff) {
    set((state) => {
      const next = new Map(state.itemDiffs)
      next.set(itemId, diff)
      return { itemDiffs: next }
    })
  },

  revertFilesForTurn(turnId) {
    set((state) => {
      const next = new Map(state.changedFiles)
      for (const [key, entry] of next.entries()) {
        const ids = entry.turnIds?.length ? entry.turnIds : [entry.turnId]
        if (ids.includes(turnId)) {
          next.set(key, { ...entry, status: 'reverted' })
        }
      }
      return { changedFiles: next }
    })
  },

  setWorkspacePath(path) {
    set({ workspacePath: path })
  },
  setRemoteWorkspaceActive(active) {
    set({ remoteWorkspaceActive: active })
  },

  setGenericApproval(approval) {
    set({ genericApproval: approval })
  },

  onApprovalRequest(bridgeId, params) {
    const state = get()
    const activeTurnId = state.activeTurnId
    const turnId = typeof params.turnId === 'string' ? params.turnId : activeTurnId
    if (!activeTurnId && !turnId) return
    const threadId = typeof params.threadId === 'string' ? params.threadId : null
    const requestId = typeof params.requestId === 'string' ? params.requestId : ''
    const locallySubmittedDecision = normalizeApprovalDecision(params.locallySubmittedDecision)

    const rawApprovalType = params.approvalType
    const approvalType: ApprovalType =
      rawApprovalType === 'file' ||
      rawApprovalType === 'remoteResource' ||
      rawApprovalType === 'skill'
        ? rawApprovalType
        : 'shell'
    const operation = (params.operation as string) ?? ''
    const target = (params.target as string) ?? ''
    const reason = (params.reason as string) ?? ''
    const itemId = typeof params.itemId === 'string' && params.itemId.trim().length > 0
      ? params.itemId
      : `approval-${bridgeId}`

    const approvalItem: ConversationItem = {
      id: itemId,
      type: 'approvalCard',
      status: 'completed',
      approvalRequestId: requestId,
      approvalType,
      approvalOperation: operation,
      approvalTarget: target,
      approvalReason: reason,
      approvalState: 'pending',
      createdAt: new Date().toISOString()
    }

    const pending: PendingApproval = {
      bridgeId,
      threadId,
      turnId: turnId ?? null,
      requestId,
      locallySubmittedDecision,
      itemId,
      approvalType,
      operation,
      target,
      reason
    }

    set((s) => {
      const pendingApprovals = upsertPendingApproval(
        queueWithPendingApproval(s.pendingApprovals, s.pendingApproval),
        pending
      )
      return {
        turns: s.turns.map((t) =>
          t.id === turnId ? { ...t, items: upsertItemById(t.items, approvalItem) } : t
        ),
        activeTurnId: turnId ?? activeTurnId,
        turnStatus: 'waitingApproval',
        pendingApprovals,
        pendingApproval: activePendingApproval(pendingApprovals)
      }
    })
  },

  onApprovalSubmitStarted(decision, target) {
    set((state) => {
      const queue = queueWithPendingApproval(state.pendingApprovals, state.pendingApproval)
      const pending = target
        ? queue.find((candidate) => matchesPendingApproval(candidate, target)) ?? null
        : activePendingApproval(queue)
      if (!pending) return state

      const pendingApprovals = updatePendingApprovalInQueue(
        queue,
        pending,
        (approval) => ({
          ...approval,
          locallySubmittedDecision: decision
        })
      )

      return {
        pendingApprovals,
        pendingApproval: activePendingApproval(pendingApprovals)
      }
    })
  },

  onApprovalSubmitFailed(target) {
    set((state) => {
      const queue = queueWithPendingApproval(state.pendingApprovals, state.pendingApproval)
      const pending = target
        ? queue.find((candidate) => matchesPendingApproval(candidate, target)) ?? null
        : activePendingApproval(queue)
      if (!pending) return state

      const pendingApprovals = updatePendingApprovalInQueue(
        queue,
        pending,
        (approval) => ({
          ...approval,
          locallySubmittedDecision: null
        })
      )

      return {
        pendingApprovals,
        pendingApproval: activePendingApproval(pendingApprovals)
      }
    })
  },

  onApprovalDecision(decision, target) {
    set((state) => {
      const queue = queueWithPendingApproval(state.pendingApprovals, state.pendingApproval)
      const pending = target
        ? queue.find((candidate) => matchesPendingApproval(candidate, target)) ?? null
        : activePendingApproval(queue)
      const itemId = pending?.itemId ?? target?.itemId ?? null
      const requestId = pending?.requestId ?? target?.requestId ?? null
      if (!itemId && !requestId) return state

      const newState = approvalDecisionToState[decision]
      const pendingApprovals = pending
        ? queue.filter((candidate) => !samePendingApproval(candidate, pending))
        : queue
      const nextPendingApproval = activePendingApproval(pendingApprovals)
      const shouldRestoreRunning = state.turnStatus === 'waitingApproval' || pending != null

      return {
        turns: state.turns.map((t) => ({
          ...t,
          items: t.items.map((i) =>
            approvalCardMatchesResolution(i, itemId, requestId)
              ? { ...i, approvalState: newState }
              : i
          )
        })),
        pendingApprovals,
        pendingApproval: nextPendingApproval,
        turnStatus: nextPendingApproval
          ? 'waitingApproval'
          : shouldRestoreRunning
            ? 'running'
            : state.turnStatus
      }
    })
  },

  onApprovalResolved(params) {
    set((state) => {
      const queue = queueWithPendingApproval(state.pendingApprovals, state.pendingApproval)
      const pending = params
        ? queue.find((candidate) => matchesPendingApproval(candidate, params)) ?? null
        : activePendingApproval(queue)
      if (queue.length > 0 && !pending) return state

      const requestId = params?.requestId ?? pending?.requestId ?? null
      const itemId = pending?.itemId ?? null
      const resolvedState = params?.decision ? approvalDecisionToState[params.decision] : undefined
      const turns = resolvedState
        ? state.turns.map((turn) => ({
            ...turn,
            items: turn.items.map((item) =>
              approvalCardMatchesResolution(item, itemId, requestId)
                ? { ...item, approvalState: resolvedState }
                : item
            )
          }))
        : state.turns
      const shouldRestoreRunning = state.turnStatus === 'waitingApproval' || pending != null
      const pendingApprovals = pending
        ? queue.filter((candidate) => !samePendingApproval(candidate, pending))
        : queue
      const nextPendingApproval = activePendingApproval(pendingApprovals)

      return {
        turns,
        pendingApprovals,
        pendingApproval: nextPendingApproval,
        turnStatus: nextPendingApproval
          ? 'waitingApproval'
          : shouldRestoreRunning
            ? 'running'
            : state.turnStatus
      }
    })
  },

  onApprovalNoLongerPending(params) {
    set((state) => {
      const queue = queueWithPendingApproval(state.pendingApprovals, state.pendingApproval)
      const pending = queue.find((candidate) => matchesPendingApproval(candidate, params)) ?? null
      if (!pending) return state

      const pendingApprovals = queue.filter((candidate) => !samePendingApproval(candidate, pending))
      const nextPendingApproval = activePendingApproval(pendingApprovals)

      return {
        pendingApprovals,
        pendingApproval: nextPendingApproval,
        turnStatus: nextPendingApproval ? 'waitingApproval' : params.nextTurnStatus,
        activeTurnId: !nextPendingApproval && params.nextTurnStatus === 'idle'
          ? null
          : state.activeTurnId
      }
    })
  },

  onApprovalTimeout() {
    const state = get()
    const pending = state.pendingApproval
    if (!pending) return

    set((s) => {
      const queue = queueWithPendingApproval(s.pendingApprovals, pending)
      const pendingApprovals = queue.filter((candidate) => !samePendingApproval(candidate, pending))
      return {
        turns: s.turns.map((t) => ({
          ...t,
          items: t.items.map((i) =>
            i.id === pending.itemId ? { ...i, approvalState: 'timedOut' as ApprovalState } : i
          )
        })),
        pendingApprovals,
        pendingApproval: activePendingApproval(pendingApprovals),
        turnStatus: pendingApprovals.length > 0 ? 'waitingApproval' : s.turnStatus
      }
    })
  },

  onUserInputRequest(bridgeId, params) {
    const state = get()
    const activeTurnId = state.activeTurnId
    const turnId = typeof params.turnId === 'string' ? params.turnId : activeTurnId
    if (!activeTurnId && !turnId) return

    const rawQuestions = Array.isArray(params.questions) ? params.questions : []
    const questions: UserInputQuestion[] = rawQuestions
      .map((raw) => {
        const q = raw as Record<string, unknown>
        const rawOptions = Array.isArray(q.options) ? q.options : []
        return {
          id: typeof q.id === 'string' ? q.id : '',
          header: typeof q.header === 'string' ? q.header : '',
          question: typeof q.question === 'string' ? q.question : '',
          isOther: q.isOther !== false,
          isSecret: q.isSecret === true,
          options: rawOptions
            .map((rawOption) => {
              const option = rawOption as Record<string, unknown>
              return {
                label: typeof option.label === 'string' ? option.label : '',
                description: typeof option.description === 'string' ? option.description : ''
              }
            })
            .filter((option) => option.label.trim().length > 0)
        }
      })
      .filter((q) => q.id.trim().length > 0 && q.question.trim().length > 0)

    set({
      turnStatus: 'waitingInput',
      activeTurnId: turnId ?? activeTurnId,
      pendingUserInput: {
        bridgeId,
        requestId: typeof params.requestId === 'string' ? params.requestId : '',
        turnId: turnId ?? null,
        questions
      }
    })
  },

  onUserInputResolved() {
    set({ pendingUserInput: null, turnStatus: 'running' })
  },

  revertFile(filePath) {
    set((state) => {
      const entry = state.changedFiles.get(filePath)
      if (!entry) return {}
      const next = new Map(state.changedFiles)
      next.set(filePath, { ...entry, status: 'reverted' })
      return { changedFiles: next }
    })
  },

  reapplyFile(filePath) {
    set((state) => {
      const entry = state.changedFiles.get(filePath)
      if (!entry) return {}
      const next = new Map(state.changedFiles)
      next.set(filePath, { ...entry, status: 'written' })
      return { changedFiles: next }
    })
  },

  onPlanUpdated(rawPlan) {
    const plan: AgentPlan = {
      title: (rawPlan.title as string) ?? '',
      overview: (rawPlan.overview as string) ?? '',
      content: (rawPlan.content as string) ?? '',
      todos: (rawPlan.todos as PlanTodoItem[]) ?? []
    }
    set({ plan })
  },

  reset() {
    subAgentStreamingArgumentBuffers.clear()
    set((state) => ({
      ...initialState,
      workspacePath: state.workspacePath,
      remoteWorkspaceActive: state.remoteWorkspaceActive,
      changedFiles: new Map<string, FileDiff>(),
      itemDiffs: new Map<string, FileDiff>(),
      streamingItemDiffs: new Map<string, FileDiff>(),
      streamingBaselines: new Map<string, StreamingFileBaseline>(),
      pendingTerminalByCallId: new Map<string, PendingTerminalEntry>(),
      pendingToolCompletionsByCallKey: new Map<string, PendingToolCompletionEntry>(),
      subAgentEntries: [],
      pendingApproval: null,
      pendingApprovals: [],
      pendingUserInput: null,
      plan: null,
      contextUsage: null
    }))
  }
}))

// ---------------------------------------------------------------------------
// Derived selectors
// ---------------------------------------------------------------------------

/**
 * Partial plan draft reconstructed from the in-flight `CreatePlan` tool call's
 * `argumentsPreview`. `null` when no active CreatePlan stream is happening.
 *
 * Used by the Desktop plan panel to render the plan live as the agent types
 * the arguments JSON, before `plan/updated` fires on completion.
 */
export interface StreamingPlanDraft {
  itemId: string
  title: string | null
  overview: string | null
  plan: string | null
  content: string | null
  todos: Array<{ id?: string; content?: string; status?: PlanTodoStatus | string }>
}

interface StreamingCreatePlanMatch {
  itemId: string
  rawArgs: string
}

/**
 * Extract a partial JSON array for the `todos` / `plan.todos` field without
 * requiring the buffer to be fully parseable. Best-effort: returns a partial
 * list of `{id, content, status}` objects whose closing quotes have arrived.
 */
export function extractPartialTodos(rawArgs: string): StreamingPlanDraft['todos'] {
  if (!rawArgs) return []
  const needle = '"todos"'
  const keyIndex = rawArgs.indexOf(needle)
  if (keyIndex < 0) return []
  const colonIndex = rawArgs.indexOf(':', keyIndex + needle.length)
  if (colonIndex < 0) return []
  const arrayStart = rawArgs.indexOf('[', colonIndex + 1)
  if (arrayStart < 0) return []

  const entries: StreamingPlanDraft['todos'] = []
  let i = arrayStart + 1
  while (i < rawArgs.length) {
    const objStart = rawArgs.indexOf('{', i)
    if (objStart < 0) break
    let depth = 1
    let j = objStart + 1
    let inString = false
    let escaped = false
    while (j < rawArgs.length && depth > 0) {
      const ch = rawArgs[j]
      if (inString) {
        if (escaped) {
          escaped = false
        } else if (ch === '\\') {
          escaped = true
        } else if (ch === '"') {
          inString = false
        }
      } else if (ch === '"') {
        inString = true
      } else if (ch === '{') {
        depth += 1
      } else if (ch === '}') {
        depth -= 1
        if (depth === 0) {
          break
        }
      }
      j += 1
    }
    if (depth !== 0) {
      // Object not yet closed — object is still streaming; stop here.
      break
    }
    const chunk = rawArgs.slice(objStart, j + 1)
    entries.push({
      id: extractPartialJsonStringValue(chunk, 'id') ?? undefined,
      content: extractPartialJsonStringValue(chunk, 'content') ?? undefined,
      status: extractPartialJsonStringValue(chunk, 'status') ?? undefined
    })
    i = j + 1
  }
  return entries
}

function findStreamingCreatePlanCall(state: ConversationState): StreamingCreatePlanMatch | null {
  // Prefer the active turn; fall back to scanning the most recent non-completed call.
  const activeTurnId = state.activeTurnId
  const turn = activeTurnId
    ? state.turns.find((t) => t.id === activeTurnId)
    : undefined
  const turns = turn ? [turn] : [...state.turns].reverse()

  for (const t of turns) {
    for (let idx = t.items.length - 1; idx >= 0; idx -= 1) {
      const item = t.items[idx]
      if (
        item.type === 'toolCall'
        && item.toolName === 'CreatePlan'
        && item.status !== 'completed'
      ) {
        return {
          itemId: item.id,
          rawArgs: item.argumentsPreview ?? ''
        }
      }
    }
  }

  return null
}

export function selectStreamingPlanItemId(state: ConversationState): string | null {
  return findStreamingCreatePlanCall(state)?.itemId ?? null
}

export function selectStreamingPlanRawArgs(state: ConversationState): string | null {
  return findStreamingCreatePlanCall(state)?.rawArgs ?? null
}

export function buildStreamingPlanDraft(itemId: string, rawArgs: string): StreamingPlanDraft {
  const plan = extractPartialJsonStringValue(rawArgs, 'plan')
  const parsed = parsePlanMarkdown(plan ?? '')
  return {
    itemId,
    title: parsed.title || extractPartialJsonStringValue(rawArgs, 'title'),
    overview: parsed.overview || extractPartialJsonStringValue(rawArgs, 'overview'),
    plan,
    content: parsed.content || null,
    todos: extractPartialTodos(rawArgs)
  }
}

/**
 * Zustand selector: returns the partial plan draft from the newest active
 * `CreatePlan` tool call, or null when no such call is in flight.
 */
export function selectStreamingPlanDraft(
  state: ConversationState
): StreamingPlanDraft | null {
  const match = findStreamingCreatePlanCall(state)
  if (!match) return null
  return buildStreamingPlanDraft(match.itemId, match.rawArgs)
}

/**
 * Returns the most recent completed turn whose last completed tool call is a
 * successful CreatePlan invocation. Used by the plan approval composer.
 */
export function selectLatestCreatePlanTurnId(state: ConversationState): string | null {
  for (let turnIdx = state.turns.length - 1; turnIdx >= 0; turnIdx -= 1) {
    const turn = state.turns[turnIdx]
    if (turn.status !== 'completed') {
      continue
    }
    for (let itemIdx = turn.items.length - 1; itemIdx >= 0; itemIdx -= 1) {
      const item = turn.items[itemIdx]
      if (item.type !== 'toolCall') {
        continue
      }
      if (
        item.toolName === 'CreatePlan'
        && item.status === 'completed'
        && item.success !== false
      ) {
        return turn.id
      }
      return null
    }
  }
  return null
}

// Expose store to E2E / debug tooling via a window global (browser only)
if (typeof window !== 'undefined') {
  ;(window as unknown as Record<string, unknown>).__CONVERSATION_STORE_STATE = () =>
    useConversationStore.getState()
}
