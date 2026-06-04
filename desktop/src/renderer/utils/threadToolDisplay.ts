import type { ConversationItem } from '../types/conversation'

/**
 * Display helpers for the Desktop thread-management tools (the `desktop` runtime
 * tool namespace). Successful CreateThread / SendMessageToThread calls render a
 * dedicated "Chat created" / "Message sent" card before the agent footer
 * (see TurnThreadActions) instead of a raw tool row.
 */

export const DESKTOP_THREAD_TOOL_NAMESPACE = 'desktop'
export const CREATE_THREAD_TOOL_NAME = 'CreateThread'
export const SEND_MESSAGE_TO_THREAD_TOOL_NAME = 'SendMessageToThread'

export type ThreadToolActionKind = 'created' | 'messaged'

export interface ThreadToolAction {
  kind: ThreadToolActionKind
  threadId: string
  /** Display name captured in the tool result at action time (may be stale). */
  displayName?: string
  /** Whether the target turn started immediately. */
  started: boolean
  /** Whether the message was queued behind a running turn (SendMessageToThread). */
  queued: boolean
}

function matchesThreadTool(item: ConversationItem, toolName: string): boolean {
  if (item.toolName !== toolName) return false
  // Desktop thread tools are dynamic tools in the `desktop` namespace; tolerate a
  // missing namespace for forward/backward compatibility with other invocation shapes.
  return !item.pluginNamespace || item.pluginNamespace === DESKTOP_THREAD_TOOL_NAMESPACE
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
      started: data.started === true,
      queued: data.queued === true
    }
  }

  return null
}
