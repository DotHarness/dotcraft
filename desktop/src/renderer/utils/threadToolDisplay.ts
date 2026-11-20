import type { ConversationItem } from '../types/conversation'

/**
 * Display helpers for the Desktop thread-management tools (the `desktop` runtime
 * tool namespace). Successful CreateThread / SendMessageToThread calls render a
 * dedicated "Chat created" / "Message sent" card before the agent footer
 * (see TurnThreadActions) instead of a raw tool row.
 */

export const DESKTOP_THREAD_TOOL_NAMESPACE = 'desktop'
export const DESKTOP_CONVERSATION_TOOL_NAMESPACE = 'desktop_conversation'
export const WRAP_UP_TOOL_NAME = 'WrapUp'

export function isDesktopToolNamespace(namespace: string | undefined | null): boolean {
  return !namespace || namespace === DESKTOP_THREAD_TOOL_NAMESPACE || namespace === DESKTOP_CONVERSATION_TOOL_NAMESPACE
}
export const CREATE_THREAD_TOOL_NAME = 'CreateThread'
export const SEND_MESSAGE_TO_THREAD_TOOL_NAME = 'SendMessageToThread'

export type ThreadToolActionKind = 'created' | 'messaged'

export interface ThreadToolAction {
  kind: ThreadToolActionKind
  threadId: string
  /** Display name captured in the tool result at action time (may be stale). */
  displayName?: string
  workspacePath?: string
  projectId?: string
  /** Whether the target turn started immediately. */
  started: boolean
  /** Whether the message was queued behind a running turn (SendMessageToThread). */
  queued: boolean
}

function matchesThreadTool(item: ConversationItem, toolName: string): boolean {
  if (item.toolName !== toolName) return false
  // Desktop thread tools are dynamic tools in the `desktop` namespace; tolerate a
  // missing namespace for forward/backward compatibility with other invocation shapes.
  return isDesktopToolNamespace(item.pluginNamespace)
}

/** True when the item is a Desktop CreateThread / SendMessageToThread invocation. */
export function isThreadActionToolItem(item: ConversationItem): boolean {
  return matchesThreadTool(item, CREATE_THREAD_TOOL_NAME)
    || matchesThreadTool(item, SEND_MESSAGE_TO_THREAD_TOOL_NAME)
}

/**
 * Parse a successful thread-tool invocation into a card model. Returns null while
 * the call is still running, when it failed, or when the result lacks a thread id
 * (those keep their default inline rendering).
 */
export function parseThreadToolAction(item: ConversationItem): ThreadToolAction | null {
  if (item.success !== true) return null
  const result = item.structuredResult
  if (!result || typeof result !== 'object') return null
  const data = result as Record<string, unknown>

  if (matchesThreadTool(item, CREATE_THREAD_TOOL_NAME)) {
    const thread = data.thread as Record<string, unknown> | undefined
    const threadId = typeof thread?.id === 'string' ? thread.id : undefined
    if (!threadId) return null
    return {
      kind: 'created',
      threadId,
      displayName: typeof thread?.displayName === 'string' ? thread.displayName : undefined,
      workspacePath: typeof thread?.workspacePath === 'string' ? thread.workspacePath : undefined,
      projectId: typeof thread?.projectId === 'string' ? thread.projectId : undefined,
      started: data.started === true,
      queued: false
    }
  }

  if (matchesThreadTool(item, SEND_MESSAGE_TO_THREAD_TOOL_NAME)) {
    const threadId = typeof data.threadId === 'string' ? data.threadId : undefined
    if (!threadId) return null
    return {
      kind: 'messaged',
      threadId,
      displayName: undefined,
      workspacePath: typeof data.workspacePath === 'string' ? data.workspacePath : undefined,
      projectId: typeof data.projectId === 'string' ? data.projectId : undefined,
      started: data.started === true,
      queued: data.queued === true
    }
  }

  return null
}

export function threadTaskCardKind(item: ConversationItem): 'offer' | 'proposal' | null {
  if (!matchesThreadTool(item, CREATE_THREAD_TOOL_NAME)) return null
  const args = item.arguments
  if (!args || typeof args.proposalId === 'string') return null
  if (args.initiation === 'offer') return 'offer'
  if (args.initiation === 'ownInitiative') return 'proposal'
  return null
}

export interface TurnNextStep {
  callId: string
  recap: string
  ask: string
  actions: string[]
  blocked: boolean
}

export function isWrapUpItem(item: ConversationItem): boolean {
  return item.toolName === WRAP_UP_TOOL_NAME
    && (!item.pluginNamespace || item.pluginNamespace === DESKTOP_CONVERSATION_TOOL_NAMESPACE)
}

export function turnNextStep(items: readonly ConversationItem[]): TurnNextStep | null {
  for (let index = items.length - 1; index >= 0; index -= 1) {
    const item = items[index]
    if (!isWrapUpItem(item) || item.success !== true) continue
    const shown = (item.structuredResult as { shown?: unknown } | null | undefined)?.shown === true
    const args = item.arguments
    const ask = typeof args?.ask === 'string' ? args.ask.trim() : ''
    const actions = Array.isArray(args?.actions)
      ? args.actions.flatMap((action) => {
          const label = (action as { label?: unknown } | null)?.label
          return typeof label === 'string' && label.trim() ? [label.trim()] : []
        })
      : []
    if (!shown || !ask || actions.length === 0) return null
    return {
      callId: item.toolCallId ?? item.id,
      recap: typeof args?.recap === 'string' ? args.recap.trim() : '',
      ask,
      actions: actions.slice(0, 2),
      blocked: args?.blocked === true
    }
  }
  return null
}
