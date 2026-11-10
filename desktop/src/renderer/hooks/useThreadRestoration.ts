import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'
import { translate, type AppLocale } from '../../shared/locales'
import { readAppServerErrorFields } from '../../shared/appServerError'
import { isHiddenThread } from '../../shared/hiddenThreads'
import { useConnectionStore } from '../stores/connectionStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { getThreadConnection, isThreadObservationCurrent, registerThreadConnection } from '../stores/threadConnectionStore'
import { getThreadConversation } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useSubAgentStore } from '../stores/subAgentStore'
import { openThreadState, releaseThreadState } from '../stores/threadState'
import { createWarmThreads, readyWarmThread, WARM_THREAD_SWEEP_MS, type WarmThreads } from '../stores/warmThreads'
import { applyThreadHistoryHead } from '../stores/threadHistoryStore'
import { applyThreadRestoration } from '../stores/applyThreadRestoration'
import { activateParkedInteractiveRequests, parkThreadInteractiveRequests } from '../stores/threadInteractiveRequests'
import {
  applyThreadRestorationBoundary,
  beginThreadRestoration,
  finishThreadRestoration,
} from '../stores/threadRestoration'
import { readThreadHistoryHead, THREAD_OPENING_PAGE } from '../utils/threadHistory'
import { describeAppServerError } from '../utils/appServerErrorText'
import { startPendingWelcomeTurn } from '../utils/startPendingWelcomeTurn'
import {
  resolveThreadObservationTarget,
  threadObservationTargetKey,
  isSameThreadObservationTarget,
  type ThreadObservationTarget
} from '../utils/threadObservationScope'
import { wireTurnToConversationTurn } from '../types/conversation'
import type { Thread } from '../types/thread'
import { addToast } from '../stores/toastStore'

interface Props {
  workspaceKey: string
  protocolWorkspacePath: string
  locale: AppLocale
}

export function useThreadRestoration(props: Props, warmThreads?: WarmThreads): {
  updateWarmThread(thread: Thread): void
  releaseWarmThread(threadId: string): void
} {
  const { status, connectionId } = useConnectionStore()
  const projects = useWorkspaceProjectsStore(state => state.projects)
  const chat = useWorkspaceProjectsStore(state => state.chat)
  const { activeThreadId, activeThread } = useThreadStore()
  const latest = useRef(props)
  latest.current = props
  const [warm] = useState(() => warmThreads ?? createWarmThreads())
  const selected = useRef<{
    threadId: string
    target: ThreadObservationTarget
    ready: boolean
    thread?: Thread
  } | null>(null)
  if (selected.current && activeThread?.id === selected.current.threadId) selected.current.thread = activeThread
  const welcome = useRef<{ threadId: string; controller: AbortController } | null>(null)

  const targetFor = useCallback((threadId: string) => {
    const connection = useConnectionStore.getState()
    const threads = useThreadStore.getState()
    const target = resolveThreadObservationTarget({
      connected: connection.status === 'connected',
      workspaceKey: latest.current.workspaceKey,
      connectionKey: connection.connectionKey,
      connectionId: connection.connectionId,
      capabilities: connection.capabilities,
      threadListWorkspaceKey: threads.threadListProjectKey,
      threadIds: threads.threadList.map(thread => thread.id)
    }, threadId, selected.current?.threadId === threadId && selected.current.ready
      ? selected.current.target : readyWarmThread(warm, threadId)?.target)
    const summary = threads.threadListProjectKey === latest.current.workspaceKey
      ? threads.threadList.find(thread => thread.id === threadId) : undefined
    return target ? { ...target, originChannel: summary?.originChannel ?? target.originChannel } : null
  }, [warm])

  const release = useCallback((threadId: string, target?: ThreadObservationTarget) => {
    const connection = target ?? getThreadConnection(threadId)
    releaseThreadState(threadId)
    if (connection) {
      void window.api.appServer.sendConnectionRequest(connection.connectionKey, 'thread/unsubscribe', { threadId },
        undefined, { connectionId: connection.connectionId, attachmentOwner: 'warm' }).catch(() => {})
    }
  }, [])

  const releaseWarmThread = useCallback((threadId: string) => {
    if (selected.current?.threadId === threadId) selected.current.ready = false
    const entry = warm.take(threadId)
    if (entry) release(threadId, entry.target)
  }, [release, warm])

  useEffect(() => {
    for (const [threadId, entry] of warm.entries()) {
      if (isThreadObservationCurrent(entry.target)) continue
      warm.remove(threadId)
      release(threadId, entry.target)
    }
  }, [projects, chat, status, connectionId, release, warm])

  useEffect(() => {
    const timer = window.setInterval(() => {
      for (const threadId of warm.expired(Date.now())) releaseWarmThread(threadId)
    }, WARM_THREAD_SWEEP_MS)
    return () => window.clearInterval(timer)
  }, [releaseWarmThread])

  const target = activeThreadId ? targetFor(activeThreadId) : null
  const targetKey = target ? threadObservationTargetKey(target) : null
  useLayoutEffect(() => {
    const previous = selected.current
    const target = activeThreadId ? targetFor(activeThreadId) : null
    if (previous?.threadId === activeThreadId && isSameThreadObservationTarget(previous.target, target)) return

    if (welcome.current && welcome.current.threadId !== activeThreadId) {
      welcome.current.controller.abort()
      welcome.current = null
    }
    finishThreadRestoration()
    if (previous) {
      const oldHeader = previous.thread
      if (previous.ready && oldHeader?.id === previous.threadId && !isHiddenThread(oldHeader)
        && isThreadObservationCurrent(previous.target)) {
        parkThreadInteractiveRequests(previous.threadId)
        for (const evicted of warm.keep({ thread: oldHeader, target: previous.target, viewedAt: Date.now() })) {
          release(evicted)
        }
      } else {
        release(previous.threadId, previous.target)
      }
    }
    selected.current = null
    if (!activeThreadId) {
      openThreadState(null)
      useThreadStore.getState().setActiveThread(null)
      return
    }
    if (!target) return
    const selection = { threadId: activeThreadId, target, ready: false }
    selected.current = selection
    const cached = readyWarmThread(warm, activeThreadId)
    const reuseCached = cached && isSameThreadObservationTarget(cached.target, target)
    if (!reuseCached) {
      const stale = warm.take(activeThreadId)
      if (stale) release(activeThreadId, stale.target)
    }
    const owned = getThreadConnection(activeThreadId)
    if (owned && !isSameThreadObservationTarget(owned, target)) releaseThreadState(activeThreadId)
    registerThreadConnection(activeThreadId, target)
    openThreadState(activeThreadId)
    if (reuseCached) {
      warm.take(activeThreadId)
      selection.ready = true
      const threads = useThreadStore.getState()
      const summary = threads.threadListProjectKey === latest.current.workspaceKey
        ? threads.threadList.find(thread => thread.id === activeThreadId) : undefined
      threads.setActiveThread({
        ...cached.thread,
        ...(summary && {
          displayName: summary.displayName, status: summary.status,
          lastActiveAt: summary.lastActiveAt, readState: summary.readState
        }),
        runtime: threads.runtimeSnapshots.get(activeThreadId) ?? cached.thread.runtime,
        goal: threads.goalSnapshots.get(activeThreadId) ?? null
      })
      activateParkedInteractiveRequests(activeThreadId)
      return
    }
    const threadId = activeThreadId
    const gate = beginThreadRestoration(threadId)
    const isCurrent = (): boolean => selected.current === selection
      && useThreadStore.getState().activeThreadId === threadId
      && isSameThreadObservationTarget(target, targetFor(threadId))
    let authoritative = false
    const previewStore = getThreadConversation(threadId)!
    const previewTurns = previewStore.getState().turns
    const preview = readThreadHistoryHead(
      (method, params) => window.api.appServer.sendConnectionRequest(target.connectionKey, method, params, undefined,
        { connectionId: target.connectionId }), threadId,
      undefined, undefined, isCurrent
    )
    const showPreview = (result: Awaited<typeof preview>): void => {
      if (!isCurrent() || authoritative || previewTurns.length !== 0
        || getThreadConversation(threadId) !== previewStore
        || previewStore.getState().turns !== previewTurns) return
      useThreadStore.getState().setActiveThread(result.thread)
      previewStore.getState().setTurns(
        result.thread.turns.map(turn => wireTurnToConversationTurn(turn as unknown as Record<string, unknown>))
      )
    }

    const startWelcome = (): void => {
      const pending = useUIStore.getState().consumePendingWelcomeTurnIfMatch(threadId)
      if (!pending) return
      const controller = new AbortController()
      const onScreen = isCurrent()
      if (onScreen) welcome.current = { threadId, controller }
      void startPendingWelcomeTurn({
        threadId, pending,
        workspacePath: latest.current.protocolWorkspacePath,
        translate: (key, vars) => translate(latest.current.locale, key, vars),
        signal: onScreen ? controller.signal : undefined
      })
    }
    const fail = (error: unknown): void => {
      if (!isCurrent()) return
      finishThreadRestoration(gate, true)
      useUIStore.getState().cancelPendingWelcomeTurnForThread(threadId)
      if (readAppServerErrorFields(error).rpcCode === -32010) {
        useThreadStore.getState().removeThread(threadId)
        addToast(translate(latest.current.locale, 'toast.threadNotFound'), 'warning')
      } else {
        useThreadStore.getState().setActiveThreadLoadError(threadId, describeAppServerError(latest.current.locale, error))
      }
    }
    const summary = useThreadStore.getState().threadList.find(thread => thread.id === threadId)
    if (summary?.status === 'archived') {
      void preview.then(result => {
        if (!isCurrent()) return
        showPreview(result)
        applyThreadHistoryHead(threadId, result.thread.turns, result.turnCursor)
        selection.ready = true
        finishThreadRestoration(gate)
      }, fail)
      return
    }
    void preview.then(showPreview, () => {})
    void window.api.appServer.sendConnectionRequest(target.connectionKey, 'thread/resume', {
      threadId, initialTurnsPage: THREAD_OPENING_PAGE
    }, undefined, {
      connectionId: target.connectionId,
      attachmentOwner: 'warm',
      onResponse: result => {
        if (!isCurrent()) {
          if (useThreadStore.getState().activeThreadId !== threadId
            && isThreadObservationCurrent(target)) {
            startWelcome()
            release(threadId, target)
          }
          return
        }
        authoritative = true
        applyThreadRestorationBoundary(gate, () => {
          applyThreadRestoration(result)
          selection.ready = true
        })
        finishThreadRestoration(gate)
        activateParkedInteractiveRequests(threadId)
        void useSubAgentStore.getState().ensureChildren(threadId).catch(() => {})
        startWelcome()
      }
    }).catch(fail)
  }, [activeThreadId, targetKey, status, connectionId, targetFor, release, warm])

  return {
    updateWarmThread: useCallback((thread: Thread) => warm.updateThread(thread), [warm]),
    releaseWarmThread
  }
}
