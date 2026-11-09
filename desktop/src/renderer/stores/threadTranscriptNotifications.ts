import { useConversationStore, getThreadConversation } from './conversationStore'
import { useThreadStore } from './threadStore'
import { applyThreadTurnRuntime } from './threadTurnRuntime'
import { resolveParkedApproval } from './threadInteractiveRequests'
import { useSubAgentStore, isSubAgentChildClosed } from './subAgentStore'
import { useBoardStore } from './boardStore'
import { usePetStore } from '../pet/petStore'
import { showFindToast } from '../pet/findToast'
import { addToast } from './toastStore'
import { serverFallbackText } from '../utils/appServerErrorText'
import { normalizeRemoteFileTransferProgress } from '../utils/remoteToolHostDisplay'
import { autoOpenWorkflowLaunch } from '../components/workflow/WorkflowToolCard'
import { reloadVisualizationsReferencedBy } from '../components/conversation/visualizationReloads'
import type { ContextUsageSnapshotWire } from '../types/thread'
import type { SubAgentEntry } from '../types/toolCall'
import type { ApprovalDecision } from '../types/conversation'
import type { ThreadNotificationContext } from './threadNotificationContext'

function normalizeApprovalDecision(value: unknown): ApprovalDecision | null {
  return value === 'accept' ||
    value === 'acceptForSession' ||
    value === 'acceptAlways' ||
    value === 'decline' ||
    value === 'cancel'
    ? value
    : null
}

function extractApprovalResolvedParams(params: Record<string, unknown>): {
  threadId: string | null
  turnId: string | null
  requestId: string | null
  decision: ApprovalDecision | null
} {
  const item = params.item && typeof params.item === 'object' ? (params.item as Record<string, unknown>) : {}
  const payload = item.payload && typeof item.payload === 'object' ? (item.payload as Record<string, unknown>) : {}

  return {
    threadId: typeof params.threadId === 'string' ? params.threadId : null,
    turnId: typeof params.turnId === 'string' ? params.turnId : null,
    requestId: typeof payload.requestId === 'string' ? payload.requestId : null,
    decision: normalizeApprovalDecision(payload.decision)
  }
}

export function applyThreadTranscriptNotification(
  method: string,
  p: Record<string, unknown>,
  context: ThreadNotificationContext
): boolean {
  const isActiveConversation = (threadId: string | null | undefined): boolean =>
    context.foreground && !!threadId && useThreadStore.getState().activeThreadId === threadId
  const conversationFor = (threadId: string | null | undefined) =>
    threadId ? getThreadConversation(threadId)?.getState() : undefined
  switch (method) {
    case 'turn/started': {
      const rawTurn = (p.turn ?? p) as Record<string, unknown>
      const startedThreadId = (rawTurn.threadId as string | undefined) ?? (p.threadId as string | undefined)
      const conversation = conversationFor(startedThreadId)
      if (conversation) {
        conversation.onTurnStarted(rawTurn)
      }
      applyThreadTurnRuntime(startedThreadId, rawTurn, true)

      return true
    }

    case 'turn/completed': {
      const rawTurn = (p.turn ?? p) as Record<string, unknown>
      const completedThreadId = (rawTurn.threadId as string | undefined) ?? (p.threadId as string | undefined)
      const conversation = conversationFor(completedThreadId)
      if (conversation) {
        conversation.onTurnCompleted(rawTurn)
      }
      applyThreadTurnRuntime(completedThreadId, rawTurn, false)
      if (completedThreadId && context.foreground) {
        void window.api.skillMarket?.cleanupDotCraftInstall?.({ threadId: completedThreadId }).catch(() => {})
      }

      return true
    }

    case 'turn/failed': {
      const rawTurn = (p.turn ?? p) as Record<string, unknown>
      const failedThreadId = (rawTurn.threadId as string | undefined) ?? (p.threadId as string | undefined)
      const error = (p.error as string) ?? (p.message as string) ?? 'Unknown error'
      if (failedThreadId) {
        void window.api.skillMarket?.cleanupDotCraftInstall?.({ threadId: failedThreadId }).catch(() => {})
      }
      conversationFor(failedThreadId)?.onTurnFailed(rawTurn, error)
      applyThreadTurnRuntime(failedThreadId, rawTurn, false)

      return true
    }

    case 'turn/cancelled': {
      const rawTurn = (p.turn ?? p) as Record<string, unknown>
      const cancelledThreadId = (rawTurn.threadId as string | undefined) ?? (p.threadId as string | undefined)
      const reason = (p.reason as string) ?? ''
      if (cancelledThreadId) {
        void window.api.skillMarket?.cleanupDotCraftInstall?.({ threadId: cancelledThreadId }).catch(() => {})
      }
      conversationFor(cancelledThreadId)?.onTurnCancelled(rawTurn, reason)
      applyThreadTurnRuntime(cancelledThreadId, rawTurn, false)

      return true
    }

    case 'item/started': {
      const tid = (p.threadId as string | undefined) ?? ''
      conversationFor(tid)?.onItemStarted(p)

      return true
    }

    case 'item/agentMessage/delta': {
      const tid = (p.threadId as string | undefined) ?? ''
      const delta = (p.delta as string) ?? ''
      const conversation = conversationFor(tid)
      if (conversation) {
        conversation.onAgentMessageDelta(delta)
      }

      return true
    }

    case 'item/reasoning/delta': {
      const tid = (p.threadId as string | undefined) ?? ''
      const delta = (p.delta as string) ?? ''
      const conversation = conversationFor(tid)
      if (conversation) {
        conversation.onReasoningDelta(delta)
      }

      return true
    }

    case 'item/commandExecution/terminalInteraction': {
      const tid = (p.threadId as string | undefined) ?? ''
      const conversation = conversationFor(tid)
      if (conversation) {
        conversation.onTerminalInteraction({
          turnId: p.turnId as string | undefined,
          itemId: p.itemId as string | undefined,
          stdin: p.stdin as string | undefined
        })
      }

      return true
    }

    case 'item/commandExecution/outputDelta': {
      const tid = (p.threadId as string | undefined) ?? ''
      const params = {
        threadId: p.threadId as string | undefined,
        turnId: p.turnId as string | undefined,
        itemId: p.itemId as string | undefined,
        delta: p.delta as string | undefined
      }
      const conversation = conversationFor(tid)
      if (conversation) {
        conversation.onCommandExecutionDelta(params)
      }

      return true
    }

    case 'terminal/started':
    case 'terminal/outputDelta':
    case 'terminal/completed':
    case 'terminal/stalled':
    case 'terminal/cleaned': {
      const terminal = (p.terminal ?? {}) as Record<string, unknown>
      const tid = (terminal.threadId as string | undefined) ?? ''
      const params = {
        event: method,
        terminal,
        delta: p.delta as string | undefined
      }
      const conversation = conversationFor(tid)
      if (conversation) {
        conversation.onTerminalEvent(params)
      }

      return true
    }

    case 'item/toolCall/argumentsDelta': {
      const tid = (p.threadId as string | undefined) ?? ''
      const conversation = conversationFor(tid)
      if (conversation) {
        conversation.onToolCallArgumentsDelta({
          threadId: p.threadId as string | undefined,
          turnId: p.turnId as string | undefined,
          itemId: p.itemId as string | undefined,
          toolName: p.toolName as string | undefined,
          callId: p.callId as string | undefined,
          delta: p.delta as string | undefined
        })
      }
      return true
    }

    case 'item/toolExecution/progress': {
      const tid = (p.threadId as string | undefined) ?? ''
      const progress = normalizeRemoteFileTransferProgress(p.progress)
      const conversation = conversationFor(tid)
      if (progress && conversation) {
        conversation.onToolExecutionProgress({
          turnId: p.turnId as string | undefined,
          itemId: p.itemId as string | undefined,
          callId: p.callId as string | undefined,
          progress
        })
      }
      return true
    }

    case 'item/completed': {
      const tid = (p.threadId as string | undefined) ?? ''
      const conversation = conversationFor(tid)
      if (!conversation) return true
      conversation.onItemCompleted(p)
      if (!isActiveConversation(tid)) return true
      const rawItem = p.item as Record<string, unknown> | undefined
      const completedMessage =
        typeof rawItem?.id === 'string'
          ? useConversationStore
              .getState()
              .turns.flatMap((turn) => turn.items)
              .find((item) => item.id === rawItem.id && item.type === 'agentMessage')
          : undefined
      if (completedMessage?.text) reloadVisualizationsReferencedBy(tid, completedMessage.id, completedMessage.text)
      const payload = rawItem?.payload as Record<string, unknown> | undefined
      const callId = typeof payload?.callId === 'string' ? payload.callId : undefined
      if (callId) {
        const toolCall = useConversationStore
          .getState()
          .turns.flatMap((turn) => turn.items)
          .find((item) => item.type === 'toolCall' && item.toolCallId === callId)
        if (toolCall) autoOpenWorkflowLaunch(tid, toolCall.toolName ?? '', toolCall.result, toolCall.success)
      }

      return true
    }

    case 'item/usage/delta': {
      const input = (p.inputTokens as number) ?? 0
      const output = (p.outputTokens as number) ?? 0
      if (context.foreground) {
        const petFind = usePetStore.getState().advance(0, input + output)
        if (petFind) showFindToast(petFind)
      }
      const tid = (p.threadId as string | undefined) ?? ''
      const conversation = conversationFor(tid)
      if (!conversation) return true
      const totalInput = typeof p.totalInputTokens === 'number' ? (p.totalInputTokens as number) : null
      const totalOutput = typeof p.totalOutputTokens === 'number' ? (p.totalOutputTokens as number) : null
      const contextUsage =
        typeof p.contextUsage === 'object' && p.contextUsage !== null
          ? (p.contextUsage as ContextUsageSnapshotWire)
          : null
      conversation.onUsageDelta(input, output, totalInput, totalOutput, contextUsage)
      return true
    }

    case 'subagent/progress': {
      const entries = (p.entries as SubAgentEntry[]) ?? []
      const threadId = (p.threadId as string | undefined) ?? ''
      if (threadId) {
        const subAgentStore = useSubAgentStore.getState()
        const knownChildCount =
          subAgentStore.childrenByParent.get(threadId)?.filter((child) => !isSubAgentChildClosed(child)).length ?? 0
        subAgentStore.updateProgress(threadId, entries)
        const nextSubAgentStore = useSubAgentStore.getState()
        if (entries.length > 0 && knownChildCount < entries.length) {
          void nextSubAgentStore.fetchChildren(threadId).catch(() => {})
        }
      }
      return true
    }

    case 'thread/tokenUsage/updated': {
      const threadId = (p.threadId as string | undefined) ?? ''
      const last = (p.tokenUsage as { last?: { inputTokens?: number; outputTokens?: number } } | undefined)?.last
      if (threadId && typeof last?.inputTokens === 'number') {
        useSubAgentStore.getState().updateChildUsage(threadId, last.inputTokens + (last.outputTokens ?? 0))
      }
      return true
    }

    case 'subagent/board/changed': {
      const rootThreadId = p.rootThreadId as string | undefined
      if (rootThreadId) useBoardStore.getState().markChanged(rootThreadId)
      return true
    }

    case 'subagent/graph/changed': {
      const parentThreadId = (p.parentThreadId as string | undefined) ?? ''
      if (parentThreadId) {
        void useSubAgentStore
          .getState()
          .fetchChildren(parentThreadId, { authoritative: true })
          .catch(() => {})
      }
      if (context.foreground) void context.reloadThreadList()
      return true
    }

    case 'system/event': {
      const tid = (p.threadId as string | undefined) ?? ''
      const kind = (p.kind as string) ?? ''
      const serverMessage = serverFallbackText(context.locale, p.messageKey, p.params, p.fallbackText, p.message)
      if (context.foreground && kind === 'guidanceBlocked' && serverMessage) {
        addToast(serverMessage, 'warning')
      }
      const conversation = conversationFor(tid)
      if (!conversation) return true
      conversation.onSystemEvent(kind, {
        turnId: typeof p.turnId === 'string' ? (p.turnId as string) : null,
        message: serverMessage,
        messageKey: typeof p.messageKey === 'string' ? (p.messageKey as string) : null,
        params: typeof p.params === 'object' && p.params !== null ? (p.params as Record<string, unknown>) : null,
        tokenCount: typeof p.tokenCount === 'number' ? (p.tokenCount as number) : null,
        percentLeft: typeof p.percentLeft === 'number' ? (p.percentLeft as number) : null,
        contextUsage:
          typeof p.contextUsage === 'object' && p.contextUsage !== null
            ? (p.contextUsage as ContextUsageSnapshotWire)
            : null
      })
      return true
    }

    case 'plan/updated': {
      const tid = (p.threadId as string | undefined) ?? ''
      if (tid) conversationFor(tid)?.onPlanUpdated(p as Record<string, unknown>)
      return true
    }

    case 'turn/diff/updated': {
      const tid = (p.threadId as string | undefined) ?? ''
      const conversation = tid ? conversationFor(tid) : null
      if (!conversation) return true
      conversation.onTurnDiffUpdated({
        turnId: (p.turnId as string | undefined) ?? '',
        diff: typeof p.diff === 'string' ? p.diff : ''
      })
      return true
    }

    case 'item/approval/resolved': {
      const resolved = extractApprovalResolvedParams(p)
      resolveParkedApproval(resolved.threadId, resolved.requestId)
      conversationFor(resolved.threadId)?.onApprovalResolved(resolved)
      return true
    }

    case 'item/tool/requestUserInput/resolved': {
      const threadId = (p.threadId as string | undefined) ?? ''
      conversationFor(threadId)?.onUserInputResolved()
      if (!isActiveConversation(threadId)) {
        useThreadStore.getState().clearParkedUserInput(threadId)
      }
      return true
    }

    default:
      return false
  }
}
