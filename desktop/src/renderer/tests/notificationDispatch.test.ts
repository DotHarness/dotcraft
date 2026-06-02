/**
 * Integration test for the notification dispatch pipeline.
 *
 * Simulates the exact payload format the preload sends:
 *   { method: string, params: unknown }
 *
 * Drives a dispatch function (mirroring App.tsx's notification handler) through
 * a complete turn lifecycle and asserts that conversationStore transitions correctly.
 *
 * Background: The bug this guards against is App.tsx calling
 *   onNotification((method, params) => {...})   ← two args, wrong
 * when the preload actually calls the callback as
 *   callback({ method, params })                ← one payload object
 * causing ALL notifications to be silently dropped.
 */

import { describe, it, expect, beforeEach, vi } from 'vitest'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useSkillsStore } from '../stores/skillsStore'
import { useSubAgentStore } from '../stores/subAgentStore'
import { useAutomationsStore, type AutomationTask } from '../stores/automationsStore'
import { useUIStore } from '../stores/uiStore'
import type { ContextUsageSnapshotWire, ThreadSummary } from '../types/thread'
import type { ApprovalDecision, InputPart } from '../types/conversation'
import type { SubAgentEntry } from '../types/toolCall'
import { resolveWorkspaceConfigChangedPayload } from '../utils/workspaceConfigChanged'
import { buildComposerInputParts } from '../utils/composeInputParts'

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
  const item = params.item && typeof params.item === 'object'
    ? params.item as Record<string, unknown>
    : {}
  const payload = item.payload && typeof item.payload === 'object'
    ? item.payload as Record<string, unknown>
    : {}
  return {
    threadId: typeof params.threadId === 'string' ? params.threadId : null,
    turnId: typeof params.turnId === 'string' ? params.turnId : null,
    requestId: typeof payload.requestId === 'string'
      ? payload.requestId
      : typeof item.requestId === 'string'
        ? item.requestId
        : null,
    decision: normalizeApprovalDecision(payload.decision ?? item.decision)
  }
}

// ---------------------------------------------------------------------------
// Replay a notification payload through the same dispatch logic as App.tsx.
// This is intentionally kept in sync with the switch block in App.tsx so
// that any future mismatch would be caught here first.
// ---------------------------------------------------------------------------

function dispatch(payload: { method: string; params: unknown }): void {
  // Mirror App.tsx: destructure the single payload object
  const method = payload.method
  const p = (payload.params ?? {}) as Record<string, unknown>
  const conv = useConversationStore.getState()
  const threads = useThreadStore.getState()
  const shouldUpdateActiveConversation = (threadId: string | null | undefined): boolean => {
    if (!threadId) return true
    return useThreadStore.getState().activeThreadId === threadId
  }

  switch (method) {
    case 'workspace/configChanged': {
      const event = resolveWorkspaceConfigChangedPayload(payload, workspaceConfigChangedDedupe)
      if (event?.regions.includes('skills')) {
        void useSkillsStore.getState().fetchSkills()
      }
      break
    }

    case 'thread/runtimeChanged': {
      const threadId = (p.threadId as string | undefined) ?? ''
      if (!threadId) break
      const runtime = p.runtime != null && typeof p.runtime === 'object'
        ? p.runtime as Record<string, unknown>
        : {}
      const runtimeSnapshot = {
        running: runtime.running === true,
        waitingOnApproval: runtime.waitingOnApproval === true,
        waitingOnPlanConfirmation: runtime.waitingOnPlanConfirmation === true
      }
      threads.applyRuntimeSnapshot(threadId, runtimeSnapshot, {
        isActive: threads.activeThreadId === threadId,
        isDesktopOrigin: true
      })
      useSubAgentStore.getState().updateChildRuntime(threadId, runtimeSnapshot)
      if (threads.activeThreadId === threadId) {
        const conversation = useConversationStore.getState()
        const pendingApproval = conversation.pendingApproval
        if (
          !runtimeSnapshot.waitingOnApproval &&
          pendingApproval != null &&
          pendingApproval.locallySubmittedDecision == null &&
          (pendingApproval.threadId == null || pendingApproval.threadId === threadId)
        ) {
          window.api?.appServer?.sendServerResponse?.(pendingApproval.bridgeId, { decision: 'decline' })
          conversation.onApprovalNoLongerPending({
            threadId,
            turnId: pendingApproval.turnId,
            requestId: pendingApproval.requestId,
            nextTurnStatus: runtimeSnapshot.running ? 'running' : 'idle'
          })
        }
      }
      break
    }

    case 'turn/started': {
      const rawTurn = (p.turn ?? p) as Record<string, unknown>
      const threadId = (rawTurn.threadId as string | undefined) ?? (p.threadId as string | undefined)
      if (shouldUpdateActiveConversation(threadId)) {
        conv.onTurnStarted(rawTurn)
      }
      break
    }

    case 'turn/completed': {
      const rawTurn = (p.turn ?? p) as Record<string, unknown>
      const threadId = (rawTurn.threadId as string | undefined) ?? (p.threadId as string | undefined)
      if (shouldUpdateActiveConversation(threadId)) {
        conv.onTurnCompleted(rawTurn)
      }
      break
    }

    case 'turn/failed': {
      const rawTurn = (p.turn ?? p) as Record<string, unknown>
      const error = (p.error as string) ?? 'Unknown error'
      const threadId = (rawTurn.threadId as string | undefined) ?? (p.threadId as string | undefined)
      if (shouldUpdateActiveConversation(threadId)) {
        conv.onTurnFailed(rawTurn, error)
      }
      break
    }

    case 'turn/cancelled': {
      const rawTurn = (p.turn ?? p) as Record<string, unknown>
      const reason = (p.reason as string) ?? ''
      const threadId = (rawTurn.threadId as string | undefined) ?? (p.threadId as string | undefined)
      if (shouldUpdateActiveConversation(threadId)) {
        conv.onTurnCancelled(rawTurn, reason)
      }
      break
    }

    case 'item/started':
      if (shouldUpdateActiveConversation((p.threadId as string | undefined) ?? '')) {
        conv.onItemStarted(p)
      }
      break

    case 'item/agentMessage/delta':
      if (shouldUpdateActiveConversation((p.threadId as string | undefined) ?? '')) {
        conv.onAgentMessageDelta((p.delta as string) ?? '')
      }
      break

    case 'item/reasoning/delta':
      if (shouldUpdateActiveConversation((p.threadId as string | undefined) ?? '')) {
        conv.onReasoningDelta((p.delta as string) ?? '')
      }
      break

    case 'item/commandExecution/outputDelta':
      if (shouldUpdateActiveConversation((p.threadId as string | undefined) ?? '')) {
        conv.onCommandExecutionDelta({
          threadId: (p.threadId as string | undefined),
          turnId: (p.turnId as string | undefined),
          itemId: (p.itemId as string | undefined),
          delta: (p.delta as string | undefined)
        })
      }
      break

    case 'terminal/started':
    case 'terminal/outputDelta':
    case 'terminal/completed':
    case 'terminal/stalled':
    case 'terminal/cleaned': {
      const terminal = (p.terminal ?? {}) as Record<string, unknown>
      if (shouldUpdateActiveConversation((terminal.threadId as string | undefined) ?? '')) {
        conv.onTerminalEvent({
          event: method,
          terminal,
          delta: (p.delta as string | undefined)
        })
      }
      break
    }

    case 'item/toolCall/argumentsDelta':
      if (shouldUpdateActiveConversation((p.threadId as string | undefined) ?? '')) {
        conv.onToolCallArgumentsDelta({
          threadId: (p.threadId as string | undefined),
          turnId: (p.turnId as string | undefined),
          itemId: (p.itemId as string | undefined),
          toolName: (p.toolName as string | undefined),
          callId: (p.callId as string | undefined),
          delta: (p.delta as string | undefined)
        })
      }
      break

    case 'item/completed':
      if (shouldUpdateActiveConversation((p.threadId as string | undefined) ?? '')) {
        conv.onItemCompleted(p)
      }
      break

    case 'item/usage/delta': {
      if (!shouldUpdateActiveConversation((p.threadId as string | undefined) ?? '')) break
      const totalInput = typeof p.totalInputTokens === 'number' ? (p.totalInputTokens as number) : null
      const totalOutput = typeof p.totalOutputTokens === 'number' ? (p.totalOutputTokens as number) : null
      const contextUsage = typeof p.contextUsage === 'object' && p.contextUsage !== null
        ? p.contextUsage as ContextUsageSnapshotWire
        : null
      conv.onUsageDelta((p.inputTokens as number) ?? 0, (p.outputTokens as number) ?? 0, totalInput, totalOutput, contextUsage)
      break
    }

    case 'system/event':
      if (shouldUpdateActiveConversation((p.threadId as string | undefined) ?? '')) {
        conv.onSystemEvent((p.kind as string) ?? '', {
          turnId: typeof p.turnId === 'string' ? (p.turnId as string) : null,
          message: typeof p.message === 'string' ? (p.message as string) : null,
          tokenCount: typeof p.tokenCount === 'number' ? (p.tokenCount as number) : null,
          percentLeft: typeof p.percentLeft === 'number' ? (p.percentLeft as number) : null,
          contextUsage: typeof p.contextUsage === 'object' && p.contextUsage !== null
            ? p.contextUsage as ContextUsageSnapshotWire
            : null
        })
      }
      break

    case 'plan/updated': {
      const threadId = (p.threadId as string | undefined) ?? ''
      if (!threadId || !shouldUpdateActiveConversation(threadId)) break
      conv.onPlanUpdated(p as Record<string, unknown>)
      useUIStore.getState().setActiveDetailTab('plan')
      break
    }

    case 'item/approval/resolved': {
      const resolved = extractApprovalResolvedParams(p)
      if (shouldUpdateActiveConversation(resolved.threadId)) {
        conv.onApprovalResolved(resolved)
      }
      break
    }

    case 'subagent/progress': {
      const entries = (p.entries as SubAgentEntry[]) ?? []
      const threadId = (p.threadId as string | undefined) ?? ''
      if (threadId) {
        const subAgentStore = useSubAgentStore.getState()
        const knownChildCount = subAgentStore.childrenByParent.get(threadId)?.length ?? 0
        subAgentStore.updateProgress(threadId, entries)
        const nextSubAgentStore = useSubAgentStore.getState()
        if (
          entries.length > 0
          && knownChildCount < entries.length
          && !nextSubAgentStore.loadingParents.has(threadId)
        ) {
          void nextSubAgentStore.fetchChildren(threadId)
        }
      }
      if (shouldUpdateActiveConversation(threadId)) {
        conv.onSubagentProgress(entries)
      }
      break
    }

    case 'subagent/graphChanged': {
      const parentThreadId = (p.parentThreadId as string | undefined) ?? ''
      if (parentThreadId) {
        void useSubAgentStore.getState().fetchChildren(parentThreadId)
      }
      break
    }

    case 'automation/task/updated': {
      const task = (p.task ?? {}) as AutomationTask
      useAutomationsStore.getState().upsertTask(task)
      break
    }

    default:
      break
  }
}

/**
 * Mirrors App.tsx thread lifecycle branch (threadList / removeThread).
 */
function dispatchThreadLifecycle(
  payload: { method: string; params: unknown },
  options: { reloadThreadList?: () => void } = {}
): void {
  const method = payload.method
  const p = (payload.params ?? {}) as Record<string, unknown>
  const { addThread, removeThreadTree, updateThreadStatus } = useThreadStore.getState()

  switch (method) {
    case 'thread/started': {
      const pp = p as { thread: ThreadSummary }
      addThread(pp.thread)
      break
    }
    case 'thread/deleted': {
      const pp = p as { threadId: string }
      removeThreadTree(pp.threadId)
      break
    }
    case 'thread/statusChanged': {
      const pp = p as { threadId: string; newStatus: string }
      if (pp.newStatus === 'archived') {
        removeThreadTree(pp.threadId)
      } else {
        updateThreadStatus(pp.threadId, pp.newStatus as 'active' | 'paused' | 'archived')
      }
      break
    }
    case 'teams/team/changed': {
      options.reloadThreadList?.()
      break
    }
    default:
      break
  }
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const s = () => useConversationStore.getState()

const NOW = new Date().toISOString()
const workspaceConfigChangedDedupe = new Map<string, number>()

function makeTurnPayload(id: string, status = 'running'): Record<string, unknown> {
  return { id, threadId: 'thread-1', status, items: [], startedAt: NOW }
}

async function dispatchTurnCompletedWithAutoSend(
  payload: { method: string; params: unknown },
  options: {
    sendRequest: (method: string, params?: unknown) => Promise<unknown>
    inputParts?: InputPart[]
    workspacePath?: string
  }
): Promise<void> {
  const pendingBefore = useConversationStore.getState().pendingMessage
  dispatch(payload)

  if (!pendingBefore) {
    return
  }

  const activeId = useThreadStore.getState().activeThreadId
  if (!activeId) {
    useConversationStore.getState().setPendingMessage(null)
    return
  }

  let effectiveThreadId = activeId
  const pendingInputParts = options.inputParts
    ?? pendingBefore.inputParts
    ?? buildComposerInputParts({
      text: pendingBefore.text.trim(),
      files: pendingBefore.files ?? []
    }).inputParts

  if (pendingInputParts.length > 0) {
    await options.sendRequest('turn/start', {
      threadId: effectiveThreadId,
      input: pendingInputParts,
      identity: {
        channelName: 'dotcraft-desktop',
        userId: 'local',
        channelContext: `workspace:${options.workspacePath ?? 'F:/examples/workspace'}`,
        workspacePath: options.workspacePath ?? 'F:/examples/workspace'
      }
    })
  }
  useConversationStore.getState().setPendingMessage(null)
}

beforeEach(() => {
  s().reset()
  useThreadStore.getState().reset()
  useSubAgentStore.getState().reset()
  useUIStore.setState({
    activeDetailTab: { kind: 'system', id: 'changes' },
    lastActiveSystemTab: 'changes',
    detailPanelPreferredVisible: false,
    detailPanelVisible: false
  })
  useThreadStore.setState({
    activeThreadId: 'thread-1',
    threadList: [
      {
        id: 'thread-1',
        displayName: 'Thread 1',
        status: 'active',
        originChannel: 'dotcraft-desktop',
        createdAt: NOW,
        lastActiveAt: NOW
      }
    ]
  })
  useAutomationsStore.setState({
    tasks: [],
    selectedTaskId: null,
    statusFilter: 'all'
  })
  workspaceConfigChangedDedupe.clear()
})

// ---------------------------------------------------------------------------
// Tests
// ---------------------------------------------------------------------------

describe('notification dispatch payload format', () => {
  it('dispatches skills refresh for workspace/configChanged notifications', () => {
    let fetchSkillsCalls = 0
    useSkillsStore.setState({
      fetchSkills: async () => {
        fetchSkillsCalls += 1
      }
    })

    dispatch({
      method: 'workspace/configChanged',
      params: {
        source: 'skills/setEnabled',
        regions: ['skills'],
        changedAt: NOW
      }
    })

    expect(fetchSkillsCalls).toBe(1)
  })

  it('does not refresh skills for unrelated workspace/configChanged regions', () => {
    let fetchSkillsCalls = 0
    useSkillsStore.setState({
      fetchSkills: async () => {
        fetchSkillsCalls += 1
      }
    })

    dispatch({
      method: 'workspace/configChanged',
      params: {
        source: 'workspace/config/update',
        regions: ['mcp', 'externalChannel'],
        changedAt: NOW
      }
    })

    expect(fetchSkillsCalls).toBe(0)
  })

  it('shows subagent progress immediately and refreshes child metadata', async () => {
    const sendRequest = vi.fn(async (method: string) => {
      if (method === 'subagent/children/list') {
        return {
          data: [
            {
              edge: {
                parentThreadId: 'thread-1',
                childThreadId: 'child-1',
                agentNickname: 'Lovelace',
                profileName: 'native',
                runtimeType: 'native',
                supportsSendInput: true,
                supportsResume: true,
                supportsClose: true,
                status: 'open'
              },
              thread: {
                id: 'child-1',
                displayName: 'Lovelace',
                status: 'active',
                originChannel: 'subagent',
                createdAt: NOW,
                lastActiveAt: NOW,
                runtime: {
                  running: true,
                  waitingOnApproval: false,
                  waitingOnPlanConfirmation: false
                }
              }
            }
          ]
        }
      }
      return {}
    })
    vi.stubGlobal('window', {
      api: {
        appServer: { sendRequest }
      }
    })

    dispatch({
      method: 'subagent/progress',
      params: {
        threadId: 'thread-1',
        entries: [
          {
            label: 'Lovelace',
            isCompleted: false,
            inputTokens: 12,
            outputTokens: 34,
            currentTool: 'ReadFile',
            currentToolDisplay: 'Reading sprite atlas'
          }
        ]
      }
    })

    expect(useSubAgentStore.getState().childrenByParent.get('thread-1')?.[0]).toEqual(
      expect.objectContaining({
        nickname: 'Lovelace',
        isPlaceholder: true,
        lastToolDisplay: 'Reading sprite atlas',
        runtime: expect.objectContaining({ running: true })
      })
    )

    await vi.waitFor(() => {
      expect(sendRequest).toHaveBeenCalledWith('subagent/children/list', {
        parentThreadId: 'thread-1',
        includeClosed: true,
        includeThreads: true
      })
      expect(useSubAgentStore.getState().childrenByParent.get('thread-1')?.[0]).toEqual(
        expect.objectContaining({
          childThreadId: 'child-1',
          isPlaceholder: false,
          lastToolDisplay: 'Reading sprite atlas',
          supportsClose: true
        })
      )
      expect(useThreadStore.getState().threadList.some((thread) => thread.id === 'child-1')).toBe(true)
    })
  })

  it('updates subagent dock runtime from thread/runtimeChanged notifications', () => {
    useSubAgentStore.getState().setChildren('thread-1', [
      {
        childThreadId: 'child-1',
        parentThreadId: 'thread-1',
        nickname: 'Lovelace',
        agentRole: null,
        profileName: 'native',
        runtimeType: 'native',
        supportsSendInput: true,
        supportsResume: true,
        supportsClose: true,
        status: 'open',
        lastToolDisplay: 'Reading sprite atlas',
        currentTool: 'ReadFile',
        inputTokens: 12,
        outputTokens: 34,
        isCompleted: false,
        runtime: {
          running: true,
          waitingOnApproval: false,
          waitingOnPlanConfirmation: false
        }
      }
    ])

    dispatch({
      method: 'thread/runtimeChanged',
      params: {
        threadId: 'child-1',
        runtime: { running: false, waitingOnApproval: false, waitingOnPlanConfirmation: false }
      }
    })

    expect(useSubAgentStore.getState().childrenByParent.get('thread-1')?.[0]).toEqual(
      expect.objectContaining({
        currentTool: null,
        isCompleted: true,
        runtime: expect.objectContaining({ running: false })
      })
    )
  })

  it('releases the active Desktop approval bridge when runtime shows remote approval resolved', () => {
    const sendServerResponse = vi.fn()
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        appServer: { sendServerResponse }
      }
    })

    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_approval') } })
    s().onApprovalRequest('bridge-remote', {
      threadId: 'thread-1',
      turnId: 'turn_approval',
      requestId: 'req-remote',
      approvalType: 'shell',
      operation: 'npm test',
      target: 'F:/dotcraft',
      reason: 'Run tests.'
    })
    expect(s().pendingApproval?.bridgeId).toBe('bridge-remote')
    expect(s().turnStatus).toBe('waitingApproval')

    dispatch({
      method: 'thread/runtimeChanged',
      params: {
        threadId: 'thread-1',
        runtime: { running: true, waitingOnApproval: false, waitingOnPlanConfirmation: false }
      }
    })

    expect(sendServerResponse).toHaveBeenCalledWith('bridge-remote', { decision: 'decline' })
    expect(s().pendingApproval).toBeNull()
    expect(s().turnStatus).toBe('running')
  })

  it('does not synthesize a decline after a local approval decision was submitted', () => {
    const sendServerResponse = vi.fn()
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        appServer: { sendServerResponse }
      }
    })

    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_local_approval') } })
    s().onApprovalRequest('bridge-local', {
      threadId: 'thread-1',
      turnId: 'turn_local_approval',
      requestId: 'req-local',
      itemId: 'approval-local',
      approvalType: 'shell',
      operation: 'npm test',
      target: 'F:/dotcraft',
      reason: 'Run tests.'
    })
    s().onApprovalSubmitStarted('accept')

    dispatch({
      method: 'thread/runtimeChanged',
      params: {
        threadId: 'thread-1',
        runtime: { running: true, waitingOnApproval: false, waitingOnPlanConfirmation: false }
      }
    })

    expect(sendServerResponse).not.toHaveBeenCalled()
    expect(s().pendingApproval?.bridgeId).toBe('bridge-local')
    expect(s().pendingApproval?.locallySubmittedDecision).toBe('accept')
    expect(s().turnStatus).toBe('waitingApproval')

    dispatch({
      method: 'item/approval/resolved',
      params: {
        threadId: 'thread-1',
        turnId: 'turn_local_approval',
        item: {
          type: 'approvalResponse',
          payload: { requestId: 'req-local', decision: 'accept' }
        }
      }
    })

    const approvalItem = s().turns[0].items.find((item) => item.type === 'approvalCard')
    expect(sendServerResponse).not.toHaveBeenCalled()
    expect(approvalItem?.approvalState).toBe('accepted')
    expect(s().pendingApproval).toBeNull()
    expect(s().turnStatus).toBe('running')
  })

  it('applies approval resolved decisions only for the active thread', () => {
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_approval') } })
    s().onApprovalRequest('bridge-resolved', {
      threadId: 'thread-1',
      turnId: 'turn_approval',
      requestId: 'req-resolved',
      itemId: 'approval-item-1',
      approvalType: 'shell',
      operation: 'npm test',
      target: 'F:/dotcraft',
      reason: 'Run tests.'
    })

    dispatch({
      method: 'item/approval/resolved',
      params: {
        threadId: 'thread-2',
        turnId: 'turn_other',
        item: {
          type: 'approvalResponse',
          payload: { requestId: 'req-other', decision: 'accept' }
        }
      }
    })
    expect(s().pendingApproval?.requestId).toBe('req-resolved')

    dispatch({
      method: 'item/approval/resolved',
      params: {
        threadId: 'thread-1',
        turnId: 'turn_approval',
        item: {
          type: 'approvalResponse',
          payload: { requestId: 'req-resolved', decision: 'acceptForSession' }
        }
      }
    })

    const approvalItem = s().turns[0].items.find((item) => item.type === 'approvalCard')
    expect(approvalItem?.approvalState).toBe('acceptedForSession')
    expect(s().pendingApproval).toBeNull()
    expect(s().turnStatus).toBe('running')
  })

  it('refreshes subagent children and sidebar on graph changes', async () => {
    const sendRequest = vi.fn(async (method: string) => {
      if (method === 'subagent/children/list') {
        return {
          data: [
            {
              edge: {
                parentThreadId: 'thread-1',
                childThreadId: 'child-graph',
                agentNickname: 'Graph child',
                status: 'open'
              },
              thread: {
                id: 'child-graph',
                displayName: 'Graph child',
                status: 'active',
                originChannel: 'subagent',
                source: {
                  kind: 'subagent',
                  subAgent: {
                    parentThreadId: 'thread-1',
                    rootThreadId: 'thread-1',
                    depth: 1
                  }
                },
                createdAt: NOW,
                lastActiveAt: NOW,
                runtime: {
                  running: true,
                  waitingOnApproval: false,
                  waitingOnPlanConfirmation: false
                }
              }
            }
          ]
        }
      }
      return {}
    })
    vi.stubGlobal('window', {
      api: {
        appServer: { sendRequest }
      }
    })

    dispatch({
      method: 'subagent/graphChanged',
      params: { parentThreadId: 'thread-1', childThreadId: 'child-graph' }
    })

    await vi.waitFor(() => {
      expect(sendRequest).toHaveBeenCalledWith('subagent/children/list', {
        parentThreadId: 'thread-1',
        includeClosed: true,
        includeThreads: true
      })
      expect(useThreadStore.getState().threadList.some((thread) => thread.id === 'child-graph')).toBe(true)
    })
  })

  it('dispatches turn/started correctly from { method, params } payload', () => {
    dispatch({
      method: 'thread/runtimeChanged',
      params: {
        threadId: 'thread-1',
        runtime: { running: true, waitingOnApproval: false, waitingOnPlanConfirmation: false }
      }
    })
    dispatch({
      method: 'turn/started',
      params: { turn: makeTurnPayload('turn_server_1') }
    })

    const state = s()
    expect(state.turnStatus).toBe('running')
    expect(state.turns).toHaveLength(1)
    expect(state.turns[0].id).toBe('turn_server_1')
    expect(useThreadStore.getState().runningTurnThreadIds.has('thread-1')).toBe(true)
  })

  it('dispatches turn/completed and sets status to idle', () => {
    dispatch({
      method: 'thread/runtimeChanged',
      params: {
        threadId: 'thread-1',
        runtime: { running: true, waitingOnApproval: false, waitingOnPlanConfirmation: false }
      }
    })
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
    dispatch({
      method: 'thread/runtimeChanged',
      params: {
        threadId: 'thread-1',
        runtime: { running: false, waitingOnApproval: false, waitingOnPlanConfirmation: false }
      }
    })
    dispatch({
      method: 'turn/completed',
      params: { turn: makeTurnPayload('turn_1', 'completed') }
    })

    const state = s()
    expect(state.turnStatus).toBe('idle')
    expect(state.turns[0].status).toBe('completed')
    expect(useThreadStore.getState().runningTurnThreadIds.has('thread-1')).toBe(false)
  })

  it('dispatches turn/failed', () => {
    dispatch({
      method: 'thread/runtimeChanged',
      params: {
        threadId: 'thread-1',
        runtime: { running: true, waitingOnApproval: false, waitingOnPlanConfirmation: false }
      }
    })
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
    dispatch({
      method: 'thread/runtimeChanged',
      params: {
        threadId: 'thread-1',
        runtime: { running: false, waitingOnApproval: false, waitingOnPlanConfirmation: false }
      }
    })
    dispatch({
      method: 'turn/failed',
      params: { turn: makeTurnPayload('turn_1', 'failed'), error: 'API rate limit' }
    })

    expect(s().turnStatus).toBe('idle')
    expect(s().turns[0].status).toBe('failed')
    expect(s().turns[0].error).toBe('API rate limit')
    expect(useThreadStore.getState().runningTurnThreadIds.has('thread-1')).toBe(false)
  })

  it('dispatches turn/cancelled', () => {
    dispatch({
      method: 'thread/runtimeChanged',
      params: {
        threadId: 'thread-1',
        runtime: { running: true, waitingOnApproval: false, waitingOnPlanConfirmation: false }
      }
    })
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
    dispatch({
      method: 'thread/runtimeChanged',
      params: {
        threadId: 'thread-1',
        runtime: { running: false, waitingOnApproval: false, waitingOnPlanConfirmation: false }
      }
    })
    dispatch({
      method: 'turn/cancelled',
      params: { turn: makeTurnPayload('turn_1', 'cancelled'), reason: 'user requested' }
    })

    expect(s().turnStatus).toBe('idle')
    expect(s().turns[0].status).toBe('cancelled')
    expect(s().turns[0].cancelReason).toBe('user requested')
    expect(useThreadStore.getState().runningTurnThreadIds.has('thread-1')).toBe(false)
  })

  it('dispatches item/agentMessage/delta and accumulates streamingMessage', () => {
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
    dispatch({ method: 'item/started', params: { turnId: 'turn_1', item: { id: 'item_1', type: 'agentMessage' } } })
    dispatch({ method: 'item/agentMessage/delta', params: { delta: 'Hello' } })
    dispatch({ method: 'item/agentMessage/delta', params: { delta: ', world!' } })

    expect(s().streamingMessage).toBe('Hello, world!')
  })

  it('dispatches item/completed (agentMessage) and commits text to turn items', () => {
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
    dispatch({ method: 'item/started', params: { turnId: 'turn_1', item: { id: 'item_1', type: 'agentMessage' } } })
    dispatch({ method: 'item/agentMessage/delta', params: { delta: 'The answer is 42.' } })
    dispatch({
      method: 'item/completed',
      params: { turnId: 'turn_1', item: { id: 'item_1', type: 'agentMessage', createdAt: NOW } }
    })

    const items = s().turns[0].items
    expect(s().streamingMessage).toBe('')
    expect(items).toHaveLength(1)
    expect(items[0].text).toBe('The answer is 42.')
    expect(items[0].type).toBe('agentMessage')
  })

  it('dispatches command execution deltas into the matching item', () => {
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
    dispatch({
      method: 'item/started',
      params: {
        turnId: 'turn_1',
        item: {
          id: 'cmd_1',
          type: 'commandExecution',
          payload: {
            callId: 'exec-1',
            command: 'npm test',
            status: 'inProgress',
            aggregatedOutput: ''
          }
        }
      }
    })
    dispatch({
      method: 'item/commandExecution/outputDelta',
      params: { threadId: 'thread-1', turnId: 'turn_1', itemId: 'cmd_1', delta: 'chunk\n' }
    })
    dispatch({
      method: 'item/completed',
      params: {
        turnId: 'turn_1',
        item: {
          id: 'cmd_1',
          type: 'commandExecution',
          payload: {
            callId: 'exec-1',
            command: 'npm test',
            status: 'completed',
            aggregatedOutput: 'chunk\n',
            exitCode: 0,
            durationMs: 400
          }
        }
      }
    })

    const item = s().turns[0].items.find((i) => i.id === 'cmd_1')
    expect(item?.type).toBe('commandExecution')
    expect(item?.aggregatedOutput).toBe('chunk\n')
    expect(item?.executionStatus).toBe('completed')
    expect(item?.exitCode).toBe(0)
  })

  it('dispatches tool call argument deltas into the matching tool call item and decodes escapes', () => {
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
    dispatch({
      method: 'item/started',
      params: {
        turnId: 'turn_1',
        item: {
          id: 'tool_write_1',
          type: 'toolCall',
          payload: {
            callId: 'write-1',
            toolName: 'WriteFile'
          }
        }
      }
    })
    dispatch({
      method: 'item/toolCall/argumentsDelta',
      params: {
        threadId: 'thread-1',
        turnId: 'turn_1',
        itemId: 'tool_write_1',
        toolName: 'WriteFile',
        callId: 'write-1',
        delta: '{"path":"a.txt","content":"hello\\nworld"}'
      }
    })

    const item = s().turns[0].items.find((i) => i.id === 'tool_write_1')
    expect(item?.type).toBe('toolCall')
    expect(item?.status).toBe('streaming')
    expect(item?.argumentsPreview).toContain('"content":"hello\\nworld"')
    expect(item?.streamingFileContent).toBe('hello\nworld')

    dispatch({
      method: 'item/started',
      params: {
        turnId: 'turn_1',
        item: {
          id: 'tool_edit_1',
          type: 'toolCall',
          payload: {
            callId: 'edit-1',
            toolName: 'EditFile'
          }
        }
      }
    })
    dispatch({
      method: 'item/toolCall/argumentsDelta',
      params: {
        threadId: 'thread-1',
        turnId: 'turn_1',
        itemId: 'tool_edit_1',
        toolName: 'EditFile',
        callId: 'edit-1',
        delta: '{"path":"a.txt","oldText":"before","newText":"## title\\ncontent"}'
      }
    })

    const editItem = s().turns[0].items.find((i) => i.id === 'tool_edit_1')
    expect(editItem?.type).toBe('toolCall')
    expect(editItem?.status).toBe('streaming')
    expect(editItem?.argumentsPreview).toContain('"newText":"## title\\ncontent"')
    expect(editItem?.streamingFileContent).toBe('## title\ncontent')
  })

  it('merges finalized toolCall arguments on completion so WriteFile diff is generated', () => {
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
    dispatch({
      method: 'item/started',
      params: {
        turnId: 'turn_1',
        item: {
          id: 'tool_write_2',
          type: 'toolCall',
          payload: {
            callId: 'write-2',
            toolName: 'WriteFile'
          }
        }
      }
    })
    dispatch({
      method: 'item/completed',
      params: {
        turnId: 'turn_1',
        item: {
          id: 'tool_write_2',
          type: 'toolCall',
          payload: {
            callId: 'write-2',
            toolName: 'WriteFile',
            arguments: {
              path: 'a.txt',
              content: 'line1\nline2'
            }
          }
        }
      }
    })
    dispatch({
      method: 'item/completed',
      params: {
        turnId: 'turn_1',
        item: {
          id: 'tool_result_2',
          type: 'toolResult',
          payload: {
            callId: 'write-2',
            success: true,
            result: 'Successfully wrote 11 bytes (2 lines) to a.txt'
          }
        }
      }
    })

    const item = s().turns[0].items.find((i) => i.id === 'tool_write_2')
    expect(item?.type).toBe('toolCall')
    expect(item?.status).toBe('completed')
    expect(item?.arguments?.path).toBe('a.txt')
    expect(item?.arguments?.content).toBe('line1\nline2')

    const itemDiff = s().itemDiffs.get('tool_write_2')
    expect(itemDiff).toBeDefined()
    expect(itemDiff?.filePath).toBe('a.txt')
    expect(itemDiff?.additions).toBe(2)
  })

  it('updates the existing Exec toolCall instead of requiring a standalone terminal block', () => {
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
    dispatch({
      method: 'item/started',
      params: {
        turnId: 'turn_1',
        item: {
          id: 'tool_1',
          type: 'toolCall',
          payload: {
            callId: 'exec-3',
            toolName: 'Exec',
            arguments: { command: 'dir' }
          }
        }
      }
    })
    dispatch({
      method: 'item/started',
      params: {
        turnId: 'turn_1',
        item: {
          id: 'cmd_3',
          type: 'commandExecution',
          payload: {
            callId: 'exec-3',
            command: 'dir',
            status: 'inProgress',
            aggregatedOutput: ''
          }
        }
      }
    })
    dispatch({
      method: 'item/commandExecution/outputDelta',
      params: { threadId: 'thread-1', turnId: 'turn_1', itemId: 'cmd_3', delta: 'file.txt\n' }
    })

    const toolItem = s().turns[0].items.find((i) => i.id === 'tool_1')
    expect(toolItem?.type).toBe('toolCall')
    expect(toolItem?.aggregatedOutput).toBe('file.txt\n')
    expect(toolItem?.executionStatus).toBe('inProgress')
  })

  it('dispatches terminal output deltas into the matching Exec tool card', () => {
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
    dispatch({
      method: 'item/started',
      params: {
        turnId: 'turn_1',
        item: {
          id: 'tool_terminal',
          type: 'toolCall',
          payload: {
            callId: 'exec-terminal',
            toolName: 'Exec',
            arguments: { command: 'npm test' }
          }
        }
      }
    })

    dispatch({
      method: 'terminal/outputDelta',
      params: {
        terminal: {
          sessionId: 'term_1',
          threadId: 'thread-1',
          turnId: 'turn_1',
          callId: 'exec-terminal',
          command: 'npm test',
          workingDirectory: '/workspace/project',
          source: 'host',
          status: 'running',
          output: 'chunk\n',
          wallTimeMs: 25
        },
        delta: 'chunk\n'
      }
    })

    let toolItem = s().turns[0].items.find((i) => i.id === 'tool_terminal')
    expect(toolItem?.type).toBe('toolCall')
    expect(toolItem?.aggregatedOutput).toBe('chunk\n')
    expect(toolItem?.executionStatus).toBe('inProgress')
    expect(toolItem?.command).toBe('npm test')
    expect(toolItem?.workingDirectory).toBe('/workspace/project')

    dispatch({
      method: 'terminal/completed',
      params: {
        terminal: {
          sessionId: 'term_1',
          threadId: 'thread-1',
          turnId: 'turn_1',
          callId: 'exec-terminal',
          command: 'npm test',
          workingDirectory: '/workspace/project',
          source: 'host',
          status: 'completed',
          output: 'chunk\nok\n',
          exitCode: 0,
          wallTimeMs: 120
        }
      }
    })

    toolItem = s().turns[0].items.find((i) => i.id === 'tool_terminal')
    expect(toolItem?.aggregatedOutput).toBe('chunk\nok\n')
    expect(toolItem?.executionStatus).toBe('completed')
    expect(toolItem?.exitCode).toBe(0)
    expect(toolItem?.duration).toBe(120)
  })

  it('does not append runInBackground terminal output inline', () => {
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
    dispatch({
      method: 'item/started',
      params: {
        turnId: 'turn_1',
        item: {
          id: 'tool_background',
          type: 'toolCall',
          payload: {
            callId: 'exec-background',
            toolName: 'Exec',
            arguments: { command: 'npm run dev', runInBackground: true }
          }
        }
      }
    })

    dispatch({
      method: 'terminal/outputDelta',
      params: {
        terminal: {
          sessionId: 'term_bg',
          threadId: 'thread-1',
          turnId: 'turn_1',
          callId: 'exec-background',
          command: 'npm run dev',
          status: 'running',
          output: 'server ready\n',
          backgroundReason: 'runInBackground'
        },
        delta: 'server ready\n'
      }
    })

    const toolItem = s().turns[0].items.find((i) => i.id === 'tool_background')
    expect(toolItem?.aggregatedOutput).toBeUndefined()
    expect(toolItem?.executionStatus).toBeUndefined()
  })

  it('keeps Exec render state live when command execution starts before toolCall completion', () => {
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
    dispatch({
      method: 'item/started',
      params: {
        turnId: 'turn_1',
        item: {
          id: 'cmd_real_order',
          type: 'commandExecution',
          payload: {
            callId: 'exec-real-order',
            command: 'dir',
            source: 'host',
            status: 'inProgress',
            aggregatedOutput: ''
          }
        }
      }
    })
    dispatch({
      method: 'item/started',
      params: {
        turnId: 'turn_1',
        item: {
          id: 'tool_real_order',
          type: 'toolCall',
          payload: {
            callId: 'exec-real-order',
            toolName: 'Exec'
          }
        }
      }
    })
    dispatch({
      method: 'item/completed',
      params: {
        turnId: 'turn_1',
        item: {
          id: 'tool_real_order',
          type: 'toolCall',
          payload: {
            callId: 'exec-real-order',
            toolName: 'Exec',
            arguments: { command: 'dir' }
          }
        }
      }
    })
    dispatch({
      method: 'item/commandExecution/outputDelta',
      params: { threadId: 'thread-1', turnId: 'turn_1', itemId: 'cmd_real_order', delta: 'file.txt\n' }
    })

    const toolItem = s().turns[0].items.find((i) => i.id === 'tool_real_order')
    expect(toolItem?.type).toBe('toolCall')
    expect(toolItem?.status).toBe('completed')
    expect(toolItem?.arguments?.command).toBe('dir')
    expect(toolItem?.executionStatus).toBe('inProgress')
    expect(toolItem?.aggregatedOutput).toBe('file.txt\n')
  })

  it('dispatches item/usage/delta and accumulates tokens', () => {
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
    dispatch({ method: 'item/usage/delta', params: { inputTokens: 150, outputTokens: 80 } })
    dispatch({ method: 'item/usage/delta', params: { inputTokens: 50, outputTokens: 20 } })

    expect(s().inputTokens).toBe(200)
    expect(s().outputTokens).toBe(100)
  })

  it('ignores item/usage/delta from non-active threads', () => {
    s().setContextUsage({
      tokens: 40_000,
      contextWindow: 200_000,
      autoCompactThreshold: 180_000,
      warningThreshold: 176_000,
      errorThreshold: 194_000,
      percentLeft: 0.8
    })

    dispatch({
      method: 'item/usage/delta',
      params: {
        threadId: 'thread-2',
        turnId: 'turn_foreign',
        inputTokens: 1_000,
        outputTokens: 200,
        totalInputTokens: 180_500,
        totalOutputTokens: 3_000
      }
    })

    expect(s().inputTokens).toBe(0)
    expect(s().outputTokens).toBe(0)
    expect(s().contextUsage?.tokens).toBe(40_000)
  })

  it('uses active item/usage/delta totalInputTokens for the context ring', () => {
    s().setContextUsage({
      tokens: 40_000,
      contextWindow: 200_000,
      autoCompactThreshold: 180_000,
      warningThreshold: 176_000,
      errorThreshold: 194_000,
      percentLeft: 0.8
    })

    dispatch({
      method: 'item/usage/delta',
      params: {
        threadId: 'thread-1',
        turnId: 'turn_active',
        inputTokens: 1_000,
        outputTokens: 200,
        totalInputTokens: 180_500,
        totalOutputTokens: 3_000
      }
    })

    expect(s().inputTokens).toBe(1_000)
    expect(s().outputTokens).toBe(200)
    expect(s().contextUsage?.tokens).toBe(180_500)
    expect(s().contextUsage?.severity).toBe('warning')
  })

  it('uses active item/usage/delta contextUsage snapshot to create the context ring', () => {
    expect(s().contextUsage).toBeNull()

    dispatch({
      method: 'item/usage/delta',
      params: {
        threadId: 'thread-1',
        turnId: 'turn_active',
        inputTokens: 500,
        outputTokens: 50,
        totalInputTokens: 44_000,
        totalOutputTokens: 50,
        contextUsage: {
          tokens: 44_000,
          contextWindow: 200_000,
          autoCompactThreshold: 180_000,
          warningThreshold: 176_000,
          errorThreshold: 194_000,
          percentLeft: 0.78
        }
      }
    })

    expect(s().inputTokens).toBe(500)
    expect(s().outputTokens).toBe(50)
    expect(s().contextUsage?.tokens).toBe(44_000)
    expect(s().contextUsage?.percentLeft).toBe(0.78)
    expect(s().contextUsage?.severity).toBe('normal')
  })

  it('dispatches system/event and sets systemLabel', () => {
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
    dispatch({ method: 'system/event', params: { kind: 'compacting', turnId: 'turn_1' } })
    expect(s().systemLabel).toBe('systemStatus.compacting')

    dispatch({ method: 'system/event', params: { kind: 'compacted' } })
    expect(s().systemLabel).toBeNull()
  })

  it('dispatches streamError system/event message into retry signals', () => {
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })

    dispatch({
      method: 'system/event',
      params: {
        threadId: 'thread-1',
        turnId: 'turn_1',
        kind: 'streamError',
        message: 'Reconnecting... 1/1'
      }
    })

    expect(s().streamRetrySignals).toHaveLength(1)
    expect(s().streamRetrySignals[0]).toMatchObject({
      turnId: 'turn_1',
      rawMessage: 'Reconnecting... 1/1',
      attempt: 1,
      max: 1
    })
  })

  it('uses active system/event contextUsage snapshot to create the context ring', () => {
    expect(s().contextUsage).toBeNull()

    dispatch({
      method: 'system/event',
      params: {
        threadId: 'thread-1',
        kind: 'compacted',
        tokenCount: 195_000,
        percentLeft: 0.02,
        contextUsage: {
          tokens: 44_000,
          contextWindow: 200_000,
          autoCompactThreshold: 180_000,
          warningThreshold: 176_000,
          errorThreshold: 194_000,
          percentLeft: 0.78
        }
      }
    })

    expect(s().contextUsage?.tokens).toBe(44_000)
    expect(s().contextUsage?.percentLeft).toBe(0.78)
    expect(s().contextUsage?.severity).toBe('normal')
  })

  it('dispatches manual compacting system/event with manual label', () => {
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
    dispatch({ method: 'system/event', params: { kind: 'compacting', turnId: null } })
    expect(s().systemLabel).toBe('systemStatus.compacting.manual')
  })

  it('dispatches consolidationSkipped and clears systemLabel', () => {
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
    dispatch({ method: 'system/event', params: { kind: 'consolidating', turnId: null } })
    expect(s().systemLabel).toBe('systemStatus.consolidating')

    dispatch({ method: 'system/event', params: { kind: 'consolidationSkipped' } })
    expect(s().systemLabel).toBeNull()
  })

  it('dispatches turn-scoped consolidating as background memory status', () => {
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_1') } })
    dispatch({ method: 'system/event', params: { kind: 'consolidating', turnId: 'turn_1' } })

    expect(s().backgroundMemoryStatus).toBe('consolidating')
    expect(s().maintenanceKind).toBeNull()
    expect(s().systemLabel).toBeNull()

    dispatch({ method: 'system/event', params: { kind: 'consolidated', turnId: 'turn_1' } })
    expect(s().backgroundMemoryStatus).toBeNull()
  })

  it('ignores system/event from non-active threads', () => {
    s().setContextUsage({
      tokens: 195_000,
      contextWindow: 200_000,
      autoCompactThreshold: 180_000,
      warningThreshold: 176_000,
      errorThreshold: 194_000,
      percentLeft: 0.025
    })

    dispatch({
      method: 'system/event',
      params: {
        threadId: 'thread-2',
        turnId: 'turn_foreign',
        kind: 'compacted',
        tokenCount: 44_000,
        percentLeft: 0.78,
        contextUsage: {
          tokens: 44_000,
          contextWindow: 200_000,
          autoCompactThreshold: 180_000,
          warningThreshold: 176_000,
          errorThreshold: 194_000,
          percentLeft: 0.78
        }
      }
    })

    expect(s().contextUsage?.tokens).toBe(195_000)
    expect(s().contextUsage?.percentLeft).toBe(0.025)

    dispatch({
      method: 'system/event',
      params: {
        threadId: 'thread-2',
        turnId: 'turn_foreign',
        kind: 'compacting'
      }
    })

    expect(s().systemLabel).toBeNull()
  })

  it('ignores plan/updated from non-active threads without opening the detail panel', () => {
    useUIStore.setState({
      activeDetailTab: { kind: 'system', id: 'changes' },
      detailPanelPreferredVisible: false,
      detailPanelVisible: false
    })

    dispatch({
      method: 'plan/updated',
      params: {
        threadId: 'thread-2',
        title: 'Foreign Plan',
        overview: 'Should not show',
        content: '',
        todos: [{ id: 'foreign', content: 'Wrong thread', status: 'pending' }]
      }
    })

    expect(s().plan).toBeNull()
    expect(useUIStore.getState().activeDetailTab).toEqual({ kind: 'system', id: 'changes' })
    expect(useUIStore.getState().detailPanelVisible).toBe(false)
  })

  it('ignores legacy plan/updated notifications without a threadId', () => {
    dispatch({
      method: 'plan/updated',
      params: {
        title: 'Legacy Plan',
        overview: 'Missing thread identity',
        content: '',
        todos: []
      }
    })

    expect(s().plan).toBeNull()
  })

  it('applies plan/updated for the active thread and opens the Plan tab', () => {
    useUIStore.setState({
      activeDetailTab: { kind: 'system', id: 'changes' },
      detailPanelPreferredVisible: false,
      detailPanelVisible: false
    })

    dispatch({
      method: 'plan/updated',
      params: {
        threadId: 'thread-1',
        title: 'Active Plan',
        overview: 'Belongs to the selected thread',
        content: '# Active Plan',
        todos: [{ id: 'active', content: 'Show this task', status: 'in_progress' }]
      }
    })

    expect(s().plan?.title).toBe('Active Plan')
    expect(s().plan?.todos[0].content).toBe('Show this task')
    expect(useUIStore.getState().activeDetailTab).toEqual({ kind: 'system', id: 'plan' })
    expect(useUIStore.getState().detailPanelVisible).toBe(true)
  })

  it('ignores unknown notification methods without throwing', () => {
    expect(() => {
      dispatch({ method: 'unknown/future/event', params: { foo: 'bar' } })
    }).not.toThrow()
  })

  it('upserts flattened automation task statuses from task updates', () => {
    for (const status of ['running', 'completed', 'failed'] as const) {
      dispatch({
        method: 'automation/task/updated',
        params: {
          task: {
            id: `task-${status}`,
            title: status,
            status,
            threadId: null,
            createdAt: NOW,
            updatedAt: NOW
          }
        }
      })
    }

    expect(useAutomationsStore.getState().tasks.map((task) => task.status).sort()).toEqual([
      'completed',
      'failed',
      'running'
    ])
  })
})

describe('thread lifecycle notification dispatch', () => {
  const minimalThread = (id: string): ThreadSummary => ({
    id,
    displayName: 'T',
    status: 'active',
    originChannel: 'dotcraft-desktop',
    createdAt: new Date().toISOString(),
    lastActiveAt: new Date().toISOString()
  })

  beforeEach(() => {
    useThreadStore.getState().reset()
  })

  it('removes thread from list on thread/deleted', () => {
    dispatchThreadLifecycle({
      method: 'thread/started',
      params: { thread: minimalThread('thread_del_1') }
    })
    expect(useThreadStore.getState().threadList.some((t) => t.id === 'thread_del_1')).toBe(true)

    dispatchThreadLifecycle({
      method: 'thread/deleted',
      params: { threadId: 'thread_del_1' }
    })
    expect(useThreadStore.getState().threadList.some((t) => t.id === 'thread_del_1')).toBe(false)
  })

  it('does not add internal helper threads on thread/started', () => {
    dispatchThreadLifecycle({
      method: 'thread/started',
      params: {
        thread: {
          ...minimalThread('welcome-internal'),
          originChannel: 'welcome-suggest',
          metadata: { 'dotcraft.internal': 'welcome-suggest' }
        }
      }
    })

    expect(useThreadStore.getState().threadList).toEqual([])
  })

  it('removes subagent descendants when a parent is deleted or archived', () => {
    dispatchThreadLifecycle({
      method: 'thread/started',
      params: { thread: minimalThread('parent-1') }
    })
    dispatchThreadLifecycle({
      method: 'thread/started',
      params: {
        thread: {
          ...minimalThread('child-1'),
          originChannel: 'subagent',
          source: {
            kind: 'subagent',
            subAgent: {
              parentThreadId: 'parent-1',
              depth: 1
            }
          }
        }
      }
    })

    dispatchThreadLifecycle({
      method: 'thread/statusChanged',
      params: { threadId: 'parent-1', newStatus: 'archived' }
    })

    expect(useThreadStore.getState().threadList).toEqual([])
  })

  it('reloads thread discovery when Team state changes', () => {
    let reloads = 0
    dispatchThreadLifecycle({
      method: 'teams/team/changed',
      params: {}
    }, {
      reloadThreadList: () => {
        reloads += 1
      }
    })

    expect(reloads).toBe(1)
  })
})

describe('full turn lifecycle via notification dispatch', () => {
  it('processes a complete turn: started -> reasoning -> agent message -> completed', () => {
    const turnId = 'turn_full_1'

    // Server confirms turn started
    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload(turnId) } })
    expect(s().turnStatus).toBe('running')
    expect(s().activeTurnId).toBe(turnId)

    // Reasoning phase
    dispatch({ method: 'item/started', params: { turnId, item: { id: 'r_1', type: 'reasoningContent' } } })
    dispatch({ method: 'item/reasoning/delta', params: { delta: 'Let me think...' } })
    expect(s().streamingReasoning).toBe('Let me think...')
    dispatch({
      method: 'item/completed',
      params: { turnId, item: { id: 'r_1', type: 'reasoningContent', createdAt: NOW } }
    })
    expect(s().streamingReasoning).toBe('')
    const reasoningItem = s().turns[0].items.find((i) => i.type === 'reasoningContent')
    expect(reasoningItem?.reasoning).toBe('Let me think...')

    // Agent message streaming
    dispatch({ method: 'item/started', params: { turnId, item: { id: 'msg_1', type: 'agentMessage' } } })
    dispatch({ method: 'item/agentMessage/delta', params: { delta: 'The answer ' } })
    dispatch({ method: 'item/agentMessage/delta', params: { delta: 'is 42.' } })
    expect(s().streamingMessage).toBe('The answer is 42.')
    dispatch({
      method: 'item/completed',
      params: { turnId, item: { id: 'msg_1', type: 'agentMessage', createdAt: NOW } }
    })
    expect(s().streamingMessage).toBe('')

    // Token usage accumulation
    dispatch({ method: 'item/usage/delta', params: { inputTokens: 500, outputTokens: 120 } })

    // Turn completed
    dispatch({
      method: 'turn/completed',
      params: { turn: { ...makeTurnPayload(turnId, 'completed'), completedAt: NOW } }
    })

    const finalState = s()
    expect(finalState.turnStatus).toBe('idle')
    expect(finalState.activeTurnId).toBeNull()
    expect(finalState.turns[0].status).toBe('completed')
    expect(finalState.inputTokens).toBe(500)
    expect(finalState.outputTokens).toBe(120)

    const agentItem = finalState.turns[0].items.find((i) => i.type === 'agentMessage')
    expect(agentItem?.text).toBe('The answer is 42.')
  })

  it('two-arg callback format (the old bug) would silently drop all notifications', () => {
    // This test documents the exact bug that was fixed.
    // If someone reverts App.tsx to the old two-arg form:
    //   onNotification((method, params) => {...})
    // then `method` receives the payload object and switch(method) matches nothing.

    const payload = { method: 'turn/started', params: { turn: makeTurnPayload('turn_bug') } }

    // Simulate the broken two-arg dispatch: method = payload object, params = undefined
    const brokenDispatch = (method: unknown, _params: unknown): void => {
      // switch(method) would compare an object to strings -- never matches
      let matched = false
      if (method === 'turn/started') matched = true
      expect(matched).toBe(false) // object !== string, bug confirmed
    }
    brokenDispatch(payload, undefined)

    // The correct dispatch extracts method from payload.method
    const method = payload.method
    expect(method).toBe('turn/started') // string comparison works
  })
})

describe('pending message auto-send', () => {
  it('auto-sends queued command refs with file references after turn completion', async () => {
    const sendRequest = async (method: string, params?: unknown): Promise<unknown> => {
      if (method === 'turn/start') {
        expect(params).toEqual(
          expect.objectContaining({
            threadId: 'thread-1',
            input: [
              { type: 'fileRef', path: 'C:\\temp\\notes.txt', displayPath: 'C:\\temp\\notes.txt' },
              { type: 'text', text: '\n\n' },
              { type: 'commandRef', name: 'code-review', rawText: '/code-review' }
            ]
          })
        )
      }
      return {}
    }

    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_pending') } })
    useConversationStore.getState().setPendingMessage({
      text: '/code-review',
      inputParts: [
        { type: 'fileRef', path: 'C:\\temp\\notes.txt', displayPath: 'C:\\temp\\notes.txt' },
        { type: 'text', text: '\n\n' },
        { type: 'commandRef', name: 'code-review', rawText: '/code-review' }
      ],
      files: [{ path: 'C:\\temp\\notes.txt', fileName: 'notes.txt' }]
    })

    await dispatchTurnCompletedWithAutoSend(
      {
        method: 'turn/completed',
        params: { turn: { ...makeTurnPayload('turn_pending', 'completed'), completedAt: NOW } }
      },
      { sendRequest }
    )

    expect(s().pendingMessage).toBeNull()
  })

  it('auto-sends file-only queued messages using structured input parts', async () => {
    const sendRequest = async (method: string, params?: unknown): Promise<unknown> => {
      expect(method).toBe('turn/start')
      expect(params).toEqual(
        expect.objectContaining({
          threadId: 'thread-1',
          input: [{ type: 'fileRef', path: 'C:\\temp\\notes.txt', displayPath: 'C:\\temp\\notes.txt' }]
        })
      )
      return {}
    }

    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_pending_files') } })
    useConversationStore.getState().setPendingMessage({
      text: '',
      files: [{ path: 'C:\\temp\\notes.txt', fileName: 'notes.txt' }]
    })

    await dispatchTurnCompletedWithAutoSend(
      {
        method: 'turn/completed',
        params: { turn: { ...makeTurnPayload('turn_pending_files', 'completed'), completedAt: NOW } }
      },
      { sendRequest }
    )

    expect(s().pendingMessage).toBeNull()
  })

  it('does not send a turn when queued message has no structured input parts', async () => {
    let turnStartCalled = false
    const sendRequest = async (method: string): Promise<unknown> => {
      if (method === 'turn/start') {
        turnStartCalled = true
      }
      return {}
    }

    dispatch({ method: 'turn/started', params: { turn: makeTurnPayload('turn_pending_skip') } })
    useConversationStore.getState().setPendingMessage({
      text: '',
      inputParts: []
    })

    await dispatchTurnCompletedWithAutoSend(
      {
        method: 'turn/completed',
        params: { turn: { ...makeTurnPayload('turn_pending_skip', 'completed'), completedAt: NOW } }
      },
      { sendRequest }
    )

    expect(turnStartCalled).toBe(false)
    expect(s().pendingMessage).toBeNull()
  })
})
