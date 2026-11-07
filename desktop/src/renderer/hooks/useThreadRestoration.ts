import { useCallback, useEffect, useRef } from 'react'
import { translate, type AppLocale } from '../../shared/locales'
import { readAppServerErrorFields } from '../../shared/appServerError'
import { isHiddenThread } from '../../shared/hiddenThreads'
import { useConnectionStore } from '../stores/connectionStore'
import { getThreadConversation } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useSubAgentStore } from '../stores/subAgentStore'
import { openThreadState, releaseThreadState } from '../stores/threadState'
import { createWarmThreads, WARM_THREAD_SWEEP_MS } from '../stores/warmThreads'
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
  isThreadObservationConnectionCurrent,
  type ThreadObservationTarget
} from '../utils/threadObservationScope'
import { wireTurnToConversationTurn } from '../types/conversation'
import type { Thread } from '../types/thread'
import { addToast } from '../stores/toastStore'

interface Props {
  workspaceIdentity: string
  workspaceKey: string
  protocolWorkspacePath: string
  locale: AppLocale
}

export function useThreadRestoration(props: Props): {
  updateWarmThread(thread: Thread): void
  releaseWarmThread(threadId: string): void
} {
  const { status, connectionEpoch } = useConnectionStore()
  const { activeThreadId, activeThread, threadList, threadListProjectKey } = useThreadStore()
  const latest = useRef(props)
  latest.current = props
  const warm = useRef(createWarmThreads())
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
    return resolveThreadObservationTarget({
      connected: connection.status === 'connected',
      workspaceIdentity: latest.current.workspaceIdentity,
      workspaceKey: latest.current.workspaceKey,
      connectionEpoch: connection.connectionEpoch,
      threadListWorkspaceKey: threads.threadListProjectKey,
      threadIds: threads.threadList.map(thread => thread.id)
    }, threadId)
  }, [])

  const release = useCallback((threadId: string, target?: ThreadObservationTarget) => {
    parkThreadInteractiveRequests(threadId)
    releaseThreadState(threadId)
    if (target && !isThreadObservationConnectionCurrent(target, {
      workspaceIdentity: latest.current.workspaceIdentity,
      workspaceKey: latest.current.workspaceKey,
      connectionEpoch: useConnectionStore.getState().connectionEpoch
    })) return
    void window.api.appServer.sendRequest('thread/unsubscribe', { threadId }).catch(() => {})
  }, [])

  const releaseWarmThread = useCallback((threadId: string) => {
    const entry = warm.current.take(threadId)
    if (entry) release(threadId, entry.target)
  }, [release])

  useEffect(() => {
    for (const [threadId, entry] of warm.current.entries()) {
      if (isSameThreadObservationTarget(entry.target, targetFor(threadId))) continue
      warm.current.remove(threadId)
      release(threadId, entry.target)
    }
  }, [props.workspaceIdentity, props.workspaceKey, status, connectionEpoch, threadList, threadListProjectKey, release, targetFor])

  useEffect(() => {
    const timer = window.setInterval(() => {
      for (const threadId of warm.current.expired(Date.now())) releaseWarmThread(threadId)
    }, WARM_THREAD_SWEEP_MS)
    return () => window.clearInterval(timer)
  }, [releaseWarmThread])

  const target = activeThreadId ? targetFor(activeThreadId) : null
  const targetKey = target ? threadObservationTargetKey(target) : null
  useEffect(() => {
    const previous = selected.current
    const target = activeThreadId ? targetFor(activeThreadId) : null
    if (previous?.threadId === activeThreadId && isSameThreadObservationTarget(previous.target, target)) return

    if (welcome.current && welcome.current.threadId !== activeThreadId) {
      welcome.current.controller.abort()
      welcome.current = null
    }
    finishThreadRestoration()
    if (previous && previous.threadId !== activeThreadId) {
      const oldHeader = previous.thread
      if (previous.ready && oldHeader?.id === previous.threadId && !isHiddenThread(oldHeader)
        && isSameThreadObservationTarget(previous.target, targetFor(previous.threadId))) {
        for (const evicted of warm.current.keep({ thread: oldHeader, target: previous.target, viewedAt: Date.now() })) {
          release(evicted, targetFor(evicted) ?? undefined)
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
    openThreadState(activeThreadId)
    const cached = warm.current.take(activeThreadId)
    if (cached && isSameThreadObservationTarget(cached.target, target)) {
      selection.ready = true
      const threads = useThreadStore.getState()
      const summary = threads.threadList.find(thread => thread.id === activeThreadId)
      threads.setActiveThread({
        ...cached.thread,
        ...(summary && {
          displayName: summary.displayName, status: summary.status,
          lastActiveAt: summary.lastActiveAt, runtime: summary.runtime, readState: summary.readState
        }),
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
    const preview = readThreadHistoryHead(
      (method, params) => window.api.appServer.sendRequest(method, params), threadId,
      undefined, undefined, isCurrent
    )
    const showPreview = (result: Awaited<typeof preview>): void => {
      if (!isCurrent() || authoritative) return
      useThreadStore.getState().setActiveThread(result.thread)
      getThreadConversation(threadId)?.getState().setTurns(
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
    void window.api.appServer.sendRequest('thread/resume', {
      threadId, initialTurnsPage: THREAD_OPENING_PAGE
    }, undefined, {
      onResponse: result => {
        if (!isCurrent()) {
          if (useThreadStore.getState().activeThreadId !== threadId
            && isThreadObservationConnectionCurrent(target, {
              workspaceIdentity: latest.current.workspaceIdentity,
              workspaceKey: latest.current.workspaceKey,
              connectionEpoch: useConnectionStore.getState().connectionEpoch
            })) {
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
  }, [activeThreadId, targetKey, status, connectionEpoch, targetFor, release])

  return {
    updateWarmThread: useCallback((thread: Thread) => warm.current.updateThread(thread), []),
    releaseWarmThread
  }
}
