/**
 * Thread-related TypeScript types matching the AppServer Wire Protocol responses.
 * Reference: specs/protocols/appserver-protocol.md §4
 */

import type { QueuedTurnInput } from './conversation'

export type ThreadStatus = 'active' | 'paused' | 'archived'

export type ApprovalPolicyWire = 'default' | 'autoApprove' | 'interrupt'

export type ThreadGoalStatus = 'active' | 'paused' | 'budgetLimited' | 'complete'

export interface TokenUsage {
  inputTokens: number
  outputTokens: number
  cachedInputTokens?: number
  cacheWriteInputTokens?: number
  reasoningOutputTokens?: number
  totalTokens: number
}

export interface ThreadGoal {
  threadId: string
  goalId: string
  objective: string
  status: ThreadGoalStatus
  tokenBudget?: number | null
  tokensUsed: TokenUsage
  timeUsedSeconds: number
  createdAt: string
  updatedAt: string
}

export interface ThreadRuntimeSnapshot {
  running: boolean
  busy?: boolean
  waitingOnApproval: boolean
  waitingOnInput?: boolean
  waitingOnPlanConfirmation: boolean
  maintenanceKind?: 'compacting' | 'consolidating' | string | null
}

export interface SubAgentThreadSourceWire {
  parentThreadId?: string
  parentTurnId?: string
  spawnCallId?: string
  rootThreadId?: string
  depth?: number
  agentPath?: string
  taskName?: string
  agentNickname?: string
  agentRole?: string
  agentType?: string
  agent_type?: string
  role?: string
  profileName?: string
  runtimeType?: string
  supportsSendInput?: boolean
  supportsResume?: boolean
  supportsSendMessage?: boolean
  supportsFollowupTask?: boolean
  supportsClose?: boolean
}

export interface ThreadSourceWire {
  kind?: string
  subAgent?: SubAgentThreadSourceWire | null
}

export interface ThreadAppBindingSummaryWire {
  threadId: string
  bindingId: string
  appId: string
  displayName?: string | null
  icon?: string | null
  toolNamespace?: string | null
  state: string
  connectionState: string
  managed?: boolean
  requiresExternalConnection?: boolean
  grantedScopes: string[]
  expiresAt?: string | null
}

export interface ThreadOriginApp {
  appId: string
  displayName: string
  /** Data URL or safe URL for the app icon; optional — clients fall back to the channel badge. */
  icon?: string | null
  /** Set when displayName/icon carry a matched origin member (per-member origin) rather than the app. */
  memberId?: string | null
}

export interface ThreadWorktreeInfoWire {
  id: string
  sourceThreadId: string
  workspacePath: string
  sourceWorkspacePath: string
  path: string
  branchName: string
  baseRef: string
  head: string
  createdAt: string
  dirtyHandoff?: {
    requested: boolean
    status: string
    copiedFileCount: number
    deletedFileCount: number
  } | null
}

export interface ThreadSummary {
  id: string
  userId?: string | null
  workspacePath?: string
  /** Workspace root used by file tools and Git for this thread. Worktree forks may differ from workspacePath. */
  effectiveWorkspacePath?: string
  forkedFromId?: string | null
  worktree?: ThreadWorktreeInfoWire | null
  displayName: string | null
  status: ThreadStatus
  originChannel: string
  channelContext?: string | null
  createdAt: string      // ISO 8601 UTC
  lastActiveAt: string   // ISO 8601 UTC
  source?: ThreadSourceWire | null
  metadata?: Record<string, unknown>
  /** Best-effort current runtime snapshot from thread/list. Omitted by older hosts. */
  runtime?: ThreadRuntimeSnapshot
  /** Best-effort current goal snapshot from thread/list. Omitted by older hosts. */
  goal?: ThreadGoal | null
  /** Lightweight app binding summaries from thread/list or thread/read. */
  appBindings?: ThreadAppBindingSummaryWire[]
  /** Server-resolved origin-app branding when originChannel matches an installed app's declared origin channel. */
  originApp?: ThreadOriginApp | null
}

/**
 * Minimal Turn stub used in ThreadSummary / Thread sidebar data.
 * The full ConversationTurn (with items, streaming state) lives in types/conversation.ts
 * and is used by the conversation panel.
 */
export interface Turn {
  id: string
  status: string
  createdAt: string
  completedAt?: string
  /** Populated when includeTurns: true is used in thread/read */
  items?: Array<Record<string, unknown>>
  threadId?: string
  tokenUsage?: { inputTokens: number; outputTokens: number }
}

export interface ThreadConfigurationWire {
  mode?: string
  model?: string
  Model?: string
  reasoning?: ReasoningConfigurationWire | null
  Reasoning?: ReasoningConfigurationWire | null
  approvalPolicy?: ApprovalPolicyWire
  [key: string]: unknown
}

export type ReasoningEffortWire = 'low' | 'medium' | 'high' | 'extraHigh'
export type ReasoningOutputWire = 'none' | 'summary' | 'full'

export interface ReasoningConfigurationWire {
  enabled?: boolean
  Enabled?: boolean
  effort?: ReasoningEffortWire
  Effort?: ReasoningEffortWire
  output?: ReasoningOutputWire
  Output?: ReasoningOutputWire
}

/**
 * Per-thread context usage snapshot piggy-backed on thread/read, thread/start,
 * thread/resume responses, and compaction system/event notifications. Drives
 * the desktop context-usage ring; optional because older hosts or old threads
 * without persisted context usage state do not emit one. Fields mirror the
 * backend `ContextUsageSnapshot` record.
 */
export interface ContextUsageSnapshotWire {
  tokens: number
  contextWindow: number
  autoCompactThreshold: number
  warningThreshold: number
  errorThreshold: number
  percentLeft: number
}

export type ThreadPlanTodoStatus = 'pending' | 'in_progress' | 'completed' | 'cancelled'

export interface ThreadPlanTodo {
  id: string
  content: string
  priority?: string
  status: ThreadPlanTodoStatus
}

export interface ThreadPlan {
  title: string
  overview: string
  content: string
  todos: ThreadPlanTodo[]
}

export interface Thread extends ThreadSummary {
  workspacePath: string
  userId: string
  metadata: Record<string, unknown>
  configuration?: ThreadConfigurationWire | null
  turns: Turn[]
  queuedInputs?: QueuedTurnInput[]
  contextUsage?: ContextUsageSnapshotWire | null
  plan?: ThreadPlan | null
}

/**
 * Identity sent with thread/start and thread/list requests.
 * The desktop client uses channelName "dotcraft-desktop" and userId "local".
 */
export interface SessionIdentity {
  channelName: string
  userId: string
  channelContext: string
  workspacePath: string
}

/** Time-based group label for sidebar thread grouping (spec §7.2) */
export type ThreadGroup = 'Today' | 'Yesterday' | 'Previous 7 Days' | 'Previous 30 Days' | 'Older'

/** Ordered list of all group labels for consistent rendering */
export const THREAD_GROUP_ORDER: ThreadGroup[] = [
  'Today',
  'Yesterday',
  'Previous 7 Days',
  'Previous 30 Days',
  'Older'
]

/** Context menu action IDs for thread entries */
export type ThreadContextAction = 'rename' | 'archive' | 'delete'
