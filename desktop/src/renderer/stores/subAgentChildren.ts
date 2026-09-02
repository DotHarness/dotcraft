import type { SubAgentEntry } from '../types/toolCall'
import type { ThreadRuntimeSnapshot, ThreadSummary } from '../types/thread'
import { wireTurnToConversationTurn } from '../types/conversation'
import { useThreadStore } from './threadStore'

interface SubAgentEdgeWire {
  parentThreadId?: string
  childThreadId?: string
  parentTurnId?: string | null
  depth?: number
  agentPath?: string | null
  taskName?: string | null
  agentNickname?: string | null
  agentRole?: string | null
  agentType?: string | null
  agent_type?: string | null
  role?: string | null
  profileName?: string | null
  runtimeType?: string | null
  supportsSendInput?: boolean
  supportsResume?: boolean
  supportsSendMessage?: boolean
  supportsFollowupTask?: boolean
  supportsClose?: boolean
  status?: string
}

export interface SubAgentChildWire {
  edge?: SubAgentEdgeWire
  thread?: ThreadSummary | null
}

export interface SubAgentChild {
  childThreadId: string
  parentThreadId: string
  agentPath?: string | null
  taskName?: string | null
  nickname: string
  agentRole: string | null
  profileName: string | null
  runtimeType: string | null
  supportsSendInput: boolean
  supportsResume: boolean
  supportsSendMessage?: boolean
  supportsFollowupTask?: boolean
  supportsClose: boolean
  status: string
  lastToolDisplay: string | null
  /**
   * Preview of the subagent's most recent agent message, used by the Subagents
   * panel for finished subagents (running ones prefer live tool progress).
   * Loaded lazily by the store preview loader; null until read.
   */
  lastMessagePreview: string | null
  currentTool: string | null
  inputTokens: number
  outputTokens: number
  isCompleted: boolean
  isPlaceholder?: boolean
  runtime?: ThreadRuntimeSnapshot
  threadSummary?: ThreadSummary | null
}

function normalizeText(value: unknown): string | null {
  return typeof value === 'string' && value.trim().length > 0 ? value.trim() : null
}

function lastAgentPathSegment(agentPath: string | null | undefined): string | null {
  if (!agentPath) return null
  const parts = agentPath.split('/').filter((part) => part.length > 0)
  return parts.length > 0 ? parts[parts.length - 1] : null
}

function normalizeRole(source: {
  agentRole?: unknown
  agentType?: unknown
  agent_type?: unknown
  role?: unknown
} | null | undefined): string | null {
  return normalizeText(source?.agentRole)
    ?? normalizeText(source?.agentType)
    ?? normalizeText(source?.agent_type)
    ?? normalizeText(source?.role)
}

const PREVIEW_MAX_LENGTH = 140

/**
 * Extracts a short single-line preview from the most recent agent message across
 * the given raw turns (newest last). Returns null when no agent text is present.
 */
export function extractLastAgentMessagePreview(rawTurns: Array<Record<string, unknown>>): string | null {
  for (let turnIndex = rawTurns.length - 1; turnIndex >= 0; turnIndex -= 1) {
    let turn
    try {
      turn = wireTurnToConversationTurn(rawTurns[turnIndex])
    } catch {
      continue
    }
    for (let itemIndex = turn.items.length - 1; itemIndex >= 0; itemIndex -= 1) {
      const item = turn.items[itemIndex]
      if (item.type !== 'agentMessage') continue
      const text = item.text?.replace(/\s+/g, ' ').trim()
      if (!text) continue
      return text.length > PREVIEW_MAX_LENGTH ? `${text.slice(0, PREVIEW_MAX_LENGTH)}…` : text
    }
  }
  return null
}

export function isTerminalSubAgentStatus(status: string | null | undefined): boolean {
  const normalized = status?.trim().toLowerCase()
  return normalized === 'closed'
    || normalized === 'completed'
    || normalized === 'failed'
    || normalized === 'cancelled'
    || normalized === 'canceled'
}

/** True when the subagent's spawn edge has been closed (by CloseAgent or residency reclaim). */
export function isSubAgentChildClosed(child: SubAgentChild): boolean {
  return child.status.trim().toLowerCase() === 'closed'
}

export function isSubAgentChildRunning(child: SubAgentChild): boolean {
  // A closed edge is terminal even if a stale runtime snapshot still says running.
  if (isSubAgentChildClosed(child)) return false
  if (child.runtime?.running === true) return true
  if (child.runtime?.running === false) return false
  if (child.isPlaceholder === true) return !child.isCompleted && !isTerminalSubAgentStatus(child.status)
  return false
}

export function childFromWire(parentThreadId: string, wire: SubAgentChildWire): SubAgentChild | null {
  const edge = wire.edge ?? {}
  const childThreadId = normalizeText(edge.childThreadId) ?? normalizeText(wire.thread?.id)
  if (!childThreadId) return null
  const cachedThread = useThreadStore.getState().threadList.find((thread) => thread.id === childThreadId)
  const threadSummary = wire.thread ?? cachedThread ?? null
  const source = threadSummary?.source?.subAgent
  const runtime = threadSummary?.runtime
  const status = normalizeText(edge.status) ?? 'open'
  const agentPath = normalizeText(edge.agentPath) ?? normalizeText(source?.agentPath)
  const taskName = normalizeText(edge.taskName) ?? normalizeText(source?.taskName)
  const isCompleted = status === 'closed' ? true : runtime?.running === true
    ? false
    : runtime?.running === false || isTerminalSubAgentStatus(status)
  const nickname =
    normalizeText(wire.thread?.displayName)
    ?? normalizeText(edge.agentNickname)
    ?? normalizeText(source?.agentNickname)
    ?? taskName
    ?? lastAgentPathSegment(agentPath)
    ?? childThreadId
  return {
    childThreadId,
    parentThreadId: normalizeText(edge.parentThreadId) ?? parentThreadId,
    agentPath,
    taskName,
    nickname,
    agentRole: normalizeRole(edge) ?? normalizeRole(source),
    profileName: normalizeText(edge.profileName) ?? normalizeText(source?.profileName),
    runtimeType: normalizeText(edge.runtimeType) ?? normalizeText(source?.runtimeType),
    supportsSendInput: edge.supportsSendInput ?? source?.supportsSendInput ?? true,
    supportsResume: edge.supportsResume ?? source?.supportsResume ?? true,
    supportsSendMessage: edge.supportsSendMessage ?? source?.supportsSendMessage ?? false,
    supportsFollowupTask: edge.supportsFollowupTask ?? source?.supportsFollowupTask ?? false,
    supportsClose: edge.supportsClose ?? source?.supportsClose ?? true,
    status,
    lastToolDisplay: null,
    lastMessagePreview: null,
    currentTool: null,
    inputTokens: 0,
    outputTokens: 0,
    isCompleted,
    isPlaceholder: false,
    runtime,
    threadSummary
  }
}

export function mergeExistingProgress(next: SubAgentChild, existing: SubAgentChild | undefined): SubAgentChild {
  if (!existing) return next
  if (isSubAgentChildClosed(existing)) next = { ...next, status: 'closed' }
  let runtime = next.runtime ?? existing.runtime
  const nextCompleted = isSubAgentChildClosed(next) ? true : next.runtime?.running === true
    ? false
    : next.isCompleted || next.runtime?.running === false || isTerminalSubAgentStatus(next.status)
  if (nextCompleted && runtime) {
    runtime = { ...runtime, running: false }
  }
  const isCompleted = nextCompleted
    ? true
    : runtime?.running === true
      ? false
      : existing.isCompleted
  return {
    ...next,
    agentPath: next.agentPath ?? existing.agentPath,
    taskName: next.taskName ?? existing.taskName,
    agentRole: next.agentRole ?? existing.agentRole,
    profileName: next.profileName ?? existing.profileName,
    runtimeType: next.runtimeType ?? existing.runtimeType,
    lastToolDisplay: existing.lastToolDisplay,
    lastMessagePreview: existing.lastMessagePreview,
    currentTool: isCompleted ? null : existing.currentTool,
    inputTokens: existing.inputTokens,
    outputTokens: existing.outputTokens,
    isCompleted,
    isPlaceholder: false,
    runtime
  }
}

export function createPlaceholderChild(
  parentThreadId: string,
  progress: SubAgentEntry,
  index: number
): SubAgentChild {
  const label = normalizeText(progress.label) ?? `Agent ${index + 1}`
  const display = normalizeText(progress.currentToolDisplay)
    ?? normalizeText(progress.currentTool)
  const isCompleted = progress.isCompleted === true
  return {
    childThreadId: `subagent-placeholder:${parentThreadId}:${index}:${label}`,
    parentThreadId,
    agentPath: null,
    taskName: null,
    nickname: label,
    agentRole: null,
    profileName: null,
    runtimeType: null,
    supportsSendInput: false,
    supportsResume: false,
    supportsSendMessage: false,
    supportsFollowupTask: false,
    supportsClose: false,
    status: isCompleted ? 'completed' : 'open',
    lastToolDisplay: display,
    lastMessagePreview: null,
    currentTool: isCompleted ? null : progress.currentTool,
    inputTokens: progress.inputTokens,
    outputTokens: progress.outputTokens,
    isCompleted,
    isPlaceholder: true,
    runtime: {
      running: !isCompleted,
      waitingOnApproval: false,
      waitingOnPlanConfirmation: false
    },
    threadSummary: null
  }
}
