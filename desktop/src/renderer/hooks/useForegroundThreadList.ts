import { useCallback, useEffect, useRef, useState } from 'react'
import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'
import { readStateMark, useThreadStore } from '../stores/threadStore'
import { useUIStore, type PendingProjectThreadOpen } from '../stores/uiStore'
import { useTurnBookmarkStore } from '../stores/turnBookmarkStore'
import { useConnectionStore } from '../stores/connectionStore'
import { withReadStates } from '../utils/threadReadState'
import type { SessionIdentity, ThreadSummary, ThreadReadState } from '../types/thread'

function resolvePinnedThreadIdsForWorkspace(
  pinnedByWorkspace: Record<string, string[]> | undefined,
  workspacePath: string
): string[] {
  if (!pinnedByWorkspace) return []
  const exact = pinnedByWorkspace[workspacePath]
  if (Array.isArray(exact)) return exact

  const normalizedWorkspacePath = normalizeWorkspaceProjectKey(workspacePath)
  const match = Object.entries(pinnedByWorkspace).find(
    ([candidate]) => normalizeWorkspaceProjectKey(candidate) === normalizedWorkspacePath
  )
  return match?.[1] ?? []
}

export function currentForegroundThreadListKey(
  activeProjectKey: string | null | undefined,
  protocolWorkspacePath: string | null | undefined
): string {
  return normalizeWorkspaceProjectKey(activeProjectKey || protocolWorkspacePath || '')
}

export function canReloadForegroundThreadList(
  activeProjectKey: string | null | undefined,
  protocolWorkspacePath: string | null | undefined
): boolean {
  const protocolKey = normalizeWorkspaceProjectKey(protocolWorkspacePath)
  if (!protocolKey) return false
  const projectKey = normalizeWorkspaceProjectKey(activeProjectKey)
  if (!projectKey || projectKey.startsWith('remote:')) return true
  return projectKey === protocolKey
}

export function useForegroundThreadList(
  activeProjectKeyRef: { current: string | null | undefined },
  protocolWorkspacePathRef: { current: string }
) {
  const [loadedProjectKey, setLoadedProjectKey] = useState<string | null>(null)
  const setLoading = useThreadStore((s) => s.setLoading)
  const threadListReloadGenerationRef = useRef(0)
  const threadListRetryRef = useRef<{ key: string; attempts: number; timer: number | null }>({
    key: '',
    attempts: 0,
    timer: null
  })

  const loadThreadList = useCallback(async function load(pending: PendingProjectThreadOpen | null): Promise<void> {
    const requestGeneration = ++threadListReloadGenerationRef.current
    const path = protocolWorkspacePathRef.current
    if (!canReloadForegroundThreadList(activeProjectKeyRef.current, path)) {
      return
    }
    if (threadListRetryRef.current.timer != null) {
      window.clearTimeout(threadListRetryRef.current.timer)
      threadListRetryRef.current.timer = null
    }
    const requestProtocolWorkspaceKey = normalizeWorkspaceProjectKey(path)
    const projectKey = currentForegroundThreadListKey(activeProjectKeyRef.current, path)
    const connection = useConnectionStore.getState()
    const isCurrentRequest = (): boolean => {
      const currentConnection = useConnectionStore.getState()
      return requestGeneration === threadListReloadGenerationRef.current &&
        projectKey === currentForegroundThreadListKey(activeProjectKeyRef.current, protocolWorkspacePathRef.current) &&
        requestProtocolWorkspaceKey === normalizeWorkspaceProjectKey(protocolWorkspacePathRef.current) &&
        connection.connectionKey === currentConnection.connectionKey &&
        connection.connectionId === currentConnection.connectionId &&
        connection.status === currentConnection.status
    }
    const identity: SessionIdentity = {
      channelName: 'dotcraft-desktop',
      userId: 'local',
      channelContext: `workspace:${path}`,
      workspacePath: path
    }
    const startedAt = performance.now()
    let listReady = false
    let settingsReady = false
    const prunePins = (): void => {
      if (listReady && settingsReady && isCurrentRequest()) useThreadStore.getState().prunePinnedThreadIds()
    }
    setLoading(true)
    try {
      void window.api.settings.get().then(settings => {
        if (!isCurrentRequest()) return
        useThreadStore.getState().hydratePinnedThreadIds(
          projectKey,
          resolvePinnedThreadIdsForWorkspace(settings.pinnedThreadIdsByWorkspace, projectKey)
        )
        useTurnBookmarkStore.getState().hydrate(settings.turnBookmarksByThread)
        settingsReady = true
        prunePins()
      }).catch((error: unknown) => {
        if (isCurrentRequest()) console.error('Failed to load thread settings:', error)
      })
      const includeArchived = pending?.projectKey === projectKey && pending.includeArchived === true
      const params = {
        identity: { ...identity }, scope: 'workspace' as const, includeSubAgents: true,
        ...(includeArchived ? { includeArchived: true } : {})
      }
      const requestedAtMark = readStateMark()
      const result = await window.api.appServer.sendRequest('thread/list', params)
      if (!isCurrentRequest()) return
      const res = result as unknown as { data: ThreadSummary[]; readStates?: Record<string, ThreadReadState> }
      const threadStore = useThreadStore.getState()
      const listedThreads = res.data ?? []
      const selectedArchived = threadStore.threadListProjectKey === projectKey
        ? threadStore.threadList.find((thread) => thread.id === threadStore.activeThreadId && thread.status === 'archived')
        : undefined
      const observedThreads = selectedArchived && !listedThreads.some((thread) => thread.id === selectedArchived.id)
        ? [...listedThreads, selectedArchived]
        : listedThreads
      threadStore.setThreadList(withReadStates(observedThreads, res.readStates, requestedAtMark), projectKey)
      threadListRetryRef.current = { key: '', attempts: 0, timer: null }
      listReady = true
      prunePins()
      const pendingProjectThreadOpen = pending
        ? useUIStore.getState().consumePendingProjectThreadOpen(projectKey, listedThreads.map((thread) => thread.id), pending.requestId)
        : null
      if (pendingProjectThreadOpen) {
        useUIStore.getState().setActiveMainView('conversation')
        useThreadStore.getState().setActiveThreadId(pendingProjectThreadOpen.threadId)
      }
      setLoadedProjectKey(projectKey)
      const elapsedMs = Math.round(performance.now() - startedAt)
      performance.mark('app:workspace-thread-list-ready', { detail: { workspacePath: path, elapsedMs } })
      console.info('[desktop] workspace renderer timing', { workspacePath: path, stage: 'thread-list-ready', elapsedMs })
    } catch (err: unknown) {
      if (!isCurrentRequest()) return
      console.error('Failed to load thread list:', err)
      const retryKey = [projectKey, requestProtocolWorkspaceKey, connection.connectionKey, connection.connectionId].join('\u0000')
      const retry = threadListRetryRef.current
      if (retry.key !== retryKey) {
        threadListRetryRef.current = { key: retryKey, attempts: 0, timer: null }
      }
      const nextRetry = threadListRetryRef.current
      if (nextRetry.attempts < 3) {
        nextRetry.attempts += 1
        nextRetry.timer = window.setTimeout(() => {
          nextRetry.timer = null
          if (isCurrentRequest()) void load(pending)
        }, 1200)
      } else if (pending) {
        useUIStore.getState().clearPendingProjectThreadOpen(projectKey, pending.threadId, pending.requestId)
      }
    } finally {
      if (isCurrentRequest()) {
        setLoading(false)
      }
    }
  }, [activeProjectKeyRef, protocolWorkspacePathRef, setLoading])

  const reloadThreadList = useCallback(
    () => loadThreadList(useUIStore.getState().pendingProjectThreadOpen), [loadThreadList]
  )

  useEffect(() => {
    return () => {
      threadListReloadGenerationRef.current += 1
      if (threadListRetryRef.current.timer != null) {
        window.clearTimeout(threadListRetryRef.current.timer)
        threadListRetryRef.current.timer = null
      }
    }
  }, [])

  const invalidateThreadList = useCallback(() => {
    threadListReloadGenerationRef.current += 1
    if (threadListRetryRef.current.timer != null) window.clearTimeout(threadListRetryRef.current.timer)
    threadListRetryRef.current = { key: '', attempts: 0, timer: null }
    setLoadedProjectKey(null)
    setLoading(false)
  }, [setLoading])

  return { reloadThreadList, invalidateThreadList, loadedProjectKey }
}
