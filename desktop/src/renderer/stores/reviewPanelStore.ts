import { create } from 'zustand'
import type { ConversationTurn, ConversationItem, TurnStatus } from '../types/conversation'
import {
  derivePluginFunctionResultText,
  isToolLikeItemType,
  normalizePluginFunctionContentItems,
  wireItemToConversationItem,
  wireTurnToConversationTurn
} from '../types/conversation'
import { isShellToolName } from '../utils/shellTools'
import type { AutomationTask } from './automationsStore'
import { useAutomationsStore } from './automationsStore'
import type { SubAgentEntry } from '../types/toolCall'

interface PendingTerminalEntry {
  event: string
  terminal: Record<string, unknown>
}

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

function mergeToolExecutionAcrossItems(
  items: ConversationItem[],
  toolExecution: Partial<ConversationItem>
): ConversationItem[] {
  return items.map((i) => mergeToolExecutionIntoToolCall(i, toolExecution))
}

function mergeHistoricalCommandExecutions(turn: ConversationTurn): ConversationTurn {
  let items = turn.items
  for (const item of turn.items) {
    if (item.type === 'commandExecution' && item.toolCallId) {
      items = mergeCommandExecutionAcrossItems(items, item)
    } else if (item.type === 'toolExecution' && item.toolCallId) {
      items = mergeToolExecutionAcrossItems(items, item)
    }
  }
  return { ...turn, items: items.filter((item) => item.type !== 'toolExecution') }
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

export interface ReviewPanelState {
  /** Task id for which the panel was opened (used to sync when threadId appears later). */
  openedTaskId: string | null
  taskDetail: AutomationTask | null
  reviewThreadId: string | null
  /** True after thread/subscribe succeeded for live streaming. */
  subscriptionActive: boolean
  turns: ConversationTurn[]
  turnStatus: 'idle' | 'running' | 'waitingApproval'
  activeTurnId: string | null
  streamingMessage: string
  streamingMessageLastDeltaAt: number | null
  streamingReasoning: string
  streamingReasoningStartedAt: number | null
  activeItemId: string | null
  streamingActive: boolean
  pendingTerminalByCallId: Map<string, PendingTerminalEntry>
  loading: boolean
  loadError: string | null
  /** SubAgent progress rows for the thread being reviewed (isolated from main conversation). */
  subAgentEntries: SubAgentEntry[]
  /** Sequence number to prevent race conditions from stale async operations. */
  _seq: number

  openReviewPanel(taskId: string): Promise<void>
  /** Unsubscribe and clear review state (does not change sidebar selection). */
  destroyReviewPanel(): void
  closeReviewPanel(): void
  /** When automations list gains a threadId for a previously pending task, load history + subscribe. */
  maybeAdvancePendingThread(): Promise<void>
  loadThreadSnapshot(threadId: string, task: AutomationTask): Promise<void>

  onTurnStarted(rawTurn: Record<string, unknown>): void
  onItemStarted(params: Record<string, unknown>): void
  onAgentMessageDelta(delta: string): void
  onReasoningDelta(delta: string): void
  onCommandExecutionDelta(params: { threadId?: string; turnId?: string; itemId?: string; delta?: string }): void
  onTerminalEvent(params: { event: string; terminal?: Record<string, unknown>; delta?: string }): void
  onItemCompleted(params: Record<string, unknown>): void
  onTurnCompleted(rawTurn: Record<string, unknown>): void
  onTurnFailed(rawTurn: Record<string, unknown>, error: string): void
  onTurnCancelled(rawTurn: Record<string, unknown>, reason: string): void
  onSubagentProgress(entries: SubAgentEntry[]): void
}

function emptyTurnFields() {
  return {
    turns: [] as ConversationTurn[],
    turnStatus: 'idle' as const,
    activeTurnId: null as string | null,
    streamingMessage: '',
    streamingMessageLastDeltaAt: null as number | null,
    streamingReasoning: '',
    streamingReasoningStartedAt: null as number | null,
    activeItemId: null as string | null,
    streamingActive: false,
    pendingTerminalByCallId: new Map<string, PendingTerminalEntry>(),
    subAgentEntries: [] as SubAgentEntry[]
  }
}

export const useReviewPanelStore = create<ReviewPanelState>((set, get) => ({
  openedTaskId: null,
  taskDetail: null,
  reviewThreadId: null,
  subscriptionActive: false,
  ...emptyTurnFields(),
  loading: false,
  loadError: null,
  _seq: 0,

  async openReviewPanel(taskId: string) {
    const prev = get()
    
    // Unsubscribe from previous thread if any
    if (prev.subscriptionActive && prev.reviewThreadId) {
      void window.api.appServer
        .sendRequest('thread/unsubscribe', { threadId: prev.reviewThreadId })
        .catch((err) => {
          console.warn('Failed to unsubscribe from previous thread:', err)
        })
    }

    // Increment sequence to invalidate any in-flight operations
    const newSeq = prev._seq + 1
    set({
      openedTaskId: taskId,
      taskDetail: null,
      reviewThreadId: null,
      subscriptionActive: false,
      ...emptyTurnFields(),
      loading: true,
      loadError: null,
      _seq: newSeq
    })

    const tasks = useAutomationsStore.getState().tasks
    const listTask = tasks.find((t) => t.id === taskId)

    try {
      const readResult = (await window.api.appServer.sendRequest('automation/task/read', {
        taskId
      })) as Record<string, unknown>

      // Check if this request is still valid (not stale)
      const current = get()
      if (current._seq !== newSeq) {
        console.debug('openReviewPanel: stale request, ignoring')
        return
      }

      const task = mapWireTaskToAutomationTask(readResult)
      set({ taskDetail: task })

      if (task.threadId) {
        await get().loadThreadSnapshot(task.threadId, task)
      }
    } catch (e: unknown) {
      const current = get()
      // Only set error if this is still the current request
      if (current._seq === newSeq) {
        const msg = e instanceof Error ? e.message : String(e)
        set({ loadError: msg, loading: false })
      }
      return
    }

    // Only update loading state if still current
    const current = get()
    if (current._seq === newSeq) {
      set({ loading: false })
    }
  },

  async loadThreadSnapshot(threadId: string, task: AutomationTask) {
    const seqAtStart = get()._seq
    set({ reviewThreadId: threadId, ...emptyTurnFields(), subscriptionActive: false })

    try {
      const res = (await window.api.appServer.sendRequest('thread/read', {
        threadId,
        includeTurns: true
      })) as { thread?: { turns?: Array<Record<string, unknown>> } }
      
      // Check if still valid
      if (get()._seq !== seqAtStart) {
        console.debug('loadThreadSnapshot: stale request, ignoring')
        return
      }

      const rawTurns = res.thread?.turns ?? []
      const turns = rawTurns.map((t) => mergeHistoricalCommandExecutions(wireTurnToConversationTurn(t)))
      const runningTurn = turns.find((t) => t.status === 'running')
      set((state) => {
        const terminalApplied = applyPendingTerminalsToTurns(turns, state.pendingTerminalByCallId)
        return {
          turns: terminalApplied.turns,
          turnStatus: runningTurn ? 'running' : 'idle',
          activeTurnId: runningTurn ? runningTurn.id : null,
          streamingMessage: '',
          streamingMessageLastDeltaAt: null,
          streamingReasoning: '',
          streamingReasoningStartedAt: null,
          activeItemId: null,
          streamingActive: false,
          pendingTerminalByCallId: terminalApplied.pendingTerminalByCallId
        }
      })
    } catch (e: unknown) {
      // Only set error if still current
      if (get()._seq === seqAtStart) {
        const msg = e instanceof Error ? e.message : String(e)
        set({ loadError: msg })
      }
      return
    }

    if (task.status === 'running') {
      try {
        await window.api.appServer.sendRequest('thread/subscribe', { threadId })
        // Check if still valid before setting subscription
        if (get()._seq === seqAtStart) {
          set({ subscriptionActive: true, streamingActive: true })
        } else {
          // Stale - unsubscribe immediately
          void window.api.appServer
            .sendRequest('thread/unsubscribe', { threadId })
            .catch(() => {})
        }
      } catch (err) {
        // Only set state if still current
        if (get()._seq === seqAtStart) {
          set({ subscriptionActive: false })
          console.warn('Failed to subscribe to thread:', err)
        }
      }
    }
  },

  async maybeAdvancePendingThread() {
    const { openedTaskId, reviewThreadId, loading, _seq } = get()
    if (!openedTaskId || loading || reviewThreadId) return

    const task = useAutomationsStore.getState().tasks.find((t) => t.id === openedTaskId)
    if (!task?.threadId) return

    set({ taskDetail: { ...task } })
    await get().loadThreadSnapshot(task.threadId, task)
  },

  destroyReviewPanel() {
    const { reviewThreadId, subscriptionActive, _seq } = get()
    if (subscriptionActive && reviewThreadId) {
      void window.api.appServer
        .sendRequest('thread/unsubscribe', { threadId: reviewThreadId })
        .catch((err) => {
          console.warn('Failed to unsubscribe on destroy:', err)
        })
    }
    // Increment sequence to invalidate any in-flight operations
    set({
      openedTaskId: null,
      taskDetail: null,
      reviewThreadId: null,
      subscriptionActive: false,
      ...emptyTurnFields(),
      loading: false,
      loadError: null,
      _seq: _seq + 1
    })
  },

  closeReviewPanel() {
    get().destroyReviewPanel()
    useAutomationsStore.getState().selectTask(null)
  },

  onTurnStarted(rawTurn) {
    const turn = wireTurnToConversationTurn(rawTurn)
    set((state) => {
      const alreadyExists = state.turns.find((t) => t.id === turn.id)
      if (alreadyExists) {
        return {
          turns: state.turns.map((t) =>
            t.id === turn.id ? { ...t, status: 'running' as TurnStatus, startedAt: turn.startedAt } : t
          ),
          turnStatus: 'running',
          activeTurnId: turn.id,
          streamingMessage: '',
          streamingMessageLastDeltaAt: null,
          streamingReasoning: '',
          streamingReasoningStartedAt: null,
          activeItemId: null,
          streamingActive: true,
          subAgentEntries: [] as SubAgentEntry[]
        }
      }
      return {
        turns: [...state.turns, turn],
        turnStatus: 'running',
        activeTurnId: turn.id,
        streamingMessage: '',
        streamingMessageLastDeltaAt: null,
        streamingReasoning: '',
        streamingReasoningStartedAt: null,
        activeItemId: null,
        streamingActive: true,
        subAgentEntries: [] as SubAgentEntry[]
      }
    })
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
          t.id === turnId ? { ...t, items: sortItemsByCreatedAt([...t.items, newItem]) } : t
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
          t.id === turnId ? { ...t, items: sortItemsByCreatedAt([...t.items, newItem]) } : t
        )
      }))
    } else if (isToolLikeItemType(type)) {
      const newItem = buildToolLikeItem(item, type, 'started')
      set((state) => {
        let nextPending = state.pendingTerminalByCallId
        const turns = state.turns.map((t) => {
          if (t.id !== turnId) return t
          const nextTurn = { ...t, items: sortItemsByCreatedAt([...t.items, newItem]) }
          if (type !== 'toolCall') return nextTurn
          const applied = applyPendingTerminalsToTurn(nextTurn, state.pendingTerminalByCallId)
          nextPending = removePendingTerminalEntries(nextPending, applied.appliedCallIds)
          return applied.turn
        })
        return { turns, pendingTerminalByCallId: nextPending }
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
        // Same as main conversationStore: wire item.status is lifecycle; execution state is payload.status.
        executionStatus: (itemPayload.status as ConversationItem['executionStatus'] | undefined) ?? 'inProgress',
        toolCallId: (item?.callId as string | undefined)
          ?? (itemPayload.callId as string | undefined),
        createdAt: (item?.createdAt as string) ?? new Date().toISOString()
      }
      set((state) => ({
        turns: state.turns.map((t) =>
          t.id !== turnId
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
                    ? { ...i, aggregatedOutput: (i.aggregatedOutput ?? '') + delta }
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

  onItemCompleted(params) {
    const item = params.item as Record<string, unknown>
    const type = item?.type as string
    const turnId = params.turnId as string
    const state = get()

    if (type === 'agentMessage') {
      const itemId = (item?.id as string) ?? ''
      const alreadyCommitted = state.turns
        .find((t) => t.id === turnId)
        ?.items.some((i) => i.id === itemId && i.type === 'agentMessage' && i.status === 'completed')
      if (alreadyCommitted) {
        set({ streamingMessage: '', streamingMessageLastDeltaAt: null, activeItemId: null })
        return
      }
      const finalText =
        state.streamingMessage || ((item?.text as string) ?? (item?.content as string) ?? '')
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
      const finalText =
        state.streamingReasoning || ((item?.text as string) ?? (item?.content as string) ?? '')
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
    } else if (type === 'toolCall') {
      set((s) => {
        let nextPending = s.pendingTerminalByCallId
        const turns = s.turns.map((t) => {
          if (t.id !== turnId) return t
          const nextTurn = {
            ...t,
            items: sortItemsByCreatedAt(
              t.items.map((i) =>
                i.id === (item?.id as string)
                  ? { ...i, status: 'completed' as const, completedAt: (item?.completedAt as string) }
                  : i
              )
            )
          }
          const applied = applyPendingTerminalsToTurn(nextTurn, s.pendingTerminalByCallId)
          nextPending = removePendingTerminalEntries(nextPending, applied.appliedCallIds)
          return applied.turn
        })
        return { turns, pendingTerminalByCallId: nextPending }
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
                      // Prefer payload execution status; do not use wire item.status here.
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
      if (!toolExecution.toolCallId) return

      set((s) => ({
        turns: s.turns.map((t) =>
          t.id !== turnId
            ? t
            : {
                ...t,
                items: sortItemsByCreatedAt(
                  mergeToolExecutionAcrossItems(t.items, toolExecution)
                )
              }
        )
      }))
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
      const itemPayload = (item?.payload ?? {}) as Record<string, unknown>
      const callId =
        (item?.callId as string | undefined) ??
        (itemPayload.callId as string | undefined) ??
        (item?.toolCallId as string | undefined)
      const resultText =
        (item?.result as string | undefined) ??
        (itemPayload.result as string | undefined) ??
        (item?.text as string | undefined) ??
        ''
      const success = (item?.success as boolean | undefined) ?? (itemPayload.success as boolean | undefined) ?? true

      set((s) => ({
        turns: s.turns.map((t) => {
          if (t.id !== turnId) return t
          return {
            ...t,
            items: sortItemsByCreatedAt(
              t.items.map((i) => {
                if (i.type === 'toolCall' && i.toolCallId === callId) {
                  const startMs = i.createdAt ? new Date(i.createdAt).getTime() : Date.now()
                  const endMs = (item?.completedAt as string)
                    ? new Date(item.completedAt as string).getTime()
                    : Date.now()
                  return {
                    ...i,
                    status: 'completed' as const,
                    result: resultText,
                    success,
                    duration: endMs - startMs,
                    completedAt: (item?.completedAt as string) ?? new Date().toISOString()
                  }
                }
                return i
              })
            )
          }
        })
      }))
    }
  },

  onTurnCompleted(rawTurn) {
    const turn = wireTurnToConversationTurn(rawTurn)
    set((state) => ({
      turns: state.turns.map((t) =>
        t.id === turn.id
          ? {
              ...t,
              status: 'completed' as TurnStatus,
              completedAt: turn.completedAt,
              tokenUsage: turn.tokenUsage
            }
          : t
      ),
      turnStatus: 'idle',
      activeTurnId: null,
      streamingMessage: '',
      streamingMessageLastDeltaAt: null,
      streamingReasoning: '',
      streamingReasoningStartedAt: null,
      activeItemId: null,
      streamingActive: false
    }))
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
      streamingMessage: '',
      streamingMessageLastDeltaAt: null,
      streamingReasoning: '',
      streamingReasoningStartedAt: null,
      activeItemId: null,
      streamingActive: false
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
              completedAt: turn.completedAt
            }
          : t
      ),
      turnStatus: 'idle',
      activeTurnId: null,
      streamingMessage: '',
      streamingMessageLastDeltaAt: null,
      streamingReasoning: '',
      streamingReasoningStartedAt: null,
      activeItemId: null,
      streamingActive: false
    }))
  },

  onSubagentProgress(entries) {
    set({ subAgentEntries: entries })
  }
}))

function mapWireTaskToAutomationTask(raw: Record<string, unknown>): AutomationTask {
  const statusRaw = String(raw.status ?? raw.Status ?? 'pending')
  const statusMap: Record<string, AutomationTask['status']> = {
    pending: 'pending',
    running: 'running',
    completed: 'completed',
    failed: 'failed'
  }
  const status = statusMap[statusRaw] ?? 'pending'

  const createdAt = raw.createdAt ?? raw.CreatedAt
  const updatedAt = raw.updatedAt ?? raw.UpdatedAt

  const approvalPolicy =
    (raw.approvalPolicy as string | undefined) ?? (raw.ApprovalPolicy as string | undefined) ?? null

  return {
    id: (raw.id as string) ?? (raw.Id as string) ?? (raw.taskId as string) ?? '',
    title: (raw.title as string) ?? (raw.Title as string) ?? '',
    status,
    threadId: (raw.threadId as string | null) ?? (raw.ThreadId as string | null) ?? null,
    description: (raw.description as string | undefined) ?? (raw.Description as string | undefined),
    agentSummary:
      (raw.agentSummary as string | null) ??
      (raw.AgentSummary as string | null) ??
      null,
    approvalPolicy,
    createdAt: createdAt != null ? String(createdAt) : new Date().toISOString(),
    updatedAt: updatedAt != null ? String(updatedAt) : new Date().toISOString()
  }
}
